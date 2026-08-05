using System;
using Box3D.Interop;

namespace Box3D
{
    /// <summary>
    /// Character-mover solver helpers. The intended kinematic loop per move:
    /// 1. <see cref="Box3DWorld.CastMover"/> to advance the capsule with sliding,
    /// 2. <see cref="Box3DWorld.CollideMover"/> to gather contact planes,
    /// 3. <see cref="ToCollisionPlanes"/> + <see cref="SolvePlanes"/> to depenetrate,
    /// 4. <see cref="ClipVector"/> to clip velocity against the touched planes
    ///    (the Quake ClipVelocity operation).
    /// </summary>
    public static unsafe class Box3DMover
    {
        /// <summary>
        /// Convert gathered plane results into solver planes. Returns the number written
        /// (min of results and planes capacity).
        /// </summary>
        public static int ToCollisionPlanes(ReadOnlySpan<B3PlaneResult> results, Span<B3CollisionPlane> planes,
            float pushLimit = float.MaxValue, bool clipVelocity = true)
        {
            int count = Math.Min(results.Length, planes.Length);
            for (int i = 0; i < count; i++)
            {
                planes[i] = new B3CollisionPlane
                {
                    Plane = results[i].Plane,
                    PushLimit = pushLimit,
                    Push = 0f,
                    ClipVelocity = clipVelocity ? (byte)1 : (byte)0,
                };
            }

            return count;
        }

        /// <summary>
        /// Solve the mover position against the collision planes. Returns the recommended
        /// translation delta (targetDelta adjusted to respect the planes — with a zero
        /// target this is pure depenetration). Plane Push fields are written back.
        /// </summary>
        public static B3PlaneSolverResult SolvePlanes(B3Vec3 targetDelta, Span<B3CollisionPlane> planes)
        {
            fixed (B3CollisionPlane* p = planes)
            {
                return B3Api.b3SolvePlanes(targetDelta, p, planes.Length);
            }
        }

        /// <summary>Clip a vector (usually velocity) against every plane marked ClipVelocity.</summary>
        public static B3Vec3 ClipVector(B3Vec3 vector, ReadOnlySpan<B3CollisionPlane> planes)
        {
            fixed (B3CollisionPlane* p = planes)
            {
                return B3Api.b3ClipVector(vector, p, planes.Length);
            }
        }
    }
}
