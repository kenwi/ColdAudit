using System;
using System.Runtime.InteropServices;
using Box3D.Interop;

namespace Box3D
{
    /// <summary>Closest-hit result from a shape cast.</summary>
    public struct B3CastHit
    {
        public B3ShapeId ShapeId;
        public B3Pos Point;
        public B3Vec3 Normal;
        public float Fraction;
        public ulong UserMaterialId;
        public int TriangleIndex;
        public int ChildIndex;
    }

    /// <summary>
    /// A box3d simulation world. Owns the native world and exposes stepping, body creation,
    /// and the query set (rays, shape casts, overlaps, and the capsule character mover).
    ///
    /// Threading: queries are safe from any thread while no thread is inside
    /// <see cref="Step"/>; stepping requires exclusive access.
    /// </summary>
    public sealed unsafe class Box3DWorld : IDisposable
    {
        private B3WorldId _id;

        /// <summary>Native handle for direct <see cref="B3Api"/> calls.</summary>
        public B3WorldId Id => _id;

        public bool IsValid => B3Api.b3World_IsValid(_id) != 0;

        public static B3Version NativeVersion => B3Api.b3GetVersion();

        /// <summary>Default world definition — always start from this (it carries a validation cookie).</summary>
        public static B3WorldDef DefaultWorldDef() => B3Api.b3DefaultWorldDef();

        public static B3BodyDef DefaultBodyDef() => B3Api.b3DefaultBodyDef();

        public static B3ShapeDef DefaultShapeDef() => B3Api.b3DefaultShapeDef();

        public static B3QueryFilter DefaultQueryFilter() => B3Api.b3DefaultQueryFilter();

        /// <summary>Create a world from a definition obtained via <see cref="DefaultWorldDef"/>.
        /// For <see cref="Draw"/> support, call <see cref="EnableDebugShapes"/> on the def first.</summary>
        public Box3DWorld(in B3WorldDef def)
        {
            _id = B3Api.b3CreateWorld(in def);
        }

        /// <summary>
        /// Create a world with default settings and the given gravity. <paramref name="debugShapes"/>
        /// installs the debug-shape bake hooks — box3d only reads them at world creation, and only a
        /// world created with them can draw its shapes via <see cref="Draw"/>. Costless until the
        /// first draw (handles are baked lazily, per shape).
        /// </summary>
        public Box3DWorld(B3Vec3 gravity, bool debugShapes = false)
        {
            var def = B3Api.b3DefaultWorldDef();
            def.Gravity = gravity;
            if (debugShapes)
                DebugShapeCallbacks.Install(ref def);
            _id = B3Api.b3CreateWorld(in def);
        }

        /// <summary>Create a world with default settings and no gravity (query-only worlds).</summary>
        public Box3DWorld() : this(default(B3Vec3))
        {
        }

        /// <summary>Install the debug-shape bake hooks on a caller-built definition (see the
        /// <paramref name="def"/>-taking constructor). Must happen before world creation.</summary>
        public static void EnableDebugShapes(ref B3WorldDef def) => DebugShapeCallbacks.Install(ref def);

        ~Box3DWorld() => DestroyNative();

        public void Dispose()
        {
            DestroyNative();
            GC.SuppressFinalize(this);
        }

        private void DestroyNative()
        {
            if (B3Api.b3World_IsValid(_id) != 0)
                B3Api.b3DestroyWorld(_id);
            _id = default;
        }

        // ---------------- simulation ----------------

        /// <summary>Advance the simulation. Use a fixed timeStep; subStepCount 4 is the box3d default.</summary>
        public void Step(float timeStep, int subStepCount = 4)
            => B3Api.b3World_Step(_id, timeStep, subStepCount);

        public void EnableSleeping(bool enable)
            => B3Api.b3World_EnableSleeping(_id, enable ? (byte)1 : (byte)0);

        public B3AABB Bounds => B3Api.b3World_GetBounds(_id);

        // ---------------- bodies ----------------

        public Box3DBody CreateBody(in B3BodyDef def)
            => new Box3DBody(B3Api.b3CreateBody(_id, in def));

        public Box3DBody CreateBody(B3BodyType type, B3Pos position)
        {
            var def = B3Api.b3DefaultBodyDef();
            def.Type = type;
            def.Position = position;
            return new Box3DBody(B3Api.b3CreateBody(_id, in def));
        }

        public Box3DBody CreateStaticBody(B3Pos position)
            => CreateBody(B3BodyType.Static, position);

        // ---------------- queries ----------------

        /// <summary>Closest-hit raycast from origin along translation (end = origin + translation).</summary>
        public B3RayResult CastRayClosest(B3Pos origin, B3Vec3 translation, B3QueryFilter filter)
            => B3Api.b3World_CastRayClosest(_id, origin, translation, filter);

        /// <summary>
        /// Cast the capsule mover through the world with sliding behavior and return the
        /// translation fraction. Use <see cref="CollideMover"/> for contact information.
        /// </summary>
        public float CastMover(B3Pos origin, in B3Capsule mover, B3Vec3 translation, B3QueryFilter filter)
            => B3Api.b3World_CastMover(_id, origin, in mover, translation, filter, null, null);

        /// <summary>
        /// Collide the capsule mover with the world and gather contact planes into
        /// <paramref name="planes"/>. Returns the number of planes written. Feed the planes to
        /// <see cref="Box3DMover.SolvePlanes"/> for depenetration and <see cref="Box3DMover.ClipVector"/>
        /// for velocity clipping.
        /// </summary>
        public int CollideMover(B3Pos origin, in B3Capsule mover, B3QueryFilter filter, Span<B3PlaneResult> planes)
        {
            fixed (B3PlaneResult* buffer = planes)
            {
                var ctx = new QueryCallbacks.PlaneGatherContext { Buffer = buffer, Capacity = planes.Length };
                B3Api.b3World_CollideMover(_id, origin, in mover, filter,
                    (delegate* unmanaged[Cdecl]<B3ShapeId, B3PlaneResult*, int, void*, byte>)&QueryCallbacks.GatherPlanes, &ctx);
                return ctx.Count;
            }
        }

        // Fraction below which a cast hit counts as "started in contact". Box3d reports fraction-0
        // hits for initially-touching shapes (within its contact tolerance); PhysX-style controllers
        // instead expect such shapes to be invisible to the sweep and resolved by depenetration.
        const float InitialContactFraction = 1e-6f;

        /// <summary>
        /// Closest-hit cast of a convex point cloud (radius-expanded) through the world.
        /// Points are relative to origin, so precision holds far from the world origin.
        /// With <paramref name="ignoreInitialContact"/>, shapes the proxy starts in contact with are
        /// invisible to the cast (PhysX sweep semantics) — use for kinematic character controllers
        /// that resolve overlap via <see cref="CollideMover"/>/depenetration instead of the sweep.
        /// </summary>
        public bool CastShapeClosest(B3Pos origin, ReadOnlySpan<B3Vec3> points, float radius, B3Vec3 translation,
            B3QueryFilter filter, out B3CastHit hit, bool ignoreInitialContact = false)
        {
            fixed (B3Vec3* pts = points)
            {
                var proxy = new B3ShapeProxy { Points = pts, Count = points.Length, Radius = radius };
                var ctx = new QueryCallbacks.ClosestCastContext
                {
                    MinFraction = ignoreInitialContact ? InitialContactFraction : float.MinValue,
                };
                B3Api.b3World_CastShape(_id, origin, in proxy, translation, filter,
                    (delegate* unmanaged[Cdecl]<B3ShapeId, B3Pos, B3Vec3, float, ulong, int, int, void*, float>)&QueryCallbacks.ClosestCast, &ctx);
                hit = ctx.Hit;
                return ctx.HasHit != 0;
            }
        }

        /// <summary>Closest-hit capsule cast. Returns false on no hit.
        /// See <see cref="CastShapeClosest"/> for <paramref name="ignoreInitialContact"/>.</summary>
        public bool CastCapsuleClosest(B3Pos origin, in B3Capsule capsule, B3Vec3 translation,
            B3QueryFilter filter, out B3CastHit hit, bool ignoreInitialContact = false)
        {
            var pts = stackalloc B3Vec3[2] { capsule.Center1, capsule.Center2 };
            var proxy = new B3ShapeProxy { Points = pts, Count = 2, Radius = capsule.Radius };
            var ctx = new QueryCallbacks.ClosestCastContext
            {
                MinFraction = ignoreInitialContact ? InitialContactFraction : float.MinValue,
            };
            B3Api.b3World_CastShape(_id, origin, in proxy, translation, filter,
                (delegate* unmanaged[Cdecl]<B3ShapeId, B3Pos, B3Vec3, float, ulong, int, int, void*, float>)&QueryCallbacks.ClosestCast, &ctx);
            hit = ctx.Hit;
            return ctx.HasHit != 0;
        }

        // ---------------- debug draw ----------------

        /// <summary>
        /// Capture a debug view of the world into <paramref name="snapshot"/> (cleared first): box3d
        /// hands back each shape's baked wireframe (worlds created with <c>debugShapes: true</c> —
        /// a world without the hooks draws no shapes) and the snapshot collects world-space line
        /// segments. Threading: treat like <see cref="Step"/>, NOT like a query — the first draw
        /// lazily bakes per-shape handles inside the native world, so it needs exclusive access.
        /// This path also allocates (bakes, list growth): capture on demand and cache the result
        /// rather than capturing per frame.
        /// </summary>
        public void Draw(Box3DDebugSnapshot snapshot, in Box3DDebugDrawOptions options)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.Clear();

            var draw = B3Api.b3DefaultDebugDraw();
            DebugDrawCallbacks.Install(ref draw);
            draw.DrawShapes = options.DrawShapes ? (byte)1 : (byte)0;
            draw.DrawBounds = options.DrawBounds ? (byte)1 : (byte)0;
            if (options.DrawingBounds.HasValue)
            {
                draw.DrawingBounds = options.DrawingBounds.Value;
            }
            else
            {
                // Upstream defaults to ±100 m, which silently clips anything bigger; default to the
                // world's own bounds instead so "draw everything" is the no-options behavior.
                B3AABB bounds = B3Api.b3World_GetBounds(_id);
                bounds.LowerBound = new B3Vec3(bounds.LowerBound.X - 1f, bounds.LowerBound.Y - 1f, bounds.LowerBound.Z - 1f);
                bounds.UpperBound = new B3Vec3(bounds.UpperBound.X + 1f, bounds.UpperBound.Y + 1f, bounds.UpperBound.Z + 1f);
                draw.DrawingBounds = bounds;
            }

            var handle = GCHandle.Alloc(snapshot);
            draw.Context = GCHandle.ToIntPtr(handle);
            try
            {
                B3Api.b3World_Draw(_id, ref draw, options.MaskBits);
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>True if any shape overlaps the capsule.</summary>
        public bool OverlapCapsule(B3Pos origin, in B3Capsule capsule, B3QueryFilter filter)
        {
            var pts = stackalloc B3Vec3[2] { capsule.Center1, capsule.Center2 };
            var proxy = new B3ShapeProxy { Points = pts, Count = 2, Radius = capsule.Radius };
            var ctx = new QueryCallbacks.AnyHitContext();
            B3Api.b3World_OverlapShape(_id, origin, in proxy, filter,
                (delegate* unmanaged[Cdecl]<B3ShapeId, void*, byte>)&QueryCallbacks.AnyOverlap, &ctx);
            return ctx.Hit != 0;
        }
    }
}
