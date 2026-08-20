using System.Numerics;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Math;

namespace ColdAudit.Shared.World;

/// <summary>
/// A doorway leaving one sector. <see cref="Opening"/> is wound so eye-edge planes
/// built from a viewpoint inside the owning sector face toward <see cref="OtherSectorId"/>.
/// </summary>
public sealed record PortalLink(string OtherSectorId, Aabb Bounds, Vector3[] Opening);

/// <summary>
/// A portal volume plus the sectors it joins, for viewpoints standing inside the doorway itself.
/// </summary>
public readonly record struct PortalVolume(string FromSectorId, string ToSectorId, Aabb Bounds);

/// <summary>
/// Resolved sector volumes and portal adjacency for the active level. Built once when the
/// level loads; shared by sector visibility (camera eye) and light visibility (light eye).
/// </summary>
public sealed class SectorGraph
{
    private static readonly PortalLink[] NoLinks = [];

    private readonly Dictionary<string, Aabb> _boundsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<PortalLink>> _linksBySector = new(StringComparer.Ordinal);
    private readonly List<PortalVolume> _portalVolumes = [];
    private readonly List<string> _sectorIds = [];

    public bool IsBuilt => _sectorIds.Count > 0;
    public IReadOnlyList<string> SectorIds => _sectorIds;
    public IReadOnlyList<PortalVolume> PortalVolumes => _portalVolumes;

    public void Build(LevelData? level)
    {
        Clear();
        if (level is null)
        {
            return;
        }

        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < level.Sectors.Count; i++)
        {
            var sector = level.Sectors[i];
            indexById[sector.Id] = i;
            _sectorIds.Add(sector.Id);
            _boundsById[sector.Id] = AuthoredLevelGeometry.ResolveSectorBounds(sector, i);
            _linksBySector[sector.Id] = [];
        }

        foreach (var portal in level.Portals)
        {
            if (!indexById.TryGetValue(portal.FromSectorId, out var fromIndex) ||
                !indexById.TryGetValue(portal.ToSectorId, out var toIndex))
            {
                continue;
            }

            var fromBounds = _boundsById[portal.FromSectorId];
            var toBounds = _boundsById[portal.ToSectorId];
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

            _portalVolumes.Add(new PortalVolume(portal.FromSectorId, portal.ToSectorId, portalBounds));
            AddLink(portal.FromSectorId, portal.ToSectorId, portalBounds, openingForward);

            if (portal.TwoWay)
            {
                AddLink(
                    portal.ToSectorId,
                    portal.FromSectorId,
                    portalBounds,
                    ReverseOpening(openingForward));
            }
        }
    }

    public void Clear()
    {
        _boundsById.Clear();
        _linksBySector.Clear();
        _portalVolumes.Clear();
        _sectorIds.Clear();
    }

    public bool TryGetBounds(string sectorId, out Aabb bounds) =>
        _boundsById.TryGetValue(sectorId, out bounds);

    public IReadOnlyList<PortalLink> LinksFrom(string sectorId) =>
        _linksBySector.TryGetValue(sectorId, out var links) ? links : NoLinks;

    private void AddLink(string fromSectorId, string toSectorId, Aabb portalBounds, Vector3[] opening)
    {
        if (!_linksBySector.TryGetValue(fromSectorId, out var links))
        {
            links = [];
            _linksBySector[fromSectorId] = links;
        }

        links.Add(new PortalLink(toSectorId, portalBounds, opening));
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
}
