using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.Lighting;

/// <summary>
/// Owns the shared PBR lighting shader and default scene lights.
/// </summary>
public sealed class LightingFeature : FeatureBase
{
    private const float DebugSphereRadius = 0.25f;
    private const string CarPlacementId = "prop_old_car";
    private const float CarLightRadius = 3.5f;
    private const float CarLightHeight = 1.2f;
    private const float CarLightOrbitDegreesPerSecond = 20f;
    private const float CarLightHoverAmplitude = 0.7f;
    private const float CarLightHoverDegreesPerSecond = 90f;

    private GameWorld? _world;
    private BasicLighting? _lighting;
    private readonly List<SceneLight> _carRingLights = [];
    private Vector3 _carLightCenter;
    private float _carLightOrbitDegrees;
    private float _carLightHoverDegrees;

    public override void Load(GameWorld world, EventBus events)
    {
        _world = world;

        _lighting = new BasicLighting();
        _lighting.Load();

        if (_lighting.IsLoaded)
        {
            // Stock PBR shader treats every light as a point light (inverse-square).
            // Max 4 lights: keep a room fill and put RGB around the test car.
            _lighting.AddPointLight(
                new Vector3(0f, 3.5f, 2f),
                new Color(255, 245, 230, 255),
                intensity: 10f);
            AddCarRingLights(world);
        }

        world.Lighting = _lighting;
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        world.Lighting?.UpdateViewPosition(world.PlayerPosition);
        OrbitCarRingLights(dt);
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
        _carRingLights.Clear();
        _carLightOrbitDegrees = 0f;
        _carLightHoverDegrees = 0f;
        _world = null;
    }

    private void AddCarRingLights(GameWorld world)
    {
        _carRingLights.Clear();
        if (!TryGetCarPosition(world, out _carLightCenter))
        {
            return;
        }

        ReadOnlySpan<(Color Color, float Intensity)> ring =
        [
            (Color.Red, 5f),
            (Color.Green, 5f),
            (Color.Blue, 5f)
        ];

        for (var i = 0; i < ring.Length; i++)
        {
            var light = _lighting!.AddPointLight(
                CarRingPosition(i, ring.Length, 0f, 0f),
                ring[i].Color,
                ring[i].Intensity);
            if (light is not null)
            {
                _carRingLights.Add(light);
            }
        }
    }

    private void OrbitCarRingLights(float dt)
    {
        if (_lighting is not { IsLoaded: true } || _carRingLights.Count == 0)
        {
            return;
        }

        _carLightOrbitDegrees += dt * CarLightOrbitDegreesPerSecond;
        _carLightHoverDegrees += dt * CarLightHoverDegreesPerSecond;
        var count = _carRingLights.Count;
        for (var i = 0; i < count; i++)
        {
            var light = _carRingLights[i];
            light.Position = CarRingPosition(i, count, _carLightOrbitDegrees, _carLightHoverDegrees);
            _lighting.UpdateLight(light);
        }
    }

    private Vector3 CarRingPosition(int index, int count, float orbitDegrees, float hoverDegrees)
    {
        var step = 360f / count;
        var angle = (orbitDegrees + index * step) * MathF.PI / 180f;
        var hover = (hoverDegrees + index * step) * MathF.PI / 180f;
        var height = CarLightHeight + CarLightHoverAmplitude * MathF.Sin(hover);
        return _carLightCenter + new Vector3(
            MathF.Cos(angle) * CarLightRadius,
            height,
            MathF.Sin(angle) * CarLightRadius);
    }

    private static bool TryGetCarPosition(GameWorld world, out Vector3 position)
    {
        position = default;
        if (world.ActiveLevel is null)
        {
            return false;
        }

        foreach (var placement in world.ActiveLevel.ModelPlacements)
        {
            if (placement.Id != CarPlacementId)
            {
                continue;
            }

            position = placement.Position;
            return true;
        }

        return false;
    }
}
