namespace ColdAudit.Shared.Assets;

public static class LevelCatalog
{
    public static string WingB => "wing_b";

    /// <summary>Two-storey office building (single sector). Exported by tools/export_level_glb.py.</summary>
    public static string Office => "office";

    public static string GlbPath(string levelId) =>
        Path.Combine(ContentPaths.Levels, levelId, $"{levelId}.glb");

    public static string SectorGlbPath(string levelId, string sectorId) =>
        Path.Combine(ContentPaths.Levels, levelId, $"{sectorId}.glb");

    public static string JsonPath(string levelId) =>
        Path.Combine(ContentPaths.Levels, levelId, $"{levelId}.json");
}
