namespace ColdAudit.Shared.Assets;

public static class LevelCatalog
{
    public static string LevelDirectory(int levelNumber) =>
        Path.Combine(ContentPaths.Levels, levelNumber.ToString());

    public static string ManifestPath(int levelNumber) =>
        Path.Combine(LevelDirectory(levelNumber), "level.json");
}
