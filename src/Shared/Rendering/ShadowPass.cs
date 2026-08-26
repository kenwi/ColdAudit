using System.Numerics;
using ColdAudit.Shared.Assets;
using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

/// <summary>
/// Depth-only submission context for one shadow map face. Owns the depth shader and the
/// material every caster is drawn with, so casters never see their own materials during a
/// shadow pass.
/// </summary>
/// <remarks>
/// All geometry must go through the <c>DrawMesh</c> path: Raylib only uploads
/// <c>matModel</c> for mesh draws, and the depth shader needs it to compute a world-space
/// distance to the light. Immediate-mode Rlgl draws would render with a garbage model
/// matrix, so box casters use <see cref="DrawBox"/> instead.
/// </remarks>
public sealed class ShadowPass : IDisposable
{
    private Shader _shader;
    private Material _material;
    private Mesh _unitCube;
    private bool _materialLoaded;
    private bool _unitCubeLoaded;
    private int _lightPositionLoc = -1;
    private int _farPlaneLoc = -1;

    private readonly HashSet<string> _casterSectorIds = new(StringComparer.Ordinal);

    public bool IsLoaded { get; private set; }

    public bool Load()
    {
        Unload();

        var vs = ShaderCatalog.ShadowDepthVertexPath;
        var fs = ShaderCatalog.ShadowDepthFragmentPath;
        if (!File.Exists(vs) || !File.Exists(fs))
        {
            return false;
        }

        _shader = Raylib.LoadShader(vs, fs);
        if (!Raylib.IsShaderValid(_shader))
        {
            return false;
        }

        _lightPositionLoc = Raylib.GetShaderLocation(_shader, "lightPosition");
        _farPlaneLoc = Raylib.GetShaderLocation(_shader, "farPlane");

        _material = Raylib.LoadMaterialDefault();
        _material.Shader = _shader;
        _materialLoaded = true;

        _unitCube = Raylib.GenMeshCube(1f, 1f, 1f);
        _unitCubeLoaded = true;

        IsLoaded = true;
        return true;
    }

    public void Unload()
    {
        if (_unitCubeLoaded)
        {
            Raylib.UnloadMesh(_unitCube);
            _unitCubeLoaded = false;
        }

        if (_materialLoaded)
        {
            // UnloadMaterial frees non-default shaders; the shader is unloaded separately.
            BasicLighting.DetachFromMaterial(ref _material);
            Raylib.UnloadMaterial(_material);
            _materialLoaded = false;
        }

        if (IsLoaded)
        {
            Raylib.UnloadShader(_shader);
        }

        _lightPositionLoc = -1;
        _farPlaneLoc = -1;
        _casterSectorIds.Clear();
        IsLoaded = false;
    }

    public void Dispose() => Unload();

    /// <summary>
    /// Bind the depth shader and set the per-light uniforms shared by all six faces.
    /// </summary>
    public void BeginLight(Vector3 lightPosition, float farPlane, IReadOnlyCollection<string> casterSectorIds)
    {
        _casterSectorIds.Clear();
        foreach (var id in casterSectorIds)
        {
            _casterSectorIds.Add(id);
        }

        if (!IsLoaded)
        {
            return;
        }

        Raylib.SetShaderValue(_shader, _lightPositionLoc, lightPosition, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _farPlaneLoc, farPlane, ShaderUniformDataType.Float);
    }

    /// <summary>
    /// True when geometry in this sector can occlude the current light. Sector-less geometry
    /// (empty id) always casts.
    /// </summary>
    public bool IncludesSector(string sectorId) =>
        string.IsNullOrEmpty(sectorId) || _casterSectorIds.Contains(sectorId);

    /// <summary>Draw a model at the level origin, matching <c>Raylib.DrawModel</c> at scale 1.</summary>
    public void DrawModel(Model model) =>
        DrawModel(model, Vector3.Zero, 0f, 1f);

    /// <summary>Draw a model with the same transform composition as <c>Raylib.DrawModelEx</c>.</summary>
    public void DrawModel(Model model, Vector3 position, float yawDegrees, float scale)
    {
        if (!IsLoaded)
        {
            return;
        }

        var transform = ComposeTransform(position, yawDegrees, 0f, new Vector3(scale, scale, scale));
        DrawMeshes(model, Raymath.MatrixMultiply(model.Transform, transform));
    }

    /// <summary>Box caster for geometry that is normally drawn in immediate mode.</summary>
    public void DrawBox(Vector3 center, Vector3 size, float yawDegrees) =>
        DrawBox(center, size, yawDegrees, 0f);

    public void DrawBox(Vector3 center, Vector3 size, float yawDegrees, float pitchDegrees)
    {
        if (!IsLoaded || !_unitCubeLoaded)
        {
            return;
        }

        Raylib.DrawMesh(_unitCube, _material, ComposeTransform(center, yawDegrees, pitchDegrees, size));
    }

    public void DrawMesh(Mesh mesh, Matrix4x4 transform)
    {
        if (!IsLoaded)
        {
            return;
        }

        Raylib.DrawMesh(mesh, _material, transform);
    }

    private unsafe void DrawMeshes(Model model, Matrix4x4 transform)
    {
        var meshes = model.Meshes;
        for (var i = 0; i < model.MeshCount; i++)
        {
            Raylib.DrawMesh(meshes[i], _material, transform);
        }
    }

    // Raylib matrices use the opposite storage convention to System.Numerics, so compose
    // with Raymath rather than Matrix4x4.CreateTranslation and friends.
    // DrawMesh applies v * M, so S * Pitch * Yaw * T = local pitch then world yaw.
    private static Matrix4x4 ComposeTransform(
        Vector3 position,
        float yawDegrees,
        float pitchDegrees,
        Vector3 scale)
    {
        var matScale = Raymath.MatrixScale(scale.X, scale.Y, scale.Z);
        var matPitch = Raymath.MatrixRotate(Vector3.UnitX, -pitchDegrees * MathF.PI / 180f);
        var matYaw = Raymath.MatrixRotate(Vector3.UnitY, yawDegrees * MathF.PI / 180f);
        var matRotation = Raymath.MatrixMultiply(matPitch, matYaw);
        var matTranslation = Raymath.MatrixTranslate(position.X, position.Y, position.Z);
        return Raymath.MatrixMultiply(Raymath.MatrixMultiply(matScale, matRotation), matTranslation);
    }
}
