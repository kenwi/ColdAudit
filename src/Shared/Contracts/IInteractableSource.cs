using ColdAudit.Shared.World;

namespace ColdAudit.Shared.Contracts;

/// <summary>
/// A look-at candidate for <c>InteractionFeature</c>. Closer hits win when several
/// sources report a focus in the same frame.
/// </summary>
public readonly record struct InteractableHit(string Id, string Prompt, float Distance);

public interface IInteractableSource
{
    bool TryPickFocused(GameWorld world, out InteractableHit hit);
}
