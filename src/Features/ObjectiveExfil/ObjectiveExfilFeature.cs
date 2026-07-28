using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;
using ColdAudit.Features.Inventory;

namespace ColdAudit.Features.ObjectiveExfil;

public sealed class ObjectiveExfilFeature : FeatureBase
{
    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        foreach (var taken in events.OfType<ObjectiveTaken>())
        {
            events.Publish(new ItemAcquired(taken.ItemId));
        }

        foreach (var ended in events.OfType<MissionEnded>())
        {
            world.MissionPhase = ended.Success ? MissionPhase.Won : MissionPhase.Lost;
            world.MissionMessage = ended.Reason;
        }

        // Exit volume check when level interactables exist.
        _ = InventoryFeature.Has(world, ItemId.DriveFinanceDr);
    }
}
