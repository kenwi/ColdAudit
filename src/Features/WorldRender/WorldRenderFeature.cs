using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.WorldRender;

/// <summary>
/// Owns the shared player camera on <see cref="GameWorld.Draw"/>.
/// </summary>
public sealed class WorldRenderFeature : FeatureBase
{
    public override void Load(GameWorld world, EventBus events)
    {
        world.Draw.SyncFromPlayer(world.PlayerPosition, world.PlayerYaw, world.PlayerPitch);
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        world.Draw.SyncFromPlayer(world.PlayerPosition, world.PlayerYaw, world.PlayerPitch);
    }
}
