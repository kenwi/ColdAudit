using System.Numerics;

namespace ColdAudit.Shared.Math;

public sealed class Frustum
{
    // Placeholder for portal/frustum culling. Planes filled by SectorVisibility later.
    public Vector4[] Planes { get; } = new Vector4[6];

    public void UpdateFromCamera(Vector3 position, Vector3 forward, Vector3 up, float fovYDegrees, float aspect, float near, float far)
    {
        // Intentionally empty stub - implemented with SectorVisibility.
        _ = (position, forward, up, fovYDegrees, aspect, near, far);
    }

    public bool IntersectsAabb(Aabb aabb)
    {
        _ = aabb;
        return true;
    }
}
