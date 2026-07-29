using ColdAudit.Shared.Assets;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.LevelLoad;

public sealed class LevelLoadFeature : FeatureBase
{
    public const int DefaultLevelNumber = 1;

    private readonly JsonLevelLoader _loader = new();

    public LevelData? Level { get; private set; }
    public LevelSession Session { get; } = new();

    public override void Load(GameWorld world, EventBus events)
    {
        var levelNumber = DefaultLevelNumber;
        Level = _loader.Load(levelNumber, loadModels: true);
        Session.LevelNumber = levelNumber;
        Session.LevelId = Level.LevelId;
        Session.IsLoaded = true;

        world.PlayerPosition = Level.PlayerSpawn;
        world.PlayerYaw = Level.PlayerSpawnYaw;
        world.CurrentSectorId = Level.StartSectorId;
        world.VisibleSectorIds.Clear();
        world.VisibleSectorIds.Add(world.CurrentSectorId);
        world.MissionMessage = string.IsNullOrWhiteSpace(Level.MissionMessage)
            ? $"COLD AUDIT // {Level.Name}"
            : Level.MissionMessage;
    }

    public override void Unload()
    {
        Level?.UnloadAssets();
        Level = null;
        Session.IsLoaded = false;
    }
}
