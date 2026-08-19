using Raylib_cs;
using System.Numerics;

namespace ColdAudit.Shared.Input;

public sealed class InputState
{
    public Vector2 MoveAxes { get; private set; }
    public Vector2 LookDelta { get; private set; }
    public bool UsePressed { get; private set; }
    public bool UnlockPressed { get; private set; }
    public bool LockPressed { get; private set; }
    public bool CrouchHeld { get; private set; }
    public bool ToggleDebugPressed { get; private set; }
    public bool ToggleSectorCullPressed { get; private set; }
    public bool ToggleFullscreenPressed { get; private set; }

    public void Sample()
    {
        var x = 0f;
        var z = 0f;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) x -= 1f;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) x += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) z += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) z -= 1f;

        var move = new Vector2(x, z);
        if (move.LengthSquared() > 1f)
        {
            move = Vector2.Normalize(move);
        }

        MoveAxes = move;
        LookDelta = Raylib.GetMouseDelta();
        UsePressed = Raylib.IsKeyPressed(InputMap.Use) || Raylib.IsKeyPressed(KeyboardKey.F);
        UnlockPressed = Raylib.IsKeyPressed(InputMap.Unlock);
        LockPressed = Raylib.IsKeyPressed(InputMap.Lock);
        CrouchHeld = Raylib.IsKeyDown(InputMap.Crouch) || Raylib.IsKeyDown(KeyboardKey.C);
        ToggleDebugPressed = Raylib.IsKeyPressed(InputMap.DebugToggle);
        ToggleSectorCullPressed = Raylib.IsKeyPressed(InputMap.SectorCullToggle);
        ToggleFullscreenPressed = Raylib.IsKeyPressed(InputMap.FullscreenToggle);
    }
}
