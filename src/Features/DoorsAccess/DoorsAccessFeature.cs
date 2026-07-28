using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.DoorsAccess;

public sealed class DoorState
{
    public string Id { get; init; } = string.Empty;
    public bool Locked { get; set; } = true;
    public bool IsOpen { get; set; }
    public float OpenAmount { get; set; }
}

public sealed class DoorsAccessFeature : FeatureBase
{
    private readonly List<DoorState> _doors = [];

    public override void Load(GameWorld world, EventBus events)
    {
        _doors.Clear();
        // Placeholder doors wired when LevelLoad exposes interactables.
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        foreach (var use in events.OfType<UseRequested>())
        {
            _ = use;
            // Handle door / badge reader uses here.
        }
    }
}
