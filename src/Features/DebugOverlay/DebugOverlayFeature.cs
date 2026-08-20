using ColdAudit.Features.Physics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.DebugOverlay;

/// <summary>
/// F1 debug mode + on-screen HUD. 3D physics debug is drawn earlier by
/// <see cref="PhysicsDebugDrawFeature"/> (with the level, before prop meshes).
/// </summary>
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

    private readonly PhysicsFeature _physics;

    public DebugOverlayFeature(PhysicsFeature physics)
    {
        _physics = physics;
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (input.ToggleDebugPressed)
        {
            world.DebugDraw = world.DebugDraw switch
            {
                DebugDrawMode.Off => DebugDrawMode.Wireframe,
                DebugDrawMode.Wireframe => DebugDrawMode.SolidWalls,
                _ => DebugDrawMode.Off
            };
        }

        if (world.DebugDraw == DebugDrawMode.Off || world.ActiveLevel is null)
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
        if (world.DebugDraw == DebugDrawMode.Off)
        {
            return;
        }

        var y = 80;
        var room = string.IsNullOrEmpty(world.CurrentSectorId) ? "(none)" : world.CurrentSectorId;
        Raylib.DrawText($"room: {room}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"visible: {string.Join(",", world.VisibleSectorIds)}", 12, y, 14, Color.Lime);
        y += 18;
        var cull = world.SectorCullEnabled ? "ON" : "OFF";
        Raylib.DrawText($"sector cull: {cull}  (F2)", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"pos: {world.PlayerPosition.X:0.0}, {world.PlayerPosition.Y:0.0}, {world.PlayerPosition.Z:0.0}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"physics: bodies={_physics.StaticBodyCount} walls={_physics.DebugWalls.Count}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"items: {string.Join(",", world.CarriedItemIds)}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"fps: {Raylib.GetFPS()}  debug: {DebugModeLabel(world.DebugDraw)} (F1)", 12, y, 14, Color.Lime);
        y += 18;
        var pbrTextures = world.PbrTexturesEnabled ? "ON" : "OFF";
        Raylib.DrawText($"pbr textures: {pbrTextures}  (F3)", 12, y, 14, Color.Lime);
        y += 18;
        var lighting = world.Lighting is { IsLoaded: true } && world.LightingEnabled
            ? $"ON lights={world.Lighting.Lights.Count}"
            : "OFF";
        Raylib.DrawText($"lighting: {lighting}  (F4)", 12, y, 14, Color.Lime);
        y += 18;
        var fullscreen = Raylib.IsWindowFullscreen() ? "ON" : "OFF";
        Raylib.DrawText($"fullscreen: {fullscreen}  (F11)", 12, y, 14, Color.Lime);
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
            var asset = !string.IsNullOrWhiteSpace(sector.ModelPath) && File.Exists(sector.ModelPath)
                ? Path.GetFileName(sector.ModelPath)
                : "placeholder";
            var enabled = sector.RenderEnabled ? "ON" : "OFF";
            Raylib.DrawText(
                $"{i + 1}: {sector.Id} [{enabled}] {asset}",
                12,
                y,
                14,
                Color.Lime);
            y += 18;
        }

        Raylib.DrawFPS(Shared.Rendering.UiFramebuffer.Width - 24, 12);
    }

    private static string DebugModeLabel(DebugDrawMode mode) => mode switch
    {
        DebugDrawMode.Wireframe => "wireframe",
        DebugDrawMode.SolidWalls => "solid",
        _ => "off"
    };
}
