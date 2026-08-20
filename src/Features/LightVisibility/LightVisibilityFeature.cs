using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.LightVisibility;

/// <summary>
/// Portal-clipped light occlusion. Each light gets its own room as a box volume plus one
/// shaft per doorway leaving that room, built from the light's position through the opening
/// and capped at the far side of the room it enters. The PBR shader then rejects any
/// fragment outside every volume, so lights no longer bleed through walls.
/// </summary>
public sealed class LightVisibilityFeature : FeatureBase
{
    /// <summary>
    /// Grows sector boxes and shaft caps so adjacent volumes overlap instead of leaving a
    /// one-texel unlit seam at the boundary.
    /// </summary>
    private const float VolumeExpand = 0.05f;

    private readonly List<LightVolumeSet> _volumeSets = [];
    private readonly Frustum _shaft = new();

    /// <summary>Plane-less parent so shafts inherit no near/far clip from a camera.</summary>
    private readonly Frustum _unbounded = new();

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (input.ToggleLightVolumesPressed)
        {
            world.LightVolumeMaskEnabled = !world.LightVolumeMaskEnabled;
        }

        if (world.Lighting is not { IsLoaded: true } lighting)
        {
            return;
        }

        if (!world.LightVolumeMaskEnabled || !world.Sectors.IsBuilt)
        {
            lighting.ClearLightVolumes();
            return;
        }

        RebuildVolumes(world, lighting.Lights);
        lighting.SetLightVolumes(_volumeSets);
    }

    public override void Unload()
    {
        _volumeSets.Clear();
    }

    private void RebuildVolumes(GameWorld world, IReadOnlyList<SceneLight> lights)
    {
        while (_volumeSets.Count < lights.Count)
        {
            _volumeSets.Add(new LightVolumeSet());
        }

        var graph = world.Sectors;
        for (var i = 0; i < lights.Count; i++)
        {
            var light = lights[i];
            var set = _volumeSets[i];
            set.Clear();

            // No authored sector: leave the light unmasked rather than unlighting the level.
            if (string.IsNullOrEmpty(light.SectorId) ||
                !graph.TryGetBounds(light.SectorId, out var sectorBounds))
            {
                continue;
            }

            var ownVolume = set.Add();
            ownVolume?.SetFromAabb(sectorBounds, VolumeExpand);

            foreach (var link in graph.LinksFrom(light.SectorId))
            {
                if (!graph.TryGetBounds(link.OtherSectorId, out var neighbourBounds))
                {
                    continue;
                }

                var shaftVolume = set.Add();
                if (shaftVolume is null)
                {
                    // Set is full: remaining doorways stay dark rather than leaking.
                    break;
                }

                if (!TryBuildShaft(shaftVolume, light.Position, link.Opening, neighbourBounds))
                {
                    set.RemoveLast();
                }
            }
        }
    }

    /// <summary>
    /// Cone from the light through the doorway, capped at the far extent of the room it
    /// enters. Fails when the doorway is degenerate or needs more planes than a volume holds.
    /// </summary>
    private bool TryBuildShaft(
        LightVolume volume,
        Vector3 lightPosition,
        Vector3[] opening,
        Aabb neighbourBounds)
    {
        var axis = OpeningNormalAwayFrom(opening, lightPosition);
        if (axis == Vector3.Zero)
        {
            return false;
        }

        if (!_shaft.TrySetFromEyeAndPortal(lightPosition, opening, _unbounded))
        {
            return false;
        }

        // Dropping a side plane would widen the cone and leak, so bail instead of truncating.
        if (_shaft.PlaneCount + 1 > LightVolume.MaxPlanes)
        {
            return false;
        }

        volume.CopyPlanesFrom(_shaft.Planes);
        return volume.TryAddCap(axis, SupportPoint(neighbourBounds, axis, VolumeExpand));
    }

    /// <summary>Doorway plane normal, flipped to point away from the light.</summary>
    private static Vector3 OpeningNormalAwayFrom(Vector3[] opening, Vector3 lightPosition)
    {
        if (opening.Length < 3)
        {
            return Vector3.Zero;
        }

        var normal = Vector3.Cross(opening[1] - opening[0], opening[2] - opening[0]);
        if (normal.LengthSquared() < 1e-10f)
        {
            return Vector3.Zero;
        }

        normal = Vector3.Normalize(normal);

        var centroid = Vector3.Zero;
        for (var i = 0; i < opening.Length; i++)
        {
            centroid += opening[i];
        }

        centroid /= opening.Length;

        return Vector3.Dot(normal, centroid - lightPosition) < 0f ? -normal : normal;
    }

    /// <summary>Corner of the box furthest along <paramref name="direction"/>.</summary>
    private static Vector3 SupportPoint(Aabb bounds, Vector3 direction, float expand)
    {
        var min = bounds.Min - new Vector3(expand);
        var max = bounds.Max + new Vector3(expand);
        return new Vector3(
            direction.X >= 0f ? max.X : min.X,
            direction.Y >= 0f ? max.Y : min.Y,
            direction.Z >= 0f ? max.Z : min.Z);
    }
}
