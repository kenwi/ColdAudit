using System.Numerics;
using ColdAudit.Features.LevelLoad;
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
    private const float CameraFovYDegrees = 70f;
    private const float CameraNear = 0.05f;
    private const float CameraFar = 250f;
    private const int MaxClipVerts = 32;

    private readonly SectorVisibilityState _state = new();
    private readonly Dictionary<string, int> _sectorIndexById = new(StringComparer.Ordinal);
    private readonly Frustum _cameraFrustum = new();
    private readonly Queue<(string SectorId, Frustum Frustum)> _frontier = new();
    private readonly Stack<Frustum> _frustumPool = new();
    private readonly HashSet<(string From, string To)> _expandedPortals = new();
    private readonly List<(string OtherSectorId, int OtherIndex)> _portalScratch = [];
    private readonly Vector3[] _portalCorners = new Vector3[4];
    private readonly Vector3[] _clippedPortal = new Vector3[MaxClipVerts];

    public override void Load(GameWorld world, EventBus events)
    {
        RebuildSectorIndex(world.ActiveLevel);
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (input.ToggleSectorCullPressed)
        {
            world.SectorCullEnabled = !world.SectorCullEnabled;
        }

        if (_sectorIndexById.Count == 0)
        {
            RebuildSectorIndex(world.ActiveLevel);
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
        _sectorIndexById.Clear();
        _state.Visible.Clear();
        RecycleFrontierFrustums();
        _portalScratch.Clear();
        _expandedPortals.Clear();
        _frustumPool.Clear();
    }

    private void RebuildSectorIndex(LevelData? level)
    {
        _sectorIndexById.Clear();
        if (level is null)
        {
            return;
        }

        for (var i = 0; i < level.Sectors.Count; i++)
        {
            _sectorIndexById[level.Sectors[i].Id] = i;
        }
    }

    private void ResolveVisibleSectors(GameWorld world)
    {
        RecycleFrontierFrustums();
        _state.Visible.Clear();
        _expandedPortals.Clear();

        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        if (!world.SectorCullEnabled)
        {
            foreach (var sector in level.Sectors)
            {
                _state.Visible.Add(sector.Id);
            }

            return;
        }

        var eye = world.PlayerPosition;
        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);
        _cameraFrustum.UpdateFromCamera(
            eye,
            forward,
            Vector3.UnitY,
            CameraFovYDegrees,
            ResolveAspect(),
            CameraNear,
            CameraFar);

        if (!string.IsNullOrEmpty(world.CurrentSectorId))
        {
            EnqueueSeed(world.CurrentSectorId);
        }

        // Standing in a portal volume: seed both rooms with the camera frustum.
        foreach (var portal in level.Portals)
        {
            if (!_sectorIndexById.TryGetValue(portal.FromSectorId, out var fromIndex) ||
                !_sectorIndexById.TryGetValue(portal.ToSectorId, out var toIndex))
            {
                continue;
            }

            if (!DebugSectorLayout.PortalBounds(fromIndex, toIndex).ContainsXz(eye))
            {
                continue;
            }

            EnqueueSeed(portal.FromSectorId);
            EnqueueSeed(portal.ToSectorId);
        }

        while (_frontier.Count > 0)
        {
            var (sectorId, frustum) = _frontier.Dequeue();
            if (!_sectorIndexById.TryGetValue(sectorId, out var sectorIndex))
            {
                ReturnFrustumIfRented(frustum);
                continue;
            }

            CollectPortalsFrom(level, sectorId, _portalScratch);
            foreach (var (otherId, otherIndex) in _portalScratch)
            {
                if (!_expandedPortals.Add((sectorId, otherId)))
                {
                    continue;
                }

                DebugSectorLayout.GetPortalOpening(sectorIndex, otherIndex, _portalCorners);
                if (!frustum.TryClipPolygon(_portalCorners, _clippedPortal, out var clippedCount))
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

                _state.Visible.Add(otherId);
                _frontier.Enqueue((otherId, child));
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

    private void CollectPortalsFrom(
        LevelData level,
        string sectorId,
        List<(string OtherSectorId, int OtherIndex)> into)
    {
        into.Clear();
        foreach (var portal in level.Portals)
        {
            string? other = null;
            if (portal.FromSectorId == sectorId)
            {
                other = portal.ToSectorId;
            }
            else if (portal.TwoWay && portal.ToSectorId == sectorId)
            {
                other = portal.FromSectorId;
            }

            if (other is null || !_sectorIndexById.TryGetValue(other, out var otherIndex))
            {
                continue;
            }

            into.Add((other, otherIndex));
        }
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
        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        foreach (var sector in level.Sectors)
        {
            if (sector.Bounds.ContainsXz(world.PlayerPosition))
            {
                world.CurrentSectorId = sector.Id;
                return;
            }
        }
    }
}
