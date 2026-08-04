using Box3D.Interop;

namespace Box3D
{
    /// <summary>
    /// Safe wrappers over box3d's standalone geometry casts — pure functions against a single
    /// shape, no world required. This is the toolkit for stateless hit-testing: pose a primitive,
    /// cast a ray or a swept sphere against it, get fraction/point/normal back. Because no world
    /// state is involved, these are thread-safe, deterministic, and ideal for lag-compensated
    /// hit validation against historical (rewound) shape poses.
    ///
    /// All inputs share one frame: the shape's own. Callers put the shape and the cast in the
    /// same space (commonly world space, or shape-local for precision far from the origin).
    /// </summary>
    public static unsafe class Box3DGeometry
    {
        /// <summary>Ray cast against a capsule. Returns true on hit.</summary>
        public static bool RayCastCapsule(in B3Capsule capsule, B3Vec3 origin, B3Vec3 translation, out B3CastOutput hit)
        {
            var input = new B3RayCastInput { Origin = origin, Translation = translation, MaxFraction = 1f };
            hit = B3Api.b3RayCastCapsule(in capsule, in input);
            return hit.Hit != 0;
        }

        /// <summary>Ray cast against a sphere. Returns true on hit.</summary>
        public static bool RayCastSphere(in B3Sphere sphere, B3Vec3 origin, B3Vec3 translation, out B3CastOutput hit)
        {
            var input = new B3RayCastInput { Origin = origin, Translation = translation, MaxFraction = 1f };
            hit = B3Api.b3RayCastSphere(in sphere, in input);
            return hit.Hit != 0;
        }

        /// <summary>Sweep a sphere (point + radius) along a translation against a capsule.</summary>
        public static bool CastSphereAgainstCapsule(in B3Capsule capsule, B3Vec3 origin, float radius,
            B3Vec3 translation, out B3CastOutput hit)
        {
            B3Vec3 point = origin;
            var input = new B3ShapeCastInput
            {
                Proxy = new B3ShapeProxy { Points = &point, Count = 1, Radius = radius },
                Translation = translation,
                MaxFraction = 1f,
            };
            hit = B3Api.b3ShapeCastCapsule(in capsule, in input);
            return hit.Hit != 0;
        }

        /// <summary>Sweep a sphere (point + radius) along a translation against a sphere.</summary>
        public static bool CastSphereAgainstSphere(in B3Sphere sphere, B3Vec3 origin, float radius,
            B3Vec3 translation, out B3CastOutput hit)
        {
            B3Vec3 point = origin;
            var input = new B3ShapeCastInput
            {
                Proxy = new B3ShapeProxy { Points = &point, Count = 1, Radius = radius },
                Translation = translation,
                MaxFraction = 1f,
            };
            hit = B3Api.b3ShapeCastSphere(in sphere, in input);
            return hit.Hit != 0;
        }
    }
}
