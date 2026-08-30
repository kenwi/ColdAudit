using System.Numerics;
using ColdAudit.Features.LevelLoad;
using Raylib_cs;

namespace ColdAudit.Features.Inventory;

/// <summary>Display data for a carried item in the inventory HUD.</summary>
public readonly record struct ItemVisual(
    string Label,
    Color Color,
    Vector3 BoxSize,
    string? ModelPath);

public static class ItemVisualCatalog
{
    private static readonly Color ChipGold = new(212, 175, 55, 255);
    private static readonly Vector3 KeycardSize = new(0.18f, 0.012f, 0.115f);

    public static Color KeycardChipColor => ChipGold;

    public static ItemVisual Resolve(string itemId, LevelData? level)
    {
        var visual = Builtin(itemId);
        if (level is null)
        {
            return visual;
        }

        foreach (var card in level.Keycards)
        {
            if (!string.Equals(card.ItemId, itemId, StringComparison.Ordinal))
            {
                continue;
            }

            var color = card.Color.A == 0 ? visual.Color : card.Color;
            var size = new Vector3(card.Width, card.Height, card.Depth);
            var path = card.HasModel ? card.ModelPath : visual.ModelPath;
            return visual with { Color = color, BoxSize = size, ModelPath = path };
        }

        foreach (var pickup in level.Pickups)
        {
            if (!string.Equals(pickup.ItemId, itemId, StringComparison.Ordinal))
            {
                continue;
            }

            var color = pickup.Color.A == 0 ? visual.Color : pickup.Color;
            var size = new Vector3(pickup.Width, pickup.Height, pickup.Depth);
            var path = pickup.HasModel ? pickup.ModelPath : visual.ModelPath;
            return visual with { Color = color, BoxSize = size, ModelPath = path };
        }

        return visual;
    }

    public static bool IsKeycard(string itemId) =>
        itemId is ItemId.KeycardBlue or ItemId.KeycardOrange or ItemId.KeycardGreen
            or ItemId.KeycardPurple or ItemId.KeycardRed;

    private static ItemVisual Builtin(string itemId) => itemId switch
    {
        ItemId.KeycardBlue => new("Blue", new(36, 92, 188, 255), KeycardSize, null),
        ItemId.KeycardOrange => new("Orange", new(212, 96, 28, 255), KeycardSize, null),
        ItemId.KeycardGreen => new("Green", new(42, 148, 78, 255), KeycardSize, null),
        ItemId.KeycardPurple => new("Purple", new(128, 58, 188, 255), KeycardSize, null),
        ItemId.KeycardRed => new("Red", new(196, 48, 58, 255), KeycardSize, null),
        ItemId.BadgeSpare => new("Badge", new(200, 180, 60, 255), new(0.12f, 0.01f, 0.08f), null),
        ItemId.DriveFinanceDr => new("USB", new(48, 48, 52, 255), new(0.06f, 0.02f, 0.1f), null),
        _ => new(itemId, new(140, 140, 140, 255), new(0.08f, 0.08f, 0.08f), null)
    };
}
