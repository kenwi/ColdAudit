using ColdAudit.Shared.Assets;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.LevelLoad;

public sealed class LevelLoadFeature : FeatureBase
{
    private GameWorld? _world;

    public LevelData? Level { get; private set; }
    public LevelSession Session { get; } = new();

    public override void Load(GameWorld world, EventBus events)
    {
        _world = world;
        Session.LevelId = LevelCatalog.WingB;
        // Level = CreatePlaceholderLevel(Session.LevelId);
        Level = CreateTestLevel();
        Session.IsLoaded = true;
        world.ActiveLevel = Level;

        world.PlayerPosition = Level.PlayerSpawn;
        world.PlayerYaw = Level.PlayerSpawnYaw;
        world.CurrentSectorId = Level.Sectors.Count > 0 ? Level.Sectors[0].Id : "room_a";
        world.VisibleSectorIds.Clear();
        world.VisibleSectorIds.Add(world.CurrentSectorId);
        world.MissionMessage = "COLD AUDIT // SCOPE: Wing B // Retrieve FINANCE-DR // Exfil lobby";
    }

    public override void Unload()
    {
        if (_world is not null && ReferenceEquals(_world.ActiveLevel, Level))
        {
            _world.ActiveLevel = null;
        }

        Level = null;
        Session.IsLoaded = false;
        _world = null;
    }

    private static LevelData CreateTestLevel()
    {
        // Flat export under content/levels/ (not content/models/, and not wing_b/room_a.glb).
        var level = new LevelData
        {
            LevelId = "test_level",
            PlayerSpawn = new System.Numerics.Vector3(0f, 1.7f, 0f),
            PlayerSpawnYaw = MathF.PI
        };

        var room_a = Path.Combine(ContentPaths.Levels, "room_a.glb");
        level.Sectors.Add(new SectorDef
        {
            Id = "room_a",
            ModelPath = room_a,
            CollisionMeshPath = room_a
        });

        var room_b = Path.Combine(ContentPaths.Levels, "room_b.glb");
        level.Sectors.Add(new SectorDef
        {
            Id = "room_b",
            ModelPath = room_b,
            CollisionMeshPath = room_b
        });

        var room_c = Path.Combine(ContentPaths.Levels, "room_c.glb");
        level.Sectors.Add(new SectorDef
        {
            Id = "room_c",
            ModelPath = room_c,
            CollisionMeshPath = room_c
        });

        var room_d = Path.Combine(ContentPaths.Levels, "room_d.glb");
        level.Sectors.Add(new SectorDef
        {
            Id = "room_d",
            ModelPath = room_d,
            CollisionMeshPath = room_d
        });

        var portalAb = Path.Combine(ContentPaths.Levels, "portal_a_b.glb");
        level.Portals.Add(new PortalDef
        {
            Id = "portal_a_b",
            FromSectorId = "room_a",
            ToSectorId = "room_b",
            ModelPath = portalAb,
            CollisionMeshPath = portalAb
        });

        var portalBc = Path.Combine(ContentPaths.Levels, "portal_b_c.glb");
        level.Portals.Add(new PortalDef
        {
            Id = "portal_b_c",
            FromSectorId = "room_b",
            ToSectorId = "room_c",
            ModelPath = portalBc,
            CollisionMeshPath = portalBc
        });

        var portalBd = Path.Combine(ContentPaths.Levels, "portal_b_d.glb");
        level.Portals.Add(new PortalDef
        {
            Id = "portal_b_d",
            FromSectorId = "room_b",
            ToSectorId = "room_d",
            ModelPath = portalBd,
            CollisionMeshPath = portalBd
        });

        // Placeholder box doors. Hinge is the pivot; swap in ModelPath later (origin at hinge).
        level.Doors.Add(new DoorDef
        {
            Id = "door_debug",
            SectorId = "room_a",
            HingePosition = new System.Numerics.Vector3(-0.45f, 0f, -3.5f),
            ClosedYawDegrees = 0f,
            Locked = false
        });
        level.Doors.Add(new DoorDef
        {
            Id = "door_a_b",
            SectorId = "room_a",
            HingePosition = new System.Numerics.Vector3(4.47f, 0f, -13.5f),
            ClosedYawDegrees = 0f,
            Locked = false
        });

        return level;
    }

    private static LevelData CreatePlaceholderLevel(string levelId)
    {
        // Stand-in until Blender sector meshes + sidecar exist.
        var sectorIds = new[] { "room_a", "room_b", "room_c", "room_d", "room_e", "room_f" };
        var level = new LevelData
        {
            LevelId = levelId,
            PlayerSpawn = new System.Numerics.Vector3(0f, 1.7f, 0f)
        };

        for (var i = 0; i < sectorIds.Length; i++)
        {
            var id = sectorIds[i];
            level.Sectors.Add(new SectorDef
            {
                Id = id,
                ModelPath = LevelCatalog.SectorGlbPath(levelId, id),
                Bounds = DebugSectorLayout.Bounds(i)
            });
        }

        level.Portals.Add(new PortalDef { Id = "portal_a_b", FromSectorId = "room_a", ToSectorId = "room_b" });
        level.Portals.Add(new PortalDef { Id = "portal_a_c", FromSectorId = "room_a", ToSectorId = "room_c" });
        level.Portals.Add(new PortalDef { Id = "portal_c_d", FromSectorId = "room_c", ToSectorId = "room_d" });
        level.Portals.Add(new PortalDef { Id = "portal_d_f", FromSectorId = "room_d", ToSectorId = "room_f" });
        level.Portals.Add(new PortalDef { Id = "portal_c_e", FromSectorId = "room_c", ToSectorId = "room_e" });

        // Test placement: monkey.glb a few units ahead of spawn in room_a.
        // CollisionMeshPath uses the same GLB for now; swap to a low-poly collider later.
        var monkeyPath = ModelCatalog.GlbPath("monkey.glb");
        level.ModelPlacements.Add(new ModelPlacementDef
        {
            Id = "prop_monkey",
            ModelPath = monkeyPath,
            CollisionMeshPath = monkeyPath,
            SectorId = "room_a",
            Position = new System.Numerics.Vector3(0f, 1f, 2f),
            YawDegrees = -0f,
            Scale = 1f
        });

        var monkeyPath2 = ModelCatalog.GlbPath("monkey2.glb");
        level.ModelPlacements.Add(new ModelPlacementDef
        {
            Id = "prop_monkey_b",
            ModelPath = monkeyPath2,
            CollisionMeshPath = monkeyPath2,
            SectorId = "room_a",
            Position = new System.Numerics.Vector3(0f, 0f, -4f),
            YawDegrees = 0f,
            Scale = 1f
        });

        // Desk setup along +X in room_a (table, chair facing it, computer on top).
        level.ModelPlacements.Add(new ModelPlacementDef
        {
            Id = "prop_table",
            ModelPath = ModelCatalog.GlbPath("table.glb"),
            SectorId = "room_a",
            Position = new System.Numerics.Vector3(3.5f, 0f, 2.5f),
            YawDegrees = 0f,
            Scale = 1.5f
        });
        level.ModelPlacements.Add(new ModelPlacementDef
        {
            Id = "prop_chair",
            ModelPath = ModelCatalog.GlbPath("chair.glb"),
            SectorId = "room_a",
            Position = new System.Numerics.Vector3(3.5f, 0f, 1.4f),
            YawDegrees = 180f,
            Scale = 1.5f
        });
        level.ModelPlacements.Add(new ModelPlacementDef
        {
            Id = "prop_computer",
            ModelPath = ModelCatalog.GlbPath("computer.glb"),
            SectorId = "room_a",
            Position = new System.Numerics.Vector3(3.5f, 1.125f, 2.5f),
            YawDegrees = 180f,
            Scale = 1.5f
        });

        return level;
    }
}
