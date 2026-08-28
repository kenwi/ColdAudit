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

public enum DoorLeaf
{
    Single = 0,
    Left = 1,
    Right = 2
}

public sealed class DoorState
{
    public string Id { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public DoorMotion Motion { get; init; } = DoorMotion.Swing;

    /// <summary>Swing hinge, or doorway center, on the floor.</summary>
    public Vector3 HingePosition { get; init; }

    public float ClosedYaw { get; init; }
    public float Width { get; init; } = 1.5f;
    public float Height { get; init; } = 2.1f;
    public float Thickness { get; init; } = 0.08f;
    public float OpenAngle { get; init; } = MathF.PI * 0.5f;
    public float SlideTravel { get; init; }
    public SlideDirection SlideDirection { get; init; } = SlideDirection.Right;
    public float InteractRadius { get; init; } = 2.5f;
    public string? ModelPath { get; init; }
    public string? RequiredItemId { get; init; }
    public Color Color { get; init; }
    public bool Locked { get; set; }
    public bool IsOpen { get; set; }
    public float OpenAmount { get; set; }
    public float LockDeniedTime { get; set; }

    public bool AutoClose { get; init; }
    public float AutoCloseSeconds { get; init; } = 3f;
    public float AutoCloseRemaining { get; set; }
    public bool AutoCloseArmed { get; set; }

    public bool LeftIsOpen { get; set; }
    public bool RightIsOpen { get; set; }
    public float LeftOpenAmount { get; set; }
    public float RightOpenAmount { get; set; }
    public float LeftSwingSign { get; set; } = 1f;
    public float RightSwingSign { get; set; } = 1f;
    public float LeftAutoCloseRemaining { get; set; }
    public float RightAutoCloseRemaining { get; set; }
    public bool LeftAutoCloseArmed { get; set; }
    public bool RightAutoCloseArmed { get; set; }

    /// <summary>+1 / -1. Chosen when opening so a swing slab moves away from the player.</summary>
    public float SwingSign { get; set; } = 1f;

    public bool IsSlidingDouble => Motion == DoorMotion.SlidingDouble;
    public bool IsSwingDouble => Motion == DoorMotion.SwingDouble;
    public bool IsSlidingSingle => Motion == DoorMotion.SlidingSingle;
    public bool IsCenteredDoorway => IsSlidingDouble || IsSwingDouble || IsSlidingSingle;

    public float LeafWidth => Width * 0.5f;

    public float CurrentYaw => IsSlidingDouble || IsSwingDouble || IsSlidingSingle
        ? ClosedYaw
        : ClosedYaw + OpenAmount * OpenAngle * SwingSign;

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);
    public bool RequiresItem => !string.IsNullOrWhiteSpace(RequiredItemId);

    public string GetPrompt(DoorLeaf leaf, bool hasRequiredItem)
    {
        var label = Motion switch
        {
            DoorMotion.SlidingDouble => "Double door",
            DoorMotion.SwingDouble => leaf switch
            {
                DoorLeaf.Left => "Door (left)",
                DoorLeaf.Right => "Door (right)",
                _ => "Double door"
            },
            DoorMotion.SlidingSingle => "Sliding door",
            _ => "Door"
        };

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

        var openAmount = GetLeafOpenAmount(leaf);
        var isOpen = GetLeafIsOpen(leaf);

        if (openAmount < 0.01f && !isOpen)
        {
            return $"{label}: Closed  [E] Open  [L] Lock";
        }

        if (openAmount > 0.99f && isOpen)
        {
            return $"{label}: Open  [E] Close";
        }

        return isOpen
            ? $"{label}: Opening {(int)(openAmount * 100f)}%"
            : $"{label}: Closing {(int)(openAmount * 100f)}%";
    }

    public bool GetLeafIsOpen(DoorLeaf leaf) =>
        Motion is DoorMotion.SlidingDouble or DoorMotion.SlidingSingle
            ? IsOpen
            : leaf switch
            {
                DoorLeaf.Left => LeftIsOpen,
                DoorLeaf.Right => RightIsOpen,
                _ => IsOpen
            };

    public float GetLeafOpenAmount(DoorLeaf leaf) =>
        Motion is DoorMotion.SlidingDouble or DoorMotion.SlidingSingle
            ? OpenAmount
            : leaf switch
            {
                DoorLeaf.Left => LeftOpenAmount,
                DoorLeaf.Right => RightOpenAmount,
                _ => OpenAmount
            };
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
            var slideTravel = def.SlideDistance > 1e-4f
                ? def.SlideDistance
                : def.Motion switch
                {
                    DoorMotion.SlidingSingle => def.Width,
                    DoorMotion.SlidingDouble => leafWidth,
                    _ => def.Width
                };

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
                SlideTravel = slideTravel,
                SlideDirection = def.SlideDirection,
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
            if (TryResolveInteractable(focusedId, out var focused, out var focusedLeaf))
            {
                if (input.UnlockPressed)
                {
                    focused.Locked = false;
                    focused.LockDeniedTime = 0f;
                }

                if (input.LockPressed && IsDoorFullyClosed(focused))
                {
                    focused.Locked = true;
                }

                world.UsePrompt = focused.GetPrompt(focusedLeaf, HasRequiredItem(world, focused));
            }
        }

        foreach (var use in events.OfType<UseRequested>())
        {
            if (!TryResolveInteractable(use.InteractableId, out var door, out var leaf))
            {
                continue;
            }

            if (door.Locked && !door.GetLeafIsOpen(leaf))
            {
                if (HasRequiredItem(world, door))
                {
                    door.Locked = false;
                    door.LockDeniedTime = 0f;
                }
                else
                {
                    door.LockDeniedTime = LockDeniedDuration;
                    world.UsePrompt = door.GetPrompt(leaf, false);
                    continue;
                }
            }

            ToggleLeaf(door, leaf, world.PlayerPosition);
            if (!door.GetLeafIsOpen(leaf))
            {
                ResetLeafAutoClose(door, leaf);
            }
        }

        foreach (var door in _doors)
        {
            if (door.LockDeniedTime > 0f)
            {
                door.LockDeniedTime = MathF.Max(0f, door.LockDeniedTime - dt);
            }

            AnimateDoor(door, dt, world);
        }
    }

    private void AnimateDoor(DoorState door, float dt, GameWorld world)
    {
        var geometryMoved = false;

        if (door.IsSwingDouble)
        {
            geometryMoved |= AnimateLeaf(door, dt, DoorLeaf.Left);
            geometryMoved |= AnimateLeaf(door, dt, DoorLeaf.Right);
        }
        else
        {
            UpdateAutoClose(door, DoorLeaf.Single, dt);
            var target = door.IsOpen ? 1f : 0f;
            var previous = door.OpenAmount;
            door.OpenAmount = MathUtil.MoveTowards(previous, target, OpenSpeed * dt);
            geometryMoved = MathF.Abs(door.OpenAmount - previous) > 1e-5f;
        }

        if (geometryMoved)
        {
            world.InvalidateShadowGeometry();
        }
    }

    private bool AnimateLeaf(DoorState door, float dt, DoorLeaf leaf)
    {
        UpdateAutoClose(door, leaf, dt);
        var target = door.GetLeafIsOpen(leaf) ? 1f : 0f;
        var previous = door.GetLeafOpenAmount(leaf);
        var next = MathUtil.MoveTowards(previous, target, OpenSpeed * dt);

        switch (leaf)
        {
            case DoorLeaf.Left:
                door.LeftOpenAmount = next;
                break;
            case DoorLeaf.Right:
                door.RightOpenAmount = next;
                break;
            default:
                door.OpenAmount = next;
                break;
        }

        return MathF.Abs(next - previous) > 1e-5f;
    }

    private static void UpdateAutoClose(DoorState door, DoorLeaf leaf, float dt)
    {
        if (!door.AutoClose || !door.GetLeafIsOpen(leaf))
        {
            ResetLeafAutoClose(door, leaf);
            return;
        }

        if (door.GetLeafOpenAmount(leaf) < 0.99f)
        {
            return;
        }

        switch (leaf)
        {
            case DoorLeaf.Left:
                if (!door.LeftAutoCloseArmed)
                {
                    door.LeftAutoCloseArmed = true;
                    door.LeftAutoCloseRemaining = door.AutoCloseSeconds;
                }

                door.LeftAutoCloseRemaining -= dt;
                if (door.LeftAutoCloseRemaining <= 0f)
                {
                    door.LeftIsOpen = false;
                    ResetLeafAutoClose(door, leaf);
                }

                break;
            case DoorLeaf.Right:
                if (!door.RightAutoCloseArmed)
                {
                    door.RightAutoCloseArmed = true;
                    door.RightAutoCloseRemaining = door.AutoCloseSeconds;
                }

                door.RightAutoCloseRemaining -= dt;
                if (door.RightAutoCloseRemaining <= 0f)
                {
                    door.RightIsOpen = false;
                    ResetLeafAutoClose(door, leaf);
                }

                break;
            default:
                if (!door.AutoCloseArmed)
                {
                    door.AutoCloseArmed = true;
                    door.AutoCloseRemaining = door.AutoCloseSeconds;
                }

                door.AutoCloseRemaining -= dt;
                if (door.AutoCloseRemaining <= 0f)
                {
                    door.IsOpen = false;
                    ResetLeafAutoClose(door, leaf);
                }

                break;
        }
    }

    private static void ResetLeafAutoClose(DoorState door, DoorLeaf leaf)
    {
        switch (leaf)
        {
            case DoorLeaf.Left:
                door.LeftAutoCloseArmed = false;
                door.LeftAutoCloseRemaining = 0f;
                break;
            case DoorLeaf.Right:
                door.RightAutoCloseArmed = false;
                door.RightAutoCloseRemaining = 0f;
                break;
            default:
                door.AutoCloseArmed = false;
                door.AutoCloseRemaining = 0f;
                break;
        }
    }

    private static void SetLeafIsOpen(DoorState door, DoorLeaf leaf, bool isOpen)
    {
        switch (leaf)
        {
            case DoorLeaf.Left:
                door.LeftIsOpen = isOpen;
                break;
            case DoorLeaf.Right:
                door.RightIsOpen = isOpen;
                break;
            default:
                door.IsOpen = isOpen;
                break;
        }
    }

    private static void ToggleLeaf(DoorState door, DoorLeaf leaf, Vector3 playerPosition)
    {
        if (door.IsSlidingDouble || door.IsSlidingSingle)
        {
            door.IsOpen = !door.IsOpen;
            return;
        }

        var opening = !door.GetLeafIsOpen(leaf);
        SetLeafIsOpen(door, leaf, opening);

        if (!opening || door.GetLeafOpenAmount(leaf) >= 0.01f)
        {
            return;
        }

        switch (door.Motion)
        {
            case DoorMotion.Swing:
                door.SwingSign = SwingAwayFromPlayer(playerPosition, door);
                break;
            case DoorMotion.SwingDouble when leaf == DoorLeaf.Left:
                door.LeftSwingSign = SwingDoubleLeafSign(playerPosition, door, DoorLeaf.Left);
                break;
            case DoorMotion.SwingDouble when leaf == DoorLeaf.Right:
                door.RightSwingSign = SwingDoubleLeafSign(playerPosition, door, DoorLeaf.Right);
                break;
        }
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

            DrawDoor(door, GetFocusedLeaf(world, door.Id));
        }

        world.Lighting?.RestorePbrDrawDefaults();
        world.Lighting?.SetAlbedoMapEnabled(false);

        foreach (var door in _doors)
        {
            if (!IsDoorDrawn(world, door) || TryGetModel(door, out _))
            {
                continue;
            }

            DrawDoor(door, GetFocusedLeaf(world, door.Id));
        }

        world.Lighting?.RestorePbrDrawDefaults();
        Raylib.EndMode3D();
    }

    public void DrawDepth(GameWorld world, ShadowPass pass)
    {
        foreach (var door in _doors)
        {
            if (!pass.IncludesSector(door.SectorId))
            {
                continue;
            }

            switch (door.Motion)
            {
                case DoorMotion.SlidingDouble:
                    DrawSlidingDoubleDepth(door, pass);
                    break;
                case DoorMotion.SwingDouble:
                    DrawSwingDoubleDepth(door, pass);
                    break;
                case DoorMotion.SlidingSingle:
                    DrawSlidingSingleDepth(door, pass);
                    break;
                default:
                    DrawSwingDepth(door, pass);
                    break;
            }
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
        DoorLeaf bestLeaf = DoorLeaf.Single;
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

            if (!TryRaycastLeaf(candidate, origin, forward, out var leaf, out var distance) ||
                distance >= bestDistance)
            {
                continue;
            }

            best = candidate;
            bestLeaf = leaf;
            bestDistance = distance;
        }

        if (best is null)
        {
            return false;
        }

        hit = new InteractableHit(
            FormatInteractableId(best.Id, bestLeaf),
            best.GetPrompt(bestLeaf, HasRequiredItem(world, best)),
            bestDistance);
        return true;
    }

    private DoorLeaf GetFocusedLeaf(GameWorld world, string doorId)
    {
        if (world.FocusedInteractableId is not { } focusedId ||
            !TryResolveInteractable(focusedId, out var focused, out var leaf) ||
            focused.Id != doorId)
        {
            return DoorLeaf.Single;
        }

        return leaf;
    }

    private void DrawDoor(DoorState door, DoorLeaf focusedLeaf)
    {
        switch (door.Motion)
        {
            case DoorMotion.SlidingDouble:
                DrawSlidingDoubleDoor(door, focusedLeaf);
                break;
            case DoorMotion.SwingDouble:
                DrawSwingDoubleDoor(door, focusedLeaf);
                break;
            case DoorMotion.SlidingSingle:
                DrawSlidingSingleDoor(door, focusedLeaf != DoorLeaf.Single);
                break;
            default:
                DrawSwingDoor(door, focusedLeaf != DoorLeaf.Single);
                break;
        }
    }

    private void DrawSwingDoor(DoorState door, bool focused)
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

    private void DrawSwingDepth(DoorState door, ShadowPass pass)
    {
        var yawDegrees = MathUtil.RadToDeg(door.CurrentYaw);
        if (TryGetModel(door, out var handle))
        {
            pass.DrawModel(handle.Model, door.HingePosition, yawDegrees, 1f);
            return;
        }

        var localCenter = Vector3.Transform(
            new Vector3(door.Width * 0.5f, door.Height * 0.5f, 0f),
            Matrix4x4.CreateRotationY(door.CurrentYaw));
        pass.DrawBox(
            door.HingePosition + localCenter,
            new Vector3(door.Width, door.Height, door.Thickness),
            yawDegrees);
    }

    private void DrawSlidingDoubleDoor(DoorState door, DoorLeaf focusedLeaf)
    {
        var yawDegrees = MathUtil.RadToDeg(door.ClosedYaw);
        GetSlidingDoubleLeafCenters(door, out var leftCenter, out var rightCenter);
        var leafSize = new Vector3(door.LeafWidth, door.Height, door.Thickness);

        if (TryGetModel(door, out var handle))
        {
            Raylib.DrawModelEx(handle.Model, leftCenter, Vector3.UnitY, yawDegrees, Vector3.One, Color.White);
            Raylib.DrawModelEx(handle.Model, rightCenter, Vector3.UnitY, yawDegrees, Vector3.One, Color.White);
            return;
        }

        var fill = ResolveFill(door);
        _placeholder.Draw(leftCenter, leafSize, yawDegrees, focusedLeaf == DoorLeaf.Left ? Lighten(fill) : fill);
        _placeholder.Draw(rightCenter, leafSize, yawDegrees, focusedLeaf == DoorLeaf.Right ? Lighten(fill) : fill);
    }

    private void DrawSlidingDoubleDepth(DoorState door, ShadowPass pass)
    {
        var yawDegrees = MathUtil.RadToDeg(door.ClosedYaw);
        GetSlidingDoubleLeafCenters(door, out var leftCenter, out var rightCenter);
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

    private void DrawSwingDoubleDoor(DoorState door, DoorLeaf focusedLeaf)
    {
        GetSwingDoubleLeafPoses(door, out var leftHinge, out var leftYaw, out var rightHinge, out var rightYaw);
        var leafSize = new Vector3(door.LeafWidth, door.Height, door.Thickness);
        var fill = ResolveFill(door);

        if (TryGetModel(door, out var handle))
        {
            Raylib.DrawModelEx(handle.Model, leftHinge, Vector3.UnitY, MathUtil.RadToDeg(leftYaw), Vector3.One, Color.White);
            Raylib.DrawModelEx(handle.Model, rightHinge, Vector3.UnitY, MathUtil.RadToDeg(rightYaw), Vector3.One, Color.White);
            return;
        }

        DrawSwingLeafPlaceholder(leftHinge, leafSize, leftYaw, fill, focusedLeaf == DoorLeaf.Left);
        DrawSwingLeafPlaceholder(rightHinge, leafSize, rightYaw, fill, focusedLeaf == DoorLeaf.Right);
    }

    private void DrawSwingDoubleDepth(DoorState door, ShadowPass pass)
    {
        GetSwingDoubleLeafPoses(door, out var leftHinge, out var leftYaw, out var rightHinge, out var rightYaw);
        var leafSize = new Vector3(door.LeafWidth, door.Height, door.Thickness);

        if (TryGetModel(door, out var handle))
        {
            pass.DrawModel(handle.Model, leftHinge, MathUtil.RadToDeg(leftYaw), 1f);
            pass.DrawModel(handle.Model, rightHinge, MathUtil.RadToDeg(rightYaw), 1f);
            return;
        }

        DrawSwingLeafDepth(leftHinge, leafSize, leftYaw, pass);
        DrawSwingLeafDepth(rightHinge, leafSize, rightYaw, pass);
    }

    private void DrawSlidingSingleDoor(DoorState door, bool focused)
    {
        var center = GetSlidingSingleCenter(door);
        var yawDegrees = MathUtil.RadToDeg(door.ClosedYaw);
        var size = new Vector3(door.Width, door.Height, door.Thickness);
        var fill = focused ? Lighten(ResolveFill(door)) : ResolveFill(door);

        if (TryGetModel(door, out var handle))
        {
            Raylib.DrawModelEx(handle.Model, center, Vector3.UnitY, yawDegrees, Vector3.One, Color.White);
            return;
        }

        _placeholder.Draw(center, size, yawDegrees, fill);
    }

    private void DrawSlidingSingleDepth(DoorState door, ShadowPass pass)
    {
        var center = GetSlidingSingleCenter(door);
        var yawDegrees = MathUtil.RadToDeg(door.ClosedYaw);
        var size = new Vector3(door.Width, door.Height, door.Thickness);

        if (TryGetModel(door, out var handle))
        {
            pass.DrawModel(handle.Model, center, yawDegrees, 1f);
            return;
        }

        pass.DrawBox(center, size, yawDegrees);
    }

    private void DrawSwingLeafPlaceholder(
        Vector3 hinge,
        Vector3 size,
        float yaw,
        Color fill,
        bool focused)
    {
        var localCenter = Vector3.Transform(
            new Vector3(size.X * 0.5f, size.Y * 0.5f, 0f),
            Matrix4x4.CreateRotationY(yaw));
        _placeholder.Draw(
            hinge + localCenter,
            size,
            MathUtil.RadToDeg(yaw),
            focused ? Lighten(fill) : fill);
    }

    private static void DrawSwingLeafDepth(Vector3 hinge, Vector3 size, float yaw, ShadowPass pass)
    {
        var localCenter = Vector3.Transform(
            new Vector3(size.X * 0.5f, size.Y * 0.5f, 0f),
            Matrix4x4.CreateRotationY(yaw));
        pass.DrawBox(hinge + localCenter, size, MathUtil.RadToDeg(yaw));
    }

    private static void GetSlidingDoubleLeafCenters(
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

    private static void GetSwingDoubleLeafPoses(
        DoorState door,
        out Vector3 leftHinge,
        out float leftYaw,
        out Vector3 rightHinge,
        out float rightYaw)
    {
        var rot = Matrix4x4.CreateRotationY(door.ClosedYaw);
        leftHinge = door.HingePosition + Vector3.Transform(new Vector3(-door.LeafWidth, 0f, 0f), rot);
        rightHinge = door.HingePosition + Vector3.Transform(new Vector3(door.LeafWidth, 0f, 0f), rot);
        leftYaw = door.ClosedYaw + door.LeftOpenAmount * door.OpenAngle * door.LeftSwingSign;
        rightYaw = door.ClosedYaw + MathF.PI + door.RightOpenAmount * door.OpenAngle * door.RightSwingSign;
    }

    private static Vector3 GetSlidingSingleCenter(DoorState door)
    {
        var rot = Matrix4x4.CreateRotationY(door.ClosedYaw);
        var slide = door.OpenAmount * door.SlideTravel * (int)door.SlideDirection;
        var local = new Vector3(slide, door.Height * 0.5f, 0f);
        return door.HingePosition + Vector3.Transform(local, rot);
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
        if (!TryRaycastLeaf(door, origin, direction, out _, out distance))
        {
            distance = 0f;
            return false;
        }

        return true;
    }

    private bool TryRaycastLeaf(
        DoorState door,
        Vector3 origin,
        Vector3 direction,
        out DoorLeaf leaf,
        out float distance)
    {
        leaf = DoorLeaf.Single;
        distance = float.MaxValue;
        var hit = false;

        switch (door.Motion)
        {
            case DoorMotion.SlidingDouble:
                return TryRaycastSlidingDouble(door, origin, direction, out leaf, out distance);
            case DoorMotion.SwingDouble:
                if (TryRaycastSwingLeaf(door, DoorLeaf.Left, origin, direction, out var leftHit) && leftHit < distance)
                {
                    leaf = DoorLeaf.Left;
                    distance = leftHit;
                    hit = true;
                }

                if (TryRaycastSwingLeaf(door, DoorLeaf.Right, origin, direction, out var rightHit) && rightHit < distance)
                {
                    leaf = DoorLeaf.Right;
                    distance = rightHit;
                    hit = true;
                }

                return hit;
            case DoorMotion.SlidingSingle:
                return TryRaycastSlidingSingle(door, origin, direction, out distance);
            default:
                if (TryRaycastSwingLeaf(door, DoorLeaf.Single, origin, direction, out var swingHit))
                {
                    distance = swingHit;
                    return true;
                }

                return false;
        }
    }

    private bool TryRaycastSlidingDouble(
        DoorState door,
        Vector3 origin,
        Vector3 direction,
        out DoorLeaf leaf,
        out float distance)
    {
        leaf = DoorLeaf.Single;
        distance = float.MaxValue;
        var hit = false;

        GetSlidingDoubleLeafCenters(door, out var leftCenter, out var rightCenter);
        if (TryRaycastBoxAt(leftCenter, door.ClosedYaw, new Vector3(door.LeafWidth, door.Height, door.Thickness), origin, direction, out var leftHit) &&
            leftHit < distance)
        {
            leaf = DoorLeaf.Left;
            distance = leftHit;
            hit = true;
        }

        if (TryRaycastBoxAt(rightCenter, door.ClosedYaw, new Vector3(door.LeafWidth, door.Height, door.Thickness), origin, direction, out var rightHit) &&
            rightHit < distance)
        {
            leaf = DoorLeaf.Right;
            distance = rightHit;
            hit = true;
        }

        if (TryGetModel(door, out var handle))
        {
            if (TryRaycastModelAt(handle, leftCenter, door.ClosedYaw, origin, direction, ref distance))
            {
                leaf = DoorLeaf.Left;
                hit = true;
            }

            if (TryRaycastModelAt(handle, rightCenter, door.ClosedYaw, origin, direction, ref distance))
            {
                leaf = DoorLeaf.Right;
                hit = true;
            }
        }

        return hit;
    }

    private bool TryRaycastSlidingSingle(DoorState door, Vector3 origin, Vector3 direction, out float distance)
    {
        distance = float.MaxValue;
        var center = GetSlidingSingleCenter(door);
        var size = new Vector3(door.Width, door.Height, door.Thickness);

        if (TryGetModel(door, out var handle))
        {
            return TryRaycastModelAt(handle, center, door.ClosedYaw, origin, direction, ref distance);
        }

        return TryRaycastBoxAt(center, door.ClosedYaw, size, origin, direction, out distance);
    }

    private bool TryRaycastSwingLeaf(
        DoorState door,
        DoorLeaf leaf,
        Vector3 origin,
        Vector3 direction,
        out float distance)
    {
        GetSwingLeafPose(door, leaf, out var hinge, out var yaw, out var size);

        if (TryGetModel(door, out var handle))
        {
            distance = float.MaxValue;
            return TryRaycastModelAt(handle, hinge, yaw, origin, direction, ref distance);
        }

        var localCenter = Vector3.Transform(
            new Vector3(size.X * 0.5f, size.Y * 0.5f, 0f),
            Matrix4x4.CreateRotationY(yaw));
        return TryRaycastBoxAt(hinge + localCenter, yaw, size, origin, direction, out distance);
    }

    private static void GetSwingLeafPose(
        DoorState door,
        DoorLeaf leaf,
        out Vector3 hinge,
        out float yaw,
        out Vector3 size)
    {
        if (door.IsSwingDouble)
        {
            GetSwingDoubleLeafPoses(door, out var leftHinge, out var leftYaw, out var rightHinge, out var rightYaw);
            if (leaf == DoorLeaf.Left)
            {
                hinge = leftHinge;
                yaw = leftYaw;
            }
            else
            {
                hinge = rightHinge;
                yaw = rightYaw;
            }

            size = new Vector3(door.LeafWidth, door.Height, door.Thickness);
            return;
        }

        hinge = door.HingePosition;
        yaw = door.CurrentYaw;
        size = new Vector3(door.Width, door.Height, door.Thickness);
    }

    private static bool TryRaycastBoxAt(
        Vector3 center,
        float yaw,
        Vector3 size,
        Vector3 origin,
        Vector3 direction,
        out float distance)
    {
        var half = size * 0.5f;
        var invYaw = Matrix4x4.CreateRotationY(-yaw);
        var localOrigin = Vector3.Transform(origin - center, invYaw);
        var localDir = Vector3.TransformNormal(direction, invYaw);
        if (localDir.LengthSquared() < 1e-8f)
        {
            distance = 0f;
            return false;
        }

        localDir = Vector3.Normalize(localDir);
        var localBox = new Aabb(-half, half);
        return localBox.TryIntersectRay(localOrigin, localDir, out distance);
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

        return Vector3.Dot(toPlayer, closedNormal) >= 0f ? 1f : -1f;
    }

    private static float SwingDoubleLeafSign(Vector3 playerPosition, DoorState door, DoorLeaf leaf)
    {
        var doorwayRot = Matrix4x4.CreateRotationY(door.ClosedYaw);
        var closedLeafYaw = leaf == DoorLeaf.Left ? door.ClosedYaw : door.ClosedYaw + MathF.PI;
        var hingeLocal = new Vector3(leaf == DoorLeaf.Left ? -door.LeafWidth : door.LeafWidth, 0f, 0f);
        var hinge = door.HingePosition + Vector3.Transform(hingeLocal, doorwayRot);
        var leafCenter = hinge + Vector3.Transform(
            new Vector3(door.LeafWidth * 0.5f, 0f, 0f),
            Matrix4x4.CreateRotationY(closedLeafYaw));
        var leafNormal = Vector3.Transform(Vector3.UnitZ, Matrix4x4.CreateRotationY(closedLeafYaw));
        var toPlayer = playerPosition - leafCenter;
        toPlayer.Y = 0f;

        // +sign swings the leaf toward its local -Z, away from the player on the +normal side.
        return Vector3.Dot(toPlayer, leafNormal) >= 0f ? 1f : -1f;
    }

    private static bool IsPlayerInRadius(Vector3 playerPosition, DoorState door)
    {
        Vector3 center;
        if (door.IsCenteredDoorway)
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

    private static bool IsDoorFullyClosed(DoorState door)
    {
        if (door.IsSwingDouble)
        {
            return !door.LeftIsOpen && !door.RightIsOpen &&
                   door.LeftOpenAmount < 0.01f && door.RightOpenAmount < 0.01f;
        }

        return !door.IsOpen && door.OpenAmount < 0.01f;
    }

    private static bool IsDoorDrawn(GameWorld world, DoorState door)
    {
        if (string.IsNullOrEmpty(door.SectorId) || !world.SectorCullEnabled)
        {
            return true;
        }

        return world.VisibleSectorIds.Contains(door.SectorId);
    }

    private static string FormatInteractableId(string doorId, DoorLeaf leaf) =>
        leaf switch
        {
            DoorLeaf.Left => $"{doorId}:left",
            DoorLeaf.Right => $"{doorId}:right",
            _ => doorId
        };

    private bool TryResolveInteractable(string interactableId, out DoorState door, out DoorLeaf leaf)
    {
        door = null!;
        leaf = DoorLeaf.Single;

        if (interactableId.EndsWith(":left", StringComparison.Ordinal))
        {
            leaf = DoorLeaf.Left;
            interactableId = interactableId[..^5];
        }
        else if (interactableId.EndsWith(":right", StringComparison.Ordinal))
        {
            leaf = DoorLeaf.Right;
            interactableId = interactableId[..^6];
        }

        var found = Find(interactableId);
        if (found is null)
        {
            door = null!;
            return false;
        }

        door = found;
        return true;
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

    private static bool HasRequiredItem(GameWorld world, DoorState door) =>
        door.RequiresItem && InventoryFeature.Has(world, door.RequiredItemId!);
}
