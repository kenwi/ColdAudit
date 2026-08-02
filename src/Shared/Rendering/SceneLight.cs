using System.Numerics;
using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

public enum LightType
{
    Directional = 0,
    Point = 1
}

/// <summary>
/// One dynamic light bound to the basic lighting shader (max 4).
/// </summary>
public sealed class SceneLight
{
    public LightType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public Vector3 Position { get; set; }
    public Vector3 Target { get; set; }
    public Color Color { get; set; } = Color.White;

    internal int EnabledLoc { get; set; } = -1;
    internal int TypeLoc { get; set; } = -1;
    internal int PositionLoc { get; set; } = -1;
    internal int TargetLoc { get; set; } = -1;
    internal int ColorLoc { get; set; } = -1;
}
