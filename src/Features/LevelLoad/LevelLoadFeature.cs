using ColdAudit.Shared.Assets;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.LevelLoad;

public sealed class LevelLoadFeature : FeatureBase
{
    public LevelData? Level { get; private set; }
    public LevelSession Session { get; } = new();

    public override void Load(GameWorld world, EventBus events)
    {
        Session.LevelId = LevelCatalog.WingB;
        Level = CreatePlaceholderLevel(Session.LevelId);
        Session.IsLoaded = true;

        world.PlayerPosition = Level.PlayerSpawn;
        world.PlayerYaw = Level.PlayerSpawnYaw;
        world.CurrentSectorId = Level.Sectors.Count > 0 ? Level.Sectors[0].Id : "room_a";
        world.VisibleSectorIds.Clear();
        world.VisibleSectorIds.Add(world.CurrentSectorId);
        world.MissionMessage = "COLD AUDIT // SCOPE: Wing B // Retrieve FINANCE-DR // Exfil lobby";
    }

    public override void Unload()
    {
        Level = null;
        Session.IsLoaded = false;
    }

    private static LevelData CreatePlaceholderLevel(string levelId)
    {
        // Stand-in until Blender wing_b.glb + sidecar exist.
        return new LevelData
        {
            LevelId = levelId,
            PlayerSpawn = new System.Numerics.Vector3(0f, 1.7f, 0f),
            Sectors =
            {
                new SectorDef { Id = "room_a" },
                new SectorDef { Id = "room_b" },
                new SectorDef { Id = "room_c" },
                new SectorDef { Id = "room_d" }
            },
            Portals =
            {
                new PortalDef { Id = "portal_a_b", FromSectorId = "room_a", ToSectorId = "room_b" },
                new PortalDef { Id = "portal_a_c", FromSectorId = "room_a", ToSectorId = "room_c" },
                new PortalDef { Id = "portal_c_d", FromSectorId = "room_c", ToSectorId = "room_d" }
            }
        };
    }
}
