using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.Workstations;

public enum WorkstationActionKind
{
    DisableCamera,
    RevealCode,
    StartExfilCopy
}

public sealed class WorkstationsFeature : FeatureBase
{
    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        foreach (var use in events.OfType<UseRequested>())
        {
            _ = use;
            // CCTV PC / console / patch port handling.
        }
    }
}
