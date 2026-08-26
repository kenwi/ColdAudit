using System.Numerics;
using ColdAudit.Features.DoorsAccess;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Features.Physics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.Cameras;

/// <summary>
/// Runtime wall camera. Plate and leg stay fixed to the mount; the body yaws left/right.
/// Eye/frustum sit at the lens. Swap in <see cref="ModelPath"/> later for a single GLB.
/// </summary>
public sealed class SecurityCamera
{
    public string Id { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public Vector3 MountPosition { get; init; }
    public float MountYaw { get; init; }
    public float Pitch { get; init; }
    public float HorizontalFovDegrees { get; init; } = 70f;
    public float VerticalFovDegrees { get; init; } = 42f;
    public float NearPlane { get; set; } = 0.2f;
    public float FarPlane { get; set; } = 12f;
    public float DetectRate { get; init; } = 0.35f;
    public float SweepYawAmplitude { get; init; }
    public float SweepSpeed { get; init; }
    public float SweepPhase { get; init; }
    public string? ModelPath { get; init; }
    public bool Interactable { get; init; }
    public float InteractRadius { get; init; } = 3f;
    public bool Enabled { get; set; } = true;

    public float SweepOffset { get; set; }
    public float SweepElapsed { get; set; }
    public bool SeeingPlayer { get; set; }

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);

    public string Prompt => Enabled
        ? "Camera  [E] Disable"
        : "Camera  [E] Enable";

    public float CurrentYaw => MountYaw + SweepOffset;

    public Vector3 MountForward => MathUtil.ForwardFromYawPitch(MountYaw, 0f);

    public Vector3 LookForward => MathUtil.ForwardFromYawPitch(CurrentYaw, Pitch);

    public Vector3 PivotPosition =>
        MountPosition + MountForward * (CamerasFeature.PlateThickness * 0.5f + CamerasFeature.LegLength);

    public Vector3 EyePosition =>
        PivotPosition + LookForward * (CamerasFeature.BodyDepth * 0.45f);
}

public sealed class CamerasFeature : FeatureBase, IShadowCaster, IInteractableSource
{
    internal const float PlateWidth = 0.28f;
    internal const float PlateHeight = 0.28f;
    internal const float PlateThickness = 0.04f;
    internal const float LegLength = 0.32f;
    internal const float LegThickness = 0.045f;
    internal const float BodyWidth = 0.14f;
    internal const float BodyHeight = 0.11f;
    internal const float BodyDepth = 0.20f;

    private const float LosEndSkin = 0.08f;

    private static readonly Color PlateColor = new(56, 60, 66, 255);
    private static readonly Color LegColor = new(72, 76, 84, 255);
    private static readonly Color BodyColor = new(28, 30, 34, 255);
    private static readonly Color BodyAlertColor = new(160, 36, 36, 255);
    private static readonly Color FrustumIdle = new(80, 190, 220, 180);
    private static readonly Color FrustumAlert = new(230, 70, 60, 220);
    private static readonly Color RayIdle = new(120, 220, 255, 255);
    private static readonly Color RayAlert = new(255, 90, 70, 255);

    private readonly PhysicsFeature _physics;
    private readonly DoorsAccessFeature _doors;
    private readonly List<SecurityCamera> _cameras = [];
    private readonly Dictionary<string, ModelHandle> _handlesByPath = new(StringComparer.Ordinal);
    private readonly LitBoxMesh _placeholder = new();
    private readonly Frustum _vision = new();
    private readonly Vector3[] _frustumCorners = new Vector3[8];

    public IReadOnlyList<SecurityCamera> Cameras => _cameras;

    public CamerasFeature(PhysicsFeature physics, DoorsAccessFeature doors)
    {
        _physics = physics;
        _doors = doors;
    }

    public override void Load(GameWorld world, EventBus events)
    {
        _cameras.Clear();
        _placeholder.Load();

        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        foreach (var def in level.Cameras)
        {
            _cameras.Add(new SecurityCamera
            {
                Id = def.Id,
                SectorId = def.SectorId,
                MountPosition = def.MountPosition,
                MountYaw = MathUtil.DegToRad(def.MountYawDegrees),
                Pitch = MathUtil.DegToRad(def.PitchDegrees),
                HorizontalFovDegrees = def.HorizontalFovDegrees,
                VerticalFovDegrees = def.VerticalFovDegrees,
                NearPlane = MathF.Max(0.05f, def.NearPlane),
                FarPlane = MathF.Max(MathF.Max(0.05f, def.NearPlane) + 0.1f, def.FarPlane),
                DetectRate = def.DetectRate,
                SweepYawAmplitude = MathUtil.DegToRad(def.SweepYawDegrees),
                SweepSpeed = MathUtil.DegToRad(def.SweepSpeedDegrees),
                SweepPhase = MathUtil.DegToRad(def.SweepPhaseDegrees),
                ModelPath = def.ModelPath,
                Interactable = def.Interactable,
                InteractRadius = def.InteractRadius
            });

            TryLoadModel(world, def.ModelPath);
        }
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        foreach (var disabled in events.OfType<CameraDisabled>())
        {
            foreach (var cam in _cameras)
            {
                if (cam.Id == disabled.CameraId)
                {
                    cam.Enabled = false;
                    cam.SeeingPlayer = false;
                }
            }
        }

        foreach (var use in events.OfType<UseRequested>())
        {
            var cam = Find(use.InteractableId);
            if (cam is null || !cam.Interactable)
            {
                continue;
            }

            cam.Enabled = !cam.Enabled;
            if (!cam.Enabled)
            {
                cam.SeeingPlayer = false;
            }

            if (world.FocusedInteractableId == cam.Id)
            {
                world.UsePrompt = cam.Prompt;
            }
        }

        var geometryMoved = false;

        foreach (var cam in _cameras)
        {
            if (!cam.Enabled)
            {
                cam.SeeingPlayer = false;
                continue;
            }

            var previousOffset = cam.SweepOffset;
            if (cam.SweepYawAmplitude > 1e-5f && cam.SweepSpeed > 1e-5f)
            {
                cam.SweepElapsed += dt;
                cam.SweepOffset =
                    MathF.Sin(cam.SweepElapsed * cam.SweepSpeed + cam.SweepPhase) * cam.SweepYawAmplitude;
            }
            else
            {
                cam.SweepOffset = 0f;
            }

            if (MathF.Abs(cam.SweepOffset - previousOffset) > 1e-5f)
            {
                geometryMoved = true;
            }

            UpdateVisionFrustum(cam);
            var eyeInFrustum = _vision.ContainsPoint(world.PlayerPosition);
            var bodySample = PlayerBodySample(world);
            var bodyInFrustum = _vision.ContainsPoint(bodySample);

            cam.SeeingPlayer = false;
            if (eyeInFrustum && HasLineOfSight(cam.EyePosition, world.PlayerPosition))
            {
                cam.SeeingPlayer = true;
            }
            else if (bodyInFrustum && HasLineOfSight(cam.EyePosition, bodySample))
            {
                cam.SeeingPlayer = true;
            }

            if (cam.SeeingPlayer && world.MissionPhase == MissionPhase.Playing)
            {
                events.Publish(new DetectionSample(cam.Id, cam.DetectRate * dt));
            }
        }

        if (geometryMoved)
        {
            world.InvalidateShadowGeometry();
        }
    }

    public override void Draw(GameWorld world)
    {
        if (_cameras.Count == 0)
        {
            return;
        }

        EnsureModelLighting(world);
        _placeholder.EnsureLighting(world.Lighting);

        Raylib.BeginMode3D(world.Draw.Camera);

        foreach (var cam in _cameras)
        {
            if (!IsCameraDrawn(world, cam) || !TryGetModel(cam, out var handle))
            {
                continue;
            }

            Raylib.DrawModelEx(
                handle.Model,
                cam.MountPosition,
                Vector3.UnitY,
                MathUtil.RadToDeg(cam.CurrentYaw),
                Vector3.One,
                Color.White);
        }

        world.Lighting?.RestorePbrDrawDefaults();
        world.Lighting?.SetAlbedoMapEnabled(false);

        foreach (var cam in _cameras)
        {
            if (!IsCameraDrawn(world, cam) || TryGetModel(cam, out _))
            {
                continue;
            }

            DrawPlaceholder(cam, world.FocusedInteractableId == cam.Id);
        }

        world.Lighting?.RestorePbrDrawDefaults();

        foreach (var cam in _cameras)
        {
            if (!IsCameraDrawn(world, cam))
            {
                continue;
            }

            DrawVisionWires(cam);
        }

        Raylib.EndMode3D();
    }

    public void DrawDepth(GameWorld world, ShadowPass pass)
    {
        foreach (var cam in _cameras)
        {
            if (!cam.Enabled || !pass.IncludesSector(cam.SectorId))
            {
                continue;
            }

            if (TryGetModel(cam, out var handle))
            {
                pass.DrawModel(handle.Model, cam.MountPosition, MathUtil.RadToDeg(cam.CurrentYaw), 1f);
                continue;
            }

            DrawPlaceholderDepth(cam, pass);
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
        _cameras.Clear();
        _placeholder.Unload();
    }

    public bool TryPickFocused(GameWorld world, out InteractableHit hit)
    {
        hit = default;
        var origin = world.PlayerPosition;
        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);

        SecurityCamera? best = null;
        var bestDistance = float.MaxValue;

        foreach (var candidate in _cameras)
        {
            if (!candidate.Interactable || !IsCameraDrawn(world, candidate))
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

        hit = new InteractableHit(best.Id, best.Prompt, bestDistance);
        return true;
    }

    private void DrawPlaceholder(SecurityCamera cam, bool focused)
    {
        var mountYawDeg = MathUtil.RadToDeg(cam.MountYaw);
        var lookYawDeg = MathUtil.RadToDeg(cam.CurrentYaw);
        var lookPitchDeg = MathUtil.RadToDeg(cam.Pitch);
        var mountForward = cam.MountForward;

        var plate = focused ? Lighten(PlateColor) : PlateColor;
        var leg = focused ? Lighten(LegColor) : LegColor;
        var body = cam.SeeingPlayer ? BodyAlertColor : BodyColor;
        if (focused && !cam.SeeingPlayer)
        {
            body = Lighten(body);
        }

        _placeholder.Draw(
            cam.MountPosition,
            new Vector3(PlateWidth, PlateHeight, PlateThickness),
            mountYawDeg,
            plate);

        var legCenter = cam.MountPosition + mountForward * (PlateThickness * 0.5f + LegLength * 0.5f);
        _placeholder.Draw(
            legCenter,
            new Vector3(LegThickness, LegThickness, LegLength),
            mountYawDeg,
            leg);

        _placeholder.Draw(
            cam.PivotPosition,
            new Vector3(BodyWidth, BodyHeight, BodyDepth),
            lookYawDeg,
            lookPitchDeg,
            body);
    }

    private static void DrawPlaceholderDepth(SecurityCamera cam, ShadowPass pass)
    {
        var mountYawDeg = MathUtil.RadToDeg(cam.MountYaw);
        var lookYawDeg = MathUtil.RadToDeg(cam.CurrentYaw);
        var lookPitchDeg = MathUtil.RadToDeg(cam.Pitch);
        var mountForward = cam.MountForward;

        pass.DrawBox(
            cam.MountPosition,
            new Vector3(PlateWidth, PlateHeight, PlateThickness),
            mountYawDeg);

        var legCenter = cam.MountPosition + mountForward * (PlateThickness * 0.5f + LegLength * 0.5f);
        pass.DrawBox(
            legCenter,
            new Vector3(LegThickness, LegThickness, LegLength),
            mountYawDeg);

        pass.DrawBox(
            cam.PivotPosition,
            new Vector3(BodyWidth, BodyHeight, BodyDepth),
            lookYawDeg,
            lookPitchDeg);
    }

    private void DrawVisionWires(SecurityCamera cam)
    {
        if (!cam.Enabled)
        {
            return;
        }

        WriteFrustumCorners(cam, _frustumCorners);
        var wire = cam.SeeingPlayer ? FrustumAlert : FrustumIdle;
        var ray = cam.SeeingPlayer ? RayAlert : RayIdle;

        // Near rectangle
        Raylib.DrawLine3D(_frustumCorners[0], _frustumCorners[1], wire);
        Raylib.DrawLine3D(_frustumCorners[1], _frustumCorners[2], wire);
        Raylib.DrawLine3D(_frustumCorners[2], _frustumCorners[3], wire);
        Raylib.DrawLine3D(_frustumCorners[3], _frustumCorners[0], wire);

        // Far rectangle
        Raylib.DrawLine3D(_frustumCorners[4], _frustumCorners[5], wire);
        Raylib.DrawLine3D(_frustumCorners[5], _frustumCorners[6], wire);
        Raylib.DrawLine3D(_frustumCorners[6], _frustumCorners[7], wire);
        Raylib.DrawLine3D(_frustumCorners[7], _frustumCorners[4], wire);

        // Sides
        Raylib.DrawLine3D(_frustumCorners[0], _frustumCorners[4], wire);
        Raylib.DrawLine3D(_frustumCorners[1], _frustumCorners[5], wire);
        Raylib.DrawLine3D(_frustumCorners[2], _frustumCorners[6], wire);
        Raylib.DrawLine3D(_frustumCorners[3], _frustumCorners[7], wire);

        var eye = cam.EyePosition;
        var tip = eye + cam.LookForward * cam.FarPlane;
        Raylib.DrawLine3D(eye, tip, ray);
    }

    private void UpdateVisionFrustum(SecurityCamera cam)
    {
        _vision.UpdateFromFovHV(
            cam.EyePosition,
            cam.LookForward,
            Vector3.UnitY,
            cam.HorizontalFovDegrees,
            cam.VerticalFovDegrees,
            cam.NearPlane,
            cam.FarPlane);
    }

    private static void WriteFrustumCorners(SecurityCamera cam, Span<Vector3> corners)
    {
        var forward = cam.LookForward;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        if (right.LengthSquared() < 1e-8f)
        {
            right = Vector3.UnitX;
        }

        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        var eye = cam.EyePosition;

        var halfH = MathUtil.DegToRad(cam.HorizontalFovDegrees) * 0.5f;
        var halfV = MathUtil.DegToRad(cam.VerticalFovDegrees) * 0.5f;
        FillPlaneCorners(eye, forward, right, up, cam.NearPlane, halfH, halfV, corners, 0);
        FillPlaneCorners(eye, forward, right, up, cam.FarPlane, halfH, halfV, corners, 4);
    }

    private static void FillPlaneCorners(
        Vector3 eye,
        Vector3 forward,
        Vector3 right,
        Vector3 up,
        float depth,
        float halfH,
        float halfV,
        Span<Vector3> corners,
        int offset)
    {
        var center = eye + forward * depth;
        var halfWidth = depth * MathF.Tan(halfH);
        var halfHeight = depth * MathF.Tan(halfV);
        corners[offset + 0] = center + up * halfHeight - right * halfWidth;
        corners[offset + 1] = center + up * halfHeight + right * halfWidth;
        corners[offset + 2] = center - up * halfHeight + right * halfWidth;
        corners[offset + 3] = center - up * halfHeight - right * halfWidth;
    }

    private bool HasLineOfSight(Vector3 origin, Vector3 target)
    {
        if (!_physics.HasClearLineOfSight(origin, target, LosEndSkin))
        {
            return false;
        }

        // Doors are drawn/interactable but not Box3D bodies yet.
        if (!_doors.TryGetOcclusionHit(origin, target, out var doorDistance))
        {
            return true;
        }

        var reach = Vector3.Distance(origin, target) - LosEndSkin;
        return doorDistance >= reach;
    }

    private bool TryRaycast(SecurityCamera cam, Vector3 origin, Vector3 direction, out float distance)
    {
        if (TryGetModel(cam, out var handle))
        {
            return TryRaycastModel(handle, cam, origin, direction, out distance);
        }

        distance = float.MaxValue;
        var hit = false;

        if (TryRaycastYawBox(
                origin,
                direction,
                cam.MountPosition,
                cam.MountYaw,
                new Vector3(PlateWidth, PlateHeight, PlateThickness),
                ref distance))
        {
            hit = true;
        }

        var legCenter = cam.MountPosition + cam.MountForward * (PlateThickness * 0.5f + LegLength * 0.5f);
        if (TryRaycastYawBox(
                origin,
                direction,
                legCenter,
                cam.MountYaw,
                new Vector3(LegThickness, LegThickness, LegLength),
                ref distance))
        {
            hit = true;
        }

        if (TryRaycastYawPitchBox(
                origin,
                direction,
                cam.PivotPosition,
                cam.CurrentYaw,
                cam.Pitch,
                new Vector3(BodyWidth, BodyHeight, BodyDepth),
                ref distance))
        {
            hit = true;
        }

        return hit;
    }

    private static bool TryRaycastModel(
        ModelHandle handle,
        SecurityCamera cam,
        Vector3 origin,
        Vector3 direction,
        out float distance)
    {
        distance = float.MaxValue;
        var ray = new Ray(origin, direction);
        var transform =
            Matrix4x4.CreateRotationY(cam.CurrentYaw) *
            Matrix4x4.CreateTranslation(cam.MountPosition);

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

    private static bool TryRaycastYawBox(
        Vector3 origin,
        Vector3 direction,
        Vector3 center,
        float yaw,
        Vector3 size,
        ref float bestDistance)
    {
        var invYaw = Matrix4x4.CreateRotationY(-yaw);
        var localOrigin = Vector3.Transform(origin - center, invYaw);
        var localDir = Vector3.TransformNormal(direction, invYaw);
        if (localDir.LengthSquared() < 1e-8f)
        {
            return false;
        }

        localDir = Vector3.Normalize(localDir);
        var half = size * 0.5f;
        var localBox = new Aabb(-half, half);
        if (!localBox.TryIntersectRay(localOrigin, localDir, out var distance) || distance >= bestDistance)
        {
            return false;
        }

        bestDistance = distance;
        return true;
    }

    private static bool TryRaycastYawPitchBox(
        Vector3 origin,
        Vector3 direction,
        Vector3 center,
        float yaw,
        float pitch,
        Vector3 size,
        ref float bestDistance)
    {
        // Inverse of world Ry(yaw)*Rx(-pitch) used by the pitched placeholder body.
        var invRot = Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(-yaw);
        var localOrigin = Vector3.Transform(origin - center, invRot);
        var localDir = Vector3.TransformNormal(direction, invRot);
        if (localDir.LengthSquared() < 1e-8f)
        {
            return false;
        }

        localDir = Vector3.Normalize(localDir);
        var half = size * 0.5f;
        var localBox = new Aabb(-half, half);
        if (!localBox.TryIntersectRay(localOrigin, localDir, out var distance) || distance >= bestDistance)
        {
            return false;
        }

        bestDistance = distance;
        return true;
    }

    private static bool IsPlayerInRadius(Vector3 playerPosition, SecurityCamera cam)
    {
        var delta = playerPosition - cam.MountPosition;
        var radius = cam.InteractRadius;
        return delta.LengthSquared() <= radius * radius;
    }

    private static Color Lighten(Color color) =>
        new(
            (byte)Math.Min(255, color.R + 48),
            (byte)Math.Min(255, color.G + 48),
            (byte)Math.Min(255, color.B + 48),
            color.A);

    private SecurityCamera? Find(string id)
    {
        foreach (var cam in _cameras)
        {
            if (cam.Id == id)
            {
                return cam;
            }
        }

        return null;
    }

    private static Vector3 PlayerBodySample(GameWorld world)
    {
        var eye = world.PlayerPosition;
        var height = world.IsCrouching ? 1.0f : 1.7f;
        return new Vector3(eye.X, eye.Y - height * 0.45f, eye.Z);
    }

    private static bool IsCameraDrawn(GameWorld world, SecurityCamera cam)
    {
        if (string.IsNullOrEmpty(cam.SectorId) || !world.SectorCullEnabled)
        {
            return true;
        }

        return world.VisibleSectorIds.Contains(cam.SectorId);
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

    private bool TryGetModel(SecurityCamera cam, out ModelHandle handle)
    {
        handle = null!;
        return cam.HasModel &&
               cam.ModelPath is not null &&
               _handlesByPath.TryGetValue(cam.ModelPath, out handle!) &&
               handle.IsLoaded;
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
}
