namespace ColdAudit.Shared.Assets;

public static class ShaderCatalog
{
    public static string LightingVertexPath => Path.Combine(ContentPaths.Shaders, "lighting.vs");
    public static string LightingFragmentPath => Path.Combine(ContentPaths.Shaders, "lighting.fs");

    public static string PbrVertexPath => Path.Combine(ContentPaths.Shaders, "pbr.vs");
    public static string PbrFragmentPath => Path.Combine(ContentPaths.Shaders, "pbr.fs");
}
