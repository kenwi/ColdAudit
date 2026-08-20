using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;

namespace ColdAudit.Shared.Contracts;

/// <summary>
/// A feature owning geometry that should block light. <see cref="DrawDepth"/> is called once
/// per shadow map face, so implementations must be cheap and stateless: submit meshes through
/// the pass and skip anything <see cref="ShadowPass.IncludesSector"/> rejects.
/// </summary>
public interface IShadowCaster
{
    void DrawDepth(GameWorld world, ShadowPass pass);
}
