using System.Numerics;

namespace ColdAudit.Shared.Math;

public readonly record struct Aabb(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;

    public bool Contains(Vector3 point) =>
        point.X >= Min.X && point.X <= Max.X &&
        point.Y >= Min.Y && point.Y <= Max.Y &&
        point.Z >= Min.Z && point.Z <= Max.Z;

    public bool ContainsXz(Vector3 point) =>
        point.X >= Min.X && point.X <= Max.X &&
        point.Z >= Min.Z && point.Z <= Max.Z;

    /// <summary>
    /// Ray vs AABB. <paramref name="tHit"/> is the entry distance along a
    /// normalized direction (0 when the origin is inside).
    /// </summary>
    public bool TryIntersectRay(Vector3 origin, Vector3 direction, out float tHit)
    {
        tHit = 0f;
        var tMin = 0f;
        var tMax = float.PositiveInfinity;

        if (!ClipAxis(origin.X, direction.X, Min.X, Max.X, ref tMin, ref tMax) ||
            !ClipAxis(origin.Y, direction.Y, Min.Y, Max.Y, ref tMin, ref tMax) ||
            !ClipAxis(origin.Z, direction.Z, Min.Z, Max.Z, ref tMin, ref tMax))
        {
            return false;
        }

        tHit = tMin;
        return true;
    }

    private static bool ClipAxis(
        float origin,
        float direction,
        float min,
        float max,
        ref float tMin,
        ref float tMax)
    {
        const float epsilon = 1e-8f;
        if (MathF.Abs(direction) < epsilon)
        {
            return origin >= min && origin <= max;
        }

        var inv = 1f / direction;
        var t0 = (min - origin) * inv;
        var t1 = (max - origin) * inv;
        if (t0 > t1)
        {
            (t0, t1) = (t1, t0);
        }

        tMin = MathF.Max(tMin, t0);
        tMax = MathF.Min(tMax, t1);
        return tMin <= tMax && tMax >= 0f;
    }
}
