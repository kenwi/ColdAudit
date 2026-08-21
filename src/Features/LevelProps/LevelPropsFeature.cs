using System.Numerics;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.Time;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.LevelProps;

/// <summary>
/// Loads and draws positioned model assets declared on the active level.
/// </summary>
public sealed class LevelPropsFeature : FeatureBase, IShadowCaster
{
    private readonly Dictionary<string, ModelHandle> _handlesByPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BoundPbrMaps> _pbrMapsByPath = new(StringComparer.Ordinal);

    public override void Load(GameWorld world, EventBus events)
    {
        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        foreach (var placement in level.ModelPlacements)
        {
            if (string.IsNullOrWhiteSpace(placement.ModelPath))
            {
                continue;
            }

            if (_handlesByPath.ContainsKey(placement.ModelPath))
            {
                continue;
            }

            if (!File.Exists(placement.ModelPath))
            {
                continue;
            }

            var handle = new ModelHandle();
            handle.Load(placement.ModelPath);
            world.Lighting?.ApplyToModel(handle);
            if (placement.PbrMaps is not null)
            {
                var bound = BindPlacementPbrMaps(world, handle, placement.PbrMaps);
                if (bound is not null)
                {
                    _pbrMapsByPath[placement.ModelPath] = bound;
                }
            }

            _handlesByPath[placement.ModelPath] = handle;
        }
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        foreach (var placement in level.ModelPlacements)
        {
            if (placement.YawSpeedDegrees != 0f)
            {
                // Spinning props keep moving, so their shadows can never be cached.
                world.InvalidateShadowGeometry();
                return;
            }
        }
    }

    public override void Draw(GameWorld world)
    {
        var level = world.ActiveLevel;
        if (level is null || level.ModelPlacements.Count == 0)
        {
            return;
        }

        Raylib.BeginMode3D(world.Draw.Camera);

        foreach (var placement in level.ModelPlacements)
        {
            if (!IsPlacementDrawn(world, placement))
            {
                continue;
            }

            if (!_handlesByPath.TryGetValue(placement.ModelPath, out var handle) || !handle.IsLoaded)
            {
                continue;
            }

            var appliedPbr = false;
            if (world.Lighting is { IsLoaded: true } lighting &&
                _pbrMapsByPath.TryGetValue(placement.ModelPath, out var pbrMaps))
            {
                lighting.ApplyPbrDrawParams(
                    pbrMaps.HasNormal,
                    pbrMaps.HasMra,
                    pbrMaps.HasEmissive,
                    pbrMaps.Maps.Metallic,
                    pbrMaps.Maps.Roughness,
                    pbrMaps.Maps.EmissivePower,
                    pbrMaps.Maps.EmissiveColor);
                appliedPbr = true;
            }

            var scale = placement.Scale;
            var yaw = placement.YawDegrees + FrameTime.Total * placement.YawSpeedDegrees;
            Raylib.DrawModelEx(
                handle.Model,
                placement.Position,
                Vector3.UnitY,
                yaw,
                new Vector3(scale, scale, scale),
                Color.White);

            if (appliedPbr)
            {
                world.Lighting!.RestorePbrDrawDefaults();
            }
        }

        Raylib.EndMode3D();
    }

    public void DrawDepth(GameWorld world, ShadowPass pass)
    {
        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        foreach (var placement in level.ModelPlacements)
        {
            if (!pass.IncludesSector(placement.SectorId))
            {
                continue;
            }

            if (!_handlesByPath.TryGetValue(placement.ModelPath, out var handle) || !handle.IsLoaded)
            {
                continue;
            }

            var yaw = placement.YawDegrees + FrameTime.Total * placement.YawSpeedDegrees;
            pass.DrawModel(handle.Model, placement.Position, yaw, placement.Scale);
        }
    }

    public override void Unload()
    {
        foreach (var handle in _handlesByPath.Values)
        {
            // UnloadModel frees non-default material shaders; detach shared lighting first.
            BasicLighting.DetachFromModel(handle);
            handle.Dispose();
        }

        _handlesByPath.Clear();
        _pbrMapsByPath.Clear();
    }

    private static BoundPbrMaps? BindPlacementPbrMaps(GameWorld world, ModelHandle handle, ModelPbrMapsDef maps)
    {
        if (world.Lighting is not { IsLoaded: true } lighting)
        {
            return null;
        }

        var albedo = TryLoadMap(maps.AlbedoPath);
        var mra = TryLoadMap(maps.MraPath);
        var normal = TryLoadMap(maps.NormalPath);
        var emissive = TryLoadMap(maps.EmissivePath);
        lighting.BindPbrMaps(handle, albedo, mra, normal, emissive);
        return new BoundPbrMaps
        {
            Maps = maps,
            HasMra = mra.Id != 0,
            HasNormal = normal.Id != 0,
            HasEmissive = emissive.Id != 0
        };
    }

    private static Texture2D TryLoadMap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return default;
        }

        return Raylib.LoadTexture(path);
    }

    private static bool IsPlacementDrawn(GameWorld world, ModelPlacementDef placement)
    {
        if (string.IsNullOrEmpty(placement.SectorId))
        {
            return true;
        }

        if (!world.SectorCullEnabled)
        {
            return true;
        }

        return world.VisibleSectorIds.Contains(placement.SectorId);
    }

    private sealed class BoundPbrMaps
    {
        public required ModelPbrMapsDef Maps { get; init; }
        public bool HasMra { get; init; }
        public bool HasNormal { get; init; }
        public bool HasEmissive { get; init; }
    }
}
