using System.Numerics;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.LevelProps;

/// <summary>
/// Loads and draws positioned model assets declared on the active level.
/// </summary>
public sealed class LevelPropsFeature : FeatureBase
{
    private readonly Dictionary<string, ModelHandle> _handlesByPath = new(StringComparer.Ordinal);

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
            _handlesByPath[placement.ModelPath] = handle;
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

            var scale = placement.Scale;
            Raylib.DrawModelEx(
                handle.Model,
                placement.Position,
                Vector3.UnitY,
                placement.YawDegrees,
                new Vector3(scale, scale, scale),
                Color.White);
        }

        Raylib.EndMode3D();
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
}
