using System.Numerics;
using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

public enum LightType
{
    Directional = 0,
    Point = 1
}

/// <summary>
/// One dynamic light bound to the shared PBR lighting shader (max 4).
/// </summary>
public sealed class SceneLight
{
    public LightType Type { get; set; }
    public bool Enabled { get; set; } = true;
    public Vector3 Position { get; set; }
    public Vector3 Target { get; set; }
    public Color Color { get; set; } = Color.White;
    public float Intensity { get; set; } = 100f;

    /// <summary>
    /// Sector the light lives in. Drives portal-based light occlusion; empty leaves the
    /// light unmasked (it lights the whole level, as before occlusion existed).
    /// </summary>
    public string SectorId { get; set; } = string.Empty;

    internal int EnabledLoc { get; set; } = -1;
    internal int TypeLoc { get; set; } = -1;
    internal int PositionLoc { get; set; } = -1;
    internal int TargetLoc { get; set; } = -1;
    internal int ColorLoc { get; set; } = -1;
    internal int IntensityLoc { get; set; } = -1;
}
