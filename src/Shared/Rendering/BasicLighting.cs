using System.Numerics;
using ColdAudit.Shared.Assets;
using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

/// <summary>
/// Shared Raylib PBR lighting shader (point lights, up to 4).
/// Original shader ignores light type; directional entries are treated as point lights.
/// </summary>
public sealed class BasicLighting : IDisposable
{
    public const int MaxLights = 4;

    private Shader _shader;
    private int _viewPosLoc = -1;
    private int _ambientLoc = -1;
    private int _ambientColorLoc = -1;
    private int _metallicValueLoc = -1;
    private int _roughnessValueLoc = -1;
    private int _aoValueLoc = -1;
    private int _emissivePowerLoc = -1;
    private int _emissiveColorLoc = -1;
    private int _tilingLoc = -1;
    private int _useTexAlbedoLoc = -1;
    private int _useTexNormalLoc = -1;
    private int _useTexMRALoc = -1;
    private int _useTexEmissiveLoc = -1;
    private readonly List<SceneLight> _lights = [];
    private bool _lightingEnabled = true;
    private bool _pbrTexturesEnabled = true;

    public bool IsLoaded { get; private set; }
    public IReadOnlyList<SceneLight> Lights => _lights;

    public void Load()
    {
        Unload();

        var vs = ShaderCatalog.PbrVertexPath;
        var fs = ShaderCatalog.PbrFragmentPath;
        if (!File.Exists(vs) || !File.Exists(fs))
        {
            return;
        }

        _shader = Raylib.LoadShader(vs, fs);
        if (!Raylib.IsShaderValid(_shader))
        {
            return;
        }

        BindPbrLocations();
        SetPbrDefaults();

        IsLoaded = true;
    }

    public SceneLight? AddDirectionalLight(Vector3 position, Vector3 target, Color color, float intensity = 1f)
    {
        return AddLight(LightType.Directional, position, target, color, intensity);
    }

    public SceneLight? AddPointLight(Vector3 position, Color color, float intensity = 1f)
    {
        return AddLight(LightType.Point, position, Vector3.Zero, color, intensity);
    }

    public void UpdateLight(SceneLight light)
    {
        if (!IsLoaded)
        {
            return;
        }

        PushLightEnabled(light);
        Raylib.SetShaderValue(_shader, light.TypeLoc, (int)light.Type, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_shader, light.PositionLoc, light.Position, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, light.TargetLoc, light.Target, ShaderUniformDataType.Vec3);

        var color = new Vector4(
            light.Color.R / 255f,
            light.Color.G / 255f,
            light.Color.B / 255f,
            light.Color.A / 255f);
        Raylib.SetShaderValue(_shader, light.ColorLoc, color, ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(_shader, light.IntensityLoc, light.Intensity, ShaderUniformDataType.Float);
    }

    public void UpdateViewPosition(Vector3 cameraPosition)
    {
        if (!IsLoaded || _viewPosLoc < 0)
        {
            return;
        }

        Raylib.SetShaderValue(_shader, _viewPosLoc, cameraPosition, ShaderUniformDataType.Vec3);
    }

    /// <summary>
    /// Enable or disable dynamic lights without changing per-light <see cref="SceneLight.Enabled"/>.
    /// </summary>
    public void SetLightingEnabled(bool enabled)
    {
        _lightingEnabled = enabled;
        if (!IsLoaded)
        {
            return;
        }

        foreach (var light in _lights)
        {
            PushLightEnabled(light);
        }
    }

    /// <summary>
    /// Enable or disable PBR map sampling (albedo, normal, MRA, emissive).
    /// </summary>
    public void SetPbrTexturesEnabled(bool enabled)
    {
        _pbrTexturesEnabled = enabled;
        if (!IsLoaded)
        {
            return;
        }

        PushTextureUsage(useNormal: false, useMra: false, useEmissive: false);
    }

    /// <summary>
    /// Begin drawing with the lighting shader (immediate-mode or custom meshes).
    /// Caller must pair with <see cref="EndShaderMode"/>.
    /// </summary>
    public bool TryBeginShaderMode()
    {
        if (!IsLoaded)
        {
            return false;
        }

        Raylib.BeginShaderMode(_shader);
        return true;
    }

    public void EndShaderMode()
    {
        if (!IsLoaded)
        {
            return;
        }

        Raylib.EndShaderMode();
    }

    public void ApplyToModel(ModelHandle handle)
    {
        if (!IsLoaded || !handle.IsLoaded)
        {
            return;
        }

        var model = handle.Model;
        for (var i = 0; i < model.MaterialCount; i++)
        {
            Raylib.SetMaterialShader(ref model, i, ref _shader);
        }
    }

    public void ApplyToMaterial(ref Material material)
    {
        if (!IsLoaded)
        {
            return;
        }

        material.Shader = _shader;
    }

    /// <summary>
    /// Bind PBR maps on every material slot. Pass <see langword="default"/> to skip a map.
    /// Generates tangents when a normal map is present.
    /// </summary>
    public unsafe void BindPbrMaps(
        ModelHandle handle,
        Texture2D albedo = default,
        Texture2D mra = default,
        Texture2D normal = default,
        Texture2D emissive = default)
    {
        if (!handle.IsLoaded)
        {
            return;
        }

        var model = handle.Model;
        var materials = model.Materials;
        for (var i = 0; i < model.MaterialCount; i++)
        {
            if (albedo.Id != 0)
            {
                Raylib.SetMaterialTexture(&materials[i], MaterialMapIndex.Albedo, albedo);
                materials[i].Maps[(int)MaterialMapIndex.Albedo].Color = Color.White;
            }

            if (mra.Id != 0)
            {
                Raylib.SetMaterialTexture(&materials[i], MaterialMapIndex.Metalness, mra);
            }

            if (normal.Id != 0)
            {
                Raylib.SetMaterialTexture(&materials[i], MaterialMapIndex.Normal, normal);
            }

            if (emissive.Id != 0)
            {
                Raylib.SetMaterialTexture(&materials[i], MaterialMapIndex.Emission, emissive);
            }
        }

        if (normal.Id == 0)
        {
            return;
        }

        var meshes = model.Meshes;
        for (var i = 0; i < model.MeshCount; i++)
        {
            Raylib.GenMeshTangents(&meshes[i]);
        }
    }

    public void ApplyPbrDrawParams(
        bool useNormal,
        bool useMra,
        bool useEmissive,
        float metallic,
        float roughness,
        float emissivePower,
        Vector3 emissiveColor)
    {
        if (!IsLoaded)
        {
            return;
        }

        PushTextureUsage(useNormal, useMra, useEmissive);
        Raylib.SetShaderValue(_shader, _metallicValueLoc, metallic, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _roughnessValueLoc, roughness, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _emissivePowerLoc, emissivePower, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(
            _shader,
            _emissiveColorLoc,
            new Vector4(emissiveColor.X, emissiveColor.Y, emissiveColor.Z, 1f),
            ShaderUniformDataType.Vec4);
    }

    public void RestorePbrDrawDefaults()
    {
        if (!IsLoaded)
        {
            return;
        }

        SetPbrDefaults();
    }

    /// <summary>
    /// Replace the shared lighting shader with Raylib's default so
    /// <see cref="Raylib.UnloadModel"/> / <see cref="Raylib.UnloadMaterial"/> do not free it.
    /// </summary>
    public static void DetachFromModel(ModelHandle handle)
    {
        if (!handle.IsLoaded)
        {
            return;
        }

        var model = handle.Model;
        var defaultShader = CreateDefaultShader();
        for (var i = 0; i < model.MaterialCount; i++)
        {
            Raylib.SetMaterialShader(ref model, i, ref defaultShader);
        }
    }

    public static void DetachFromMaterial(ref Material material)
    {
        material.Shader = CreateDefaultShader();
    }

    public void Unload()
    {
        if (!IsLoaded)
        {
            _lights.Clear();
            return;
        }

        Raylib.UnloadShader(_shader);
        IsLoaded = false;
        _viewPosLoc = -1;
        _ambientLoc = -1;
        _ambientColorLoc = -1;
        _metallicValueLoc = -1;
        _roughnessValueLoc = -1;
        _aoValueLoc = -1;
        _emissivePowerLoc = -1;
        _emissiveColorLoc = -1;
        _tilingLoc = -1;
        _useTexAlbedoLoc = -1;
        _useTexNormalLoc = -1;
        _useTexMRALoc = -1;
        _useTexEmissiveLoc = -1;
        _lights.Clear();
    }

    public void Dispose() => Unload();

    private unsafe void BindPbrLocations()
    {
        _shader.Locs[(int)ShaderLocationIndex.MapAlbedo] = Raylib.GetShaderLocation(_shader, "albedoMap");
        _shader.Locs[(int)ShaderLocationIndex.MapMetalness] = Raylib.GetShaderLocation(_shader, "mraMap");
        _shader.Locs[(int)ShaderLocationIndex.MapNormal] = Raylib.GetShaderLocation(_shader, "normalMap");
        _shader.Locs[(int)ShaderLocationIndex.MapEmission] = Raylib.GetShaderLocation(_shader, "emissiveMap");
        _shader.Locs[(int)ShaderLocationIndex.ColorDiffuse] = Raylib.GetShaderLocation(_shader, "albedoColor");
        _shader.Locs[(int)ShaderLocationIndex.VectorView] = Raylib.GetShaderLocation(_shader, "viewPos");

        _viewPosLoc = _shader.Locs[(int)ShaderLocationIndex.VectorView];
        _ambientLoc = Raylib.GetShaderLocation(_shader, "ambient");
        _ambientColorLoc = Raylib.GetShaderLocation(_shader, "ambientColor");
        _metallicValueLoc = Raylib.GetShaderLocation(_shader, "metallicValue");
        _roughnessValueLoc = Raylib.GetShaderLocation(_shader, "roughnessValue");
        _aoValueLoc = Raylib.GetShaderLocation(_shader, "aoValue");
        _emissivePowerLoc = Raylib.GetShaderLocation(_shader, "emissivePower");
        _emissiveColorLoc = Raylib.GetShaderLocation(_shader, "emissiveColor");
        _tilingLoc = Raylib.GetShaderLocation(_shader, "tiling");
        _useTexAlbedoLoc = Raylib.GetShaderLocation(_shader, "useTexAlbedo");
        _useTexNormalLoc = Raylib.GetShaderLocation(_shader, "useTexNormal");
        _useTexMRALoc = Raylib.GetShaderLocation(_shader, "useTexMRA");
        _useTexEmissiveLoc = Raylib.GetShaderLocation(_shader, "useTexEmissive");
    }

    private void SetPbrDefaults()
    {
        var lightCountLoc = Raylib.GetShaderLocation(_shader, "numOfLights");
        Raylib.SetShaderValue(_shader, lightCountLoc, MaxLights, ShaderUniformDataType.Int);

        // Stock pbr.vs doubles UVs; tiling 0.5 restores authored texcoords 1:1.
        Raylib.SetShaderValue(_shader, _tilingLoc, new Vector2(0.5f, 0.5f), ShaderUniformDataType.Vec2);

        PushTextureUsage(useNormal: false, useMra: false, useEmissive: false);

        Raylib.SetShaderValue(_shader, _metallicValueLoc, 0f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _roughnessValueLoc, 0.5f, ShaderUniformDataType.Float);
        // Direct lighting is multiplied by aoValue when MRA is off; 0 would unlit the scene.
        Raylib.SetShaderValue(_shader, _aoValueLoc, 1f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _emissivePowerLoc, 0f, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _emissiveColorLoc, Vector4.Zero, ShaderUniformDataType.Vec4);

        var ambientColor = new Vector3(0.35f, 0.38f, 0.42f);
        Raylib.SetShaderValue(_shader, _ambientColorLoc, ambientColor, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _ambientLoc, 0.15f, ShaderUniformDataType.Float);
    }

    private static unsafe Shader CreateDefaultShader() =>
        new()
        {
            Id = Rlgl.GetShaderIdDefault(),
            Locs = Rlgl.GetShaderLocsDefault()
        };

    private void PushLightEnabled(SceneLight light)
    {
        Raylib.SetShaderValue(
            _shader,
            light.EnabledLoc,
            _lightingEnabled && light.Enabled ? 1 : 0,
            ShaderUniformDataType.Int);
    }

    private void PushTextureUsage(bool useNormal, bool useMra, bool useEmissive)
    {
        var maps = _pbrTexturesEnabled;
        Raylib.SetShaderValue(_shader, _useTexAlbedoLoc, maps ? 1 : 0, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_shader, _useTexNormalLoc, maps && useNormal ? 1 : 0, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_shader, _useTexMRALoc, maps && useMra ? 1 : 0, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_shader, _useTexEmissiveLoc, maps && useEmissive ? 1 : 0, ShaderUniformDataType.Int);
    }

    private SceneLight? AddLight(LightType type, Vector3 position, Vector3 target, Color color, float intensity)
    {
        if (!IsLoaded || _lights.Count >= MaxLights)
        {
            return null;
        }

        var index = _lights.Count;
        var light = new SceneLight
        {
            Type = type,
            Enabled = true,
            Position = position,
            Target = target,
            Color = color,
            Intensity = intensity,
            EnabledLoc = Raylib.GetShaderLocation(_shader, $"lights[{index}].enabled"),
            TypeLoc = Raylib.GetShaderLocation(_shader, $"lights[{index}].type"),
            PositionLoc = Raylib.GetShaderLocation(_shader, $"lights[{index}].position"),
            TargetLoc = Raylib.GetShaderLocation(_shader, $"lights[{index}].target"),
            ColorLoc = Raylib.GetShaderLocation(_shader, $"lights[{index}].color"),
            IntensityLoc = Raylib.GetShaderLocation(_shader, $"lights[{index}].intensity")
        };

        _lights.Add(light);
        UpdateLight(light);
        return light;
    }
}
