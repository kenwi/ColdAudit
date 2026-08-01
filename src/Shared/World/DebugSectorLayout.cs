using System.Numerics;
using ColdAudit.Shared.Math;

namespace ColdAudit.Shared.World;

/// <summary>
/// Placeholder grid used until authored sector transforms/bounds exist.
/// </summary>
public static class DebugSectorLayout
{
    public const float Spacing = 14f;
    public const float Extent = 12f;
    public const float PortalGap = Spacing - Extent;
    public const float PortalWidth = 4f;

    public static Vector3 Origin(int index) =>
        new((index % 2) * Spacing, 0f, (index / 2) * Spacing);

    public static Aabb Bounds(int index)
    {
        var origin = Origin(index);
        var half = Extent * 0.5f;
        return new Aabb(
            new Vector3(origin.X - half, -1f, origin.Z - half),
            new Vector3(origin.X + half, 4f, origin.Z + half));
    }
}
