using ColdAudit.Features.Cameras;
using ColdAudit.Features.DebugOverlay;
using ColdAudit.Features.DetectionHeat;
using ColdAudit.Features.DoorsAccess;
using ColdAudit.Features.Fullscreen;
using ColdAudit.Features.Hud;
using ColdAudit.Features.Interaction;
using ColdAudit.Features.Inventory;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Features.LevelModels;
using ColdAudit.Features.LevelProps;
using ColdAudit.Features.Lighting;
using ColdAudit.Features.LightVisibility;
using ColdAudit.Features.ObjectiveExfil;
using ColdAudit.Features.Physics;
using ColdAudit.Features.Pickups;
using ColdAudit.Features.PlayerController;
using ColdAudit.Features.SectorVisibility;
using ColdAudit.Features.Shadows;
using ColdAudit.Features.UiPresent;
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
    private bool _cursorLocked;

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(ScreenWidth, ScreenHeight, Title);
        Raylib.SetTargetFPS(60);
        // Cursor stays free through Load so early breakpoints do not trap the mouse.

        BuildFeatures();

        foreach (var feature in _features)
        {
            feature.Load(_world, _events);
        }

        while (!Raylib.WindowShouldClose())
        {
            SyncCursorCapture();

            var dt = Raylib.GetFrameTime();
            FrameTime.Delta = dt;
            FrameTime.Total += dt;
            _input.Sample();
            foreach (var feature in _features)
            {
                feature.Update(dt, _world, _input, _events);
            }

            _events.Clear();

            foreach (var feature in _features)
            {
                feature.DrawOffscreen(_world);
            }

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

    /// <summary>
    /// Locks the cursor only while the window is focused, and only after Load.
    /// Unfocus releases it for the debugger; focus returns re-grabs for look.
    /// </summary>
    private void SyncCursorCapture()
    {
        var shouldLock = Raylib.IsWindowFocused();
        if (shouldLock == _cursorLocked)
        {
            return;
        }

        _cursorLocked = shouldLock;
        if (shouldLock)
        {
            Raylib.DisableCursor();
            // Discard the recenter spike from entering relative mouse mode.
            _ = Raylib.GetMouseDelta();
        }
        else
        {
            Raylib.EnableCursor();
        }
    }

    private void BuildFeatures()
    {
        // Order matters: input consumers first, visibility after movement, render/hud last.
        // Physics after LevelLoad (builds colliders); before PlayerController (capsule mover).
        // WorldRender.Update syncs the shared player camera before 3D draws.
        // Physics debug draw sits with the level pass (after sectors, before prop meshes).
        var physics = new PhysicsFeature();
        var levelModels = new LevelModelsFeature();
        var levelProps = new LevelPropsFeature();
        var doors = new DoorsAccessFeature();
        var pickups = new PickupsFeature();
        var cameras = new CamerasFeature(physics, doors);
        // Shadow casters are drawn once per cubemap face, before the main pass.
        var shadows = new ShadowMapFeature([levelModels, levelProps, doors, cameras]);

        _features.Add(new LevelLoadFeature());
        _features.Add(physics);
        _features.Add(new FullscreenFeature());
        _features.Add(new PlayerControllerFeature(physics));
        _features.Add(new InteractionFeature(doors, pickups, cameras));
        // Before InventoryFeature so UseRequested → ItemAcquired lands same frame.
        _features.Add(pickups);
        _features.Add(new InventoryFeature());
        _features.Add(doors);
        _features.Add(cameras);
        _features.Add(new WorkstationsFeature());
        _features.Add(new DetectionHeatFeature());
        _features.Add(new ObjectiveExfilFeature());
        _features.Add(new SectorVisibilityFeature());
        _features.Add(new LightingFeature());
        // After LightingFeature: needs this frame's light positions to build occlusion volumes.
        _features.Add(new LightVisibilityFeature());
        // After the volumes: shadow cubes cull casters against each light's sector reach.
        _features.Add(shadows);
        _features.Add(new WorldRenderFeature());
        _features.Add(levelModels);
        _features.Add(new PhysicsDebugDrawFeature(physics));
        _features.Add(levelProps);
        _features.Add(new HudFeature());
        // After HudFeature: draws into the UI framebuffer that Hud already began.
        _features.Add(new InventoryHudFeature());
        _features.Add(new DebugOverlayFeature(physics, shadows));
        _features.Add(new UiPresentFeature());
    }
}
