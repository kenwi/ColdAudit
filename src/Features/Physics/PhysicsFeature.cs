using System.Numerics;
using Box3D;
using Box3D.Interop;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.Physics;

/// <summary>
/// Owns the Box3D world, static level colliders, and character-mover queries.
/// </summary>
public sealed class PhysicsFeature : FeatureBase
{
    private Box3DWorld? _world;
    private B3QueryFilter _filter;
    private readonly Box3DDebugSnapshot _debugSnapshot = new();
    private readonly List<DebugWallQuad> _debugWalls = [];
    private readonly List<Box3DMesh> _collisionMeshes = [];
    private bool _debugSnapshotValid;
    private int _staticBodyCount;
    private Aabb _floorBounds;
    private bool _hasFloorBounds;

    public Box3DWorld? World => _world;
    public int StaticBodyCount => _staticBodyCount;
    public IReadOnlyList<DebugWallQuad> DebugWalls => _debugWalls;

    public override void Load(GameWorld world, EventBus events)
    {
        _world = new Box3DWorld(gravity: new B3Vec3(0f, -9.8f, 0f), debugShapes: true);
        _filter = Box3DWorld.DefaultQueryFilter();
        _debugWalls.Clear();
        DisposeCollisionMeshes();
        _hasFloorBounds = false;

        if (world.ActiveLevel is not null)
        {
            _staticBodyCount = LevelCollisionBuilder.Build(
                _world,
                world.ActiveLevel,
                _debugWalls,
                out _floorBounds);
            _staticBodyCount += ModelCollisionBuilder.Build(_world, world.ActiveLevel, _collisionMeshes);
            _hasFloorBounds = true;
        }

        var version = Box3DWorld.NativeVersion;
        var floorOk = VerifyFloorRaycast(_world);
        Console.WriteLine(
            $"[Physics] Box3D {version} bodies={_staticBodyCount} walls={_debugWalls.Count} meshColliders={_collisionMeshes.Count} floor={(floorOk ? "OK" : "FAIL")}");
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (_world is null)
        {
            _debugSnapshotValid = false;
            return;
        }

        // Capture while exclusive (before/after mover queries on this thread is fine).
        // Done here so DebugOverlay can draw after world meshes.
        if (world.DebugDraw == DebugDrawMode.Wireframe)
        {
            _world.Draw(_debugSnapshot, Box3DDebugDrawOptions.Default);
            _debugSnapshotValid = true;
        }
        else
        {
            _debugSnapshotValid = false;
        }
    }

    public override void Unload()
    {
        // Destroy bodies/shapes before releasing shared mesh data they reference.
        _world?.Dispose();
        _world = null;
        DisposeCollisionMeshes();
        _staticBodyCount = 0;
        _debugSnapshotValid = false;
        _debugSnapshot.Clear();
        _debugWalls.Clear();
        _hasFloorBounds = false;
    }

    private void DisposeCollisionMeshes()
    {
        foreach (var mesh in _collisionMeshes)
        {
            mesh.Dispose();
        }

        _collisionMeshes.Clear();
    }

    public bool TryGetDebugSnapshot(out Box3DDebugSnapshot snapshot)
    {
        snapshot = _debugSnapshot;
        return _debugSnapshotValid;
    }

    public bool TryGetFloorBounds(out Aabb floorBounds)
    {
        floorBounds = _floorBounds;
        return _hasFloorBounds;
    }

    /// <summary>
    /// Min plane normal Y to treat as walkable ground (~45°). Steeper faces are walls.
    /// </summary>
    public const float WalkableNormalMinY = 0.7f;

    /// <summary>
    /// Max upward correction during the horizontal move (blocks climbing props / launches).
    /// </summary>
    public const float MaxStepUp = 0.08f;

    /// <summary>
    /// Quake-style capsule move with separated axes so steep mesh faces cannot lift or
    /// bury the player (common with triangle-mesh props).
    /// </summary>
    public bool TryMoveCapsule(
        Vector3 feetPosition,
        in B3Capsule capsule,
        Vector3 velocity,
        float dt,
        out Vector3 newFeet,
        out Vector3 newVelocity)
    {
        newFeet = feetPosition;
        newVelocity = velocity;
        if (_world is null)
        {
            return false;
        }

        var origin = ToPos(feetPosition);
        var vx = velocity.X;
        var vy = velocity.Y;
        var vz = velocity.Z;

        // --- Horizontal: slide on walls; only tiny step-up allowed (no prop climbing). ---
        var horiz = new B3Vec3(vx * dt, 0f, vz * dt);
        if (horiz.X != 0f || horiz.Z != 0f)
        {
            var fraction = _world.CastMover(origin, in capsule, horiz, _filter);
            origin = new B3Pos(
                origin.X + horiz.X * fraction,
                origin.Y + horiz.Y * fraction,
                origin.Z + horiz.Z * fraction);
        }

        Span<B3PlaneResult> contacts = stackalloc B3PlaneResult[16];
        Span<B3CollisionPlane> planes = stackalloc B3CollisionPlane[16];

        var contactCount = _world.CollideMover(origin, in capsule, _filter, contacts);
        var planeCount = BuildMoverPlanes(contacts[..contactCount], planes, flattenSteep: true);
        if (planeCount > 0)
        {
            var solved = Box3DMover.SolvePlanes(default, planes[..planeCount]);
            var stepY = System.Math.Clamp(solved.Delta.Y, 0f, MaxStepUp);
            origin = new B3Pos(
                origin.X + solved.Delta.X,
                origin.Y + stepY,
                origin.Z + solved.Delta.Z);

            var clipped = Box3DMover.ClipVector(new B3Vec3(vx, 0f, vz), planes[..planeCount]);
            vx = clipped.X;
            vz = clipped.Z;
        }

        // --- Vertical: gravity / floor / ceiling only. ---
        var vert = new B3Vec3(0f, vy * dt, 0f);
        if (vert.Y != 0f)
        {
            var fraction = _world.CastMover(origin, in capsule, vert, _filter);
            origin = new B3Pos(
                origin.X + vert.X * fraction,
                origin.Y + vert.Y * fraction,
                origin.Z + vert.Z * fraction);
        }

        contactCount = _world.CollideMover(origin, in capsule, _filter, contacts);
        planeCount = BuildMoverPlanes(contacts[..contactCount], planes, flattenSteep: false);
        if (planeCount > 0)
        {
            var solved = Box3DMover.SolvePlanes(default, planes[..planeCount]);
            // Never launch from vertical resolve; allow settling into floor.
            var dy = System.Math.Min(solved.Delta.Y, MaxStepUp);
            origin = new B3Pos(
                origin.X + solved.Delta.X,
                origin.Y + dy,
                origin.Z + solved.Delta.Z);

            var clipped = Box3DMover.ClipVector(new B3Vec3(vx, vy, vz), planes[..planeCount]);
            vx = clipped.X;
            vy = clipped.Y;
            vz = clipped.Z;

            // Steep contacts must not create upward velocity (mesh "ramps").
            if (vy > 0f && !HasWalkableContact(contacts[..contactCount]))
            {
                vy = 0f;
            }
        }

        newFeet = ToVector3(origin);
        newVelocity = new Vector3(vx, vy, vz);
        return true;
    }

    /// <summary>
    /// Convert contacts to solver planes. When <paramref name="flattenSteep"/> is true,
    /// non-walkable planes become horizontal walls so SolvePlanes cannot push along Y.
    /// </summary>
    private static int BuildMoverPlanes(
        ReadOnlySpan<B3PlaneResult> contacts,
        Span<B3CollisionPlane> planes,
        bool flattenSteep)
    {
        var count = 0;
        for (var i = 0; i < contacts.Length && count < planes.Length; i++)
        {
            var plane = contacts[i].Plane;
            var n = plane.Normal;
            if (flattenSteep && n.Y < WalkableNormalMinY && n.Y > -WalkableNormalMinY)
            {
                var xz = System.MathF.Sqrt(n.X * n.X + n.Z * n.Z);
                if (xz < 1e-4f)
                {
                    continue;
                }

                var flat = new B3Vec3(n.X / xz, 0f, n.Z / xz);
                var p = contacts[i].Point;
                // b3Plane: distance = dot(normal, x) - offset (point on plane => offset = dot(n, p)).
                plane = new B3Plane
                {
                    Normal = flat,
                    Offset = flat.X * p.X + flat.Y * p.Y + flat.Z * p.Z
                };
            }

            planes[count++] = new B3CollisionPlane
            {
                Plane = plane,
                PushLimit = float.MaxValue,
                Push = 0f,
                ClipVelocity = 1
            };
        }

        return count;
    }

    private static bool HasWalkableContact(ReadOnlySpan<B3PlaneResult> contacts)
    {
        for (var i = 0; i < contacts.Length; i++)
        {
            if (contacts[i].Plane.Normal.Y >= WalkableNormalMinY)
            {
                return true;
            }
        }

        return false;
    }

    public static B3Capsule MakeCapsule(float height, float radius = 0.35f)
    {
        var r = System.MathF.Min(radius, height * 0.45f);
        return new B3Capsule
        {
            Center1 = new B3Vec3(0f, r, 0f),
            Center2 = new B3Vec3(0f, height - r, 0f),
            Radius = r
        };
    }

    /// <summary>Headless check: placeholder sector floors/walls + mover stop on wall.</summary>
    public static bool RunLevelSmoke(out string message)
    {
        using var world = new Box3DWorld(gravity: new B3Vec3(0f, -9.8f, 0f), debugShapes: true);
        var level = CreateSmokeLevel();
        var walls = new List<DebugWallQuad>();
        var bodies = LevelCollisionBuilder.Build(world, level, walls, out _);
        var filter = Box3DWorld.DefaultQueryFilter();

        var floor = world.CastRayClosest(new B3Pos(0, 5, 0), new B3Vec3(0, -10, 0), filter);
        if (floor.Hit == 0 || floor.Point.Y is < -0.05 or > 0.05)
        {
            message = $"floor fail hit={floor.Hit} y={floor.Point.Y}";
            return false;
        }

        var capsule = MakeCapsule(1.7f);
        // Portal opens on +X from room A; cast +Z into a solid wall instead.
        var fraction = world.CastMover(new B3Pos(0, 0.02, 0), in capsule, new B3Vec3(0, 0, 20), filter);
        if (fraction is < 0.25f or > 0.32f)
        {
            message = $"wall mover fraction={fraction} (want ~0.28 for wall at z=6, r=0.35)";
            return false;
        }

        // Through the portal doorway (+X) the mover should travel past room A's wall.
        var throughDoor = world.CastMover(new B3Pos(0, 0.02, 0), in capsule, new B3Vec3(20, 0, 0), filter);
        if (throughDoor < 0.5f)
        {
            message = $"portal blocked: mover fraction={throughDoor}";
            return false;
        }

        // Crossing the old room/portal floor seam must not hitch (full travel on continuous floor).
        var acrossSeam = world.CastMover(new B3Pos(5, 0.02, 0), in capsule, new B3Vec3(4, 0, 0), filter);
        if (acrossSeam < 0.95f)
        {
            message = $"floor seam hitch: mover fraction={acrossSeam} (want ~1 across x=6)";
            return false;
        }

        // Portal side walls: from corridor center, +Z should stop near z=2 (portal half-width).
        var sideBlocked = world.CastMover(new B3Pos(7, 0.02, 0), in capsule, new B3Vec3(0, 0, 10), filter);
        if (sideBlocked is < 0.12f or > 0.22f)
        {
            message = $"portal side wall fraction={sideBlocked} (want ~0.165 for wall at z=2, r=0.35)";
            return false;
        }

        var snapshot = new Box3DDebugSnapshot();
        world.Draw(snapshot, Box3DDebugDrawOptions.Default);
        if (snapshot.Segments.Count == 0)
        {
            message = "debug draw produced no segments";
            return false;
        }

        message =
            $"OK bodies={bodies} wallFrac={fraction:F3} doorFrac={throughDoor:F3} seamFrac={acrossSeam:F3} sideFrac={sideBlocked:F3} segments={snapshot.Segments.Count}";
        return true;
    }

    private static LevelData CreateSmokeLevel()
    {
        var level = new LevelData { LevelId = "smoke" };
        for (var i = 0; i < 2; i++)
        {
            level.Sectors.Add(new SectorDef
            {
                Id = i == 0 ? "room_a" : "room_b",
                Bounds = DebugSectorLayout.Bounds(i)
            });
        }

        level.Portals.Add(new PortalDef
        {
            Id = "portal_a_b",
            FromSectorId = "room_a",
            ToSectorId = "room_b"
        });
        return level;
    }

    private static bool VerifyFloorRaycast(Box3DWorld physicsWorld)
    {
        var filter = Box3DWorld.DefaultQueryFilter();
        var hit = physicsWorld.CastRayClosest(
            new B3Pos(0, 5, 0),
            new B3Vec3(0, -10, 0),
            filter);

        if (hit.Hit == 0)
        {
            Console.WriteLine("[Physics] floor raycast missed");
            return false;
        }

        // Floors have top face at FloorY (0).
        if (hit.Point.Y is < -0.05 or > 0.05)
        {
            Console.WriteLine($"[Physics] floor hit Y={hit.Point.Y} (want ~0)");
            return false;
        }

        return true;
    }

    private static B3Pos ToPos(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3 ToVector3(B3Pos p) =>
        new((float)p.X, (float)p.Y, (float)p.Z);
}
