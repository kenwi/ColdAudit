namespace ColdAudit.Shared.Assets;

public static class ModelCatalog
{
    public static string GlbPath(string modelFileName) =>
        Path.Combine(ContentPaths.Models, modelFileName);
}
