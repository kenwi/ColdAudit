namespace ColdAudit.Shared.Assets;

public static class LevelCatalog
{
    public static string WingB => "wing_b";

    public static string GlbPath(string levelId) =>
        Path.Combine(ContentPaths.Levels, levelId, $"{levelId}.glb");

    public static string SectorGlbPath(string levelId, string sectorId) =>
        Path.Combine(ContentPaths.Levels, levelId, $"{sectorId}.glb");

    public static string JsonPath(string levelId) =>
        Path.Combine(ContentPaths.Levels, levelId, $"{levelId}.json");
}
