using System.Numerics;
using ColdAudit.Shared.Math;
using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

/// <summary>
/// Shared per-frame view state. One player camera is synced by
/// <c>WorldRenderFeature</c>; 3D drawers use <see cref="Camera"/>.
/// </summary>
public sealed class DrawContext
{
    public const float FovYDegrees = 70f;

    public Camera3D Camera { get; set; }
    public bool DrawDebug { get; set; }

    public DrawContext()
    {
        Camera = CreatePlayerCamera(Vector3.Zero, Vector3.UnitZ);
    }

    public void SyncFromPlayer(Vector3 position, float yaw, float pitch)
    {
        var forward = MathUtil.ForwardFromYawPitch(yaw, pitch);
        Camera = CreatePlayerCamera(position, position + forward);
    }

    private static Camera3D CreatePlayerCamera(Vector3 position, Vector3 target) => new()
    {
        Position = position,
        Target = target,
        Up = Vector3.UnitY,
        FovY = FovYDegrees,
        Projection = CameraProjection.Perspective
    };
}
