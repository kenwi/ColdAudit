using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.Cameras;

public sealed class SecurityCamera
{
    public string Id { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public Vector3 Position { get; set; }
    public float Yaw { get; set; }
    public float FovDegrees { get; set; } = 60f;
    public float DetectRate { get; set; } = 0.35f;
    public bool Enabled { get; set; } = true;
}

public sealed class CamerasFeature : FeatureBase
{
    private readonly List<SecurityCamera> _cameras = [];

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        foreach (var disabled in events.OfType<CameraDisabled>())
        {
            foreach (var cam in _cameras)
            {
                if (cam.Id == disabled.CameraId)
                {
                    cam.Enabled = false;
                }
            }
        }

        // Vision cone tests + DetectionSample publish come next.
        _ = dt;
        _ = world;
        _ = input;
    }
}
