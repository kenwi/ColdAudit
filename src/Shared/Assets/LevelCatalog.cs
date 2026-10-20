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

    /// <summary>
    /// Baked static lighting for the level, written by <c>tools/bake_lightmaps.py</c>.
    /// One atlas per level: the tool packs every mesh in the sector into a single UV set.
    /// </summary>
    public static string LightMapImagePath(string levelId) =>
        Path.Combine(ContentPaths.Levels, levelId, $"{levelId}_lightmap.png");

    /// <summary>Sidecar written next to the atlas; carries <c>encode_scale</c>.</summary>
    public static string LightMapMetaPath(string levelId) =>
        Path.Combine(ContentPaths.Levels, levelId, $"{levelId}_lightmap.json");
}
