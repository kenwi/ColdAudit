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
    private readonly Dictionary<string, Aabb> _sectorBoundsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Aabb> _portalBoundsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3[]> _portalOpeningsById = new(StringComparer.Ordinal);
    private readonly Frustum _cameraFrustum = new();
    private readonly Queue<(string SectorId, Frustum Frustum)> _frontier = new();
    private readonly Stack<Frustum> _frustumPool = new();
    private readonly HashSet<(string From, string To)> _expandedPortals = new();
    private readonly List<(string OtherSectorId, PortalDef Portal)> _portalScratch = [];
    private readonly Vector3[] _facingPortal = new Vector3[MaxClipVerts];
    private readonly Vector3[] _clippedPortal = new Vector3[MaxClipVerts];

    public override void Load(GameWorld world, EventBus events)
    {
        RebuildCaches(world.ActiveLevel);
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (input.ToggleSectorCullPressed)
        {
            world.SectorCullEnabled = !world.SectorCullEnabled;
        }

        if (_sectorIndexById.Count == 0)
        {
            RebuildCaches(world.ActiveLevel);
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
        ClearCaches();
        _state.Visible.Clear();
        RecycleFrontierFrustums();
        _portalScratch.Clear();
        _expandedPortals.Clear();
        _frustumPool.Clear();
    }

    private void RebuildCaches(LevelData? level)
    {
        ClearCaches();
        if (level is null)
        {
            return;
        }

        for (var i = 0; i < level.Sectors.Count; i++)
        {
            var sector = level.Sectors[i];
            _sectorIndexById[sector.Id] = i;
            _sectorBoundsById[sector.Id] = AuthoredLevelGeometry.ResolveSectorBounds(sector, i);
        }

        foreach (var portal in level.Portals)
        {
            if (!_sectorIndexById.TryGetValue(portal.FromSectorId, out var fromIndex) ||
                !_sectorIndexById.TryGetValue(portal.ToSectorId, out var toIndex))
            {
                continue;
            }

            var fromBounds = _sectorBoundsById[portal.FromSectorId];
            var toBounds = _sectorBoundsById[portal.ToSectorId];
            var portalBounds = AuthoredLevelGeometry.ResolvePortalBounds(
                portal,
                fromIndex,
                toIndex,
                fromBounds,
                toBounds);
            _portalBoundsById[portal.Id] = portalBounds;

            var opening = new Vector3[4];
            if (AuthoredLevelGeometry.TryWritePortalOpening(
                    portal,
                    fromIndex,
                    toIndex,
                    portalBounds,
                    fromBounds,
                    toBounds,
                    opening))
            {
                _portalOpeningsById[portal.Id] = opening;
            }
        }
    }

    private void ClearCaches()
    {
        _sectorIndexById.Clear();
        _sectorBoundsById.Clear();
        _portalBoundsById.Clear();
        _portalOpeningsById.Clear();
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
            if (!_portalBoundsById.TryGetValue(portal.Id, out var portalBounds) ||
                !portalBounds.ContainsXz(eye))
            {
                continue;
            }

            EnqueueSeed(portal.FromSectorId);
            EnqueueSeed(portal.ToSectorId);
        }

        while (_frontier.Count > 0)
        {
            var (sectorId, frustum) = _frontier.Dequeue();
            if (!_sectorIndexById.ContainsKey(sectorId))
            {
                ReturnFrustumIfRented(frustum);
                continue;
            }

            CollectPortalsFrom(level, sectorId, _portalScratch);
            foreach (var (otherId, portal) in _portalScratch)
            {
                if (!_expandedPortals.Add((sectorId, otherId)))
                {
                    continue;
                }

                if (!_portalOpeningsById.TryGetValue(portal.Id, out var opening))
                {
                    continue;
                }

                // Wind opening toward the sector we are entering.
                var openingCount = System.Math.Min(opening.Length, _facingPortal.Length);
                WriteOpeningFacing(opening, sectorId, portal, _facingPortal.AsSpan(0, openingCount));

                if (!frustum.TryClipPolygon(
                        _facingPortal.AsSpan(0, openingCount),
                        _clippedPortal,
                        out var clippedCount))
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

    private void WriteOpeningFacing(
        Vector3[] opening,
        string fromSectorId,
        PortalDef portal,
        Span<Vector3> facing)
    {
        var count = System.Math.Min(opening.Length, facing.Length);
        var reverse = portal.TwoWay &&
                      portal.ToSectorId == fromSectorId &&
                      portal.FromSectorId != fromSectorId;

        if (!reverse)
        {
            for (var i = 0; i < count; i++)
            {
                facing[i] = opening[i];
            }

            return;
        }

        for (var i = 0; i < count; i++)
        {
            facing[i] = opening[count - 1 - i];
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
        List<(string OtherSectorId, PortalDef Portal)> into)
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

            if (other is null || !_sectorIndexById.ContainsKey(other))
            {
                continue;
            }

            into.Add((other, portal));
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

    private void ResolveCurrentSector(GameWorld world)
    {
        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        // Prefer the smallest authored volume containing the player (overlapping rooms).
        string? bestId = null;
        var bestArea = float.MaxValue;
        foreach (var sector in level.Sectors)
        {
            if (!_sectorBoundsById.TryGetValue(sector.Id, out var bounds) ||
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
            bestId = sector.Id;
        }

        if (bestId is not null)
        {
            world.CurrentSectorId = bestId;
        }
    }
}
