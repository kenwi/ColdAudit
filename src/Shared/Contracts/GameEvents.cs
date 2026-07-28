namespace ColdAudit.Shared.Contracts;

public readonly record struct UseRequested(string InteractableId);
public readonly record struct ItemAcquired(string ItemId);
public readonly record struct CameraDisabled(string CameraId);
public readonly record struct DetectionSample(string SourceId, float Amount);
public readonly record struct ObjectiveTaken(string ItemId);
public readonly record struct AlarmTriggered(string Reason);
public readonly record struct MissionEnded(bool Success, string Reason);
