using System.Numerics;
using Box3D;
using Box3D.Interop;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.Physics;

/// <summary>
/// Builds static Box3D colliders from placeholder sector AABBs: floors, perimeter walls
/// with portal cutouts, and portal floors bridging room gaps.
/// </summary>
internal static class LevelCollisionBuilder
{
    public const float FloorY = 0f;
    public const float FloorHalfThickness = 0.5f;
    public const float WallThickness = 0.3f;
    public const float WallHeight = 3f;

    private enum Face
    {
        NegX,
        PosX,
        NegZ,
        PosZ
    }

    public static int Build(Box3DWorld world, LevelData level)
    {
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < level.Sectors.Count; i++)
        {
            indexById[level.Sectors[i].Id] = i;
        }

        var openings = new Dictionary<(string SectorId, Face Face), List<(float Min, float Max)>>();
        var bodyCount = 0;

        foreach (var sector in level.Sectors)
        {
            AddFloor(world, sector.Bounds);
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
            AddFloor(world, portalBounds);
            bodyCount++;

            RegisterOpening(openings, portal.FromSectorId, level.Sectors[fromIndex].Bounds, portalBounds);
            RegisterOpening(openings, portal.ToSectorId, level.Sectors[toIndex].Bounds, portalBounds);
        }

        foreach (var sector in level.Sectors)
        {
            bodyCount += AddWalls(world, sector.Id, sector.Bounds, openings);
        }

        return bodyCount;
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
        Dictionary<(string, Face), List<(float, float)>> openings)
    {
        var count = 0;
        count += AddWallFace(world, bounds, Face.NegX, GetOpenings(openings, sectorId, Face.NegX));
        count += AddWallFace(world, bounds, Face.PosX, GetOpenings(openings, sectorId, Face.PosX));
        count += AddWallFace(world, bounds, Face.NegZ, GetOpenings(openings, sectorId, Face.NegZ));
        count += AddWallFace(world, bounds, Face.PosZ, GetOpenings(openings, sectorId, Face.PosZ));
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
        List<(float Min, float Max)> holes)
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

            AddWallSegment(world, bounds, face, segMin, segMax);
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

    private static void AddWallSegment(Box3DWorld world, Aabb bounds, Face face, float segMin, float segMax)
    {
        var halfH = WallHeight * 0.5f;
        var halfT = WallThickness * 0.5f;
        var mid = (segMin + segMax) * 0.5f;
        var halfLen = (segMax - segMin) * 0.5f;
        var y = FloorY + halfH;

        B3Pos center;
        float halfX;
        float halfZ;
        switch (face)
        {
            case Face.NegX:
                center = new B3Pos(bounds.Min.X - halfT, y, mid);
                halfX = halfT;
                halfZ = halfLen;
                break;
            case Face.PosX:
                center = new B3Pos(bounds.Max.X + halfT, y, mid);
                halfX = halfT;
                halfZ = halfLen;
                break;
            case Face.NegZ:
                center = new B3Pos(mid, y, bounds.Min.Z - halfT);
                halfX = halfLen;
                halfZ = halfT;
                break;
            default:
                center = new B3Pos(mid, y, bounds.Max.Z + halfT);
                halfX = halfLen;
                halfZ = halfT;
                break;
        }

        world.CreateStaticBody(center).AddBox(halfX, halfH, halfZ);
    }
}
