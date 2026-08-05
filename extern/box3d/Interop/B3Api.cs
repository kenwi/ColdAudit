// Raw P/Invoke surface for box3d v0.1.0. Function names match the C API verbatim so this
// file diffs directly against include/box3d/box3d.h and collision.h across upstream updates.
// Conventions:
//  - C `bool` parameters/returns are `byte` (1 byte, keeps signatures blittable).
//  - Callback parameters are `void*`; callers pass `delegate* unmanaged[Cdecl]` shims
//    (see Box3D core QueryCallbacks) or IntPtr.Zero where the callback is optional.
//  - The DLL is built with BOX3D_DOUBLE_PRECISION, which renames b3CreateWorld to
//    b3CreateWorldDoublePrecision as a deliberate link-time ABI tripwire. Only that one
//    symbol is renamed; everything else shares names with the float build.

using System;
using System.Runtime.InteropServices;

namespace Box3D.Interop
{
    public static unsafe class B3Api
    {
        public const string Dll = "box3d";

        // ---------------- base ----------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3Version b3GetVersion();

        // ---------------- world ----------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3WorldDef b3DefaultWorldDef();

        [DllImport(Dll, EntryPoint = "b3CreateWorldDoublePrecision", CallingConvention = CallingConvention.Cdecl)]
        public static extern B3WorldId b3CreateWorld(in B3WorldDef def);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3DestroyWorld(B3WorldId worldId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte b3World_IsValid(B3WorldId id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3World_Step(B3WorldId worldId, float timeStep, int subStepCount);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3AABB b3World_GetBounds(B3WorldId worldId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3World_EnableSleeping(B3WorldId worldId, byte flag);

        // ---------------- world queries ----------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3TreeStats b3World_OverlapAABB(B3WorldId worldId, B3AABB aabb, B3QueryFilter filter,
            void* fcn, void* context); // fcn: b3OverlapResultFcn = byte(B3ShapeId, void*)

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3TreeStats b3World_OverlapShape(B3WorldId worldId, B3Pos origin, in B3ShapeProxy proxy,
            B3QueryFilter filter, void* fcn, void* context); // fcn: b3OverlapResultFcn

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3TreeStats b3World_CastRay(B3WorldId worldId, B3Pos origin, B3Vec3 translation,
            B3QueryFilter filter, void* fcn, void* context); // fcn: b3CastResultFcn = float(B3ShapeId, B3Pos, B3Vec3, float, ulong, int, int, void*)

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3RayResult b3World_CastRayClosest(B3WorldId worldId, B3Pos origin, B3Vec3 translation, B3QueryFilter filter);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3TreeStats b3World_CastShape(B3WorldId worldId, B3Pos origin, in B3ShapeProxy proxy,
            B3Vec3 translation, B3QueryFilter filter, void* fcn, void* context); // fcn: b3CastResultFcn

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern float b3World_CastMover(B3WorldId worldId, B3Pos origin, in B3Capsule mover,
            B3Vec3 translation, B3QueryFilter filter, void* fcn, void* context); // fcn: optional b3MoverFilterFcn = byte(B3ShapeId, void*)

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3World_CollideMover(B3WorldId worldId, B3Pos origin, in B3Capsule mover,
            B3QueryFilter filter, void* fcn, void* context); // fcn: b3PlaneResultFcn = byte(B3ShapeId, B3PlaneResult*, int, void*)

        // ---------------- world debug draw ----------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3DebugDraw b3DefaultDebugDraw();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3World_Draw(B3WorldId worldId, ref B3DebugDraw draw, ulong maskBits);

        // ---------------- bodies ----------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3BodyDef b3DefaultBodyDef();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3BodyId b3CreateBody(B3WorldId worldId, in B3BodyDef def);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3DestroyBody(B3BodyId bodyId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte b3Body_IsValid(B3BodyId id);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3BodyType b3Body_GetType(B3BodyId bodyId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3Body_SetType(B3BodyId bodyId, B3BodyType type);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3Pos b3Body_GetPosition(B3BodyId bodyId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3Quat b3Body_GetRotation(B3BodyId bodyId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3WorldTransform b3Body_GetTransform(B3BodyId bodyId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3Body_SetTransform(B3BodyId bodyId, B3Pos position, B3Quat rotation);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3Body_SetTargetTransform(B3BodyId bodyId, B3WorldTransform target, float timeStep, byte wake);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3Vec3 b3Body_GetLinearVelocity(B3BodyId bodyId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3Vec3 b3Body_GetAngularVelocity(B3BodyId bodyId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3Body_SetLinearVelocity(B3BodyId bodyId, B3Vec3 linearVelocity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3Body_SetAngularVelocity(B3BodyId bodyId, B3Vec3 angularVelocity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3Body_ApplyForce(B3BodyId bodyId, B3Vec3 force, B3Pos point, byte wake);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3Body_ApplyForceToCenter(B3BodyId bodyId, B3Vec3 force, byte wake);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3Body_ApplyLinearImpulse(B3BodyId bodyId, B3Vec3 impulse, B3Pos point, byte wake);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3Body_ApplyLinearImpulseToCenter(B3BodyId bodyId, B3Vec3 impulse, byte wake);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern float b3Body_GetMass(B3BodyId bodyId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte b3Body_IsAwake(B3BodyId bodyId);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3Body_SetAwake(B3BodyId bodyId, byte awake);

        // ---------------- shapes ----------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3ShapeDef b3DefaultShapeDef();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3Filter b3DefaultFilter();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3QueryFilter b3DefaultQueryFilter();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3SurfaceMaterial b3DefaultSurfaceMaterial();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3ShapeId b3CreateSphereShape(B3BodyId bodyId, in B3ShapeDef def, in B3Sphere sphere);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3ShapeId b3CreateCapsuleShape(B3BodyId bodyId, in B3ShapeDef def, in B3Capsule capsule);

        // Takes const b3HullData*; a B3BoxHull passes by-ref because its header is the first field.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3ShapeId b3CreateHullShape(B3BodyId bodyId, in B3ShapeDef def, in B3BoxHull hull);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3ShapeId b3CreateMeshShape(B3BodyId bodyId, in B3ShapeDef def, IntPtr meshData, B3Vec3 scale);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3DestroyShape(B3ShapeId shapeId, byte updateBodyMass);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte b3Shape_IsValid(B3ShapeId id);

        // ---------------- geometry ----------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3BoxHull b3MakeBoxHull(float hx, float hy, float hz);

        // Returns an opaque b3MeshData*; free with b3DestroyMesh.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr b3CreateMesh(in B3MeshDef def, int* degenerateTriangleIndices, int degenerateCapacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr b3CreateBoxMesh(B3Vec3 center, B3Vec3 extent, byte identifyEdges);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr b3CreateGridMesh(int xCount, int zCount, float cellWidth, int materialCount, byte identifyEdges);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void b3DestroyMesh(IntPtr mesh);

        // ---------------- standalone geometry casts (no world; shapes in their local frame) ----------------
        // For b3RayCastHull / b3ShapeCastHull pass `in someBoxHull.Base` — the embedded header is the
        // b3HullData* the C API expects.

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3CastOutput b3RayCastSphere(in B3Sphere shape, in B3RayCastInput input);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3CastOutput b3RayCastCapsule(in B3Capsule shape, in B3RayCastInput input);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3CastOutput b3RayCastHull(in B3HullData shape, in B3RayCastInput input);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3CastOutput b3ShapeCastSphere(in B3Sphere shape, in B3ShapeCastInput input);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3CastOutput b3ShapeCastCapsule(in B3Capsule shape, in B3ShapeCastInput input);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3CastOutput b3ShapeCastHull(in B3HullData shape, in B3ShapeCastInput input);

        // ---------------- character mover solver ----------------

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3PlaneSolverResult b3SolvePlanes(B3Vec3 targetDelta, B3CollisionPlane* planes, int count);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern B3Vec3 b3ClipVector(B3Vec3 vector, B3CollisionPlane* planes, int count);
    }
}
