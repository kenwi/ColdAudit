using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.Lighting;

/// <summary>
/// Owns the shared basic lighting shader and default scene lights.
/// </summary>
public sealed class LightingFeature : FeatureBase
{
    private const float DebugSphereRadius = 0.25f;

    private GameWorld? _world;
    private BasicLighting? _lighting;

    public override void Load(GameWorld world, EventBus events)
    {
        _world = world;

        _lighting = new BasicLighting();
        _lighting.Load();

        if (_lighting.IsLoaded)
        {
            // Soft key light from above/front-right; cool fill for the office look.
            _lighting.AddDirectionalLight(
                new Vector3(4f, 8f, 2f),
                Vector3.Zero,
                new Color(230, 235, 245, 255));
            _lighting.AddPointLight(new Vector3(0f, 3.5f, 2f), new Color(255, 245, 230, 255));
        }

        world.Lighting = _lighting;
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        world.Lighting?.UpdateViewPosition(world.PlayerPosition);
    }

    public override void Draw(GameWorld world)
    {
        if (world.DebugDraw == DebugDrawMode.Off ||
            world.Lighting is not { IsLoaded: true } lighting ||
            lighting.Lights.Count == 0)
        {
            return;
        }

        Raylib.BeginMode3D(world.Draw.Camera);

        foreach (var light in lighting.Lights)
        {
            if (!light.Enabled)
            {
                continue;
            }

            Raylib.DrawSphereWires(light.Position, DebugSphereRadius, 12, 12, light.Color);
        }

        Raylib.EndMode3D();
    }

    public override void Unload()
    {
        if (_world is not null && ReferenceEquals(_world.Lighting, _lighting))
        {
            _world.Lighting = null;
        }

        _lighting?.Dispose();
        _lighting = null;
        _world = null;
    }
}
