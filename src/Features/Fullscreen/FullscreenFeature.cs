using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.Fullscreen;

/// <summary>
/// Toggles the window between windowed and fullscreen.
/// </summary>
public sealed class FullscreenFeature : FeatureBase
{
    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (!input.ToggleFullscreenPressed)
        {
            return;
        }

        Raylib.ToggleFullscreen();
    }
}
