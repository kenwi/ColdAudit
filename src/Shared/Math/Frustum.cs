using System.Numerics;

namespace ColdAudit.Shared.Math;

/// <summary>
/// View frustum built from a first-person camera. Planes face inward
/// (positive half-space is inside the frustum).
/// </summary>
public sealed class Frustum
{
    private readonly Vector4[] _planes = new Vector4[6];

    public IReadOnlyList<Vector4> Planes => _planes;

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

        // Extract inward-facing planes from the view-projection matrix (row-major).
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
        for (var i = 0; i < _planes.Length; i++)
        {
            var plane = _planes[i];
            var nx = plane.X;
            var ny = plane.Y;
            var nz = plane.Z;

            // Corner farthest along the plane normal (p-vertex).
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
    /// True when the AABB is at least partly in front of the camera and inside the frustum.
    /// Rejects portals wholly behind the eye even if the near plane test is soft.
    /// </summary>
    public bool IsAabbPotentiallyVisible(Aabb aabb, Vector3 eye, Vector3 forward)
    {
        var toCenter = aabb.Center - eye;
        // Allow a little behind the eye plane so near doorways still count.
        if (Vector3.Dot(toCenter, forward) < -aabb.Size.Length() * 0.5f)
        {
            return false;
        }

        return IntersectsAabb(aabb);
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
