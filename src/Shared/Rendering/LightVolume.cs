using System.Numerics;
using ColdAudit.Shared.Math;

namespace ColdAudit.Shared.Rendering;

/// <summary>
/// A convex volume stored as inward-facing planes (xyz = unit normal, w = offset).
/// A point is inside when <c>dot(normal, p) + w &gt;= 0</c> holds for every plane.
/// </summary>
public sealed class LightVolume
{
    /// <summary>
    /// Matches <c>VOLUME_PLANES</c> in <c>pbr.fs</c>. Six is exactly an AABB, or a
    /// four-sided portal shaft plus a far cap.
    /// </summary>
    public const int MaxPlanes = 6;

    private readonly Vector4[] _planes = new Vector4[MaxPlanes];

    public int PlaneCount { get; private set; }

    public ReadOnlySpan<Vector4> Planes => _planes.AsSpan(0, PlaneCount);

    public void Clear() => PlaneCount = 0;

    public bool TryAddPlane(Vector3 normal, float offset)
    {
        if (PlaneCount >= MaxPlanes)
        {
            return false;
        }

        _planes[PlaneCount++] = new Vector4(normal, offset);
        return true;
    }

    /// <summary>
    /// Add a plane whose inward side contains everything up to <paramref name="point"/>
    /// along <paramref name="outwardNormal"/>.
    /// </summary>
    public bool TryAddCap(Vector3 outwardNormal, Vector3 point)
    {
        var inward = -outwardNormal;
        return TryAddPlane(inward, -Vector3.Dot(inward, point));
    }

    /// <summary>
    /// Replace the volume with the six inward planes of an axis-aligned box,
    /// grown by <paramref name="expand"/> so neighbouring volumes overlap slightly.
    /// </summary>
    public void SetFromAabb(Aabb bounds, float expand = 0f)
    {
        var min = bounds.Min - new Vector3(expand);
        var max = bounds.Max + new Vector3(expand);

        PlaneCount = 0;
        TryAddPlane(Vector3.UnitX, -min.X);
        TryAddPlane(-Vector3.UnitX, max.X);
        TryAddPlane(Vector3.UnitY, -min.Y);
        TryAddPlane(-Vector3.UnitY, max.Y);
        TryAddPlane(Vector3.UnitZ, -min.Z);
        TryAddPlane(-Vector3.UnitZ, max.Z);
    }

    public void CopyPlanesFrom(ReadOnlySpan<Vector4> planes)
    {
        PlaneCount = 0;
        var count = System.Math.Min(planes.Length, MaxPlanes);
        for (var i = 0; i < count; i++)
        {
            _planes[PlaneCount++] = planes[i];
        }
    }

    public bool Contains(Vector3 point)
    {
        for (var i = 0; i < PlaneCount; i++)
        {
            var plane = _planes[i];
            if (plane.X * point.X + plane.Y * point.Y + plane.Z * point.Z + plane.W < 0f)
            {
                return false;
            }
        }

        return PlaneCount > 0;
    }
}

/// <summary>
/// The volumes one light can reach, with union semantics: the light illuminates a point
/// when the point is inside any volume. An empty set leaves the light unmasked.
/// </summary>
public sealed class LightVolumeSet
{
    /// <summary>
    /// Matches <c>MAX_LIGHT_VOLUMES</c> in <c>pbr.fs</c>. One volume for the light's own
    /// sector plus one shaft per doorway leaving it.
    /// </summary>
    public const int MaxVolumes = 4;

    private readonly LightVolume[] _volumes = CreateVolumes();

    public int VolumeCount { get; private set; }

    public void Clear() => VolumeCount = 0;

    /// <summary>
    /// Reserve and return the next volume, already cleared. Null when the set is full.
    /// </summary>
    public LightVolume? Add()
    {
        if (VolumeCount >= MaxVolumes)
        {
            return null;
        }

        var volume = _volumes[VolumeCount++];
        volume.Clear();
        return volume;
    }

    /// <summary>Drop the most recently added volume (it turned out to be degenerate).</summary>
    public void RemoveLast()
    {
        if (VolumeCount > 0)
        {
            VolumeCount--;
        }
    }

    public LightVolume this[int index] => _volumes[index];

    private static LightVolume[] CreateVolumes()
    {
        var volumes = new LightVolume[MaxVolumes];
        for (var i = 0; i < MaxVolumes; i++)
        {
            volumes[i] = new LightVolume();
        }

        return volumes;
    }
}
