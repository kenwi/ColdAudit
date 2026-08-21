namespace ColdAudit.Shared.Assets;

public static class ModelCatalog
{
    public static string GlbPath(string modelFileName) =>
        Path.Combine(ContentPaths.Models, modelFileName);

    /// <summary>
    /// Raylib basic PBR example car ("Old Rusty Car" by Renafox, CC-BY-NC).
    /// </summary>
    public static string OldCarDirectory => Path.Combine(ContentPaths.Models, "old_car");

    public static string OldCarGlbPath => Path.Combine(OldCarDirectory, "old_car_new.glb");
    public static string OldCarAlbedoPath => Path.Combine(OldCarDirectory, "old_car_d.png");
    public static string OldCarMraPath => Path.Combine(OldCarDirectory, "old_car_mra.png");
    public static string OldCarNormalPath => Path.Combine(OldCarDirectory, "old_car_n.png");
    public static string OldCarEmissivePath => Path.Combine(OldCarDirectory, "old_car_e.png");
}
