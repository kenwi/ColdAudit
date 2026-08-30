using System.Numerics;
using ColdAudit.Shared.Math;
using Raylib_cs;

namespace ColdAudit.Features.LevelLoad;

public sealed class LevelData
{
    public string LevelId { get; init; } = string.Empty;
    public Vector3 PlayerSpawn { get; init; } = new(0f, 1.7f, 0f);
    public float PlayerSpawnYaw { get; init; }
    public List<SectorDef> Sectors { get; } = [];
    public List<PortalDef> Portals { get; } = [];
    public List<InteractableDef> Interactables { get; } = [];
    public List<ModelPlacementDef> ModelPlacements { get; } = [];
    public List<DoorDef> Doors { get; } = [];
    public List<KeycardDef> Keycards { get; } = [];
    public List<PickupDef> Pickups { get; } = [];
    public List<CameraDef> Cameras { get; } = [];
    public List<LightDef> Lights { get; } = [];
}

/// <summary>
/// An authored point light. <see cref="SectorId"/> is the room the light lives in and
/// drives portal-based light occlusion, so it must name a real sector.
/// </summary>
public sealed class LightDef
{
    public string Id { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public Vector3 Position { get; init; }
    public Color Color { get; init; } = Color.White;
    public float Intensity { get; init; } = 1f;

    /// <summary>
    /// Optional animation: orbit a <see cref="ModelPlacementDef.Id"/> instead of holding
    /// <see cref="Position"/>. Null/empty keeps the light static.
    /// </summary>
    public string? AnchorPlacementId { get; init; }

    /// <summary>Orbit radius in metres around the anchor. Zero keeps the light on the anchor.</summary>
    public float OrbitRadius { get; init; }

    public float OrbitDegreesPerSecond { get; init; }

    /// <summary>Starting angle so several lights can share one anchor without overlapping.</summary>
    public float OrbitPhaseDegrees { get; init; }

    /// <summary>Height above the anchor origin before hover is applied.</summary>
    public float OrbitHeight { get; init; }

    public float HoverAmplitude { get; init; }
    public float HoverDegreesPerSecond { get; init; }

    public bool HasAnchor => !string.IsNullOrWhiteSpace(AnchorPlacementId);
}

/// <summary>
/// A drawable glTF/glb asset placed in the level at a world transform.
/// Optional <see cref="CollisionMeshPath"/> cooks a static Box3D triangle mesh
/// (same file as the visual, or a dedicated low-poly collider GLB).
/// </summary>
public sealed class ModelPlacementDef
{
    public string Id { get; init; } = string.Empty;
    public string ModelPath { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public Vector3 Position { get; init; }
    public float YawDegrees { get; init; }

    /// <summary>
    /// Extra yaw in degrees per second (YawDegrees + speed * elapsed time).
    /// </summary>
    public float YawSpeedDegrees { get; init; }

    public float Scale { get; init; } = 1f;

    /// <summary>
    /// GLB used as a static triangle-mesh collider. Null/empty skips mesh collision.
    /// May equal <see cref="ModelPath"/> or point at a simplified collider asset.
    /// </summary>
    public string? CollisionMeshPath { get; init; }

    /// <summary>
    /// Optional PBR maps bound after load (albedo / packed MRA / normal / emissive).
    /// </summary>
    public ModelPbrMapsDef? PbrMaps { get; init; }

    public bool HasCollisionMesh => !string.IsNullOrWhiteSpace(CollisionMeshPath);
    public bool HasPbrMaps => PbrMaps is not null;
}

/// <summary>
/// Sidecar PBR textures and scalar overrides for a placed model.
/// MRA is packed metallic (R), roughness (G), ambient occlusion (B).
/// </summary>
public sealed class ModelPbrMapsDef
{
    public string? AlbedoPath { get; init; }
    public string? MraPath { get; init; }
    public string? NormalPath { get; init; }
    public string? EmissivePath { get; init; }
    public float Metallic { get; init; }
    public float Roughness { get; init; }
    public float EmissivePower { get; init; }
    public Vector3 EmissiveColor { get; init; }
}

public sealed class SectorDef
{
    public string Id { get; init; } = string.Empty;
    public string? ModelPath { get; init; }
    public bool RenderEnabled { get; set; } = true;
    public Aabb Bounds { get; init; }

    /// <summary>
    /// GLB used as a static triangle-mesh collider at the sector origin.
    /// Null/empty keeps placeholder AABB floor/walls from <c>LevelCollisionBuilder</c>.
    /// May equal <see cref="ModelPath"/> or point at a simplified collider asset.
    /// </summary>
    public string? CollisionMeshPath { get; init; }

    public bool HasCollisionMesh => !string.IsNullOrWhiteSpace(CollisionMeshPath);
}

public sealed class PortalDef
{
    public string Id { get; init; } = string.Empty;
    public string FromSectorId { get; init; } = string.Empty;
    public string ToSectorId { get; init; } = string.Empty;
    public bool TwoWay { get; init; } = true;
    public Vector3[] Corners { get; init; } = [];

    /// <summary>
    /// Optional doorway/corridor GLB drawn at the level origin (world-authored like sectors).
    /// Null/empty keeps the placeholder portal strip from <c>LevelModelsFeature</c>.
    /// </summary>
    public string? ModelPath { get; init; }

    /// <summary>
    /// GLB used as a static triangle-mesh collider at the level origin.
    /// Null/empty keeps placeholder portal boxes from <c>LevelCollisionBuilder</c>
    /// (unless both sectors already use mesh collision).
    /// May equal <see cref="ModelPath"/> or point at a simplified collider asset.
    /// </summary>
    public string? CollisionMeshPath { get; init; }

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);
    public bool HasCollisionMesh => !string.IsNullOrWhiteSpace(CollisionMeshPath);
}

public enum InteractableKind
{
    Door,
    BadgeReader,
    Workstation,
    Console,
    Note,
    PatchPort,
    Pickup,
    ExitVolume
}

public sealed class InteractableDef
{
    public string Id { get; init; } = string.Empty;
    public InteractableKind Kind { get; init; }
    public string SectorId { get; init; } = string.Empty;
    public Vector3 Position { get; init; }
    public Dictionary<string, string> Params { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Authored door. <see cref="HingePosition"/> is the floor pivot for swing doors, or the
/// doorway center for <see cref="DoorMotion.SlidingDouble"/>.
/// Local +X is along the door plane (width / slide), +Y is up, thickness is along local Z.
/// </summary>
public enum DoorMotion
{
    /// <summary>Single slab swings on a hinge.</summary>
    Swing = 0,

    /// <summary>Two leaves meet at the center and slide apart like elevator doors.</summary>
    SlidingDouble = 1,

    /// <summary>Two hinged leaves at the jambs; each opens independently.</summary>
    SwingDouble = 2,

    /// <summary>One panel slides along the doorway on a chosen side.</summary>
    SlidingSingle = 3
}

/// <summary>Which way a single sliding panel moves when opening.</summary>
public enum SlideDirection
{
    Left = -1,
    Right = 1
}

public sealed class DoorDef
{
    public string Id { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public DoorMotion Motion { get; init; } = DoorMotion.Swing;

    /// <summary>
    /// Swing: hinge floor pivot. Multi-leaf / sliding: center of the closed doorway on the floor.
    /// </summary>
    public Vector3 HingePosition { get; init; }

    public float ClosedYawDegrees { get; init; }
    public float Width { get; init; } = 1.5f;
    public float Height { get; init; } = 2.5f;
    public float Thickness { get; init; } = 0.08f;
    public float OpenAngleDegrees { get; init; } = 90f;

    /// <summary>
    /// Sliding: travel distance along local X. Zero defaults to <see cref="Width"/> for a single
    /// panel, or half <see cref="Width"/> per leaf on <see cref="DoorMotion.SlidingDouble"/>.
    /// </summary>
    public float SlideDistance { get; init; }

    /// <summary>Used by <see cref="DoorMotion.SlidingSingle"/> only.</summary>
    public SlideDirection SlideDirection { get; init; } = SlideDirection.Right;

    public float InteractRadius { get; init; } = 2.5f;
    public bool Locked { get; init; }

    /// <summary>
    /// When true, the door closes itself after staying fully open for
    /// <see cref="AutoCloseSeconds"/>.
    /// </summary>
    public bool AutoClose { get; init; }

    /// <summary>Seconds to wait fully open before auto-closing. Ignored unless <see cref="AutoClose"/>.</summary>
    public float AutoCloseSeconds { get; init; } = 3f;

    /// <summary>
    /// Inventory item that unlocks this door. Null/empty means no key item (debug U still works).
    /// </summary>
    public string? RequiredItemId { get; init; }

    /// <summary>
    /// Placeholder tint. Alpha 0 keeps the default wood color. Match the paired keycard.
    /// </summary>
    public Color Color { get; init; }

    /// <summary>
    /// Optional GLB. Null/empty draws the placeholder box.
    /// Swing: origin at the hinge. Sliding: origin at each leaf center.
    /// </summary>
    public string? ModelPath { get; init; }

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);
    public bool RequiresItem => !string.IsNullOrWhiteSpace(RequiredItemId);
}

/// <summary>
/// Wall-mounted security camera. <see cref="MountPosition"/> is the plate center on the wall.
/// <see cref="MountYawDegrees"/> faces into the room (camera look when sweep is centered).
/// Placeholder draws plate + leg + body until <see cref="ModelPath"/> is set.
/// </summary>
public sealed class CameraDef
{
    public string Id { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;

    /// <summary>Center of the wall mounting plate.</summary>
    public Vector3 MountPosition { get; init; }

    /// <summary>Facing into the room when the sweep is centered (degrees).</summary>
    public float MountYawDegrees { get; init; }

    /// <summary>Look pitch in degrees. Negative tilts the lens toward the floor.</summary>
    public float PitchDegrees { get; init; } = -12f;

    public float HorizontalFovDegrees { get; init; } = 70f;
    public float VerticalFovDegrees { get; init; } = 42f;
    public float NearPlane { get; init; } = 0.2f;
    public float FarPlane { get; init; } = 16f;

    /// <summary>Heat added per second while the player is inside the frustum.</summary>
    public float DetectRate { get; init; } = 0.35f;

    /// <summary>Half-amplitude of the left/right yaw sweep in degrees.</summary>
    public float SweepYawDegrees { get; init; } = 40f;

    /// <summary>Sweep angular speed in degrees per second (sine cycle).</summary>
    public float SweepSpeedDegrees { get; init; } = 28f;

    public float SweepPhaseDegrees { get; init; }

    /// <summary>
    /// When true, the player can look at the camera and press Use to disable it.
    /// </summary>
    public bool Interactable { get; init; }

    /// <summary>Max player distance from the mount to interact.</summary>
    public float InteractRadius { get; init; } = 3f;

    /// <summary>
    /// Optional GLB for the full assembly. Null/empty draws the plate/leg/body placeholder.
    /// Model origin should sit at the mount plate center, +Z out from the wall.
    /// </summary>
    public string? ModelPath { get; init; }

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);
}

/// <summary>
/// Floor keycard pickup. <see cref="Position"/> is the floor contact. Swap in <see cref="ModelPath"/>
/// later; placeholder is a generated card slab until then.
/// </summary>
public sealed class KeycardDef
{
    public string Id { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public Vector3 Position { get; init; }
    public float YawDegrees { get; init; }
    public float Width { get; init; } = 0.18f;
    public float Height { get; init; } = 0.012f;
    public float Depth { get; init; } = 0.115f;
    public float InteractRadius { get; init; } = 2f;

    /// <summary>
    /// Placeholder tint. Match the door this card unlocks. Alpha 0 uses the default blue.
    /// </summary>
    public Color Color { get; init; }

    /// <summary>
    /// Optional GLB. Null/empty draws the generated placeholder card.
    /// </summary>
    public string? ModelPath { get; init; }

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);
}

/// <summary>
/// Generic floor pickup (USB sticks, badges, etc.). <see cref="Position"/> is the floor contact.
/// Placeholder is a lit box sized by Width/Height/Depth until <see cref="ModelPath"/> is set.
/// </summary>
public sealed class PickupDef
{
    public string Id { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public Vector3 Position { get; init; }
    public float YawDegrees { get; init; }
    public float Width { get; init; } = 0.08f;
    public float Height { get; init; } = 0.08f;
    public float Depth { get; init; } = 0.08f;
    public float InteractRadius { get; init; } = 2f;

    /// <summary>Placeholder tint. Alpha 0 uses the inventory catalog color.</summary>
    public Color Color { get; init; }

    /// <summary>Optional GLB. Null/empty draws the lit-box placeholder.</summary>
    public string? ModelPath { get; init; }

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);
}
