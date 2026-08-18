using System.Numerics;
using ColdAudit.Shared.Math;

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
    public float Scale { get; init; } = 1f;

    /// <summary>
    /// GLB used as a static triangle-mesh collider. Null/empty skips mesh collision.
    /// May equal <see cref="ModelPath"/> or point at a simplified collider asset.
    /// </summary>
    public string? CollisionMeshPath { get; init; }

    public bool HasCollisionMesh => !string.IsNullOrWhiteSpace(CollisionMeshPath);
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
