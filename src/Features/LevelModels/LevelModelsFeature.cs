using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.LevelModels;

public sealed class LevelModelsFeature : FeatureBase
{
    private readonly Dictionary<string, ModelHandle> _handles = new(StringComparer.Ordinal);
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

        foreach (var sector in level.Sectors)
        {
            if (string.IsNullOrWhiteSpace(sector.ModelPath) || !File.Exists(sector.ModelPath))
            {
                continue;
            }

            var handle = new ModelHandle();
            handle.Load(sector.ModelPath);
            _handles[sector.Id] = handle;
        }
    }

    public override void Draw(GameWorld world)
    {
        if (_handles.Count == 0 || world.ActiveLevel is null)
        {
            return;
        }

        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);
        _camera.Position = world.PlayerPosition;
        _camera.Target = world.PlayerPosition + forward;

        Raylib.BeginMode3D(_camera);

        foreach (var sector in world.ActiveLevel.Sectors)
        {
            if (!sector.RenderEnabled)
            {
                continue;
            }

            if (!_handles.TryGetValue(sector.Id, out var handle) || !handle.IsLoaded)
            {
                continue;
            }

            Raylib.DrawModel(handle.Model, Vector3.Zero, 1f, Color.White);
        }

        Raylib.EndMode3D();
    }

    public override void Unload()
    {
        foreach (var handle in _handles.Values)
        {
            handle.Dispose();
        }

        _handles.Clear();
    }
}
