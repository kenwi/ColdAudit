using System.Numerics;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.LevelModels;

public sealed class LevelModelsFeature : FeatureBase
{
    private static readonly Vector2 PlaceholderPlaneSize =
        new(DebugSectorLayout.Extent, DebugSectorLayout.Extent);

    private static readonly Color PlaceholderPlaneColor = new(40, 48, 58, 255);
    private static readonly Color PortalPlaneColor = new(70, 110, 95, 255);

    private readonly Dictionary<string, ModelHandle> _handles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missingSectorIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _sectorIndexById = new(StringComparer.Ordinal);
    private Camera3D _camera;

    public override void Load(GameWorld world, EventBus events)
    {
        _camera = new Camera3D
        {
            Position = world.PlayerPosition,
            Target = world.PlayerPosition + Vector3.UnitZ,
            Up = Vector3.UnitY,
            FovY = 70f,
            Projection = CameraProjection.Perspective
        };

        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        for (var i = 0; i < level.Sectors.Count; i++)
        {
            var sector = level.Sectors[i];
            _sectorIndexById[sector.Id] = i;

            if (string.IsNullOrWhiteSpace(sector.ModelPath) || !File.Exists(sector.ModelPath))
            {
                _missingSectorIds.Add(sector.Id);
                continue;
            }

            var handle = new ModelHandle();
            handle.Load(sector.ModelPath);
            _handles[sector.Id] = handle;
        }
    }

    public override void Draw(GameWorld world)
    {
        if (world.ActiveLevel is null)
        {
            return;
        }

        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);
        _camera.Position = world.PlayerPosition;
        _camera.Target = world.PlayerPosition + forward;

        Raylib.BeginMode3D(_camera);

        var sectors = world.ActiveLevel.Sectors;
        for (var i = 0; i < sectors.Count; i++)
        {
            var sector = sectors[i];
            if (!sector.RenderEnabled)
            {
                continue;
            }

            if (_handles.TryGetValue(sector.Id, out var handle) && handle.IsLoaded)
            {
                Raylib.DrawModel(handle.Model, Vector3.Zero, 1f, Color.White);
                continue;
            }

            if (_missingSectorIds.Contains(sector.Id))
            {
                Raylib.DrawPlane(DebugSectorLayout.Origin(i), PlaceholderPlaneSize, PlaceholderPlaneColor);
            }
        }

        DrawPortalPlaceholders(world.ActiveLevel);

        Raylib.EndMode3D();
    }

    public override void Unload()
    {
        foreach (var handle in _handles.Values)
        {
            handle.Dispose();
        }

        _handles.Clear();
        _missingSectorIds.Clear();
        _sectorIndexById.Clear();
    }

    private void DrawPortalPlaceholders(LevelData level)
    {
        foreach (var portal in level.Portals)
        {
            if (!_sectorIndexById.TryGetValue(portal.FromSectorId, out var fromIndex) ||
                !_sectorIndexById.TryGetValue(portal.ToSectorId, out var toIndex))
            {
                continue;
            }

            var fromSector = level.Sectors[fromIndex];
            var toSector = level.Sectors[toIndex];
            if (!fromSector.RenderEnabled || !toSector.RenderEnabled)
            {
                continue;
            }

            var from = DebugSectorLayout.Origin(fromIndex);
            var to = DebugSectorLayout.Origin(toIndex);
            var center = (from + to) * 0.5f;
            var delta = to - from;

            // Thin along the connection axis to fill the gap; wider across as a doorway strip.
            Vector2 size;
            if (System.MathF.Abs(delta.X) >= System.MathF.Abs(delta.Z))
            {
                size = new Vector2(DebugSectorLayout.PortalGap, DebugSectorLayout.PortalWidth);
            }
            else
            {
                size = new Vector2(DebugSectorLayout.PortalWidth, DebugSectorLayout.PortalGap);
            }

            Raylib.DrawPlane(center, size, PortalPlaneColor);
        }
    }
}
