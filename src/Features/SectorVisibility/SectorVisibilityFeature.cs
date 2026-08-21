using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.SectorVisibility;

public sealed class SectorVisibilityState
{
    public HashSet<string> Visible { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Portal-clipped sector visibility: recurse only through openings that remain
/// visible after clipping each hop against the current frustum.
/// </summary>
public sealed class SectorVisibilityFeature : FeatureBase
{
    private const float CameraNear = 0.05f;
    private const float CameraFar = 250f;
    private const int MaxClipVerts = 32;

    private readonly SectorVisibilityState _state = new();
    private readonly Frustum _cameraFrustum = new();
    private readonly Queue<(string SectorId, Frustum Frustum)> _frontier = new();
    private readonly Stack<Frustum> _frustumPool = new();
    private readonly HashSet<(string From, string To)> _expandedPortals = new();
    private readonly Vector3[] _clippedPortal = new Vector3[MaxClipVerts];

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (input.ToggleSectorCullPressed)
        {
            world.SectorCullEnabled = !world.SectorCullEnabled;
        }

        ResolveCurrentSector(world);
        ResolveVisibleSectors(world);

        world.VisibleSectorIds.Clear();
        foreach (var id in _state.Visible)
        {
            world.VisibleSectorIds.Add(id);
        }
    }

    public override void Unload()
    {
        _state.Visible.Clear();
        RecycleFrontierFrustums();
        _expandedPortals.Clear();
        _frustumPool.Clear();
    }

    private void ResolveVisibleSectors(GameWorld world)
    {
        RecycleFrontierFrustums();
        _state.Visible.Clear();
        _expandedPortals.Clear();

        var graph = world.Sectors;
        if (!graph.IsBuilt)
        {
            return;
        }

        if (!world.SectorCullEnabled)
        {
            foreach (var id in graph.SectorIds)
            {
                _state.Visible.Add(id);
            }

            return;
        }

        var eye = world.PlayerPosition;
        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);
        _cameraFrustum.UpdateFromCamera(
            eye,
            forward,
            Vector3.UnitY,
            DrawContext.FovYDegrees,
            ResolveAspect(),
            CameraNear,
            CameraFar);

        if (!string.IsNullOrEmpty(world.CurrentSectorId))
        {
            EnqueueSeed(world.CurrentSectorId);
        }

        // Standing in a portal volume: seed both rooms with the camera frustum.
        foreach (var volume in graph.PortalVolumes)
        {
            if (!volume.Bounds.ContainsXz(eye))
            {
                continue;
            }

            EnqueueSeed(volume.FromSectorId);
            EnqueueSeed(volume.ToSectorId);
        }

        while (_frontier.Count > 0)
        {
            var (sectorId, frustum) = _frontier.Dequeue();

            foreach (var link in graph.LinksFrom(sectorId))
            {
                if (!_expandedPortals.Add((sectorId, link.OtherSectorId)))
                {
                    continue;
                }

                // Cheap reject before polygon clip.
                if (!frustum.IntersectsAabb(link.Bounds))
                {
                    continue;
                }

                if (!frustum.TryClipPolygon(link.Opening, _clippedPortal, out var clippedCount))
                {
                    continue;
                }

                var child = RentFrustum();
                if (!child.TrySetFromEyeAndPortal(
                        eye,
                        _clippedPortal.AsSpan(0, clippedCount),
                        frustum))
                {
                    ReturnFrustum(child);
                    continue;
                }

                _state.Visible.Add(link.OtherSectorId);
                _frontier.Enqueue((link.OtherSectorId, child));
            }

            ReturnFrustumIfRented(frustum);
        }
    }

    private void EnqueueSeed(string sectorId)
    {
        if (string.IsNullOrEmpty(sectorId) || !_state.Visible.Add(sectorId))
        {
            return;
        }

        var copy = RentFrustum();
        copy.CopyFrom(_cameraFrustum);
        _frontier.Enqueue((sectorId, copy));
    }

    private Frustum RentFrustum() =>
        _frustumPool.Count > 0 ? _frustumPool.Pop() : new Frustum();

    private void ReturnFrustum(Frustum frustum) => _frustumPool.Push(frustum);

    private void ReturnFrustumIfRented(Frustum frustum)
    {
        if (!ReferenceEquals(frustum, _cameraFrustum))
        {
            ReturnFrustum(frustum);
        }
    }

    private void RecycleFrontierFrustums()
    {
        while (_frontier.Count > 0)
        {
            var (_, frustum) = _frontier.Dequeue();
            ReturnFrustumIfRented(frustum);
        }
    }

    private static float ResolveAspect()
    {
        var width = Raylib.GetScreenWidth();
        var height = Raylib.GetScreenHeight();
        if (width <= 0 || height <= 0)
        {
            return UiFramebuffer.Width / (float)UiFramebuffer.Height;
        }

        return width / (float)height;
    }

    private static void ResolveCurrentSector(GameWorld world)
    {
        var graph = world.Sectors;

        // Prefer the smallest authored volume containing the player (overlapping rooms).
        string? bestId = null;
        var bestArea = float.MaxValue;
        foreach (var sectorId in graph.SectorIds)
        {
            if (!graph.TryGetBounds(sectorId, out var bounds) ||
                !bounds.ContainsXz(world.PlayerPosition))
            {
                continue;
            }

            var area = bounds.Size.X * bounds.Size.Z;
            if (area >= bestArea)
            {
                continue;
            }

            bestArea = area;
            bestId = sectorId;
        }

        if (bestId is not null)
        {
            world.CurrentSectorId = bestId;
        }
    }
}
