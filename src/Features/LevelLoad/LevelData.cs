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
}

public sealed class PortalDef
{
    public string Id { get; init; } = string.Empty;
    public string FromSectorId { get; init; } = string.Empty;
    public string ToSectorId { get; init; } = string.Empty;
    public bool TwoWay { get; init; } = true;
    public Vector3[] Corners { get; init; } = [];
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
