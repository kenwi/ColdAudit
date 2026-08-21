using System.Numerics;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;

namespace ColdAudit.Shared.World;

/// <summary>
/// Resolves sector/portal volumes and doorway openings from authored mesh paths
/// (or explicit <see cref="PortalDef.Corners"/>), falling back to
/// <see cref="DebugSectorLayout"/> for placeholder levels.
/// </summary>
internal static class AuthoredLevelGeometry
{
    private const float EmptyBoundsEpsilon = 0.01f;
    private const float FlatMeshHeightEpsilon = 0.1f;

    public static Aabb ResolveSectorBounds(SectorDef sector, int sectorIndex)
    {
        if (HasMeaningfulXz(sector.Bounds))
        {
            return sector.Bounds;
        }

        if (TryMeshAabb(sector.ModelPath, out var fromModel) ||
            TryMeshAabb(sector.CollisionMeshPath, out fromModel))
        {
            return EnsureVerticalExtent(fromModel);
        }

        return DebugSectorLayout.Bounds(sectorIndex);
    }

    public static Aabb ResolvePortalBounds(
        PortalDef portal,
        int fromIndex,
        int toIndex,
        Aabb fromSectorBounds,
        Aabb toSectorBounds)
    {
        if (portal.Corners.Length >= 3)
        {
            return EnsureVerticalExtent(AabbFromPoints(portal.Corners));
        }

        if (TryMeshAabb(portal.ModelPath, out var fromModel) ||
            TryMeshAabb(portal.CollisionMeshPath, out fromModel))
        {
            return EnsureVerticalExtent(fromModel);
        }

        if (fromSectorBounds.Size.LengthSquared() > 0f && toSectorBounds.Size.LengthSquared() > 0f)
        {
            // No portal mesh: approximate the gap between authored sector volumes.
            return EnsureVerticalExtent(AabbBetweenCenters(fromSectorBounds, toSectorBounds));
        }

        return DebugSectorLayout.PortalBounds(fromIndex, toIndex);
    }

    public static bool TryWritePortalOpening(
        PortalDef portal,
        int fromIndex,
        int toIndex,
        Aabb portalBounds,
        Aabb fromSectorBounds,
        Aabb toSectorBounds,
        Span<Vector3> corners)
    {
        if (corners.Length < 4)
        {
            return false;
        }

        if (portal.Corners.Length >= 3)
        {
            var count = System.Math.Min(portal.Corners.Length, corners.Length);
            for (var i = 0; i < count; i++)
            {
                corners[i] = portal.Corners[i];
            }

            if (count >= 4)
            {
                return true;
            }

            if (count == 3 && corners.Length >= 4)
            {
                corners[3] = portal.Corners[0];
                return true;
            }

            return false;
        }

        if (portal.HasModel || portal.HasCollisionMesh || HasMeaningfulXz(portalBounds))
        {
            WriteOpeningFromBounds(portalBounds, fromSectorBounds.Center, toSectorBounds.Center, corners);
            return true;
        }

        DebugSectorLayout.GetPortalOpening(fromIndex, toIndex, corners);
        return true;
    }

    private static void WriteOpeningFromBounds(
        Aabb portalBounds,
        Vector3 fromCenter,
        Vector3 toCenter,
        Span<Vector3> corners)
    {
        var center = portalBounds.Center;
        var size = portalBounds.Size;
        var y0 = portalBounds.Min.Y;
        var y1 = portalBounds.Max.Y;
        if (y1 - y0 < FlatMeshHeightEpsilon)
        {
            y0 = 0f;
            y1 = DebugSectorLayout.PortalHeight;
        }

        var delta = toCenter - fromCenter;
        // Doorway faces along the stronger from→to axis (mesh footprint can be nearly square).
        var faceAlongX = System.MathF.Abs(delta.X) >= System.MathF.Abs(delta.Z);

        if (faceAlongX)
        {
            var halfW = System.MathF.Max(size.Z * 0.5f, 0.5f);
            var z0 = center.Z - halfW;
            var z1 = center.Z + halfW;
            var faceTowardTo = delta.X >= 0f;
            if (faceTowardTo)
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
            var halfW = System.MathF.Max(size.X * 0.5f, 0.5f);
            var x0 = center.X - halfW;
            var x1 = center.X + halfW;
            var faceTowardTo = delta.Z >= 0f;
            if (faceTowardTo)
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

    private static Aabb AabbBetweenCenters(Aabb from, Aabb to)
    {
        var a = from.Center;
        var b = to.Center;
        var center = (a + b) * 0.5f;
        var delta = b - a;
        float halfX;
        float halfZ;
        if (System.MathF.Abs(delta.X) >= System.MathF.Abs(delta.Z))
        {
            halfX = DebugSectorLayout.PortalGap * 0.5f;
            halfZ = DebugSectorLayout.PortalWidth * 0.5f;
        }
        else
        {
            halfX = DebugSectorLayout.PortalWidth * 0.5f;
            halfZ = DebugSectorLayout.PortalGap * 0.5f;
        }

        return new Aabb(
            new Vector3(center.X - halfX, 0f, center.Z - halfZ),
            new Vector3(center.X + halfX, DebugSectorLayout.PortalHeight, center.Z + halfZ));
    }

    private static Aabb EnsureVerticalExtent(Aabb bounds)
    {
        if (bounds.Max.Y - bounds.Min.Y >= FlatMeshHeightEpsilon)
        {
            return bounds;
        }

        return new Aabb(
            new Vector3(bounds.Min.X, 0f, bounds.Min.Z),
            new Vector3(bounds.Max.X, DebugSectorLayout.PortalHeight, bounds.Max.Z));
    }

    private static bool HasMeaningfulXz(Aabb bounds) =>
        bounds.Size.X > EmptyBoundsEpsilon || bounds.Size.Z > EmptyBoundsEpsilon;

    private static Aabb AabbFromPoints(IReadOnlyList<Vector3> points)
    {
        var min = points[0];
        var max = points[0];
        for (var i = 1; i < points.Count; i++)
        {
            min = Vector3.Min(min, points[i]);
            max = Vector3.Max(max, points[i]);
        }

        return new Aabb(min, max);
    }

    private static bool TryMeshAabb(string? path, out Aabb aabb)
    {
        aabb = default;
        if (string.IsNullOrWhiteSpace(path) || !ModelTriangleSoup.TryLoad(path, out var soup))
        {
            return false;
        }

        if (soup.Vertices.Length == 0)
        {
            return false;
        }

        var min = soup.Vertices[0];
        var max = soup.Vertices[0];
        for (var i = 1; i < soup.Vertices.Length; i++)
        {
            min = Vector3.Min(min, soup.Vertices[i]);
            max = Vector3.Max(max, soup.Vertices[i]);
        }

        aabb = new Aabb(min, max);
        return HasMeaningfulXz(aabb);
    }
}
