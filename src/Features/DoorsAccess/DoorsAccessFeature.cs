using System.Numerics;
using ColdAudit.Features.Inventory;
using ColdAudit.Features.LevelLoad;
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
    public DoorMotion Motion { get; init; } = DoorMotion.Swing;

    /// <summary>Swing hinge, or sliding-doorway center, on the floor.</summary>
    public Vector3 HingePosition { get; init; }

    public float ClosedYaw { get; init; }
    public float Width { get; init; } = 1.5f;
    public float Height { get; init; } = 2.1f;
    public float Thickness { get; init; } = 0.08f;
    public float OpenAngle { get; init; } = MathF.PI * 0.5f;
    public float SlideTravel { get; init; }
    public float InteractRadius { get; init; } = 2.5f;
    public string? ModelPath { get; init; }
    public string? RequiredItemId { get; init; }
    public Color Color { get; init; }
    public bool Locked { get; set; }
    public bool IsOpen { get; set; }
    public float OpenAmount { get; set; }
    public float LockDeniedTime { get; set; }

    /// <summary>When true, start an auto-close countdown after the door is fully open.</summary>
    public bool AutoClose { get; init; }

    /// <summary>Seconds fully open before auto-close fires.</summary>
    public float AutoCloseSeconds { get; init; } = 3f;

    /// <summary>Countdown while fully open; 0 when idle.</summary>
    public float AutoCloseRemaining { get; set; }

    /// <summary>True after the fully-open auto-close timer has been armed this open cycle.</summary>
    public bool AutoCloseArmed { get; set; }

    /// <summary>+1 / -1. Chosen when opening so a swing slab moves away from the player.</summary>
    public float SwingSign { get; set; } = 1f;

    public bool IsSlidingDouble => Motion == DoorMotion.SlidingDouble;

    public float LeafWidth => Width * 0.5f;

    public float CurrentYaw => IsSlidingDouble
        ? ClosedYaw
        : ClosedYaw + OpenAmount * OpenAngle * SwingSign;

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);
    public bool RequiresItem => !string.IsNullOrWhiteSpace(RequiredItemId);

    public string GetPrompt(bool hasRequiredItem)
    {
        var label = IsSlidingDouble ? "Double door" : "Door";

        if (LockDeniedTime > 0f)
        {
            return "Cannot open: locked";
        }

        if (Locked)
        {
            if (RequiresItem)
            {
                return hasRequiredItem
                    ? $"{label}: Locked  [E] Use keycard"
                    : $"{label}: Locked";
            }

            return $"{label}: Locked  [U] Unlock";
        }

        if (OpenAmount < 0.01f && !IsOpen)
        {
            return $"{label}: Closed  [E] Open  [L] Lock";
        }

        if (OpenAmount > 0.99f && IsOpen)
        {
            return $"{label}: Open  [E] Close";
        }

        return IsOpen
            ? $"{label}: Opening {(int)(OpenAmount * 100f)}%"
            : $"{label}: Closing {(int)(OpenAmount * 100f)}%";
    }
}

public sealed class DoorsAccessFeature : FeatureBase, IShadowCaster, IInteractableSource
{
    private const float OpenSpeed = 2f;
    private const float LockDeniedDuration = 1.75f;
    private static readonly Color DoorFill = new(118, 82, 48, 255);

    private readonly List<DoorState> _doors = [];
    private readonly Dictionary<string, ModelHandle> _handlesByPath = new(StringComparer.Ordinal);
    private readonly LitBoxMesh _placeholder = new();

    public IReadOnlyList<DoorState> Doors => _doors;

    public override void Load(GameWorld world, EventBus events)
    {
        _doors.Clear();
        _placeholder.Load();
        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        foreach (var def in level.Doors)
        {
            var leafWidth = def.Width * 0.5f;
            _doors.Add(new DoorState
            {
                Id = def.Id,
                SectorId = def.SectorId,
                Motion = def.Motion,
                HingePosition = def.HingePosition,
                ClosedYaw = MathUtil.DegToRad(def.ClosedYawDegrees),
                Width = def.Width,
                Height = def.Height,
                Thickness = def.Thickness,
                OpenAngle = MathF.Abs(MathUtil.DegToRad(def.OpenAngleDegrees)),
                SlideTravel = def.SlideDistance > 1e-4f ? def.SlideDistance : leafWidth,
                InteractRadius = def.InteractRadius,
                ModelPath = def.ModelPath,
                RequiredItemId = def.RequiredItemId,
                Color = def.Color,
                Locked = def.Locked,
                AutoClose = def.AutoClose,
                AutoCloseSeconds = MathF.Max(0f, def.AutoCloseSeconds)
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

                if (input.LockPressed && !focused.IsOpen && focused.OpenAmount < 0.01f)
                {
                    focused.Locked = true;
                }

                world.UsePrompt = focused.GetPrompt(HasRequiredItem(world, focused));
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
                if (HasRequiredItem(world, door))
                {
                    door.Locked = false;
                    door.LockDeniedTime = 0f;
                }
                else
                {
                    door.LockDeniedTime = LockDeniedDuration;
                    world.UsePrompt = door.GetPrompt(false);
                    continue;
                }
            }

            door.IsOpen = !door.IsOpen;
            if (!door.IsOpen)
            {
                door.AutoCloseArmed = false;
                door.AutoCloseRemaining = 0f;
            }
            else if (door.OpenAmount < 0.01f && !door.IsSlidingDouble)
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

            UpdateAutoClose(door, dt);

            var target = door.IsOpen ? 1f : 0f;
            var previous = door.OpenAmount;
            door.OpenAmount = MathUtil.MoveTowards(previous, target, OpenSpeed * dt);
            if (MathF.Abs(door.OpenAmount - previous) > 1e-5f)
            {
                // Moving slabs change what the lights can reach.
                world.InvalidateShadowGeometry();
            }
        }
    }

    private static void UpdateAutoClose(DoorState door, float dt)
    {
        if (!door.AutoClose || !door.IsOpen)
        {
            door.AutoCloseArmed = false;
            door.AutoCloseRemaining = 0f;
            return;
        }

        if (door.OpenAmount < 0.99f)
        {
            return;
        }

        if (!door.AutoCloseArmed)
        {
            door.AutoCloseArmed = true;
            door.AutoCloseRemaining = door.AutoCloseSeconds;
        }

        door.AutoCloseRemaining -= dt;
        if (door.AutoCloseRemaining > 0f)
        {
            return;
        }

        door.IsOpen = false;
        door.AutoCloseArmed = false;
        door.AutoCloseRemaining = 0f;
    }

    public override void Draw(GameWorld world)
    {
        if (_doors.Count == 0)
        {
            return;
        }

        EnsureModelLighting(world);
        _placeholder.EnsureLighting(world.Lighting);

        Raylib.BeginMode3D(world.Draw.Camera);

        foreach (var door in _doors)
        {
            if (!IsDoorDrawn(world, door) || !TryGetModel(door, out _))
            {
                continue;
            }

            DrawDoor(door, world.FocusedInteractableId == door.Id);
        }

        world.Lighting?.RestorePbrDrawDefaults();
        world.Lighting?.SetAlbedoMapEnabled(false);

        foreach (var door in _doors)
        {
            if (!IsDoorDrawn(world, door) || TryGetModel(door, out _))
            {
                continue;
            }

            DrawDoor(door, world.FocusedInteractableId == door.Id);
        }

        world.Lighting?.RestorePbrDrawDefaults();
        Raylib.EndMode3D();
    }

    /// <summary>
    /// A closed slab blocks light through its doorway; an open one clears the opening.
    /// </summary>
    public void DrawDepth(GameWorld world, ShadowPass pass)
    {
        foreach (var door in _doors)
        {
            if (!pass.IncludesSector(door.SectorId))
            {
                continue;
            }

            if (door.IsSlidingDouble)
            {
                DrawSlidingDepth(door, pass);
                continue;
            }

            var yawDegrees = MathUtil.RadToDeg(door.CurrentYaw);
            if (TryGetModel(door, out var handle))
            {
                pass.DrawModel(handle.Model, door.HingePosition, yawDegrees, 1f);
                continue;
            }

            var localCenter = Vector3.Transform(
                new Vector3(door.Width * 0.5f, door.Height * 0.5f, 0f),
                Matrix4x4.CreateRotationY(door.CurrentYaw));
            pass.DrawBox(
                door.HingePosition + localCenter,
                new Vector3(door.Width, door.Height, door.Thickness),
                yawDegrees);
        }
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
        _placeholder.Unload();
    }

    /// <summary>
    /// Closest door slab hit between <paramref name="origin"/> and <paramref name="target"/>.
    /// Used for camera LOS; doors are not Box3D bodies.
    /// </summary>
    public bool TryGetOcclusionHit(Vector3 origin, Vector3 target, out float distance)
    {
        distance = float.MaxValue;
        var delta = target - origin;
        var maxDistance = delta.Length();
        if (maxDistance < 1e-5f)
        {
            return false;
        }

        var direction = delta / maxDistance;
        var hit = false;
        foreach (var door in _doors)
        {
            if (!TryRaycast(door, origin, direction, out var doorDistance) ||
                doorDistance >= maxDistance ||
                doorDistance >= distance)
            {
                continue;
            }

            hit = true;
            distance = doorDistance;
        }

        return hit;
    }

    public bool TryPickFocused(GameWorld world, out InteractableHit hit)
    {
        hit = default;
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

        hit = new InteractableHit(best.Id, best.GetPrompt(HasRequiredItem(world, best)), bestDistance);
        return true;
    }

    private static bool HasRequiredItem(GameWorld world, DoorState door) =>
        door.RequiresItem && InventoryFeature.Has(world, door.RequiredItemId!);

    private void DrawDoor(DoorState door, bool focused)
    {
        if (door.IsSlidingDouble)
        {
            DrawSlidingDoor(door, focused);
            return;
        }

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

        var fill = focused ? Lighten(ResolveFill(door)) : ResolveFill(door);
        var localCenter = Vector3.Transform(
            new Vector3(door.Width * 0.5f, door.Height * 0.5f, 0f),
            Matrix4x4.CreateRotationY(door.CurrentYaw));
        _placeholder.Draw(
            door.HingePosition + localCenter,
            new Vector3(door.Width, door.Height, door.Thickness),
            MathUtil.RadToDeg(door.CurrentYaw),
            fill);
    }

    private void DrawSlidingDoor(DoorState door, bool focused)
    {
        var yawDegrees = MathUtil.RadToDeg(door.ClosedYaw);
        GetSlidingLeafCenters(door, out var leftCenter, out var rightCenter);
        var leafSize = new Vector3(door.LeafWidth, door.Height, door.Thickness);

        if (TryGetModel(door, out var handle))
        {
            Raylib.DrawModelEx(handle.Model, leftCenter, Vector3.UnitY, yawDegrees, Vector3.One, Color.White);
            Raylib.DrawModelEx(handle.Model, rightCenter, Vector3.UnitY, yawDegrees, Vector3.One, Color.White);
            return;
        }

        var fill = focused ? Lighten(ResolveFill(door)) : ResolveFill(door);
        _placeholder.Draw(leftCenter, leafSize, yawDegrees, fill);
        _placeholder.Draw(rightCenter, leafSize, yawDegrees, fill);
    }

    private void DrawSlidingDepth(DoorState door, ShadowPass pass)
    {
        var yawDegrees = MathUtil.RadToDeg(door.ClosedYaw);
        GetSlidingLeafCenters(door, out var leftCenter, out var rightCenter);
        var leafSize = new Vector3(door.LeafWidth, door.Height, door.Thickness);

        if (TryGetModel(door, out var handle))
        {
            pass.DrawModel(handle.Model, leftCenter, yawDegrees, 1f);
            pass.DrawModel(handle.Model, rightCenter, yawDegrees, 1f);
            return;
        }

        pass.DrawBox(leftCenter, leafSize, yawDegrees);
        pass.DrawBox(rightCenter, leafSize, yawDegrees);
    }

    private static void GetSlidingLeafCenters(
        DoorState door,
        out Vector3 leftCenter,
        out Vector3 rightCenter)
    {
        var rot = Matrix4x4.CreateRotationY(door.ClosedYaw);
        var slide = door.OpenAmount * door.SlideTravel;
        var leftLocal = new Vector3(-door.LeafWidth * 0.5f - slide, door.Height * 0.5f, 0f);
        var rightLocal = new Vector3(door.LeafWidth * 0.5f + slide, door.Height * 0.5f, 0f);
        leftCenter = door.HingePosition + Vector3.Transform(leftLocal, rot);
        rightCenter = door.HingePosition + Vector3.Transform(rightLocal, rot);
    }

    private static Color ResolveFill(DoorState door) =>
        door.Color.A == 0 ? DoorFill : door.Color;

    private static Color Lighten(Color color) =>
        new(
            (byte)Math.Min(255, color.R + 48),
            (byte)Math.Min(255, color.G + 48),
            (byte)Math.Min(255, color.B + 48),
            color.A);

    private bool TryRaycast(DoorState door, Vector3 origin, Vector3 direction, out float distance)
    {
        if (door.IsSlidingDouble)
        {
            return TryRaycastSliding(door, origin, direction, out distance);
        }

        if (TryGetModel(door, out var handle))
        {
            return TryRaycastSwingModel(handle, door, origin, direction, out distance);
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

    private bool TryRaycastSliding(DoorState door, Vector3 origin, Vector3 direction, out float distance)
    {
        distance = float.MaxValue;
        var hit = false;

        if (TryGetModel(door, out var handle))
        {
            GetSlidingLeafCenters(door, out var leftCenter, out var rightCenter);
            if (TryRaycastModelAt(handle, leftCenter, door.ClosedYaw, origin, direction, ref distance))
            {
                hit = true;
            }

            if (TryRaycastModelAt(handle, rightCenter, door.ClosedYaw, origin, direction, ref distance))
            {
                hit = true;
            }

            return hit;
        }

        var invYaw = Matrix4x4.CreateRotationY(-door.ClosedYaw);
        var localOrigin = Vector3.Transform(origin - door.HingePosition, invYaw);
        var localDir = Vector3.TransformNormal(direction, invYaw);
        if (localDir.LengthSquared() < 1e-8f)
        {
            distance = 0f;
            return false;
        }

        localDir = Vector3.Normalize(localDir);
        var slide = door.OpenAmount * door.SlideTravel;
        var halfT = door.Thickness * 0.5f;

        var leftBox = new Aabb(
            new Vector3(-door.LeafWidth - slide, 0f, -halfT),
            new Vector3(-slide, door.Height, halfT));
        var rightBox = new Aabb(
            new Vector3(slide, 0f, -halfT),
            new Vector3(door.LeafWidth + slide, door.Height, halfT));

        if (leftBox.TryIntersectRay(localOrigin, localDir, out var leftHit) && leftHit < distance)
        {
            distance = leftHit;
            hit = true;
        }

        if (rightBox.TryIntersectRay(localOrigin, localDir, out var rightHit) && rightHit < distance)
        {
            distance = rightHit;
            hit = true;
        }

        return hit;
    }

    private static bool TryRaycastSwingModel(
        ModelHandle handle,
        DoorState door,
        Vector3 origin,
        Vector3 direction,
        out float distance)
    {
        distance = float.MaxValue;
        return TryRaycastModelAt(handle, door.HingePosition, door.CurrentYaw, origin, direction, ref distance);
    }

    private static bool TryRaycastModelAt(
        ModelHandle handle,
        Vector3 position,
        float yaw,
        Vector3 origin,
        Vector3 direction,
        ref float bestDistance)
    {
        var ray = new Ray(origin, direction);
        var transform =
            Matrix4x4.CreateRotationY(yaw) *
            Matrix4x4.CreateTranslation(position);

        var hit = false;
        var meshes = handle.Model.MeshesAsSpan();
        for (var i = 0; i < meshes.Length; i++)
        {
            var collision = Raylib.GetRayCollisionMesh(ray, meshes[i], transform);
            if (!collision.Hit || collision.Distance >= bestDistance)
            {
                continue;
            }

            hit = true;
            bestDistance = collision.Distance;
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
        Vector3 center;
        if (door.IsSlidingDouble)
        {
            center = door.HingePosition + new Vector3(0f, door.Height * 0.5f, 0f);
        }
        else
        {
            var hingeToCenter = Vector3.Transform(
                new Vector3(door.Width * 0.5f, 0f, 0f),
                Matrix4x4.CreateRotationY(door.ClosedYaw));
            center = door.HingePosition + hingeToCenter;
        }

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
