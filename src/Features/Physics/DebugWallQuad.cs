using System.Numerics;

namespace ColdAudit.Features.Physics;

/// <summary>Authoring-side wall quad for solid debug draw (matches collider faces).</summary>
public readonly struct DebugWallQuad
{
    public DebugWallQuad(Vector3 bottomLeft, Vector3 bottomRight, Vector3 topRight, Vector3 topLeft)
    {
        BottomLeft = bottomLeft;
        BottomRight = bottomRight;
        TopRight = topRight;
        TopLeft = topLeft;
    }

    public Vector3 BottomLeft { get; }
    public Vector3 BottomRight { get; }
    public Vector3 TopRight { get; }
    public Vector3 TopLeft { get; }

    public Vector3 Center => (BottomLeft + BottomRight + TopRight + TopLeft) * 0.25f;
}
