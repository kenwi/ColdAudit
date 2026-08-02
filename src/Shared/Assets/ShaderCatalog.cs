namespace ColdAudit.Shared.Assets;

public static class ShaderCatalog
{
    public static string LightingVertexPath => Path.Combine(ContentPaths.Shaders, "lighting.vs");
    public static string LightingFragmentPath => Path.Combine(ContentPaths.Shaders, "lighting.fs");
}
