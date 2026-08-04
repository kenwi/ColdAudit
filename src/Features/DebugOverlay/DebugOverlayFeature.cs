using System.Numerics;
using ColdAudit.Features.Physics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
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

    private readonly PhysicsFeature _physics;
    private Camera3D _camera;

    public DebugOverlayFeature(PhysicsFeature physics)
    {
        _physics = physics;
    }

    public override void Load(GameWorld world, EventBus events)
    {
        _camera = new Camera3D
        {
            Position = world.PlayerPosition,
            Target = world.PlayerPosition + Vector3.UnitZ,
            Up = Vector3.UnitY,
            FovY = 70f,
            Projection = CameraProjection.Perspective
        };
    }

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

        DrawPhysicsWireframes(world);

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
        Raylib.DrawText($"physics: bodies={_physics.StaticBodyCount}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"items: {string.Join(",", world.CarriedItemIds)}", 12, y, 14, Color.Lime);
        y += 18;
        Raylib.DrawText($"fps: {Raylib.GetFPS()}  (F1 debug)", 12, y, 14, Color.Lime);
        y += 18;
        var lighting = world.Lighting is { IsLoaded: true } lightingState
            ? $"ON lights={lightingState.Lights.Count}"
            : "OFF";
        Raylib.DrawText($"lighting: {lighting}", 12, y, 14, Color.Lime);
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
            var indexLabel = i < 9 ? $"{i + 1}" : "-";
            var enabled = sector.RenderEnabled ? "ON" : "OFF";
            var asset = !string.IsNullOrWhiteSpace(sector.ModelPath) && File.Exists(sector.ModelPath)
                ? "file"
                : "missing";
            Raylib.DrawText($"{indexLabel} {sector.Id} [{enabled}] {asset}", 12, y, 14, Color.Lime);
            y += 18;
        }

        y += 6;
        var placements = world.ActiveLevel.ModelPlacements;
        Raylib.DrawText($"model props: {placements.Count}", 12, y, 14, Color.Lime);
        y += 18;
        foreach (var placement in placements)
        {
            var fileName = Path.GetFileName(placement.ModelPath);
            var asset = File.Exists(placement.ModelPath) ? "file" : "missing";
            Raylib.DrawText(
                $"{placement.Id} {fileName} @{placement.Position.X:0.0},{placement.Position.Y:0.0},{placement.Position.Z:0.0} [{asset}]",
                12,
                y,
                14,
                Color.Lime);
            y += 18;
        }
    }

    private void DrawPhysicsWireframes(GameWorld world)
    {
        if (!_physics.TryGetDebugSnapshot(out var snapshot) || snapshot.Segments.Count == 0)
        {
            return;
        }

        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);
        _camera.Position = world.PlayerPosition;
        _camera.Target = world.PlayerPosition + forward;

        Raylib.BeginMode3D(_camera);
        foreach (var segment in snapshot.Segments)
        {
            var a = new Vector3((float)segment.A.X, (float)segment.A.Y, (float)segment.A.Z);
            var b = new Vector3((float)segment.B.X, (float)segment.B.Y, (float)segment.B.Z);
            Raylib.DrawLine3D(a, b, ToRayColor(segment.Rgb, segment.Alpha));
        }

        Raylib.EndMode3D();
    }

    private static Color ToRayColor(uint rgb, float alpha)
    {
        var r = (byte)((rgb >> 16) & 0xFF);
        var g = (byte)((rgb >> 8) & 0xFF);
        var b = (byte)(rgb & 0xFF);
        var a = (byte)System.Math.Clamp((int)(alpha * 255f), 0, 255);
        return new Color(r, g, b, a);
    }
}
