using System.Numerics;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.Lighting;

/// <summary>
/// Owns the shared PBR lighting shader and instantiates the level's authored lights.
/// </summary>
public sealed class LightingFeature : FeatureBase
{
    private const float DebugSphereRadius = 0.25f;
    private const byte VolumeOutlineAlpha = 70;

    private GameWorld? _world;
    private BasicLighting? _lighting;
    private readonly List<AnimatedLight> _animatedLights = [];
    private float _elapsedSeconds;

    public override void Load(GameWorld world, EventBus events)
    {
        _world = world;

        _lighting = new BasicLighting();
        _lighting.Load();

        if (_lighting.IsLoaded)
        {
            AddAuthoredLights(world);
        }

        world.Lighting = _lighting;
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (input.TogglePbrTexturesPressed)
        {
            world.PbrTexturesEnabled = !world.PbrTexturesEnabled;
        }

        if (input.ToggleLightingPressed)
        {
            world.LightingEnabled = !world.LightingEnabled;
        }

        if (world.Lighting is not { IsLoaded: true } lighting)
        {
            return;
        }

        lighting.SetPbrTexturesEnabled(world.PbrTexturesEnabled);
        lighting.SetLightingEnabled(world.LightingEnabled);
        lighting.UpdateViewPosition(world.PlayerPosition);

        _elapsedSeconds += dt;
        AnimateLights(lighting);
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

            if (world.LightVolumeMaskEnabled)
            {
                DrawLightVolumes(world, light);
            }
        }

        Raylib.EndMode3D();
    }

    /// <summary>
    /// Outline of what <c>LightVisibilityFeature</c> lets this light reach: its room box
    /// plus the doorway shafts leaving that room.
    /// </summary>
    private static void DrawLightVolumes(GameWorld world, SceneLight light)
    {
        var graph = world.Sectors;
        if (string.IsNullOrEmpty(light.SectorId) ||
            !graph.TryGetBounds(light.SectorId, out var bounds))
        {
            return;
        }

        var outline = Fade(light.Color, VolumeOutlineAlpha);
        Raylib.DrawCubeWires(bounds.Center, bounds.Size.X, bounds.Size.Y, bounds.Size.Z, outline);

        foreach (var link in graph.LinksFrom(light.SectorId))
        {
            var opening = link.Opening;
            for (var i = 0; i < opening.Length; i++)
            {
                Raylib.DrawLine3D(opening[i], opening[(i + 1) % opening.Length], outline);
                Raylib.DrawLine3D(light.Position, opening[i], outline);
            }
        }
    }

    private static Color Fade(Color color, byte alpha) =>
        new(color.R, color.G, color.B, alpha);

    public override void Unload()
    {
        if (_world is not null && ReferenceEquals(_world.Lighting, _lighting))
        {
            _world.Lighting = null;
        }

        _lighting?.Dispose();
        _lighting = null;
        _animatedLights.Clear();
        _elapsedSeconds = 0f;
        _world = null;
    }

    private void AddAuthoredLights(GameWorld world)
    {
        _animatedLights.Clear();
        if (world.ActiveLevel is null)
        {
            return;
        }

        foreach (var def in world.ActiveLevel.Lights)
        {
            var anchor = Vector3.Zero;
            var hasAnchor = def.HasAnchor && TryGetPlacementPosition(world, def.AnchorPlacementId!, out anchor);

            var light = _lighting!.AddPointLight(
                hasAnchor ? OrbitPosition(def, anchor, 0f) : def.Position,
                def.Color,
                def.Intensity,
                def.SectorId);
            if (light is null)
            {
                // Shader is capped at BasicLighting.MaxLights; extra defs are ignored.
                break;
            }

            if (hasAnchor)
            {
                _animatedLights.Add(new AnimatedLight(def, light, anchor));
            }
        }
    }

    private void AnimateLights(BasicLighting lighting)
    {
        foreach (var animated in _animatedLights)
        {
            animated.Light.Position = OrbitPosition(animated.Def, animated.Anchor, _elapsedSeconds);
            lighting.UpdateLight(animated.Light);
        }
    }

    private static Vector3 OrbitPosition(LightDef def, Vector3 anchor, float elapsedSeconds)
    {
        var orbit = (def.OrbitPhaseDegrees + def.OrbitDegreesPerSecond * elapsedSeconds) * MathF.PI / 180f;
        var hover = (def.OrbitPhaseDegrees + def.HoverDegreesPerSecond * elapsedSeconds) * MathF.PI / 180f;
        var height = def.OrbitHeight + def.HoverAmplitude * MathF.Sin(hover);
        return anchor + new Vector3(
            MathF.Cos(orbit) * def.OrbitRadius,
            height,
            MathF.Sin(orbit) * def.OrbitRadius);
    }

    private static bool TryGetPlacementPosition(GameWorld world, string placementId, out Vector3 position)
    {
        position = default;
        if (world.ActiveLevel is null)
        {
            return false;
        }

        foreach (var placement in world.ActiveLevel.ModelPlacements)
        {
            if (placement.Id != placementId)
            {
                continue;
            }

            position = placement.Position;
            return true;
        }

        return false;
    }

    private sealed record AnimatedLight(LightDef Def, SceneLight Light, Vector3 Anchor);
}
