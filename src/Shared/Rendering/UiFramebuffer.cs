using System.Numerics;
using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

public sealed class UiFramebuffer
{
    public const int Width = 1280;
    public const int Height = 720;

    private RenderTexture2D _target;
    private bool _loaded;
    private bool _begun;

    public bool IsLoaded => _loaded;
    public bool IsBegun => _begun;

    public void Load()
    {
        if (_loaded)
        {
            return;
        }

        _target = Raylib.LoadRenderTexture(Width, Height);
        _loaded = true;
    }

    public void Unload()
    {
        if (!_loaded)
        {
            return;
        }

        if (_begun)
        {
            Raylib.EndTextureMode();
            _begun = false;
        }

        Raylib.UnloadRenderTexture(_target);
        _loaded = false;
    }

    public void Begin()
    {
        if (!_loaded || _begun)
        {
            return;
        }

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Color.Blank);
        _begun = true;
    }

    public void EndAndPresent()
    {
        if (!_loaded)
        {
            return;
        }

        if (_begun)
        {
            Raylib.EndTextureMode();
            _begun = false;
        }

        var screenW = Raylib.GetScreenWidth();
        var screenH = Raylib.GetScreenHeight();
        var scale = screenW / (float)Width;
        var destW = screenW;
        var destH = Height * scale;
        var destY = (screenH - destH) * 0.5f;

        // Render textures are flipped on Y in OpenGL; invert the source rect.
        var source = new Rectangle(0, 0, Width, -Height);
        var dest = new Rectangle(0, destY, destW, destH);
        Raylib.DrawTexturePro(_target.Texture, source, dest, Vector2.Zero, 0f, Color.White);
    }
}
