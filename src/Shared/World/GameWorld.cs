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

    /// <summary>F3. When false, PBR map sampling (albedo / normal / MRA / emissive) is skipped.</summary>
    public bool PbrTexturesEnabled { get; set; } = true;

    /// <summary>F4. When false, dynamic lights are disabled (ambient remains).</summary>
    public bool LightingEnabled { get; set; } = true;

    /// <summary>
    /// F5. When false, lights ignore walls and portals again (they illuminate the whole
    /// level, as before portal light occlusion existed).
    /// </summary>
    public bool LightVolumeMaskEnabled { get; set; } = true;

    /// <summary>F6. When false, meshes stop casting shadows (portal volumes still apply).</summary>
    public bool ShadowsEnabled { get; set; } = true;

    /// <summary>
    /// Bumped by any feature whose shadow-casting geometry moved this frame (doors swinging,
    /// spinning props, sector meshes toggled). Cached shadow cubes rebuild when it changes.
    /// </summary>
    public int ShadowGeometryRevision { get; private set; }

    public void InvalidateShadowGeometry() => ShadowGeometryRevision++;

    public LevelData? ActiveLevel { get; set; }

    /// <summary>
    /// Sector volumes and portal adjacency resolved from <see cref="ActiveLevel"/>.
    /// Rebuilt by <c>LevelLoadFeature</c> whenever the active level changes.
    /// </summary>
    public SectorGraph Sectors { get; } = new();

    /// <summary>
    /// Shared PBR lighting shader/lights used by 3D model drawers.
    /// </summary>
    public BasicLighting? Lighting { get; set; }

    /// <summary>Shared player camera and per-frame draw flags.</summary>
    public DrawContext Draw { get; } = new();

    public UiFramebuffer Ui { get; } = new();
}
