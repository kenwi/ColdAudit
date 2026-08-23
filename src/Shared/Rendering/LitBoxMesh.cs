using System.Numerics;
using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

/// <summary>
/// Unit cube submitted through <see cref="Raylib.DrawMesh"/> so the PBR shader receives
/// <c>matModel</c> and <c>albedoColor</c>. Immediate-mode <c>DrawCube</c> skips both and
/// shades gray.
/// </summary>
public sealed class LitBoxMesh : IDisposable
{
    private Mesh _cube;
    private Material _material;
    private bool _meshLoaded;
    private bool _materialLoaded;

    public bool IsLoaded => _meshLoaded && _materialLoaded;

    public void Load()
    {
        Unload();

        _cube = Raylib.GenMeshCube(1f, 1f, 1f);
        _meshLoaded = true;

        _material = Raylib.LoadMaterialDefault();
        _materialLoaded = true;
    }

    public void EnsureLighting(BasicLighting? lighting) =>
        lighting?.ApplyToMaterial(ref _material);

    public unsafe void Draw(Vector3 center, Vector3 size, float yawDegrees, Color color)
    {
        if (!IsLoaded)
        {
            return;
        }

        _material.Maps[(int)MaterialMapIndex.Albedo].Color = color;
        Raylib.DrawMesh(_cube, _material, ComposeTransform(center, yawDegrees, size));
    }

    public void Unload()
    {
        if (_materialLoaded)
        {
            BasicLighting.DetachFromMaterial(ref _material);
            Raylib.UnloadMaterial(_material);
            _materialLoaded = false;
        }

        if (_meshLoaded)
        {
            Raylib.UnloadMesh(_cube);
            _meshLoaded = false;
        }
    }

    public void Dispose() => Unload();

    // Raylib matrices use the opposite storage convention to System.Numerics.
    private static Matrix4x4 ComposeTransform(Vector3 position, float yawDegrees, Vector3 scale)
    {
        var matScale = Raymath.MatrixScale(scale.X, scale.Y, scale.Z);
        var matRotation = Raymath.MatrixRotate(Vector3.UnitY, yawDegrees * MathF.PI / 180f);
        var matTranslation = Raymath.MatrixTranslate(position.X, position.Y, position.Z);
        return Raymath.MatrixMultiply(Raymath.MatrixMultiply(matScale, matRotation), matTranslation);
    }
}
