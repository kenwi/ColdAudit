using System.Text.Json.Serialization;
using ColdAudit.Shared.Assets;
using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

/// <summary>
/// Static lighting baked by <c>tools/bake_lightmaps.py</c>: one atlas, sampled through the
/// mesh's second UV set (glTF <c>TEXCOORD_1</c>, raylib <c>mesh.texcoords2</c>), plus the
/// sidecar JSON that carries the linear-to-sRGB encode scale.
/// <para>
/// The atlas is deliberately not a material slot. raylib uploads each material's maps into
/// their own GPU image, so a shared atlas wired into twenty materials would be uploaded
/// twenty times; binding it to its own texture unit once per frame avoids that and keeps
/// it out of the way of <see cref="BasicLighting"/>'s shadow cubes.
/// </para>
/// </summary>
public sealed class LightMap : IDisposable
{
    /// <summary>
    /// Unit 11: raylib binds material maps to units 0-11 but only indexes 0-10 are ever
    /// used (BRDF is the last enum), and <see cref="BasicLighting"/> puts its shadow cubes
    /// on 12-15. Nothing else in the engine touches it, so it survives between draws.
    /// </summary>
    public const int AtlasTextureUnit = 11;

    /// <summary>
    /// Scene-units-to-display scale applied before the Reinhard curve. Chosen against the
    /// 2048 atlas: the median covered texel bakes to 0.34 irradiance/pi and the 90th
    /// percentile to 2.0, so 0.8 lands a mid-grey surface near 90/255 with the bright
    /// ones near 180 - close to the Blender stills - instead of clipping the top decile.
    /// </summary>
    public const float Exposure = 0.8f;

    private ShaderVariant _textured;
    private ShaderVariant _solid;
    private Texture2D _atlas;
    private bool _enabled = true;

    public bool IsLoaded { get; private set; }
    public bool Enabled => _enabled;
    public int Resolution { get; private set; }
    public float EncodeScale { get; private set; } = 1f;
    public string? UvLayer { get; private set; }
    public string? Error { get; private set; }

    public bool TryLoad(string imagePath, string metaPath)
    {
        Unload();
        Error = null;

        if (!File.Exists(imagePath))
        {
            Error = $"atlas missing: {imagePath}";
            return false;
        }

        var vs = ShaderCatalog.LightMapVertexPath;
        var fs = ShaderCatalog.LightMapFragmentPath;
        if (!File.Exists(vs) || !File.Exists(fs))
        {
            Error = $"shader missing: {vs} / {fs}";
            return false;
        }

        if (!ReadMeta(metaPath))
        {
            return false;
        }

        _atlas = Raylib.LoadTexture(imagePath);
        if (_atlas.Id == 0)
        {
            Error = $"atlas failed to load: {imagePath}";
            return false;
        }

        // Island edges are padded, so clamp: wrap would fold neighbouring islands in.
        Raylib.SetTextureFilter(_atlas, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(_atlas, TextureWrap.Clamp);

        if (!TryBuildVariant(vs, fs, textured: true, out _textured) ||
            !TryBuildVariant(vs, fs, textured: false, out _solid))
        {
            Error = "lightmap shader failed to compile";
            Unload();
            return false;
        }

        IsLoaded = true;
        return true;
    }

    /// <summary>
    /// Give every material the lightmap shader. glTF materials always carry raylib's 1x1
    /// white default albedo, so untextured ones go through the normal path; the second
    /// variant only covers a material whose albedo map is genuinely empty, where unit 0
    /// would otherwise keep the previous material's texture.
    /// </summary>
    public unsafe void ApplyToModel(ModelHandle handle)
    {
        if (!IsLoaded || !handle.IsLoaded)
        {
            return;
        }

        var model = handle.Model;
        var materials = model.Materials;
        for (var i = 0; i < model.MaterialCount; i++)
        {
            var textured = materials[i].Maps[(int)MaterialMapIndex.Albedo].Texture.Id != 0;
            var shader = textured ? _textured.Shader : _solid.Shader;
            Raylib.SetMaterialShader(ref model, i, ref shader);
        }

        // The one failure that is invisible until the level looks flat: a sector exported
        // before the bake has no TEXCOORD_1, so every mesh samples a single atlas texel.
        var meshes = model.Meshes;
        var missingUv = 0;
        for (var i = 0; i < model.MeshCount; i++)
        {
            if (meshes[i].TexCoords2 == null)
            {
                missingUv++;
            }
        }

        if (missingUv > 0)
        {
            Console.WriteLine(
                $"[LightMap] {missingUv}/{model.MeshCount} meshes have no TEXCOORD_1; " +
                "re-export the sector from the baked .blend");
        }
    }

    /// <summary>
    /// Point the atlas at its unit. Call once per 3D pass, before the sector models are
    /// drawn: raylib's <c>DrawMesh</c> leaves the active slot wherever the last material
    /// left it, but never touches this one.
    /// </summary>
    public void BindTexture()
    {
        if (!IsLoaded)
        {
            return;
        }

        Rlgl.ActiveTextureSlot(AtlasTextureUnit);
        Rlgl.EnableTexture(_atlas.Id);
        Rlgl.ActiveTextureSlot(0);
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!IsLoaded)
        {
            return;
        }

        PushEnabled(_textured);
        PushEnabled(_solid);
    }

    public void Unload()
    {
        IsLoaded = false;
        _enabled = true;

        if (_atlas.Id != 0)
        {
            Raylib.UnloadTexture(_atlas);
            _atlas = default;
        }

        UnloadVariant(ref _textured);
        UnloadVariant(ref _solid);
    }

    public void Dispose() => Unload();

    private bool ReadMeta(string metaPath)
    {
        Resolution = 0;
        EncodeScale = 1f;

        if (!File.Exists(metaPath))
        {
            Error = $"atlas metadata missing: {metaPath}";
            return false;
        }

        try
        {
            var meta = System.Text.Json.JsonSerializer.Deserialize<AtlasMeta>(File.ReadAllText(metaPath));
            if (meta is null || meta.EncodeScale <= 0f)
            {
                Error = $"atlas metadata has no positive encode_scale: {metaPath}";
                return false;
            }

            Resolution = meta.Resolution;
            EncodeScale = meta.EncodeScale;
            UvLayer = meta.UvLayer;
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            Error = $"atlas metadata is not valid JSON: {metaPath}";
            return false;
        }
    }

    private unsafe bool TryBuildVariant(string vs, string fs, bool textured, out ShaderVariant variant)
    {
        variant = default;

        var shader = Raylib.LoadShader(vs, fs);
        if (!Raylib.IsShaderValid(shader))
        {
            return false;
        }

        // pbr.fs names its material inputs albedoMap/albedoColor; raylib only knows
        // texture0/colDiffuse by default, so point its own locations at ours.
        shader.Locs[(int)ShaderLocationIndex.MapAlbedo] = Raylib.GetShaderLocation(shader, "albedoMap");
        shader.Locs[(int)ShaderLocationIndex.ColorDiffuse] = Raylib.GetShaderLocation(shader, "albedoColor");

        variant = new ShaderVariant
        {
            Shader = shader,
            EnabledLoc = Raylib.GetShaderLocation(shader, "useLightMap")
        };

        Raylib.SetShaderValue(
            shader,
            Raylib.GetShaderLocation(shader, "useTexAlbedo"),
            textured ? 1 : 0,
            ShaderUniformDataType.Int);
        Raylib.SetShaderValue(
            shader,
            Raylib.GetShaderLocation(shader, "lightMap"),
            AtlasTextureUnit,
            ShaderUniformDataType.Int);
        Raylib.SetShaderValue(
            shader,
            Raylib.GetShaderLocation(shader, "lightMapScale"),
            1f/EncodeScale,
            ShaderUniformDataType.Float);
        Raylib.SetShaderValue(
            shader,
            Raylib.GetShaderLocation(shader, "exposure"),
            Exposure,
            ShaderUniformDataType.Float);

        PushEnabled(variant);
        return true;
    }

    private void PushEnabled(ShaderVariant variant)
    {
        if (variant.Shader.Id == 0 || variant.EnabledLoc < 0)
        {
            return;
        }

        Raylib.SetShaderValue(
            variant.Shader,
            variant.EnabledLoc,
            _enabled ? 1 : 0,
            ShaderUniformDataType.Int);
    }

    private static void UnloadVariant(ref ShaderVariant variant)
    {
        if (variant.Shader.Id != 0)
        {
            Raylib.UnloadShader(variant.Shader);
        }

        variant = default;
    }

    private struct ShaderVariant
    {
        public Shader Shader;
        public int EnabledLoc;
    }

    private sealed class AtlasMeta
    {
        [JsonPropertyName("resolution")]
        public int Resolution { get; init; }

        [JsonPropertyName("encode_scale")]
        public float EncodeScale { get; init; }

        [JsonPropertyName("uv_layer")]
        public string? UvLayer { get; init; }
    }
}
