using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.DetectionHeat;

public sealed class HeatConfig
{
    public float DecayPerSecond { get; set; } = 0.12f;
    public float CrouchMultiplier { get; set; } = 0.5f;
    public float FailThreshold { get; set; } = 1f;
}

public sealed class DetectionHeatFeature : FeatureBase
{
    private readonly HeatConfig _config = new();

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (world.MissionPhase != MissionPhase.Playing)
        {
            return;
        }

        foreach (var sample in events.OfType<DetectionSample>())
        {
            var amount = sample.Amount;
            if (world.IsCrouching)
            {
                amount *= _config.CrouchMultiplier;
            }

            world.Heat = System.Math.Clamp(world.Heat + amount, 0f, 1f);
        }

        world.Heat = System.Math.Clamp(world.Heat - _config.DecayPerSecond * dt, 0f, 1f);

        if (world.Heat >= _config.FailThreshold)
        {
            events.Publish(new AlarmTriggered("SOC heat max"));
            events.Publish(new MissionEnded(false, "ALERT CORP-SOC-12: unauthorized presence Wing B"));
            world.MissionPhase = MissionPhase.Lost;
            world.MissionMessage = "ALERT CORP-SOC-12: unauthorized presence Wing B";
        }
    }
}
