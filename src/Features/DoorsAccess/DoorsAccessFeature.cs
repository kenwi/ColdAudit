using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.DoorsAccess;

public sealed class DoorState
{
    public string Id { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public Vector3 HingePosition { get; init; }
    public float ClosedYaw { get; init; }
    public float Width { get; init; } = 1.5f;
    public float Height { get; init; } = 2.1f;
    public float Thickness { get; init; } = 0.08f;
    public float OpenAngle { get; init; } = MathF.PI * 0.5f;
    public float InteractRadius { get; init; } = 2.5f;
    public string? ModelPath { get; init; }
    public bool Locked { get; set; }
    public bool IsOpen { get; set; }
    public float OpenAmount { get; set; }
    public float LockDeniedTime { get; set; }

    /// <summary>+1 / -1. Chosen when opening so the slab swings away from the player.</summary>
    public float SwingSign { get; set; } = 1f;

    public float CurrentYaw => ClosedYaw + OpenAmount * OpenAngle * SwingSign;

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);

    public string Prompt
    {
        get
        {
            if (LockDeniedTime > 0f)
            {
                return "Cannot open: locked";
            }

            if (Locked)
            {
                return "Door: Locked  [U] Unlock";
            }

            if (OpenAmount < 0.01f && !IsOpen)
            {
                return "Door: Closed  [E] Open  [L] Lock";
            }

            if (OpenAmount > 0.99f && IsOpen)
            {
                return "Door: Open  [E] Close  [L] Lock";
            }

            return IsOpen
                ? $"Door: Opening {(int)(OpenAmount * 100f)}%"
                : $"Door: Closing {(int)(OpenAmount * 100f)}%";
        }
    }
}

public sealed class DoorsAccessFeature : FeatureBase
{
    private const float OpenSpeed = 2f;
    private const float LockDeniedDuration = 1.75f;
    private static readonly Color DoorFill = new(118, 82, 48, 255);
    private static readonly Color DoorFillFocused = new(168, 124, 72, 255);
    private static readonly Color DoorWire = new(42, 28, 16, 255);

    private readonly List<DoorState> _doors = [];
    private readonly Dictionary<string, ModelHandle> _handlesByPath = new(StringComparer.Ordinal);
    private Camera3D _camera;

    public IReadOnlyList<DoorState> Doors => _doors;

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

        _doors.Clear();
        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        foreach (var def in level.Doors)
        {
            _doors.Add(new DoorState
            {
                Id = def.Id,
                SectorId = def.SectorId,
                HingePosition = def.HingePosition,
                ClosedYaw = MathUtil.DegToRad(def.ClosedYawDegrees),
                Width = def.Width,
                Height = def.Height,
                Thickness = def.Thickness,
                OpenAngle = MathF.Abs(MathUtil.DegToRad(def.OpenAngleDegrees)),
                InteractRadius = def.InteractRadius,
                ModelPath = def.ModelPath,
                Locked = def.Locked
            });

            TryLoadModel(world, def.ModelPath);
        }
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (world.FocusedInteractableId is { } focusedId)
        {
            var focused = Find(focusedId);
            if (focused is not null)
            {
                if (input.UnlockPressed)
                {
                    focused.Locked = false;
                    focused.LockDeniedTime = 0f;
                }

                if (input.LockPressed)
                {
                    focused.Locked = true;
                }

                world.UsePrompt = focused.Prompt;
            }
        }

        foreach (var use in events.OfType<UseRequested>())
        {
            var door = Find(use.InteractableId);
            if (door is null)
            {
                continue;
            }

            if (door.Locked && !door.IsOpen)
            {
                door.LockDeniedTime = LockDeniedDuration;
                world.UsePrompt = door.Prompt;
                continue;
            }

            door.IsOpen = !door.IsOpen;
            if (door.IsOpen && door.OpenAmount < 0.01f)
            {
                door.SwingSign = SwingAwayFromPlayer(world.PlayerPosition, door);
            }
        }

        foreach (var door in _doors)
        {
            if (door.LockDeniedTime > 0f)
            {
                door.LockDeniedTime = MathF.Max(0f, door.LockDeniedTime - dt);
            }

            var target = door.IsOpen ? 1f : 0f;
            door.OpenAmount = MathUtil.MoveTowards(door.OpenAmount, target, OpenSpeed * dt);
        }
    }

    public override void Draw(GameWorld world)
    {
        if (_doors.Count == 0)
        {
            return;
        }

        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);
        _camera.Position = world.PlayerPosition;
        _camera.Target = world.PlayerPosition + forward;

        EnsureModelLighting(world);

        Raylib.BeginMode3D(_camera);

        foreach (var door in _doors)
        {
            if (!IsDoorDrawn(world, door) || !TryGetModel(door, out _))
            {
                continue;
            }

            DrawDoor(door, world.FocusedInteractableId == door.Id);
        }

        var lighting = world.Lighting is { IsLoaded: true } lit ? lit : null;
        var useLighting = lighting is not null && lighting.TryBeginShaderMode();

        foreach (var door in _doors)
        {
            if (!IsDoorDrawn(world, door) || TryGetModel(door, out _))
            {
                continue;
            }

            DrawDoor(door, world.FocusedInteractableId == door.Id);
        }

        if (useLighting)
        {
            lighting!.EndShaderMode();
        }

        Raylib.EndMode3D();
    }

    public override void Unload()
    {
        foreach (var handle in _handlesByPath.Values)
        {
            BasicLighting.DetachFromModel(handle);
            handle.Dispose();
        }

        _handlesByPath.Clear();
        _doors.Clear();
    }

    public bool TryPickFocused(GameWorld world, out DoorState door)
    {
        door = null!;
        var origin = world.PlayerPosition;
        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);

        DoorState? best = null;
        var bestDistance = float.MaxValue;

        foreach (var candidate in _doors)
        {
            if (!IsDoorDrawn(world, candidate))
            {
                continue;
            }

            if (!IsPlayerInRadius(origin, candidate))
            {
                continue;
            }

            if (!TryRaycast(candidate, origin, forward, out var distance) || distance >= bestDistance)
            {
                continue;
            }

            best = candidate;
            bestDistance = distance;
        }

        if (best is null)
        {
            return false;
        }

        door = best;
        return true;
    }

    private void DrawDoor(DoorState door, bool focused)
    {
        if (TryGetModel(door, out var handle))
        {
            Raylib.DrawModelEx(
                handle.Model,
                door.HingePosition,
                Vector3.UnitY,
                MathUtil.RadToDeg(door.CurrentYaw),
                Vector3.One,
                Color.White);
            return;
        }

        var fill = focused ? DoorFillFocused : DoorFill;
        Rlgl.PushMatrix();
        Rlgl.Translatef(door.HingePosition.X, door.HingePosition.Y, door.HingePosition.Z);
        Rlgl.Rotatef(MathUtil.RadToDeg(door.CurrentYaw), 0f, 1f, 0f);
        Rlgl.Translatef(door.Width * 0.5f, door.Height * 0.5f, 0f);
        Raylib.DrawCube(Vector3.Zero, door.Width, door.Height, door.Thickness, fill);
        Raylib.DrawCubeWires(Vector3.Zero, door.Width, door.Height, door.Thickness, DoorWire);
        Rlgl.PopMatrix();
    }

    private bool TryRaycast(DoorState door, Vector3 origin, Vector3 direction, out float distance)
    {
        if (TryGetModel(door, out var handle))
        {
            return TryRaycastModel(handle, door, origin, direction, out distance);
        }

        var invYaw = Matrix4x4.CreateRotationY(-door.CurrentYaw);
        var localOrigin = Vector3.Transform(origin - door.HingePosition, invYaw);
        var localDir = Vector3.TransformNormal(direction, invYaw);
        if (localDir.LengthSquared() < 1e-8f)
        {
            distance = 0f;
            return false;
        }

        localDir = Vector3.Normalize(localDir);
        var localBox = new Aabb(
            new Vector3(0f, 0f, -door.Thickness * 0.5f),
            new Vector3(door.Width, door.Height, door.Thickness * 0.5f));
        return localBox.TryIntersectRay(localOrigin, localDir, out distance);
    }

    private static bool TryRaycastModel(
        ModelHandle handle,
        DoorState door,
        Vector3 origin,
        Vector3 direction,
        out float distance)
    {
        distance = float.MaxValue;
        var ray = new Ray(origin, direction);
        var transform =
            Matrix4x4.CreateRotationY(door.CurrentYaw) *
            Matrix4x4.CreateTranslation(door.HingePosition);

        var hit = false;
        var meshes = handle.Model.MeshesAsSpan();
        for (var i = 0; i < meshes.Length; i++)
        {
            var collision = Raylib.GetRayCollisionMesh(ray, meshes[i], transform);
            if (!collision.Hit || collision.Distance >= distance)
            {
                continue;
            }

            hit = true;
            distance = collision.Distance;
        }

        return hit;
    }

    private static float SwingAwayFromPlayer(Vector3 playerPosition, DoorState door)
    {
        var closedNormal = Vector3.Transform(Vector3.UnitZ, Matrix4x4.CreateRotationY(door.ClosedYaw));
        var hingeToCenter = Vector3.Transform(
            new Vector3(door.Width * 0.5f, 0f, 0f),
            Matrix4x4.CreateRotationY(door.ClosedYaw));
        var toPlayer = playerPosition - (door.HingePosition + hingeToCenter);
        toPlayer.Y = 0f;

        // +yaw swings the slab toward local -Z, so a player on +Z needs +sign (away).
        return Vector3.Dot(toPlayer, closedNormal) >= 0f ? 1f : -1f;
    }

    private static bool IsPlayerInRadius(Vector3 playerPosition, DoorState door)
    {
        var hingeToCenter = Vector3.Transform(
            new Vector3(door.Width * 0.5f, 0f, 0f),
            Matrix4x4.CreateRotationY(door.ClosedYaw));
        var center = door.HingePosition + hingeToCenter;
        var dx = playerPosition.X - center.X;
        var dz = playerPosition.Z - center.Z;
        var radius = door.InteractRadius;
        return dx * dx + dz * dz <= radius * radius;
    }

    private static bool IsDoorDrawn(GameWorld world, DoorState door)
    {
        if (string.IsNullOrEmpty(door.SectorId) || !world.SectorCullEnabled)
        {
            return true;
        }

        return world.VisibleSectorIds.Contains(door.SectorId);
    }

    private DoorState? Find(string id)
    {
        foreach (var door in _doors)
        {
            if (door.Id == id)
            {
                return door;
            }
        }

        return null;
    }

    private void EnsureModelLighting(GameWorld world)
    {
        if (world.Lighting is not { IsLoaded: true } lighting)
        {
            return;
        }

        foreach (var handle in _handlesByPath.Values)
        {
            lighting.ApplyToModel(handle);
        }
    }

    private void TryLoadModel(GameWorld world, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _handlesByPath.ContainsKey(path) || !File.Exists(path))
        {
            return;
        }

        var handle = new ModelHandle();
        handle.Load(path);
        world.Lighting?.ApplyToModel(handle);
        _handlesByPath[path] = handle;
    }

    private bool TryGetModel(DoorState door, out ModelHandle handle)
    {
        handle = null!;
        return door.HasModel &&
               door.ModelPath is not null &&
               _handlesByPath.TryGetValue(door.ModelPath, out handle!) &&
               handle.IsLoaded;
    }
}
