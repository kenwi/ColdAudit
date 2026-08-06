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
    public const float PortalHeight = 3f;

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

    public static Aabb PortalBounds(int fromIndex, int toIndex)
    {
        var from = Origin(fromIndex);
        var to = Origin(toIndex);
        var center = (from + to) * 0.5f;
        var delta = to - from;

        float halfX;
        float halfZ;
        if (System.MathF.Abs(delta.X) >= System.MathF.Abs(delta.Z))
        {
            halfX = PortalGap * 0.5f;
            halfZ = PortalWidth * 0.5f;
        }
        else
        {
            halfX = PortalWidth * 0.5f;
            halfZ = PortalGap * 0.5f;
        }

        return new Aabb(
            new Vector3(center.X - halfX, -1f, center.Z - halfZ),
            new Vector3(center.X + halfX, 4f, center.Z + halfZ));
    }

    /// <summary>
    /// Doorway rectangle in the middle of the portal gap (4 corners).
    /// Winding faces from <paramref name="fromIndex"/> toward <paramref name="toIndex"/>.
    /// </summary>
    public static void GetPortalOpening(int fromIndex, int toIndex, Span<Vector3> corners)
    {
        if (corners.Length < 4)
        {
            throw new ArgumentException("Portal opening needs 4 corners.", nameof(corners));
        }

        var from = Origin(fromIndex);
        var to = Origin(toIndex);
        var center = (from + to) * 0.5f;
        var delta = to - from;
        var halfW = PortalWidth * 0.5f;
        const float y0 = 0f;
        const float y1 = PortalHeight;

        if (System.MathF.Abs(delta.X) >= System.MathF.Abs(delta.Z))
        {
            var z0 = center.Z - halfW;
            var z1 = center.Z + halfW;
            if (delta.X >= 0f)
            {
                corners[0] = new Vector3(center.X, y0, z0);
                corners[1] = new Vector3(center.X, y0, z1);
                corners[2] = new Vector3(center.X, y1, z1);
                corners[3] = new Vector3(center.X, y1, z0);
            }
            else
            {
                corners[0] = new Vector3(center.X, y0, z1);
                corners[1] = new Vector3(center.X, y0, z0);
                corners[2] = new Vector3(center.X, y1, z0);
                corners[3] = new Vector3(center.X, y1, z1);
            }
        }
        else
        {
            var x0 = center.X - halfW;
            var x1 = center.X + halfW;
            if (delta.Z >= 0f)
            {
                corners[0] = new Vector3(x0, y0, center.Z);
                corners[1] = new Vector3(x1, y0, center.Z);
                corners[2] = new Vector3(x1, y1, center.Z);
                corners[3] = new Vector3(x0, y1, center.Z);
            }
            else
            {
                corners[0] = new Vector3(x1, y0, center.Z);
                corners[1] = new Vector3(x0, y0, center.Z);
                corners[2] = new Vector3(x0, y1, center.Z);
                corners[3] = new Vector3(x1, y1, center.Z);
            }
        }
    }
}
