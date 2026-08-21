using System.Numerics;

namespace ColdAudit.Shared.Math;

/// <summary>
/// View / portal frustum. Planes face inward (positive half-space is inside).
/// Camera frustums use 6 planes; portal frustums use eye-edge planes (+ optional near/far).
/// </summary>
public sealed class Frustum
{
    private const int MaxPlanes = 16;
    private const int MaxClipVerts = 32;

    private readonly Vector4[] _planes = new Vector4[MaxPlanes];
    private int _planeCount;

    private readonly Vector3[] _clipA = new Vector3[MaxClipVerts];
    private readonly Vector3[] _clipB = new Vector3[MaxClipVerts];

    public int PlaneCount => _planeCount;

    /// <summary>Inward-facing planes (xyz = normal, w = offset).</summary>
    public ReadOnlySpan<Vector4> Planes => _planes.AsSpan(0, _planeCount);

    public void CopyFrom(Frustum other)
    {
        _planeCount = other._planeCount;
        for (var i = 0; i < _planeCount; i++)
        {
            _planes[i] = other._planes[i];
        }
    }

    public void UpdateFromCamera(
        Vector3 position,
        Vector3 forward,
        Vector3 up,
        float fovYDegrees,
        float aspect,
        float near,
        float far)
    {
        if (aspect < 1e-4f)
        {
            aspect = 1f;
        }

        forward = Vector3.Normalize(forward);
        up = Vector3.Normalize(up);
        var target = position + forward;
        var view = Matrix4x4.CreateLookAt(position, target, up);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(
            MathUtil.DegToRad(fovYDegrees),
            aspect,
            near,
            far);
        var vp = view * proj;

        _planeCount = 6;
        _planes[0] = NormalizePlane(new Vector4(
            vp.M14 + vp.M11,
            vp.M24 + vp.M21,
            vp.M34 + vp.M31,
            vp.M44 + vp.M41)); // left
        _planes[1] = NormalizePlane(new Vector4(
            vp.M14 - vp.M11,
            vp.M24 - vp.M21,
            vp.M34 - vp.M31,
            vp.M44 - vp.M41)); // right
        _planes[2] = NormalizePlane(new Vector4(
            vp.M14 + vp.M12,
            vp.M24 + vp.M22,
            vp.M34 + vp.M32,
            vp.M44 + vp.M42)); // bottom
        _planes[3] = NormalizePlane(new Vector4(
            vp.M14 - vp.M12,
            vp.M24 - vp.M22,
            vp.M34 - vp.M32,
            vp.M44 - vp.M42)); // top
        _planes[4] = NormalizePlane(new Vector4(
            vp.M14 + vp.M13,
            vp.M24 + vp.M23,
            vp.M34 + vp.M33,
            vp.M44 + vp.M43)); // near
        _planes[5] = NormalizePlane(new Vector4(
            vp.M14 - vp.M13,
            vp.M24 - vp.M23,
            vp.M34 - vp.M33,
            vp.M44 - vp.M43)); // far
    }

    public bool IntersectsAabb(Aabb aabb)
    {
        for (var i = 0; i < _planeCount; i++)
        {
            var plane = _planes[i];
            var nx = plane.X;
            var ny = plane.Y;
            var nz = plane.Z;

            var x = nx >= 0f ? aabb.Max.X : aabb.Min.X;
            var y = ny >= 0f ? aabb.Max.Y : aabb.Min.Y;
            var z = nz >= 0f ? aabb.Max.Z : aabb.Min.Z;

            if (nx * x + ny * y + nz * z + plane.W < 0f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Clip a portal polygon against this frustum. Returns false if nothing remains.
    /// </summary>
    public bool TryClipPolygon(ReadOnlySpan<Vector3> input, Span<Vector3> output, out int outputCount)
    {
        outputCount = 0;
        if (input.Length < 3 || output.Length < 3)
        {
            return false;
        }

        var count = System.Math.Min(input.Length, MaxClipVerts);
        for (var i = 0; i < count; i++)
        {
            _clipA[i] = input[i];
        }

        var src = _clipA;
        var dst = _clipB;
        var srcCount = count;

        for (var p = 0; p < _planeCount; p++)
        {
            srcCount = ClipAgainstPlane(_planes[p], src, srcCount, dst);
            if (srcCount < 3)
            {
                return false;
            }

            (src, dst) = (dst, src);
        }

        outputCount = System.Math.Min(srcCount, output.Length);
        for (var i = 0; i < outputCount; i++)
        {
            output[i] = src[i];
        }

        return outputCount >= 3;
    }

    /// <summary>
    /// Build a child frustum whose sides pass through <paramref name="eye"/> and each
    /// edge of the (already clipped) portal polygon. Copies near/far from parent when present.
    /// </summary>
    public bool TrySetFromEyeAndPortal(
        Vector3 eye,
        ReadOnlySpan<Vector3> portalPolygon,
        Frustum parent)
    {
        if (portalPolygon.Length < 3)
        {
            return false;
        }

        var centroid = Vector3.Zero;
        for (var i = 0; i < portalPolygon.Length; i++)
        {
            centroid += portalPolygon[i];
        }

        centroid /= portalPolygon.Length;

        // Reject portals mostly behind the eye.
        var toPortal = centroid - eye;
        if (toPortal.LengthSquared() < 1e-8f)
        {
            return false;
        }

        _planeCount = 0;
        for (var i = 0; i < portalPolygon.Length; i++)
        {
            var a = portalPolygon[i];
            var b = portalPolygon[(i + 1) % portalPolygon.Length];
            var edge0 = a - eye;
            var edge1 = b - eye;
            var normal = Vector3.Cross(edge0, edge1);
            if (normal.LengthSquared() < 1e-10f)
            {
                continue;
            }

            normal = Vector3.Normalize(normal);
            // Inward: centroid should be on the positive side.
            if (Vector3.Dot(normal, centroid - eye) < 0f)
            {
                normal = -normal;
            }

            if (_planeCount >= MaxPlanes)
            {
                break;
            }

            _planes[_planeCount++] = new Vector4(normal, -Vector3.Dot(normal, eye));
        }

        if (_planeCount < 3)
        {
            return false;
        }

        // Keep parent near/far when available (camera frustums store them at 4/5).
        if (parent._planeCount >= 6)
        {
            if (_planeCount + 2 <= MaxPlanes)
            {
                _planes[_planeCount++] = parent._planes[4];
                _planes[_planeCount++] = parent._planes[5];
            }
        }

        return true;
    }

    private static int ClipAgainstPlane(Vector4 plane, Vector3[] src, int srcCount, Vector3[] dst)
    {
        var dstCount = 0;
        if (srcCount == 0)
        {
            return 0;
        }

        var prev = src[srcCount - 1];
        var prevDist = Distance(plane, prev);
        var prevInside = prevDist >= -1e-5f;

        for (var i = 0; i < srcCount; i++)
        {
            var curr = src[i];
            var currDist = Distance(plane, curr);
            var currInside = currDist >= -1e-5f;

            if (currInside)
            {
                if (!prevInside)
                {
                    dst[dstCount++] = IntersectEdge(plane, prev, curr, prevDist, currDist);
                    if (dstCount >= MaxClipVerts)
                    {
                        return dstCount;
                    }
                }

                dst[dstCount++] = curr;
            }
            else if (prevInside)
            {
                dst[dstCount++] = IntersectEdge(plane, prev, curr, prevDist, currDist);
            }

            if (dstCount >= MaxClipVerts)
            {
                return dstCount;
            }

            prev = curr;
            prevDist = currDist;
            prevInside = currInside;
        }

        return dstCount;
    }

    private static float Distance(Vector4 plane, Vector3 point) =>
        plane.X * point.X + plane.Y * point.Y + plane.Z * point.Z + plane.W;

    private static Vector3 IntersectEdge(Vector4 plane, Vector3 a, Vector3 b, float distA, float distB)
    {
        var denom = distA - distB;
        var t = System.MathF.Abs(denom) < 1e-8f ? 0f : distA / denom;
        t = System.Math.Clamp(t, 0f, 1f);
        return a + (b - a) * t;
    }

    private static Vector4 NormalizePlane(Vector4 plane)
    {
        var length = MathF.Sqrt(plane.X * plane.X + plane.Y * plane.Y + plane.Z * plane.Z);
        if (length < 1e-8f)
        {
            return plane;
        }

        var inv = 1f / length;
        return new Vector4(plane.X * inv, plane.Y * inv, plane.Z * inv, plane.W * inv);
    }
}
