namespace ColdAudit.Shared.Assets;

public static class LevelCatalog
{
    public static string WingB => "wing_b";

    public static string GlbPath(string levelId) =>
        Path.Combine(ContentPaths.Levels, levelId, $"{levelId}.glb");

    public static string JsonPath(string levelId) =>
        Path.Combine(ContentPaths.Levels, levelId, $"{levelId}.json");
}
