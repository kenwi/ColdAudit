using ColdAudit.Shared.Input;
using ColdAudit.Shared.World;

namespace ColdAudit.Shared.Contracts;

public interface IFeature
{
    void Load(GameWorld world, EventBus events);
    void Update(float dt, GameWorld world, InputState input, EventBus events);
    void Draw(GameWorld world);
    void Unload();
}
