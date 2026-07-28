using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.WorldRender;

public sealed class WorldRenderFeature : FeatureBase
{
    private Camera3D _camera;

    public override void Load(GameWorld world, EventBus events)
    {
        _camera = new Camera3D
        {
            Position = world.PlayerPosition,
            Target = world.PlayerPosition + Vector3.UnitZ,
            Up = Vector3.UnitY,
            FovY = 70f,
            Projection = CameraProjection.Perspective
        };
    }

    public override void Draw(GameWorld world)
    {
        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);
        _camera.Position = world.PlayerPosition;
        _camera.Target = world.PlayerPosition + forward;

        Raylib.BeginMode3D(_camera);

        // Placeholder floor/grid until sector meshes load.
        Raylib.DrawPlane(Vector3.Zero, new Vector2(40f, 40f), new Color(28, 32, 40, 255));
        Raylib.DrawCube(new Vector3(0f, 1f, 4f), 1f, 2f, 0.2f, new Color(70, 90, 120, 255));
        Raylib.DrawCube(new Vector3(4f, 1f, 0f), 0.2f, 2f, 1f, new Color(70, 90, 120, 255));

        Raylib.EndMode3D();
    }
}
