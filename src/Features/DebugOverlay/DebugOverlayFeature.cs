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

    private static readonly Color SolidWallColor = new(80, 160, 200, 255);

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

        switch (world.DebugDraw)
        {
            case DebugDrawMode.Wireframe:
                DrawPhysicsWireframes(world);
                break;
            case DebugDrawMode.SolidWalls:
                DrawSolidWalls(world);
                break;
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

    private static string DebugModeLabel(DebugDrawMode mode) => mode switch
    {
        DebugDrawMode.Wireframe => "wireframe",
        DebugDrawMode.SolidWalls => "solid",
        _ => "off"
    };

    private void DrawPhysicsWireframes(GameWorld world)
    {
        if (!_physics.TryGetDebugSnapshot(out var snapshot) || snapshot.Segments.Count == 0)
        {
            return;
        }

        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);
        _camera.Position = world.PlayerPosition;
        _camera.Target = world.PlayerPosition + forward;

        List<Aabb>? cullVolumes = null;
        if (world.SectorCullEnabled && world.ActiveLevel is not null)
        {
            cullVolumes = BuildWireframeCullVolumes(world);
        }

        var hasFloor = _physics.TryGetFloorBounds(out var floorBounds);

        Raylib.BeginMode3D(_camera);
        foreach (var segment in snapshot.Segments)
        {
            var a = new Vector3((float)segment.A.X, (float)segment.A.Y, (float)segment.A.Z);
            var b = new Vector3((float)segment.B.X, (float)segment.B.Y, (float)segment.B.Z);
            if (hasFloor && IsFloorWireSegment(a, b, floorBounds))
            {
                continue;
            }

            if (cullVolumes is not null && !SegmentInVolumes(a, b, cullVolumes))
            {
                continue;
            }

            Raylib.DrawLine3D(a, b, ToRayColor(segment.Rgb, segment.Alpha));
        }

        Raylib.EndMode3D();
    }

    private void DrawSolidWalls(GameWorld world)
    {
        if (_physics.DebugWalls.Count == 0)
        {
            return;
        }

        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);
        _camera.Position = world.PlayerPosition;
        _camera.Target = world.PlayerPosition + forward;

        List<Aabb>? cullVolumes = null;
        if (world.SectorCullEnabled && world.ActiveLevel is not null)
        {
            cullVolumes = BuildWireframeCullVolumes(world);
        }

        Raylib.BeginMode3D(_camera);
        Raylib.BeginBlendMode(BlendMode.Alpha);
        foreach (var wall in _physics.DebugWalls)
        {
            if (cullVolumes is not null && !PointInVolumes(wall.Center, cullVolumes))
            {
                continue;
            }

            // Double-sided so walls read from either room.
            Raylib.DrawTriangle3D(wall.BottomLeft, wall.BottomRight, wall.TopRight, SolidWallColor);
            Raylib.DrawTriangle3D(wall.BottomLeft, wall.TopRight, wall.TopLeft, SolidWallColor);
            Raylib.DrawTriangle3D(wall.BottomLeft, wall.TopRight, wall.BottomRight, SolidWallColor);
            Raylib.DrawTriangle3D(wall.BottomLeft, wall.TopLeft, wall.TopRight, SolidWallColor);
        }

        Raylib.EndBlendMode();
        Raylib.EndMode3D();
    }

    /// <summary>
    /// Long near-horizontal edges of the continuous floor AABB (longer than a room face).
    /// </summary>
    private static bool IsFloorWireSegment(Vector3 a, Vector3 b, Aabb floorBounds)
    {
        var floorY = LevelCollisionBuilder.FloorY;
        if (System.MathF.Abs(a.Y - floorY) > 0.05f || System.MathF.Abs(b.Y - floorY) > 0.05f)
        {
            return false;
        }

        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        var xzLen = System.MathF.Sqrt(dx * dx + dz * dz);
        // Room faces are at most Extent; floor perimeter edges span the union.
        if (xzLen <= DebugSectorLayout.Extent + 0.5f)
        {
            return false;
        }

        // Confirm the segment sits on the floor footprint.
        var mid = (a + b) * 0.5f;
        return floorBounds.ContainsXz(mid);
    }

    private static List<Aabb> BuildWireframeCullVolumes(GameWorld world)
    {
        var level = world.ActiveLevel!;
        var volumes = new List<Aabb>();
        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < level.Sectors.Count; i++)
        {
            indexById[level.Sectors[i].Id] = i;
        }

        const float pad = 0.5f;
        foreach (var sector in level.Sectors)
        {
            if (!sector.RenderEnabled || !world.VisibleSectorIds.Contains(sector.Id))
            {
                continue;
            }

            volumes.Add(PadXz(sector.Bounds, pad));
        }

        foreach (var portal in level.Portals)
        {
            if (!indexById.TryGetValue(portal.FromSectorId, out var fromIndex) ||
                !indexById.TryGetValue(portal.ToSectorId, out var toIndex))
            {
                continue;
            }

            var from = level.Sectors[fromIndex];
            var to = level.Sectors[toIndex];
            var fromVisible = from.RenderEnabled && world.VisibleSectorIds.Contains(from.Id);
            var toVisible = to.RenderEnabled && world.VisibleSectorIds.Contains(to.Id);
            if (!fromVisible && !toVisible)
            {
                continue;
            }

            volumes.Add(PadXz(DebugSectorLayout.PortalBounds(fromIndex, toIndex), pad));
        }

        return volumes;
    }

    private static Aabb PadXz(Aabb bounds, float pad) =>
        new(
            new Vector3(bounds.Min.X - pad, bounds.Min.Y, bounds.Min.Z - pad),
            new Vector3(bounds.Max.X + pad, bounds.Max.Y, bounds.Max.Z + pad));

    private static bool SegmentInVolumes(Vector3 a, Vector3 b, List<Aabb> volumes)
    {
        var mid = (a + b) * 0.5f;
        return PointInVolumes(a, volumes) || PointInVolumes(b, volumes) || PointInVolumes(mid, volumes);
    }

    private static bool PointInVolumes(Vector3 point, List<Aabb> volumes)
    {
        foreach (var volume in volumes)
        {
            if (volume.ContainsXz(point))
            {
                return true;
            }
        }

        return false;
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
