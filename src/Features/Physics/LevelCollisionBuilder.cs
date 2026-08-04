using System.Numerics;
using Box3D;
using Box3D.Interop;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.Physics;

/// <summary>
/// Builds static Box3D colliders from placeholder sector AABBs: one continuous floor
/// (avoids portal seam hitches) plus perimeter walls with portal cutouts.
/// </summary>
internal static class LevelCollisionBuilder
{
    public const float FloorY = 0f;
    public const float FloorHalfThickness = 0.0f;
    public const float WallThickness = 0.0f;
    public const float WallHeight = 3f;

    private enum Face
    {
        NegX,
        PosX,
        NegZ,
        PosZ
    }

    public static int Build(
        Box3DWorld world,
        LevelData level,
        List<DebugWallQuad> walls,
        out Aabb floorBounds)
    {
        floorBounds = default;
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < level.Sectors.Count; i++)
        {
            indexById[level.Sectors[i].Id] = i;
        }

        var openings = new Dictionary<(string SectorId, Face Face), List<(float Min, float Max)>>();
        var bodyCount = 0;

        // Single floor over the union of sector + portal bounds so the capsule never
        // crosses coplanar box edges at doorway thresholds.
        if (TryComputeFloorBounds(level, indexById, out floorBounds))
        {
            AddFloor(world, floorBounds);
            bodyCount++;
        }

        foreach (var portal in level.Portals)
        {
            if (!indexById.TryGetValue(portal.FromSectorId, out var fromIndex) ||
                !indexById.TryGetValue(portal.ToSectorId, out var toIndex))
            {
                continue;
            }

            var portalBounds = DebugSectorLayout.PortalBounds(fromIndex, toIndex);
            RegisterOpening(openings, portal.FromSectorId, level.Sectors[fromIndex].Bounds, portalBounds);
            RegisterOpening(openings, portal.ToSectorId, level.Sectors[toIndex].Bounds, portalBounds);
            bodyCount += AddPortalSideWalls(world, portalBounds, walls);
        }

        foreach (var sector in level.Sectors)
        {
            bodyCount += AddWalls(world, sector.Id, sector.Bounds, openings, walls);
        }

        return bodyCount;
    }

    private static bool TryComputeFloorBounds(
        LevelData level,
        Dictionary<string, int> indexById,
        out Aabb floorBounds)
    {
        floorBounds = default;
        if (level.Sectors.Count == 0)
        {
            return false;
        }

        var min = level.Sectors[0].Bounds.Min;
        var max = level.Sectors[0].Bounds.Max;

        for (var i = 1; i < level.Sectors.Count; i++)
        {
            Expand(ref min, ref max, level.Sectors[i].Bounds);
        }

        foreach (var portal in level.Portals)
        {
            if (!indexById.TryGetValue(portal.FromSectorId, out var fromIndex) ||
                !indexById.TryGetValue(portal.ToSectorId, out var toIndex))
            {
                continue;
            }

            Expand(ref min, ref max, DebugSectorLayout.PortalBounds(fromIndex, toIndex));
        }

        // Keep the walkable surface at FloorY; sector Min.Y is only a volume bound.
        floorBounds = new Aabb(
            new Vector3(min.X, FloorY - FloorHalfThickness * 2f, min.Z),
            new Vector3(max.X, FloorY, max.Z));
        return true;
    }

    private static void Expand(ref Vector3 min, ref Vector3 max, Aabb bounds)
    {
        min = Vector3.Min(min, bounds.Min);
        max = Vector3.Max(max, bounds.Max);
    }

    private static void AddFloor(Box3DWorld world, Aabb bounds)
    {
        var size = bounds.Size;
        var center = new B3Pos(bounds.Center.X, FloorY - FloorHalfThickness, bounds.Center.Z);
        world.CreateStaticBody(center).AddBox(size.X * 0.5f, FloorHalfThickness, size.Z * 0.5f);
    }

    private static void RegisterOpening(
        Dictionary<(string, Face), List<(float, float)>> openings,
        string sectorId,
        Aabb sectorBounds,
        Aabb portalBounds)
    {
        var face = FaceToward(sectorBounds.Center, portalBounds.Center);
        var (min, max) = face is Face.NegX or Face.PosX
            ? (portalBounds.Min.Z, portalBounds.Max.Z)
            : (portalBounds.Min.X, portalBounds.Max.X);

        var key = (sectorId, face);
        if (!openings.TryGetValue(key, out var list))
        {
            list = [];
            openings[key] = list;
        }

        list.Add((min, max));
    }

    /// <summary>
    /// Corridor side walls along the portal's long edges (the short-length sides of the
    /// footprint) so you cannot strafe out of bounds while inside the gap.
    /// </summary>
    private static int AddPortalSideWalls(Box3DWorld world, Aabb portalBounds, List<DebugWallQuad> walls)
    {
        var size = portalBounds.Size;
        if (size.X <= size.Z)
        {
            // Corridor runs along X (short depth); walls on ±Z spanning the short sides.
            AddWallSegment(world, portalBounds, Face.NegZ, portalBounds.Min.X, portalBounds.Max.X, walls);
            AddWallSegment(world, portalBounds, Face.PosZ, portalBounds.Min.X, portalBounds.Max.X, walls);
        }
        else
        {
            // Corridor runs along Z; walls on ±X spanning the short sides.
            AddWallSegment(world, portalBounds, Face.NegX, portalBounds.Min.Z, portalBounds.Max.Z, walls);
            AddWallSegment(world, portalBounds, Face.PosX, portalBounds.Min.Z, portalBounds.Max.Z, walls);
        }

        return 2;
    }

    private static Face FaceToward(Vector3 from, Vector3 toward)
    {
        var delta = toward - from;
        return System.MathF.Abs(delta.X) >= System.MathF.Abs(delta.Z)
            ? (delta.X >= 0f ? Face.PosX : Face.NegX)
            : (delta.Z >= 0f ? Face.PosZ : Face.NegZ);
    }

    private static int AddWalls(
        Box3DWorld world,
        string sectorId,
        Aabb bounds,
        Dictionary<(string, Face), List<(float, float)>> openings,
        List<DebugWallQuad> walls)
    {
        var count = 0;
        count += AddWallFace(world, bounds, Face.NegX, GetOpenings(openings, sectorId, Face.NegX), walls);
        count += AddWallFace(world, bounds, Face.PosX, GetOpenings(openings, sectorId, Face.PosX), walls);
        count += AddWallFace(world, bounds, Face.NegZ, GetOpenings(openings, sectorId, Face.NegZ), walls);
        count += AddWallFace(world, bounds, Face.PosZ, GetOpenings(openings, sectorId, Face.PosZ), walls);
        return count;
    }

    private static List<(float Min, float Max)> GetOpenings(
        Dictionary<(string, Face), List<(float, float)>> openings,
        string sectorId,
        Face face) =>
        openings.TryGetValue((sectorId, face), out var list) ? list : [];

    private static int AddWallFace(
        Box3DWorld world,
        Aabb bounds,
        Face face,
        List<(float Min, float Max)> holes,
        List<DebugWallQuad> walls)
    {
        float axisMin;
        float axisMax;
        if (face is Face.NegX or Face.PosX)
        {
            axisMin = bounds.Min.Z;
            axisMax = bounds.Max.Z;
        }
        else
        {
            axisMin = bounds.Min.X;
            axisMax = bounds.Max.X;
        }

        var segments = SubtractHoles(axisMin, axisMax, holes);
        var count = 0;
        foreach (var (segMin, segMax) in segments)
        {
            var length = segMax - segMin;
            if (length < 0.05f)
            {
                continue;
            }

            AddWallSegment(world, bounds, face, segMin, segMax, walls);
            count++;
        }

        return count;
    }

    private static List<(float Min, float Max)> SubtractHoles(
        float axisMin,
        float axisMax,
        List<(float Min, float Max)> holes)
    {
        if (holes.Count == 0)
        {
            return [(axisMin, axisMax)];
        }

        var ordered = holes.OrderBy(h => h.Min).ToList();
        var result = new List<(float Min, float Max)>();
        var cursor = axisMin;
        foreach (var (holeMin, holeMax) in ordered)
        {
            var clippedMin = System.Math.Max(holeMin, axisMin);
            var clippedMax = System.Math.Min(holeMax, axisMax);
            if (clippedMax <= clippedMin)
            {
                continue;
            }

            if (clippedMin > cursor + 0.01f)
            {
                result.Add((cursor, clippedMin));
            }

            cursor = System.Math.Max(cursor, clippedMax);
        }

        if (cursor < axisMax - 0.01f)
        {
            result.Add((cursor, axisMax));
        }

        return result;
    }

    private static void AddWallSegment(
        Box3DWorld world,
        Aabb bounds,
        Face face,
        float segMin,
        float segMax,
        List<DebugWallQuad> walls)
    {
        var halfH = WallHeight * 0.5f;
        var halfT = WallThickness * 0.5f;
        var mid = (segMin + segMax) * 0.5f;
        var halfLen = (segMax - segMin) * 0.5f;
        var y = FloorY + halfH;
        var y0 = FloorY;
        var y1 = FloorY + WallHeight;

        B3Pos center;
        float halfX;
        float halfZ;
        DebugWallQuad quad;
        switch (face)
        {
            case Face.NegX:
            {
                var x = bounds.Min.X - halfT;
                center = new B3Pos(x, y, mid);
                halfX = halfT;
                halfZ = halfLen;
                quad = new DebugWallQuad(
                    new Vector3(x, y0, segMin),
                    new Vector3(x, y0, segMax),
                    new Vector3(x, y1, segMax),
                    new Vector3(x, y1, segMin));
                break;
            }
            case Face.PosX:
            {
                var x = bounds.Max.X + halfT;
                center = new B3Pos(x, y, mid);
                halfX = halfT;
                halfZ = halfLen;
                quad = new DebugWallQuad(
                    new Vector3(x, y0, segMin),
                    new Vector3(x, y0, segMax),
                    new Vector3(x, y1, segMax),
                    new Vector3(x, y1, segMin));
                break;
            }
            case Face.NegZ:
            {
                var z = bounds.Min.Z - halfT;
                center = new B3Pos(mid, y, z);
                halfX = halfLen;
                halfZ = halfT;
                quad = new DebugWallQuad(
                    new Vector3(segMin, y0, z),
                    new Vector3(segMax, y0, z),
                    new Vector3(segMax, y1, z),
                    new Vector3(segMin, y1, z));
                break;
            }
            default:
            {
                var z = bounds.Max.Z + halfT;
                center = new B3Pos(mid, y, z);
                halfX = halfLen;
                halfZ = halfT;
                quad = new DebugWallQuad(
                    new Vector3(segMin, y0, z),
                    new Vector3(segMax, y0, z),
                    new Vector3(segMax, y1, z),
                    new Vector3(segMin, y1, z));
                break;
            }
        }

        walls.Add(quad);
        world.CreateStaticBody(center).AddBox(halfX, halfH, halfZ);
    }
}
