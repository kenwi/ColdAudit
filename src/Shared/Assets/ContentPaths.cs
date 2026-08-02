namespace ColdAudit.Shared.Assets;

public static class ContentPaths
{
    public static string Root
    {
        get
        {
            var cwd = Directory.GetCurrentDirectory();
            var candidate = Path.Combine(cwd, "content");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            // When running from bin/Debug/netX.X, walk up to repo content/
            var dir = new DirectoryInfo(cwd);
            while (dir is not null)
            {
                candidate = Path.Combine(dir.FullName, "content");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return Path.Combine(cwd, "content");
        }
    }

    public static string Levels => Path.Combine(Root, "levels");
    public static string Models => Path.Combine(Root, "models");
    public static string Shaders => Path.Combine(Root, "shaders");
    public static string Audio => Path.Combine(Root, "audio");
    public static string Textures => Path.Combine(Root, "textures");
}
