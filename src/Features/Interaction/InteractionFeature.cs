using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.Interaction;

public sealed class InteractionFeature : FeatureBase
{
    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        // Stub: no raycast yet. Clear focus each frame.
        world.FocusedInteractableId = null;
        world.UsePrompt = string.Empty;

        if (input.UsePressed && world.FocusedInteractableId is { } id)
        {
            events.Publish(new UseRequested(id));
        }
    }
}
