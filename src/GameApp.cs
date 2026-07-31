using ColdAudit.Features.Cameras;
using ColdAudit.Features.DebugOverlay;
using ColdAudit.Features.DetectionHeat;
using ColdAudit.Features.DoorsAccess;
using ColdAudit.Features.Hud;
using ColdAudit.Features.Interaction;
using ColdAudit.Features.Inventory;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Features.LevelModels;
using ColdAudit.Features.ObjectiveExfil;
using ColdAudit.Features.PlayerController;
using ColdAudit.Features.SectorVisibility;
using ColdAudit.Features.Workstations;
using ColdAudit.Features.WorldRender;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Time;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit;

public sealed class GameApp
{
    private const int ScreenWidth = 1280;
    private const int ScreenHeight = 720;
    private const string Title = "Cold Audit";

    private readonly GameWorld _world = new();
    private readonly EventBus _events = new();
    private readonly InputState _input = new();
    private readonly List<IFeature> _features = [];

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(ScreenWidth, ScreenHeight, Title);
        Raylib.SetTargetFPS(60);
        Raylib.DisableCursor();

        BuildFeatures();

        foreach (var feature in _features)
        {
            feature.Load(_world, _events);
        }

        while (!Raylib.WindowShouldClose())
        {
            var dt = Raylib.GetFrameTime();
            FrameTime.Delta = dt;
            FrameTime.Total += dt;
            _input.Sample();

            foreach (var feature in _features)
            {
                feature.Update(dt, _world, _input, _events);
            }

            _events.Clear();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(12, 14, 18, 255));

            foreach (var feature in _features)
            {
                feature.Draw(_world);
            }

            Raylib.EndDrawing();
        }

        for (var i = _features.Count - 1; i >= 0; i--)
        {
            _features[i].Unload();
        }

        Raylib.CloseWindow();
    }

    private void BuildFeatures()
    {
        // Order matters: input consumers first, visibility after movement, render/hud last.
        _features.Add(new LevelLoadFeature());
        _features.Add(new PlayerControllerFeature());
        _features.Add(new InteractionFeature());
        _features.Add(new InventoryFeature());
        _features.Add(new DoorsAccessFeature());
        _features.Add(new CamerasFeature());
        _features.Add(new WorkstationsFeature());
        _features.Add(new DetectionHeatFeature());
        _features.Add(new ObjectiveExfilFeature());
        _features.Add(new SectorVisibilityFeature());
        _features.Add(new WorldRenderFeature());
        _features.Add(new LevelModelsFeature());
        _features.Add(new HudFeature());
        _features.Add(new DebugOverlayFeature());
    }
}
