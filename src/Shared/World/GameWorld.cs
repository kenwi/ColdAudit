using System.Numerics;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Rendering;

namespace ColdAudit.Shared.World;

public enum MissionPhase
{
    Playing,
    Won,
    Lost
}

public sealed class GameWorld
{
    public Vector3 PlayerPosition { get; set; } = new(0f, 1.7f, 0f);
    public float PlayerYaw { get; set; }
    public float PlayerPitch { get; set; }
    public bool IsCrouching { get; set; }

    public string CurrentSectorId { get; set; } = string.Empty;
    public HashSet<string> VisibleSectorIds { get; } = new(StringComparer.Ordinal);

    public float Heat { get; set; }
    public HashSet<string> CarriedItemIds { get; } = new(StringComparer.Ordinal);

    public MissionPhase MissionPhase { get; set; } = MissionPhase.Playing;
    public string MissionMessage { get; set; } = string.Empty;

    public string? FocusedInteractableId { get; set; }
    public string UsePrompt { get; set; } = string.Empty;

    public bool DebugDrawEnabled { get; set; } = true;

    public LevelData? ActiveLevel { get; set; }

    public UiFramebuffer Ui { get; } = new();
}
