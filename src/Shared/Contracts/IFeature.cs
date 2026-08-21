using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Shared.Contracts;

public interface IFeature
{
    void Load(GameWorld world, EventBus events);
    void Update(float dt, GameWorld world, InputState input, EventBus events);

    /// <summary>
    /// Offscreen render passes, before the frame's back buffer is bound. Anything drawn here
    /// must target its own framebuffer.
    /// </summary>
    void DrawOffscreen(GameWorld world);

    void Draw(GameWorld world);
    void Unload();
}
