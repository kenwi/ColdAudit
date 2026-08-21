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
/// Authored door. <see cref="HingePosition"/> is the floor pivot (model origin later).
/// Local +X is width away from the hinge, +Y is up, thickness is along local Z.
/// </summary>
public sealed class DoorDef
{
    public string Id { get; init; } = string.Empty;
    public string SectorId { get; init; } = string.Empty;
    public Vector3 HingePosition { get; init; }
    public float ClosedYawDegrees { get; init; }
    public float Width { get; init; } = 1.5f;
    public float Height { get; init; } = 2.5f;
    public float Thickness { get; init; } = 0.08f;
    public float OpenAngleDegrees { get; init; } = 90f;
    public float InteractRadius { get; init; } = 2.5f;
    public bool Locked { get; init; }

    /// <summary>
    /// Optional GLB. Null/empty draws the placeholder box. Model origin should be the hinge.
    /// </summary>
    public string? ModelPath { get; init; }

    public bool HasModel => !string.IsNullOrWhiteSpace(ModelPath);
}
