using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.Keycards;

public sealed class KeycardState
{
    public string Id { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public Vector3 Position { get; init; }
    public float Yaw { get; init; }
    public float Width { get; init; } = 0.18f;
    public float Height { get; init; } = 0.012f;
    public float Depth { get; init; } = 0.115f;
    public float InteractRadius { get; init; } = 2f;
    public string? ModelPath { get; init; }
    public bool PickedUp { get; set; }

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);

    public string Prompt => "Keycard  [E] Pick up";
}

public sealed class KeycardsFeature : FeatureBase, IInteractableSource
{
    private static readonly Color CardFill = new(28, 68, 132, 255);
    private static readonly Color CardFillFocused = new(72, 128, 204, 255);
    private static readonly Color CardWire = new(12, 24, 48, 255);
    private static readonly Color ChipFill = new(212, 175, 55, 255);

    private readonly List<KeycardState> _cards = [];
    private readonly Dictionary<string, ModelHandle> _handlesByPath = new(StringComparer.Ordinal);

    public IReadOnlyList<KeycardState> Cards => _cards;

    public override void Load(GameWorld world, EventBus events)
    {
        _cards.Clear();
        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        foreach (var def in level.Keycards)
        {
            _cards.Add(new KeycardState
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
                ModelPath = def.ModelPath
            });

            TryLoadModel(world, def.ModelPath);
        }
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        List<string>? acquired = null;
        foreach (var use in events.OfType<UseRequested>())
        {
            var card = Find(use.InteractableId);
            if (card is null || card.PickedUp)
            {
                continue;
            }

            card.PickedUp = true;
            acquired ??= [];
            acquired.Add(card.ItemId);
            if (world.FocusedInteractableId == card.Id)
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
        if (_cards.Count == 0)
        {
            return;
        }

        EnsureModelLighting(world);

        Raylib.BeginMode3D(world.Draw.Camera);

        foreach (var card in _cards)
        {
            if (!IsCardDrawn(world, card) || !TryGetModel(card, out _))
            {
                continue;
            }

            DrawCard(card, world.FocusedInteractableId == card.Id);
        }

        var lighting = world.Lighting is { IsLoaded: true } lit ? lit : null;
        var useLighting = lighting is not null && lighting.TryBeginShaderMode();

        foreach (var card in _cards)
        {
            if (!IsCardDrawn(world, card) || TryGetModel(card, out _))
            {
                continue;
            }

            DrawCard(card, world.FocusedInteractableId == card.Id);
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
        _cards.Clear();
    }

    public bool TryPickFocused(GameWorld world, out InteractableHit hit)
    {
        hit = default;
        var origin = world.PlayerPosition;
        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);

        KeycardState? best = null;
        var bestDistance = float.MaxValue;

        foreach (var candidate in _cards)
        {
            if (candidate.PickedUp || !IsCardDrawn(world, candidate))
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

    private void DrawCard(KeycardState card, bool focused)
    {
        if (TryGetModel(card, out var handle))
        {
            Raylib.DrawModelEx(
                handle.Model,
                card.Position,
                Vector3.UnitY,
                MathUtil.RadToDeg(card.Yaw),
                Vector3.One,
                Color.White);
            return;
        }

        var fill = focused ? CardFillFocused : CardFill;
        Rlgl.PushMatrix();
        Rlgl.Translatef(card.Position.X, card.Position.Y, card.Position.Z);
        Rlgl.Rotatef(MathUtil.RadToDeg(card.Yaw), 0f, 1f, 0f);
        Rlgl.Translatef(0f, card.Height * 0.5f, 0f);
        Raylib.DrawCube(Vector3.Zero, card.Width, card.Height, card.Depth, fill);
        Raylib.DrawCubeWires(Vector3.Zero, card.Width, card.Height, card.Depth, CardWire);

        // Gold chip so the placeholder reads as a keycard until a GLB replaces it.
        Rlgl.Translatef(-card.Width * 0.28f, card.Height * 0.5f + 0.0015f, card.Depth * 0.12f);
        Raylib.DrawCube(Vector3.Zero, 0.028f, 0.003f, 0.022f, ChipFill);
        Rlgl.PopMatrix();
    }

    private bool TryRaycast(KeycardState card, Vector3 origin, Vector3 direction, out float distance)
    {
        if (TryGetModel(card, out var handle))
        {
            return TryRaycastModel(handle, card, origin, direction, out distance);
        }

        var invYaw = Matrix4x4.CreateRotationY(-card.Yaw);
        var localOrigin = Vector3.Transform(origin - card.Position, invYaw);
        var localDir = Vector3.TransformNormal(direction, invYaw);
        if (localDir.LengthSquared() < 1e-8f)
        {
            distance = 0f;
            return false;
        }

        localDir = Vector3.Normalize(localDir);
        var halfW = card.Width * 0.5f;
        var halfD = card.Depth * 0.5f;
        var localBox = new Aabb(
            new Vector3(-halfW, 0f, -halfD),
            new Vector3(halfW, MathF.Max(card.Height, 0.08f), halfD));
        return localBox.TryIntersectRay(localOrigin, localDir, out distance);
    }

    private static bool TryRaycastModel(
        ModelHandle handle,
        KeycardState card,
        Vector3 origin,
        Vector3 direction,
        out float distance)
    {
        distance = float.MaxValue;
        var ray = new Ray(origin, direction);
        var transform =
            Matrix4x4.CreateRotationY(card.Yaw) *
            Matrix4x4.CreateTranslation(card.Position);

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

    private static bool IsPlayerInRadius(Vector3 playerPosition, KeycardState card)
    {
        var dx = playerPosition.X - card.Position.X;
        var dz = playerPosition.Z - card.Position.Z;
        var radius = card.InteractRadius;
        return dx * dx + dz * dz <= radius * radius;
    }

    private static bool IsCardDrawn(GameWorld world, KeycardState card)
    {
        if (card.PickedUp)
        {
            return false;
        }

        if (string.IsNullOrEmpty(card.SectorId) || !world.SectorCullEnabled)
        {
            return true;
        }

        return world.VisibleSectorIds.Contains(card.SectorId);
    }

    private KeycardState? Find(string id)
    {
        foreach (var card in _cards)
        {
            if (card.Id == id)
            {
                return card;
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

    private bool TryGetModel(KeycardState card, out ModelHandle handle)
    {
        handle = null!;
        return card.HasModel &&
               card.ModelPath is not null &&
               _handlesByPath.TryGetValue(card.ModelPath, out handle!) &&
               handle.IsLoaded;
    }
}
