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
    public float NearPlane { get; init; } = 0.2f;
    public float FarPlane { get; init; } = 16f;
    public float DetectRate { get; init; } = 0.35f;
    public float SweepYawAmplitude { get; init; }
    public float SweepSpeed { get; init; }
    public float SweepPhase { get; init; }
    public string? ModelPath { get; init; }
    public bool Enabled { get; set; } = true;

    public float SweepOffset { get; set; }
    public bool SeeingPlayer { get; set; }

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);

    public float CurrentYaw => MountYaw + SweepOffset;

    public Vector3 MountForward => MathUtil.ForwardFromYawPitch(MountYaw, 0f);

    public Vector3 LookForward => MathUtil.ForwardFromYawPitch(CurrentYaw, Pitch);

    public Vector3 PivotPosition =>
        MountPosition + MountForward * (CamerasFeature.PlateThickness * 0.5f + CamerasFeature.LegLength);

    public Vector3 EyePosition =>
        PivotPosition + LookForward * (CamerasFeature.BodyDepth * 0.45f);
}

public sealed class CamerasFeature : FeatureBase, IShadowCaster
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
    private float _elapsed;

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
        _elapsed = 0f;

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
                NearPlane = def.NearPlane,
                FarPlane = def.FarPlane,
                DetectRate = def.DetectRate,
                SweepYawAmplitude = MathUtil.DegToRad(def.SweepYawDegrees),
                SweepSpeed = MathUtil.DegToRad(def.SweepSpeedDegrees),
                SweepPhase = MathUtil.DegToRad(def.SweepPhaseDegrees),
                ModelPath = def.ModelPath
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

        _elapsed += dt;
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
                cam.SweepOffset = MathF.Sin(_elapsed * cam.SweepSpeed + cam.SweepPhase) * cam.SweepYawAmplitude;
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

            DrawPlaceholder(cam);
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

    private void DrawPlaceholder(SecurityCamera cam)
    {
        var mountYawDeg = MathUtil.RadToDeg(cam.MountYaw);
        var lookYawDeg = MathUtil.RadToDeg(cam.CurrentYaw);
        var lookPitchDeg = MathUtil.RadToDeg(cam.Pitch);
        var mountForward = cam.MountForward;

        _placeholder.Draw(
            cam.MountPosition,
            new Vector3(PlateWidth, PlateHeight, PlateThickness),
            mountYawDeg,
            PlateColor);

        var legCenter = cam.MountPosition + mountForward * (PlateThickness * 0.5f + LegLength * 0.5f);
        _placeholder.Draw(
            legCenter,
            new Vector3(LegThickness, LegThickness, LegLength),
            mountYawDeg,
            LegColor);

        var bodyColor = cam.SeeingPlayer ? BodyAlertColor : BodyColor;
        _placeholder.Draw(
            cam.PivotPosition,
            new Vector3(BodyWidth, BodyHeight, BodyDepth),
            lookYawDeg,
            lookPitchDeg,
            bodyColor);
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
