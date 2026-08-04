using Box3D;
using Box3D.Interop;

// Headless Box3D native-load smoke (no Raylib / GameApp).
using var world = new Box3DWorld(gravity: new B3Vec3(0f, -9.8f, 0f));
world.CreateStaticBody(B3Pos.Zero).AddBox(50f, 1f, 50f);

var version = Box3DWorld.NativeVersion;
var filter = Box3DWorld.DefaultQueryFilter();
var hit = world.CastRayClosest(new B3Pos(0, 10, 0), new B3Vec3(0, -20, 0), filter);
world.Step(1f / 60f);

if (hit.Hit == 0)
{
    Console.Error.WriteLine($"Box3D {version}: FAIL (ray missed)");
    return 1;
}

if (hit.Point.Y is < 0.9 or > 1.1)
{
    Console.Error.WriteLine($"Box3D {version}: FAIL (hit Y={hit.Point.Y}, want ~1)");
    return 1;
}

Console.WriteLine($"Box3D {version}: OK (hit Y={hit.Point.Y:F3}, fraction={hit.Fraction:F3})");
return 0;
