using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.PlayerController;

public sealed class PlayerConfig
{
    public float MoveSpeed { get; set; } = 4.5f;
    public float CrouchSpeedMultiplier { get; set; } = 0.5f;
    public float MouseSensitivity { get; set; } = 0.003f;
    public float EyeHeight { get; set; } = 1.7f;
    public float CrouchEyeHeight { get; set; } = 1.0f;
    public float PitchLimit { get; set; } = MathUtil.DegToRad(89f);
}

public sealed class PlayerState
{
    public Vector3 Velocity { get; set; }
}

public sealed class PlayerControllerFeature : FeatureBase
{
    private readonly PlayerConfig _config = new();
    private readonly PlayerState _state = new();

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (world.MissionPhase != MissionPhase.Playing)
        {
            return;
        }

        world.PlayerYaw += input.LookDelta.X * _config.MouseSensitivity;
        world.PlayerPitch -= input.LookDelta.Y * _config.MouseSensitivity;
        world.PlayerPitch = System.Math.Clamp(world.PlayerPitch, -_config.PitchLimit, _config.PitchLimit);
        world.IsCrouching = input.CrouchHeld;

        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, 0f);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var wish = forward * input.MoveAxes.Y + right * input.MoveAxes.X;
        if (wish.LengthSquared() > 0f)
        {
            wish = Vector3.Normalize(wish);
        }

        var speed = _config.MoveSpeed * (world.IsCrouching ? _config.CrouchSpeedMultiplier : 1f);
        _state.Velocity = wish * speed;
        world.PlayerPosition += _state.Velocity * dt;

        var eye = world.IsCrouching ? _config.CrouchEyeHeight : _config.EyeHeight;
        world.PlayerPosition = new Vector3(world.PlayerPosition.X, eye, world.PlayerPosition.Z);
    }
}
