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
        Level = CreatePlaceholderLevel(Session.LevelId);
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
        level.ModelPlacements.Add(new ModelPlacementDef
        {
            Id = "prop_monkey",
            ModelPath = ModelCatalog.GlbPath("monkey.glb"),
            SectorId = "room_a",
            Position = new System.Numerics.Vector3(0f, 0f, 4f),
            YawDegrees = 180f,
            Scale = 1f
        });

        return level;
    }
}
