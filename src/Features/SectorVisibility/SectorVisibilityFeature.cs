using System.Numerics;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.SectorVisibility;

public sealed class SectorVisibilityState
{
    public HashSet<string> Visible { get; } = new(StringComparer.Ordinal);
}

public sealed class SectorVisibilityFeature : FeatureBase
{
    private const float CameraFovYDegrees = 70f;
    private const float CameraNear = 0.05f;
    private const float CameraFar = 250f;

    private readonly SectorVisibilityState _state = new();
    private readonly Dictionary<string, int> _sectorIndexById = new(StringComparer.Ordinal);
    private readonly Frustum _frustum = new();
    private readonly Queue<string> _frontier = new();
    private readonly List<(string OtherSectorId, Aabb PortalBounds)> _portalScratch = [];

    public override void Load(GameWorld world, EventBus events)
    {
        RebuildSectorIndex(world.ActiveLevel);
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (input.ToggleSectorCullPressed)
        {
            world.SectorCullEnabled = !world.SectorCullEnabled;
        }

        if (_sectorIndexById.Count == 0)
        {
            RebuildSectorIndex(world.ActiveLevel);
        }

        ResolveCurrentSector(world);
        ResolveVisibleSectors(world);

        world.VisibleSectorIds.Clear();
        foreach (var id in _state.Visible)
        {
            world.VisibleSectorIds.Add(id);
        }
    }

    public override void Unload()
    {
        _sectorIndexById.Clear();
        _state.Visible.Clear();
        _frontier.Clear();
        _portalScratch.Clear();
    }

    private void RebuildSectorIndex(LevelData? level)
    {
        _sectorIndexById.Clear();
        if (level is null)
        {
            return;
        }

        for (var i = 0; i < level.Sectors.Count; i++)
        {
            _sectorIndexById[level.Sectors[i].Id] = i;
        }
    }

    private void ResolveVisibleSectors(GameWorld world)
    {
        _state.Visible.Clear();
        _frontier.Clear();

        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        if (!world.SectorCullEnabled)
        {
            foreach (var sector in level.Sectors)
            {
                _state.Visible.Add(sector.Id);
            }

            return;
        }

        // Seed: current room always includes its immediate portal neighbors.
        if (!string.IsNullOrEmpty(world.CurrentSectorId))
        {
            SeedSectorAndNeighbors(level, world.CurrentSectorId);
        }

        // Standing on a portal uses the same neighbour expansion as being in either end room.
        foreach (var portal in level.Portals)
        {
            if (!_sectorIndexById.TryGetValue(portal.FromSectorId, out var fromIndex) ||
                !_sectorIndexById.TryGetValue(portal.ToSectorId, out var toIndex))
            {
                continue;
            }

            if (!DebugSectorLayout.PortalBounds(fromIndex, toIndex).ContainsXz(world.PlayerPosition))
            {
                continue;
            }

            SeedSectorAndNeighbors(level, portal.FromSectorId);
            SeedSectorAndNeighbors(level, portal.ToSectorId);
        }

        if (_state.Visible.Count == 0)
        {
            return;
        }

        // Recurse through further portals that are actually in the camera frustum
        // (e.g. see through room_b into rooms beyond while standing in room_a).
        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);
        var aspect = ResolveAspect();
        _frustum.UpdateFromCamera(
            world.PlayerPosition,
            forward,
            Vector3.UnitY,
            CameraFovYDegrees,
            aspect,
            CameraNear,
            CameraFar);

        foreach (var id in _state.Visible)
        {
            _frontier.Enqueue(id);
        }

        while (_frontier.Count > 0)
        {
            var sectorId = _frontier.Dequeue();
            CollectPortalsFrom(level, sectorId, _portalScratch);
            foreach (var (otherId, portalBounds) in _portalScratch)
            {
                if (_state.Visible.Contains(otherId))
                {
                    continue;
                }

                if (!_frustum.IsAabbPotentiallyVisible(portalBounds, world.PlayerPosition, forward))
                {
                    continue;
                }

                _state.Visible.Add(otherId);
                _frontier.Enqueue(otherId);
            }
        }
    }

    private void SeedSectorAndNeighbors(LevelData level, string sectorId)
    {
        _state.Visible.Add(sectorId);

        CollectPortalsFrom(level, sectorId, _portalScratch);
        foreach (var (otherId, _) in _portalScratch)
        {
            _state.Visible.Add(otherId);
        }
    }

    private void CollectPortalsFrom(
        LevelData level,
        string sectorId,
        List<(string OtherSectorId, Aabb PortalBounds)> into)
    {
        into.Clear();
        foreach (var portal in level.Portals)
        {
            string? other = null;
            if (portal.FromSectorId == sectorId)
            {
                other = portal.ToSectorId;
            }
            else if (portal.TwoWay && portal.ToSectorId == sectorId)
            {
                other = portal.FromSectorId;
            }

            if (other is null)
            {
                continue;
            }

            if (!_sectorIndexById.TryGetValue(portal.FromSectorId, out var fromIndex) ||
                !_sectorIndexById.TryGetValue(portal.ToSectorId, out var toIndex))
            {
                continue;
            }

            into.Add((other, DebugSectorLayout.PortalBounds(fromIndex, toIndex)));
        }
    }

    private static float ResolveAspect()
    {
        var width = Raylib.GetScreenWidth();
        var height = Raylib.GetScreenHeight();
        if (width <= 0 || height <= 0)
        {
            return UiFramebuffer.Width / (float)UiFramebuffer.Height;
        }

        return width / (float)height;
    }

    private static void ResolveCurrentSector(GameWorld world)
    {
        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        foreach (var sector in level.Sectors)
        {
            if (sector.Bounds.ContainsXz(world.PlayerPosition))
            {
                world.CurrentSectorId = sector.Id;
                return;
            }
        }

        // Keep last sector while crossing portal gaps / outside bounds.
    }
}
