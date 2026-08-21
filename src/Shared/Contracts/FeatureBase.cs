using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Shared.Contracts;

public abstract class FeatureBase : IFeature
{
    public virtual void Load(GameWorld world, EventBus events) { }
    public virtual void Update(float dt, GameWorld world, InputState input, EventBus events) { }
    public virtual void DrawOffscreen(GameWorld world) { }
    public virtual void Draw(GameWorld world) { }
    public virtual void Unload() { }
}
