using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.Inventory;

public static class ItemId
{
    public const string BadgeSpare = "badge_spare";
    public const string DriveFinanceDr = "drive_finance_dr";
    public const string Keycard = "keycard";
}

public sealed class InventoryFeature : FeatureBase
{
    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        foreach (var acquired in events.OfType<ItemAcquired>())
        {
            world.CarriedItemIds.Add(acquired.ItemId);
        }
    }

    public static bool Has(GameWorld world, string itemId) =>
        world.CarriedItemIds.Contains(itemId);
}
