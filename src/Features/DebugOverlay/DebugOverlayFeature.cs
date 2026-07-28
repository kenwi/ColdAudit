using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.DebugOverlay;

public sealed class DebugOverlayFeature : FeatureBase
{
    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (input.ToggleDebugPressed)
        {
            world.DebugDrawEnabled = !world.DebugDrawEnabled;
        }
    }

    public override void Draw(GameWorld world)
    {
        if (!world.DebugDrawEnabled)
        {
            return;
        }

        var y = 80;
        Raylib.DrawText($"sector: {world.CurrentSectorId}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"visible: {string.Join(",", world.VisibleSectorIds)}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"pos: {world.PlayerPosition.X:0.0}, {world.PlayerPosition.Y:0.0}, {world.PlayerPosition.Z:0.0}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"items: {string.Join(",", world.CarriedItemIds)}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"fps: {Raylib.GetFPS()}  (F1 debug)", 12, y, 14, Color.Lime);
    }
}
