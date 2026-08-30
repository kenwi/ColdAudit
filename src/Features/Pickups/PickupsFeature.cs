using System.Numerics;
using ColdAudit.Features.Inventory;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.Pickups;

public sealed class PickupState
{
    public string Id { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public Vector3 Position { get; init; }
    public float Yaw { get; init; }
    public float Width { get; init; } = 0.08f;
    public float Height { get; init; } = 0.08f;
    public float Depth { get; init; } = 0.08f;
    public float InteractRadius { get; init; } = 2f;
    public string? ModelPath { get; init; }
    public Color Color { get; init; }
    public bool PickedUp { get; set; }

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);

    public string Prompt
    {
        get
        {
            var label = ItemVisualCatalog.Resolve(ItemId, null).Label;
            return $"{label}  [E] Pick up";
        }
    }
}

public sealed class PickupsFeature : FeatureBase, IInteractableSource
{
    private static readonly Color DefaultFill = new(140, 140, 140, 255);

    private readonly List<PickupState> _pickups = [];
    private readonly Dictionary<string, ModelHandle> _handlesByPath = new(StringComparer.Ordinal);
    private readonly LitBoxMesh _placeholder = new();

    public IReadOnlyList<PickupState> Pickups => _pickups;

    public override void Load(GameWorld world, EventBus events)
    {
        _pickups.Clear();
        _placeholder.Load();
        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        foreach (var def in level.Pickups)
        {
            _pickups.Add(new PickupState
            {
                Id = def.Id,
                ItemId = def.ItemId,
                SectorId = def.SectorId,
                Position = def.Position,
                Yaw = MathUtil.DegToRad(def.YawDegrees),
                Width = def.Width,
                Height = def.Height,
                Depth = def.Depth,
                InteractRadius = def.InteractRadius,
                ModelPath = def.ModelPath,
                Color = def.Color
            });

            TryLoadModel(world, def.ModelPath);
        }
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        List<string>? acquired = null;
        foreach (var use in events.OfType<UseRequested>())
        {
            var pickup = Find(use.InteractableId);
            if (pickup is null || pickup.PickedUp)
            {
                continue;
            }

            pickup.PickedUp = true;
            acquired ??= [];
            acquired.Add(pickup.ItemId);
            if (world.FocusedInteractableId == pickup.Id)
            {
                world.FocusedInteractableId = null;
                world.UsePrompt = string.Empty;
            }
        }

        if (acquired is null)
        {
            return;
        }

        foreach (var itemId in acquired)
        {
            events.Publish(new ItemAcquired(itemId));
        }
    }

    public override void Draw(GameWorld world)
    {
        if (_pickups.Count == 0)
        {
            return;
        }

        EnsureModelLighting(world);
        _placeholder.EnsureLighting(world.Lighting);

        Raylib.BeginMode3D(world.Draw.Camera);

        foreach (var pickup in _pickups)
        {
            if (!IsPickupDrawn(world, pickup) || !TryGetModel(pickup, out _))
            {
                continue;
            }

            DrawPickup(pickup, world.FocusedInteractableId == pickup.Id);
        }

        world.Lighting?.RestorePbrDrawDefaults();
        world.Lighting?.SetAlbedoMapEnabled(false);

        foreach (var pickup in _pickups)
        {
            if (!IsPickupDrawn(world, pickup) || TryGetModel(pickup, out _))
            {
                continue;
            }

            DrawPickup(pickup, world.FocusedInteractableId == pickup.Id);
        }

        world.Lighting?.RestorePbrDrawDefaults();
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
        _pickups.Clear();
        _placeholder.Unload();
    }

    public bool TryPickFocused(GameWorld world, out InteractableHit hit)
    {
        hit = default;
        var origin = world.PlayerPosition;
        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);

        PickupState? best = null;
        var bestDistance = float.MaxValue;

        foreach (var candidate in _pickups)
        {
            if (candidate.PickedUp || !IsPickupDrawn(world, candidate))
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

    private void DrawPickup(PickupState pickup, bool focused)
    {
        if (TryGetModel(pickup, out var handle))
        {
            Raylib.DrawModelEx(
                handle.Model,
                pickup.Position,
                Vector3.UnitY,
                MathUtil.RadToDeg(pickup.Yaw),
                Vector3.One,
                Color.White);
            return;
        }

        var fill = focused ? Lighten(ResolveFill(pickup)) : ResolveFill(pickup);
        var yaw = MathUtil.RadToDeg(pickup.Yaw);
        var bodyCenter = pickup.Position + new Vector3(0f, pickup.Height * 0.5f, 0f);
        _placeholder.Draw(
            bodyCenter,
            new Vector3(pickup.Width, pickup.Height, pickup.Depth),
            yaw,
            fill);
    }

    private static Color ResolveFill(PickupState pickup)
    {
        if (pickup.Color.A != 0)
        {
            return pickup.Color;
        }

        var catalog = ItemVisualCatalog.Resolve(pickup.ItemId, null).Color;
        return catalog.A == 0 ? DefaultFill : catalog;
    }

    private static Color Lighten(Color color) =>
        new(
            (byte)Math.Min(255, color.R + 48),
            (byte)Math.Min(255, color.G + 48),
            (byte)Math.Min(255, color.B + 48),
            color.A);

    private bool TryRaycast(PickupState pickup, Vector3 origin, Vector3 direction, out float distance)
    {
        if (TryGetModel(pickup, out var handle))
        {
            return TryRaycastModel(handle, pickup, origin, direction, out distance);
        }

        var invYaw = Matrix4x4.CreateRotationY(-pickup.Yaw);
        var localOrigin = Vector3.Transform(origin - pickup.Position, invYaw);
        var localDir = Vector3.TransformNormal(direction, invYaw);
        if (localDir.LengthSquared() < 1e-8f)
        {
            distance = 0f;
            return false;
        }

        localDir = Vector3.Normalize(localDir);
        var halfW = pickup.Width * 0.5f;
        var halfD = pickup.Depth * 0.5f;
        var localBox = new Aabb(
            new Vector3(-halfW, 0f, -halfD),
            new Vector3(halfW, MathF.Max(pickup.Height, 0.08f), halfD));
        return localBox.TryIntersectRay(localOrigin, localDir, out distance);
    }

    private static bool TryRaycastModel(
        ModelHandle handle,
        PickupState pickup,
        Vector3 origin,
        Vector3 direction,
        out float distance)
    {
        distance = float.MaxValue;
        var ray = new Ray(origin, direction);
        var transform =
            Matrix4x4.CreateRotationY(pickup.Yaw) *
            Matrix4x4.CreateTranslation(pickup.Position);

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

    private static bool IsPlayerInRadius(Vector3 playerPosition, PickupState pickup)
    {
        var dx = playerPosition.X - pickup.Position.X;
        var dz = playerPosition.Z - pickup.Position.Z;
        var radius = pickup.InteractRadius;
        return dx * dx + dz * dz <= radius * radius;
    }

    private static bool IsPickupDrawn(GameWorld world, PickupState pickup)
    {
        if (pickup.PickedUp)
        {
            return false;
        }

        if (string.IsNullOrEmpty(pickup.SectorId) || !world.SectorCullEnabled)
        {
            return true;
        }

        return world.VisibleSectorIds.Contains(pickup.SectorId);
    }

    private PickupState? Find(string id)
    {
        foreach (var pickup in _pickups)
        {
            if (pickup.Id == id)
            {
                return pickup;
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

    private bool TryGetModel(PickupState pickup, out ModelHandle handle)
    {
        handle = null!;
        return pickup.HasModel &&
               pickup.ModelPath is not null &&
               _handlesByPath.TryGetValue(pickup.ModelPath, out handle!) &&
               handle.IsLoaded;
    }
}
