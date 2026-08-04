using ColdAudit.Features.Physics;

if (!PhysicsFeature.RunLevelSmoke(out var message))
{
    Console.Error.WriteLine($"PhysicsLevelSmoke FAIL: {message}");
    return 1;
}

Console.WriteLine($"PhysicsLevelSmoke {message}");
return 0;
