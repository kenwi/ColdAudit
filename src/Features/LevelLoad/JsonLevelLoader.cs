using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ColdAudit.Shared.Assets;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;

namespace ColdAudit.Features.LevelLoad;

public sealed class JsonLevelLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public LevelData Load(int levelNumber, bool loadModels = true)
    {
        var levelDir = LevelCatalog.LevelDirectory(levelNumber);
        var manifestPath = LevelCatalog.ManifestPath(levelNumber);

        if (!Directory.Exists(levelDir))
        {
            throw new DirectoryNotFoundException($"Level directory not found: {levelDir}");
        }

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Level manifest not found: {manifestPath}", manifestPath);
        }

        var manifest = DeserializeFile<LevelManifestDto>(manifestPath)
                       ?? throw new InvalidDataException($"Failed to parse level manifest: {manifestPath}");

        if (manifest.LevelId != 0 && manifest.LevelId != levelNumber)
        {
            throw new InvalidDataException(
                $"Level manifest levelId ({manifest.LevelId}) does not match folder ({levelNumber}).");
        }

        var level = new LevelData
        {
            LevelNumber = levelNumber,
            LevelId = levelNumber.ToString(),
            Name = string.IsNullOrWhiteSpace(manifest.Name) ? $"Level {levelNumber}" : manifest.Name,
            MissionMessage = manifest.MissionMessage,
            StartSectorId = manifest.StartSectorId,
            PlayerSpawn = manifest.PlayerSpawn.Position.ToVector3(),
            PlayerSpawnYaw = manifest.PlayerSpawn.Yaw,
            LevelDirectory = levelDir
        };

        foreach (var relativePath in manifest.Sectors)
        {
            level.Sectors.Add(LoadSector(levelDir, relativePath, loadModels));
        }

        foreach (var relativePath in manifest.Portals)
        {
            level.Portals.Add(LoadPortal(levelDir, relativePath));
        }

        foreach (var interactable in manifest.Interactables)
        {
            level.Interactables.Add(ToInteractable(interactable));
        }

        Validate(level);
        return level;
    }

    private static SectorDef LoadSector(string levelDir, string relativePath, bool loadModels)
    {
        var path = ResolveUnderLevel(levelDir, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Sector file not found: {path}", path);
        }

        var dto = DeserializeFile<SectorFileDto>(path)
                  ?? throw new InvalidDataException($"Failed to parse sector file: {path}");

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new InvalidDataException($"Sector file missing id: {path}");
        }

        string? modelPath = null;
        if (!string.IsNullOrWhiteSpace(dto.Model))
        {
            modelPath = ResolveUnderLevel(Path.GetDirectoryName(path) ?? levelDir, dto.Model);
        }

        var bounds = dto.Bounds is null
            ? default
            : new Aabb(dto.Bounds.Min.ToVector3(), dto.Bounds.Max.ToVector3());

        var sector = new SectorDef
        {
            Id = dto.Id,
            ModelPath = modelPath,
            SourceFile = path,
            Bounds = bounds
        };

        if (loadModels && modelPath is not null)
        {
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Sector model not found: {modelPath}", modelPath);
            }

            var handle = new ModelHandle();
            handle.Load(modelPath);
            sector.AttachModel(handle);
        }

        return sector;
    }

    private static PortalDef LoadPortal(string levelDir, string relativePath)
    {
        var path = ResolveUnderLevel(levelDir, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Portal file not found: {path}", path);
        }

        var dto = DeserializeFile<PortalFileDto>(path)
                  ?? throw new InvalidDataException($"Failed to parse portal file: {path}");

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new InvalidDataException($"Portal file missing id: {path}");
        }

        if (string.IsNullOrWhiteSpace(dto.FromSectorId) || string.IsNullOrWhiteSpace(dto.ToSectorId))
        {
            throw new InvalidDataException($"Portal '{dto.Id}' must define fromSectorId and toSectorId ({path}).");
        }

        return new PortalDef
        {
            Id = dto.Id,
            FromSectorId = dto.FromSectorId,
            ToSectorId = dto.ToSectorId,
            TwoWay = dto.TwoWay,
            Corners = dto.Corners.Select(c => c.ToVector3()).ToArray(),
            SourceFile = path
        };
    }

    private static InteractableDef ToInteractable(InteractableDto dto)
    {
        if (!Enum.TryParse<InteractableKind>(dto.Kind, ignoreCase: true, out var kind))
        {
            throw new InvalidDataException($"Unknown interactable kind '{dto.Kind}' on '{dto.Id}'.");
        }

        return new InteractableDef
        {
            Id = dto.Id,
            Kind = kind,
            SectorId = dto.SectorId,
            Position = dto.Position.ToVector3(),
            Params = new Dictionary<string, string>(dto.Params, StringComparer.Ordinal)
        };
    }

    private static void Validate(LevelData level)
    {
        if (level.Sectors.Count == 0)
        {
            throw new InvalidDataException($"Level {level.LevelNumber} has no sectors.");
        }

        var sectorIds = level.Sectors.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        if (sectorIds.Count != level.Sectors.Count)
        {
            throw new InvalidDataException($"Level {level.LevelNumber} has duplicate sector ids.");
        }

        var start = level.StartSectorId;
        if (string.IsNullOrWhiteSpace(start))
        {
            throw new InvalidDataException($"Level {level.LevelNumber} is missing startSectorId.");
        }

        if (!sectorIds.Contains(start))
        {
            throw new InvalidDataException(
                $"Level {level.LevelNumber} startSectorId '{start}' is not in the sector list.");
        }

        foreach (var portal in level.Portals)
        {
            if (!sectorIds.Contains(portal.FromSectorId) || !sectorIds.Contains(portal.ToSectorId))
            {
                throw new InvalidDataException(
                    $"Portal '{portal.Id}' references unknown sector(s) " +
                    $"({portal.FromSectorId} -> {portal.ToSectorId}).");
            }
        }
    }

    private static string ResolveUnderLevel(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Level asset paths must be relative: {relativePath}");
        }

        var rootFull = Path.GetFullPath(root);
        var combined = Path.GetFullPath(Path.Combine(rootFull, relativePath));
        var rootPrefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(combined, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Asset path escapes level directory: {relativePath}");
        }

        return combined;
    }

    private static T? DeserializeFile<T>(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}

internal sealed class LevelManifestDto
{
    public int LevelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MissionMessage { get; set; } = string.Empty;
    public string StartSectorId { get; set; } = string.Empty;
    public PlayerSpawnDto PlayerSpawn { get; set; } = new();
    public List<string> Sectors { get; set; } = [];
    public List<string> Portals { get; set; } = [];
    public List<InteractableDto> Interactables { get; set; } = [];
}

internal sealed class PlayerSpawnDto
{
    public Vec3Dto Position { get; set; } = new() { Y = 1.7f };
    public float Yaw { get; set; }
}

internal sealed class SectorFileDto
{
    public string Id { get; set; } = string.Empty;
    public string? Model { get; set; }
    public AabbDto? Bounds { get; set; }
}

internal sealed class PortalFileDto
{
    public string Id { get; set; } = string.Empty;
    public string FromSectorId { get; set; } = string.Empty;
    public string ToSectorId { get; set; } = string.Empty;
    public bool TwoWay { get; set; } = true;
    public List<Vec3Dto> Corners { get; set; } = [];
}

internal sealed class InteractableDto
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string SectorId { get; set; } = string.Empty;
    public Vec3Dto Position { get; set; } = new();
    public Dictionary<string, string> Params { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class Vec3Dto
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public Vector3 ToVector3() => new(X, Y, Z);
}

internal sealed class AabbDto
{
    public Vec3Dto Min { get; set; } = new();
    public Vec3Dto Max { get; set; } = new();
}
