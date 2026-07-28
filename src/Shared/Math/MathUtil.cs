using System.Numerics;

namespace ColdAudit.Shared.Math;

public static class MathUtil
{
    public static float DegToRad(float degrees) => degrees * (MathF.PI / 180f);
    public static float RadToDeg(float radians) => radians * (180f / MathF.PI);

    public static Vector3 ForwardFromYawPitch(float yaw, float pitch)
    {
        var cy = MathF.Cos(yaw);
        var sy = MathF.Sin(yaw);
        var cp = MathF.Cos(pitch);
        var sp = MathF.Sin(pitch);
        return Vector3.Normalize(new Vector3(sy * cp, sp, cy * cp));
    }
}
