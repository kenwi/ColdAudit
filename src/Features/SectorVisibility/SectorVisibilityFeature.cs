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

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (input.ToggleSectorCullPressed)
        {
            world.SectorCullEnabled = !world.SectorCullEnabled;
        }

        ResolveCurrentSector(world);
        ResolveVisibleSectors(world);

        world.VisibleSectorIds.Clear();
        foreach (var id in _state.Visible)
        {
            world.VisibleSectorIds.Add(id);
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

        if (string.IsNullOrEmpty(world.CurrentSectorId))
        {
            return;
        }

        var current = world.CurrentSectorId;
        _state.Visible.Add(current);

        foreach (var portal in level.Portals)
        {
            if (portal.FromSectorId == current)
            {
                _state.Visible.Add(portal.ToSectorId);
            }

            if (portal.TwoWay && portal.ToSectorId == current)
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
