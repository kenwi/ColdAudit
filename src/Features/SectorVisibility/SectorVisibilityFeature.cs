using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.SectorVisibility;

public sealed class SectorVisibilityState
{
    public HashSet<string> Visible { get; } = new(StringComparer.Ordinal);
}

public sealed class SectorVisibilityFeature : FeatureBase
{
    private readonly SectorVisibilityState _state = new();
    private readonly Dictionary<string, int> _sectorIndexById = new(StringComparer.Ordinal);

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

        if (!string.IsNullOrEmpty(world.CurrentSectorId))
        {
            AddSectorAndNeighbors(level, world.CurrentSectorId);
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

            AddSectorAndNeighbors(level, portal.FromSectorId);
            AddSectorAndNeighbors(level, portal.ToSectorId);
        }
    }

    private void AddSectorAndNeighbors(LevelData level, string sectorId)
    {
        _state.Visible.Add(sectorId);

        foreach (var portal in level.Portals)
        {
            if (portal.FromSectorId == sectorId)
            {
                _state.Visible.Add(portal.ToSectorId);
            }

            if (portal.TwoWay && portal.ToSectorId == sectorId)
            {
                _state.Visible.Add(portal.FromSectorId);
            }
        }
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
