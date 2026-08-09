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
    private readonly Dictionary<string, List<PortalEdge>> _edgesBySector = new(StringComparer.Ordinal);
    private readonly List<PortalSeed> _portalSeeds = [];
    private readonly Frustum _cameraFrustum = new();
    private readonly Queue<(string SectorId, Frustum Frustum)> _frontier = new();
    private readonly Stack<Frustum> _frustumPool = new();
    private readonly HashSet<(string From, string To)> _expandedPortals = new();
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
            _edgesBySector[sector.Id] = [];
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

            var openingForward = new Vector3[4];
            if (!AuthoredLevelGeometry.TryWritePortalOpening(
                    portal,
                    fromIndex,
                    toIndex,
                    portalBounds,
                    fromBounds,
                    toBounds,
                    openingForward))
            {
                continue;
            }

            _portalSeeds.Add(new PortalSeed(portal.FromSectorId, portal.ToSectorId, portalBounds));

            AddEdge(portal.FromSectorId, portal.ToSectorId, portalBounds, openingForward);

            if (portal.TwoWay)
            {
                AddEdge(
                    portal.ToSectorId,
                    portal.FromSectorId,
                    portalBounds,
                    ReverseOpening(openingForward));
            }
        }
    }

    private void AddEdge(
        string fromSectorId,
        string toSectorId,
        Aabb portalBounds,
        Vector3[] opening)
    {
        if (!_edgesBySector.TryGetValue(fromSectorId, out var edges))
        {
            edges = [];
            _edgesBySector[fromSectorId] = edges;
        }

        edges.Add(new PortalEdge(toSectorId, portalBounds, opening));
    }

    private static Vector3[] ReverseOpening(Vector3[] opening)
    {
        var reversed = new Vector3[opening.Length];
        for (var i = 0; i < opening.Length; i++)
        {
            reversed[i] = opening[opening.Length - 1 - i];
        }

        return reversed;
    }

    private void ClearCaches()
    {
        _sectorIndexById.Clear();
        _sectorBoundsById.Clear();
        _edgesBySector.Clear();
        _portalSeeds.Clear();
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
        foreach (var seed in _portalSeeds)
        {
            if (!seed.Bounds.ContainsXz(eye))
            {
                continue;
            }

            EnqueueSeed(seed.FromSectorId);
            EnqueueSeed(seed.ToSectorId);
        }

        while (_frontier.Count > 0)
        {
            var (sectorId, frustum) = _frontier.Dequeue();
            if (!_edgesBySector.TryGetValue(sectorId, out var edges))
            {
                ReturnFrustumIfRented(frustum);
                continue;
            }

            foreach (var edge in edges)
            {
                if (!_expandedPortals.Add((sectorId, edge.OtherSectorId)))
                {
                    continue;
                }

                // Cheap reject before polygon clip.
                if (!frustum.IntersectsAabb(edge.Bounds))
                {
                    continue;
                }

                if (!frustum.TryClipPolygon(edge.Opening, _clippedPortal, out var clippedCount))
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

                _state.Visible.Add(edge.OtherSectorId);
                _frontier.Enqueue((edge.OtherSectorId, child));
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

    private readonly record struct PortalEdge(
        string OtherSectorId,
        Aabb Bounds,
        Vector3[] Opening);

    private readonly record struct PortalSeed(
        string FromSectorId,
        string ToSectorId,
        Aabb Bounds);
}
