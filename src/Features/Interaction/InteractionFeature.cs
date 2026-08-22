using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.Interaction;

public sealed class InteractionFeature : FeatureBase
{
    private readonly IInteractableSource[] _sources;

    public InteractionFeature(params IInteractableSource[] sources)
    {
        _sources = sources;
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        world.FocusedInteractableId = null;
        world.UsePrompt = string.Empty;

        InteractableHit? best = null;
        foreach (var source in _sources)
        {
            if (!source.TryPickFocused(world, out var hit))
            {
                continue;
            }

            if (best is null || hit.Distance < best.Value.Distance)
            {
                best = hit;
            }
        }

        if (best is { } focused)
        {
            world.FocusedInteractableId = focused.Id;
            world.UsePrompt = focused.Prompt;
        }

        if (input.UsePressed && world.FocusedInteractableId is { } id)
        {
            events.Publish(new UseRequested(id));
        }
    }
}
