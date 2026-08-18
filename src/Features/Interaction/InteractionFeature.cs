using ColdAudit.Features.DoorsAccess;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.Interaction;

public sealed class InteractionFeature : FeatureBase
{
    private readonly DoorsAccessFeature _doors;

    public InteractionFeature(DoorsAccessFeature doors)
    {
        _doors = doors;
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        world.FocusedInteractableId = null;
        world.UsePrompt = string.Empty;

        if (_doors.TryPickFocused(world, out var door))
        {
            world.FocusedInteractableId = door.Id;
            world.UsePrompt = door.Prompt;
        }

        if (input.UsePressed && world.FocusedInteractableId is { } id)
        {
            events.Publish(new UseRequested(id));
        }
    }
}
