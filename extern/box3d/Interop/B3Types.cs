// Blittable mirrors of the box3d v0.1.0 C ABI (include/box3d), double-precision build
// (BOX3D_DOUBLE_PRECISION): b3Vec3 is float, b3Pos/b3WorldTransform carry double positions.
// Field order and types must match the C headers exactly — every struct here is covered by
// a sizeof parity test against a checker compiled from the real headers (see Tests/Editor).
// C `bool` is 1 byte -> `byte` here so every struct stays blittable (no marshalling stubs).

using System;
using System.Runtime.InteropServices;

namespace Box3D.Interop
{
    // ---------------- math ----------------

    /// <summary>3D vector, single precision (b3Vec3). Local/relative space.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3Vec3
    {
        public float X, Y, Z;

        public B3Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static readonly B3Vec3 Zero = default;

        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>World position, double precision in large-world mode (b3Pos).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3Pos
    {
        public double X, Y, Z;

        public B3Pos(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static readonly B3Pos Zero = default;

        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>Quaternion (b3Quat). Identity is (0,0,0,1).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3Quat
    {
        public B3Vec3 V;
        public float S;

        public static readonly B3Quat Identity = new B3Quat { V = default, S = 1f };
    }

    /// <summary>Rigid transform with float translation (b3Transform). Shape-local space.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3Transform
    {
        public B3Vec3 P;
        public B3Quat Q;
    }

    /// <summary>World transform: double translation + float rotation (b3WorldTransform).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3WorldTransform
    {
        public B3Pos P;
        public B3Quat Q;
    }

    /// <summary>Plane where separation = dot(normal, point) - offset (b3Plane).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3Plane
    {
        public B3Vec3 Normal;
        public float Offset;
    }

    /// <summary>Axis-aligned bounding box, single precision (b3AABB).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3AABB
    {
        public B3Vec3 LowerBound, UpperBound;
    }

    /// <summary>3x3 matrix, column major (b3Matrix3).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3Matrix3
    {
        public B3Vec3 Cx, Cy, Cz;
    }

    // ---------------- ids ----------------

    /// <summary>Opaque world handle (b3WorldId).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3WorldId : IEquatable<B3WorldId>
    {
        public ushort Index1, Generation;

        public bool Equals(B3WorldId other) => Index1 == other.Index1 && Generation == other.Generation;
        public override int GetHashCode() => Index1 << 16 | Generation;
    }

    /// <summary>Opaque body handle (b3BodyId).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3BodyId : IEquatable<B3BodyId>
    {
        public int Index1;
        public ushort World0, Generation;

        public bool Equals(B3BodyId other) => Index1 == other.Index1 && World0 == other.World0 && Generation == other.Generation;
        public override int GetHashCode() => Index1 ^ World0 << 16 ^ Generation;
    }

    /// <summary>Opaque shape handle (b3ShapeId).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3ShapeId : IEquatable<B3ShapeId>
    {
        public int Index1;
        public ushort World0, Generation;

        public bool Equals(B3ShapeId other) => Index1 == other.Index1 && World0 == other.World0 && Generation == other.Generation;
        public override int GetHashCode() => Index1 ^ World0 << 16 ^ Generation;
    }

    // ---------------- enums ----------------

    /// <summary>b3BodyType.</summary>
    public enum B3BodyType
    {
        Static = 0,
        Kinematic = 1,
        Dynamic = 2,
    }

    /// <summary>b3ShapeType.</summary>
    public enum B3ShapeType
    {
        Capsule = 0,
        Compound = 1,
        Height = 2,
        Hull = 3,
        Mesh = 4,
        Sphere = 5,
    }

    // ---------------- geometry ----------------

    /// <summary>Solid sphere with local offset (b3Sphere).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3Sphere
    {
        public B3Vec3 Center;
        public float Radius;
    }

    /// <summary>Solid capsule: two hemisphere centers + radius (b3Capsule).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3Capsule
    {
        public B3Vec3 Center1, Center2;
        public float Radius;
    }

    /// <summary>Convex hull header; vertex/edge/face arrays hang off the end (b3HullData).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3HullData
    {
        public ulong Version;
        public int ByteCount;
        public uint Hash;
        public B3AABB Aabb;
        public float SurfaceArea, Volume, InnerRadius;
        public B3Vec3 Center;
        public B3Matrix3 CentralInertia;
        public int VertexCount, VertexOffset, PointOffset, EdgeCount, EdgeOffset, FaceCount, FaceOffset, PlaneOffset, Padding;
    }

    /// <summary>
    /// Box hull returned by value from b3MakeBoxHull (b3BoxHull, 440 bytes):
    /// the embedded <see cref="B3HullData"/> header followed by its fixed
    /// vertex/point/edge/face/plane arrays (304 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct B3BoxHull
    {
        public B3HullData Base;
        public fixed byte Tail[304];
    }

    /// <summary>Triangle-mesh creation parameters (b3MeshDef). Pointers must stay pinned during b3CreateMesh.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct B3MeshDef
    {
        public B3Vec3* Vertices;
        public int* Indices;           // 3 per triangle
        public byte* MaterialIndices;  // optional, 1 per triangle
        public float WeldTolerance;
        public int VertexCount;
        public int TriangleCount;
        public byte WeldVertices;
        public byte UseMedianSplit;
        public byte IdentifyEdges;
    }

    /// <summary>Convex point cloud + radius for GJK queries (b3ShapeProxy).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct B3ShapeProxy
    {
        public B3Vec3* Points;
        public int Count;
        public float Radius;
    }

    // ---------------- filters & materials ----------------

    /// <summary>Shape collision filter (b3Filter). Initialize via b3DefaultFilter.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3Filter
    {
        public ulong CategoryBits, MaskBits;
        public int GroupIndex;
    }

    /// <summary>Query filter (b3QueryFilter). Initialize via b3DefaultQueryFilter.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3QueryFilter
    {
        public ulong CategoryBits, MaskBits, Id;
        public IntPtr Name; // const char*, optional recording label
    }

    /// <summary>Per-shape/per-triangle surface material (b3SurfaceMaterial).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3SurfaceMaterial
    {
        public float Friction, Restitution, RollingResistance;
        public B3Vec3 TangentVelocity;
        public ulong UserMaterialId;
        public uint CustomColor;
    }

    // ---------------- defs ----------------

    /// <summary>Motion locks (b3MotionLocks). 1-byte C bools.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3MotionLocks
    {
        public byte LinearX, LinearY, LinearZ, AngularX, AngularY, AngularZ;
    }

    /// <summary>
    /// World definition (b3WorldDef). MUST originate from b3DefaultWorldDef —
    /// it carries an internal validation cookie box3d asserts on.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3WorldDef
    {
        public B3Vec3 Gravity;
        public float RestitutionThreshold, HitEventThreshold, ContactHertz, ContactDampingRatio, ContactSpeed, MaximumLinearSpeed;
        public IntPtr FrictionCallback, RestitutionCallback;
        public byte EnableSleep, EnableContinuous;
        public uint WorkerCount;
        public IntPtr EnqueueTask, FinishTask, UserTaskContext, UserData;
        public IntPtr CreateDebugShape, DestroyDebugShape, UserDebugShapeContext;
        public B3Capacity Capacity;
        public int InternalValue;
    }

    /// <summary>Optional world pre-allocation capacities (b3Capacity).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3Capacity
    {
        public int StaticShapeCount, DynamicShapeCount, StaticBodyCount, DynamicBodyCount, ContactCount;
    }

    /// <summary>
    /// Body definition (b3BodyDef). MUST originate from b3DefaultBodyDef —
    /// it carries an internal validation cookie box3d asserts on.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3BodyDef
    {
        public B3BodyType Type;
        public B3Pos Position;
        public B3Quat Rotation;
        public B3Vec3 LinearVelocity, AngularVelocity;
        public float LinearDamping, AngularDamping, GravityScale, SleepThreshold;
        public IntPtr Name;
        public IntPtr UserData;
        public B3MotionLocks MotionLocks;
        public byte EnableSleep, IsAwake, IsBullet, IsEnabled, AllowFastRotation, EnableContactRecycling;
        public int InternalValue;
    }

    /// <summary>
    /// Shape definition (b3ShapeDef). MUST originate from b3DefaultShapeDef —
    /// it carries an internal validation cookie box3d asserts on.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3ShapeDef
    {
        public IntPtr UserData;
        public IntPtr Materials; // b3SurfaceMaterial*, per-triangle materials for meshes
        public int MaterialCount;
        public B3SurfaceMaterial BaseMaterial;
        public float Density;
        public float ExplosionScale;
        public B3Filter Filter;
        public byte EnableCustomFiltering, IsSensor, EnableSensorEvents, EnableContactEvents,
                    EnableHitEvents, EnablePreSolveEvents, InvokeContactCreation, UpdateBodyMass;
        public int InternalValue;
    }

    // ---------------- results ----------------

    /// <summary>Closest-hit ray result (b3RayResult).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3RayResult
    {
        public B3ShapeId ShapeId;
        public B3Pos Point;
        public B3Vec3 Normal;
        public ulong UserMaterialId;
        public float Fraction;
        public int TriangleIndex, ChildIndex, NodeVisits, LeafVisits;
        public byte Hit;
    }

    /// <summary>Mover-vs-shape contact plane (b3PlaneResult).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3PlaneResult
    {
        public B3Plane Plane;
        public B3Vec3 Point;
    }

    /// <summary>Solver input plane for b3SolvePlanes (b3CollisionPlane).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3CollisionPlane
    {
        public B3Plane Plane;
        public float PushLimit; // float.MaxValue = rigid
        public float Push;      // output: applied push in meters
        public byte ClipVelocity;
    }

    /// <summary>Result of b3SolvePlanes (b3PlaneSolverResult).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3PlaneSolverResult
    {
        public B3Vec3 Delta;
        public int IterationCount;
    }

    /// <summary>Dynamic-tree traversal counters (b3TreeStats).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3TreeStats
    {
        public int NodeVisits, LeafVisits;
    }

    /// <summary>
    /// Ray-cast input for the standalone geometry functions (b3RayCastInput). Origin and
    /// translation are in the same frame as the shape being cast against.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3RayCastInput
    {
        public B3Vec3 Origin;
        public B3Vec3 Translation; // end = origin + translation
        public float MaxFraction;  // typically 1
    }

    /// <summary>
    /// Shape-cast input for the standalone geometry functions (b3ShapeCastInput). The proxy's
    /// points must stay pinned for the duration of the call.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3ShapeCastInput
    {
        public B3ShapeProxy Proxy;
        public B3Vec3 Translation;
        public float MaxFraction;  // typically 1
        public byte CanEncroach;   // allow encroachment when initially touching (needs radius > 0)
    }

    /// <summary>Ray/shape-cast output in the input's frame (b3CastOutput). Check <see cref="Hit"/> first.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3CastOutput
    {
        public B3Vec3 Normal;
        public B3Vec3 Point;
        public float Fraction;
        public int Iterations, TriangleIndex, ChildIndex, MaterialIndex;
        public byte Hit;
    }

    /// <summary>Library version (b3Version).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3Version
    {
        public int Major, Minor, Revision;

        public override string ToString() => $"{Major}.{Minor}.{Revision}";
    }

    // ---------------- debug draw ----------------

    /// <summary>
    /// Shape description passed to the world's CreateDebugShape callback (b3DebugShape). box3d
    /// draws shape geometry ONLY through this mechanism: on the first b3World_Draw it asks the
    /// application to bake a drawable representation per shape (the returned handle is stored and
    /// later passed to DrawShapeFcn / DestroyDebugShape). <see cref="Geometry"/> is the C union —
    /// cast by <see cref="Type"/> to B3Capsule* / B3Sphere* / B3HullData* / b3CompoundData* /
    /// b3HeightFieldData* / b3Mesh*. The pointer is only guaranteed valid during the callback.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct B3DebugShape
    {
        public B3ShapeId ShapeId;
        public B3ShapeType Type;
        public void* Geometry;
    }

    /// <summary>
    /// Debug-draw callback table passed to b3World_Draw (b3DebugDraw). Initialize via
    /// b3DefaultDebugDraw, then install callbacks. Callbacks receive WORLD coordinates —
    /// double-precision <see cref="B3Pos"/> in the large-world ABI — so they stay accurate far
    /// from the origin; rebase into a camera-relative frame inside the callback. Callback fields
    /// are C function pointers (pass <c>delegate* unmanaged[Cdecl]</c> shims cast to IntPtr; the
    /// exact managed signatures live in Box3D core <c>DebugDrawCallbacks</c>). Color parameters
    /// are b3HexColor: 0xRRGGBB in the low 24 bits of a 32-bit value.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct B3DebugDraw
    {
        public IntPtr DrawShapeFcn;     // byte (void* userShape, B3WorldTransform, uint color, void* ctx) — user debug shapes only; return 1 to continue
        public IntPtr DrawSegmentFcn;   // void (B3Pos p1, B3Pos p2, uint color, void* ctx)
        public IntPtr DrawTransformFcn; // void (B3WorldTransform, void* ctx)
        public IntPtr DrawPointFcn;     // void (B3Pos p, float size, uint color, void* ctx)
        public IntPtr DrawSphereFcn;    // void (B3Pos p, float radius, uint color, float alpha, void* ctx)
        public IntPtr DrawCapsuleFcn;   // void (B3Pos p1, B3Pos p2, float radius, uint color, float alpha, void* ctx)
        public IntPtr DrawBoundsFcn;    // void (B3AABB, uint color, void* ctx)
        public IntPtr DrawBoxFcn;       // void (B3Vec3 extents, B3WorldTransform, uint color, void* ctx)
        public IntPtr DrawStringFcn;    // void (B3Pos p, byte* s, uint color, void* ctx)
        public B3AABB DrawingBounds;
        public float ForceScale;
        public float JointScale;
        public byte DrawShapes, DrawJoints, DrawJointExtras, DrawBounds, DrawMass, DrawBodyNames, DrawContacts;
        public int DrawAnchorA;
        public byte DrawGraphColors, DrawContactFeatures, DrawContactNormals, DrawContactForces, DrawFrictionForces, DrawIslands;
        public IntPtr Context;
    }
}
