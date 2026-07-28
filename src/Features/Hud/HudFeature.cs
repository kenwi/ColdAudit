using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.Hud;

public sealed class HudFeature : FeatureBase
{
    public override void Draw(GameWorld world)
    {
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), 36, new Color(0, 0, 0, 160));
        Raylib.DrawText(world.MissionMessage, 12, 10, 16, Color.RayWhite);

        // Heat bar
        const int barX = 12;
        const int barY = 48;
        const int barW = 200;
        const int barH = 12;
        Raylib.DrawRectangle(barX, barY, barW, barH, new Color(40, 40, 40, 220));
        Raylib.DrawRectangle(barX, barY, (int)(barW * world.Heat), barH, new Color(220, 70, 60, 255));
        Raylib.DrawText("HEAT", barX, barY - 16, 12, new Color(200, 200, 200, 255));

        if (!string.IsNullOrEmpty(world.UsePrompt))
        {
            var w = Raylib.GetScreenWidth();
            var h = Raylib.GetScreenHeight();
            Raylib.DrawText(world.UsePrompt, w / 2 - 80, h - 64, 20, Color.RayWhite);
        }

        if (world.MissionPhase == MissionPhase.Won)
        {
            DrawCenterBanner("EXFIL COMPLETE", Color.Lime);
        }
        else if (world.MissionPhase == MissionPhase.Lost)
        {
            DrawCenterBanner("AUDIT FAILED", new Color(230, 70, 70, 255));
        }

        // Crosshair
        var cx = Raylib.GetScreenWidth() / 2;
        var cy = Raylib.GetScreenHeight() / 2;
        Raylib.DrawRectangle(cx - 6, cy - 1, 12, 2, Color.RayWhite);
        Raylib.DrawRectangle(cx - 1, cy - 6, 2, 12, Color.RayWhite);
    }

    private static void DrawCenterBanner(string text, Color color)
    {
        var w = Raylib.GetScreenWidth();
        var h = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, h / 2 - 40, w, 80, new Color(0, 0, 0, 180));
        Raylib.DrawText(text, w / 2 - 100, h / 2 - 12, 28, color);
    }
}
