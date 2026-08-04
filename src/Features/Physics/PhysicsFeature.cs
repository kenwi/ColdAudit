using System.Numerics;
using Box3D;
using Box3D.Interop;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
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
    private bool _debugSnapshotValid;
    private int _staticBodyCount;

    public Box3DWorld? World => _world;
    public int StaticBodyCount => _staticBodyCount;

    public override void Load(GameWorld world, EventBus events)
    {
        _world = new Box3DWorld(gravity: new B3Vec3(0f, -9.8f, 0f), debugShapes: true);
        _filter = Box3DWorld.DefaultQueryFilter();

        if (world.ActiveLevel is not null)
        {
            _staticBodyCount = LevelCollisionBuilder.Build(_world, world.ActiveLevel);
        }

        var version = Box3DWorld.NativeVersion;
        var floorOk = VerifyFloorRaycast(_world);
        Console.WriteLine(
            $"[Physics] Box3D {version} bodies={_staticBodyCount} floor={(floorOk ? "OK" : "FAIL")}");
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
        if (world.DebugDrawEnabled)
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
        _world?.Dispose();
        _world = null;
        _staticBodyCount = 0;
        _debugSnapshotValid = false;
        _debugSnapshot.Clear();
    }

    public bool TryGetDebugSnapshot(out Box3DDebugSnapshot snapshot)
    {
        snapshot = _debugSnapshot;
        return _debugSnapshotValid;
    }

    /// <summary>
    /// Quake-style capsule move: cast with slide, depenetrate, clip velocity.
    /// <paramref name="feetPosition"/> is the capsule origin (feet on floor).
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
        var translation = new B3Vec3(velocity.X * dt, velocity.Y * dt, velocity.Z * dt);
        var fraction = _world.CastMover(origin, in capsule, translation, _filter);
        origin = new B3Pos(
            origin.X + translation.X * fraction,
            origin.Y + translation.Y * fraction,
            origin.Z + translation.Z * fraction);

        Span<B3PlaneResult> contacts = stackalloc B3PlaneResult[8];
        var contactCount = _world.CollideMover(origin, in capsule, _filter, contacts);
        Span<B3CollisionPlane> planes = stackalloc B3CollisionPlane[8];
        var planeCount = Box3DMover.ToCollisionPlanes(contacts[..contactCount], planes);
        var solved = Box3DMover.SolvePlanes(default, planes[..planeCount]);
        origin = new B3Pos(
            origin.X + solved.Delta.X,
            origin.Y + solved.Delta.Y,
            origin.Z + solved.Delta.Z);

        var clipped = Box3DMover.ClipVector(
            new B3Vec3(velocity.X, velocity.Y, velocity.Z),
            planes[..planeCount]);

        newFeet = ToVector3(origin);
        newVelocity = new Vector3(clipped.X, clipped.Y, clipped.Z);
        return true;
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
        var bodies = LevelCollisionBuilder.Build(world, level);
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

        var snapshot = new Box3DDebugSnapshot();
        world.Draw(snapshot, Box3DDebugDrawOptions.Default);
        if (snapshot.Segments.Count == 0)
        {
            message = "debug draw produced no segments";
            return false;
        }

        message = $"OK bodies={bodies} wallFrac={fraction:F3} doorFrac={throughDoor:F3} segments={snapshot.Segments.Count}";
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
