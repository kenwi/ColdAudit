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
        ResolveCurrentSector(world);

        // Stub: mark current sector visible. Portal flood lands next.
        _state.Visible.Clear();
        if (!string.IsNullOrEmpty(world.CurrentSectorId))
        {
            _state.Visible.Add(world.CurrentSectorId);
        }

        world.VisibleSectorIds.Clear();
        foreach (var id in _state.Visible)
        {
            world.VisibleSectorIds.Add(id);
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
