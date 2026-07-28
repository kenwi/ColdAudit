using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

public sealed class DrawContext
{
    public Camera3D Camera { get; set; }
    public bool DrawDebug { get; set; }
}
