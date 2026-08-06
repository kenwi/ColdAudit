using System.Numerics;
using ColdAudit.Shared.Assets;
using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

/// <summary>
/// Shared Raylib basic lighting shader (directional + point, up to 4 lights).
/// </summary>
public sealed class BasicLighting : IDisposable
{
    public const int MaxLights = 4;

    private Shader _shader;
    private int _viewPosLoc = -1;
    private int _ambientLoc = -1;
    private readonly List<SceneLight> _lights = [];

    public bool IsLoaded { get; private set; }
    public IReadOnlyList<SceneLight> Lights => _lights;

    public void Load()
    {
        Unload();

        var vs = ShaderCatalog.LightingVertexPath;
        var fs = ShaderCatalog.LightingFragmentPath;
        if (!File.Exists(vs) || !File.Exists(fs))
        {
            return;
        }

        _shader = Raylib.LoadShader(vs, fs);
        if (!Raylib.IsShaderValid(_shader))
        {
            return;
        }

        _viewPosLoc = Raylib.GetShaderLocation(_shader, "viewPos");
        _ambientLoc = Raylib.GetShaderLocation(_shader, "ambient");

        var ambient = new Vector4(0.35f, 0.38f, 0.42f, 1f);
        Raylib.SetShaderValue(_shader, _ambientLoc, ambient, ShaderUniformDataType.Vec4);

        IsLoaded = true;
    }

    public SceneLight? AddDirectionalLight(Vector3 position, Vector3 target, Color color)
    {
        return AddLight(LightType.Directional, position, target, color);
    }

    public SceneLight? AddPointLight(Vector3 position, Color color)
    {
        return AddLight(LightType.Point, position, Vector3.Zero, color);
    }

    public void UpdateLight(SceneLight light)
    {
        if (!IsLoaded)
        {
            return;
        }

        Raylib.SetShaderValue(_shader, light.EnabledLoc, light.Enabled ? 1 : 0, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_shader, light.TypeLoc, (int)light.Type, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_shader, light.PositionLoc, light.Position, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, light.TargetLoc, light.Target, ShaderUniformDataType.Vec3);

        var color = new Vector4(
            light.Color.R / 255f,
            light.Color.G / 255f,
            light.Color.B / 255f,
            light.Color.A / 255f);
        Raylib.SetShaderValue(_shader, light.ColorLoc, color, ShaderUniformDataType.Vec4);
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
        _lights.Clear();
    }

    public void Dispose() => Unload();

    private SceneLight? AddLight(LightType type, Vector3 position, Vector3 target, Color color)
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
            EnabledLoc = Raylib.GetShaderLocation(_shader, $"lights[{index}].enabled"),
            TypeLoc = Raylib.GetShaderLocation(_shader, $"lights[{index}].type"),
            PositionLoc = Raylib.GetShaderLocation(_shader, $"lights[{index}].position"),
            TargetLoc = Raylib.GetShaderLocation(_shader, $"lights[{index}].target"),
            ColorLoc = Raylib.GetShaderLocation(_shader, $"lights[{index}].color")
        };

        _lights.Add(light);
        UpdateLight(light);
        return light;
    }
}
