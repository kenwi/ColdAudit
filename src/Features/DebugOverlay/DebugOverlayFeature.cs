using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.DebugOverlay;

public sealed class DebugOverlayFeature : FeatureBase
{
    private static readonly KeyboardKey[] SectorToggleKeys =
    [
        KeyboardKey.One,
        KeyboardKey.Two,
        KeyboardKey.Three,
        KeyboardKey.Four,
        KeyboardKey.Five,
        KeyboardKey.Six,
        KeyboardKey.Seven,
        KeyboardKey.Eight,
        KeyboardKey.Nine
    ];

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (input.ToggleDebugPressed)
        {
            world.DebugDrawEnabled = !world.DebugDrawEnabled;
        }

        if (!world.DebugDrawEnabled || world.ActiveLevel is null)
        {
            return;
        }

        var sectors = world.ActiveLevel.Sectors;
        for (var i = 0; i < SectorToggleKeys.Length && i < sectors.Count; i++)
        {
            if (Raylib.IsKeyPressed(SectorToggleKeys[i]))
            {
                sectors[i].RenderEnabled = !sectors[i].RenderEnabled;
            }
        }
    }

    public override void Draw(GameWorld world)
    {
        if (!world.DebugDrawEnabled)
        {
            return;
        }

        var y = 80;
        var room = string.IsNullOrEmpty(world.CurrentSectorId) ? "(none)" : world.CurrentSectorId;
        Raylib.DrawText($"room: {room}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"visible: {string.Join(",", world.VisibleSectorIds)}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"pos: {world.PlayerPosition.X:0.0}, {world.PlayerPosition.Y:0.0}, {world.PlayerPosition.Z:0.0}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"items: {string.Join(",", world.CarriedItemIds)}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"fps: {Raylib.GetFPS()}  (F1 debug)", 12, y, 14, Color.Lime);
        y += 24;

        if (world.ActiveLevel is null)
        {
            return;
        }

        Raylib.DrawText("sector meshes (1-9 toggle):", 12, y, 14, Color.Lime);
        y += 18;

        var sectors = world.ActiveLevel.Sectors;
        for (var i = 0; i < sectors.Count; i++)
        {
            var sector = sectors[i];
            var indexLabel = i < 9 ? $"{i + 1}" : "-";
            var enabled = sector.RenderEnabled ? "ON" : "OFF";
            var asset = !string.IsNullOrWhiteSpace(sector.ModelPath) && File.Exists(sector.ModelPath)
                ? "file"
                : "missing";
            Raylib.DrawText($"{indexLabel} {sector.Id} [{enabled}] {asset}", 12, y, 14, Color.Lime);
            y += 18;
        }
    }
}
