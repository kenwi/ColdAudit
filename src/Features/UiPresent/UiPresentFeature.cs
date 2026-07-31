using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.UiPresent;

public sealed class UiPresentFeature : FeatureBase
{
    private GameWorld? _world;

    public override void Load(GameWorld world, EventBus events)
    {
        _world = world;
        world.Ui.Load();
    }

    public override void Draw(GameWorld world)
    {
        world.Ui.EndAndPresent();
    }

    public override void Unload()
    {
        _world?.Ui.Unload();
        _world = null;
    }
}
