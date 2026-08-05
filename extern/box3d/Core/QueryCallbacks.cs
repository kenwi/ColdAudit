// Native->managed callback shims for box3d world queries. All shims are static
// [UnmanagedCallersOnly] methods (works on .NET, IL2CPP, and Unity 6 Mono) and receive
// their output buffer through the native context pointer, so queries are re-entrant and
// thread-safe with zero allocation.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Box3D.Interop;

namespace Box3D
{
    internal static unsafe class QueryCallbacks
    {
        // ---- b3World_CollideMover: gather contact planes into a caller buffer ----

        internal struct PlaneGatherContext
        {
            public B3PlaneResult* Buffer;
            public int Capacity;
            public int Count;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static byte GatherPlanes(B3ShapeId shapeId, B3PlaneResult* planes, int planeCount, void* context)
        {
            var ctx = (PlaneGatherContext*)context;
            for (int i = 0; i < planeCount && ctx->Count < ctx->Capacity; i++)
                ctx->Buffer[ctx->Count++] = planes[i];
            return ctx->Count < ctx->Capacity ? (byte)1 : (byte)0;
        }

        // ---- b3World_OverlapShape / OverlapAABB: any-hit test ----

        internal struct AnyHitContext
        {
            public byte Hit;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static byte AnyOverlap(B3ShapeId shapeId, void* context)
        {
            ((AnyHitContext*)context)->Hit = 1;
            return 0; // terminate on first hit
        }

        // ---- b3World_CastShape / CastRay: closest-hit gather ----

        internal struct ClosestCastContext
        {
            public B3CastHit Hit;
            public float MinFraction; // hits below this are treated as initial contact and ignored
            public byte HasHit;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static float ClosestCast(B3ShapeId shapeId, B3Pos point, B3Vec3 normal, float fraction,
            ulong userMaterialId, int triangleIndex, int childIndex, void* context)
        {
            var ctx = (ClosestCastContext*)context;

            // Initial-contact filter (PhysX-style "a sweep cannot see what it already touches"): box3d
            // reports fraction-0 hits for shapes the proxy starts in contact with, which wedges kinematic
            // controllers that expect depenetration — not the sweep — to own overlap. Returning -1 drops
            // this shape from the cast and continues.
            if (fraction < ctx->MinFraction)
                return -1f;

            ctx->Hit = new B3CastHit
            {
                ShapeId = shapeId,
                Point = point,
                Normal = normal,
                Fraction = fraction,
                UserMaterialId = userMaterialId,
                TriangleIndex = triangleIndex,
                ChildIndex = childIndex,
            };
            ctx->HasHit = 1;
            return fraction; // clip the cast so only closer hits report after this
        }
    }
}
