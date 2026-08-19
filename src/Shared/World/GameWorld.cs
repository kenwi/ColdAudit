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

public enum DebugDrawMode
{
    Off = 0,
    Wireframe = 1,
    SolidWalls = 2
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

    /// <summary>F1 cycles Off → Wireframe → SolidWalls.</summary>
    public DebugDrawMode DebugDraw { get; set; } = DebugDrawMode.SolidWalls;

    /// <summary>
    /// When true, only the current room and portal-adjacent rooms are drawn.
    /// </summary>
    public bool SectorCullEnabled { get; set; } = true;

    public LevelData? ActiveLevel { get; set; }

    /// <summary>
    /// Shared PBR lighting shader/lights used by 3D model drawers.
    /// </summary>
    public BasicLighting? Lighting { get; set; }

    /// <summary>Shared player camera and per-frame draw flags.</summary>
    public DrawContext Draw { get; } = new();

    public UiFramebuffer Ui { get; } = new();
}
