using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.Inventory;

/// <summary>
/// Bottom-left inventory strip. Renders each carried item as a small 3D preview
/// (GLB when authored, otherwise the same LitBoxMesh placeholder as world pickups).
/// </summary>
public sealed class InventoryHudFeature : FeatureBase
{
    private const int MaxSlots = 8;
    private const int SlotSize = 72;
    private const int SlotGap = 8;
    private const int MarginLeft = 12;
    private const int MarginBottom = 12;
    private const float SpinSpeedDegrees = 0f;

    private static readonly Color SlotBg = new(12, 14, 18, 200);
    private static readonly Color SlotBorder = new(180, 185, 195, 220);
    private static readonly Color LabelColor = new(220, 220, 220, 255);

    private readonly List<string> _displayOrder = [];
    private readonly LitBoxMesh _placeholder = new();
    private readonly Dictionary<string, ModelHandle> _handlesByPath = new(StringComparer.Ordinal);
    private readonly RenderTexture2D[] _slotTargets = new RenderTexture2D[MaxSlots];
    private readonly bool[] _slotLoaded = new bool[MaxSlots];

    private Camera3D _studioCamera;
    private float _spinYawDegrees;
    private int _activeCount;

    public override void Load(GameWorld world, EventBus events)
    {
        _placeholder.Load();
        _displayOrder.Clear();
        _spinYawDegrees = 25f;
        _studioCamera = new Camera3D
        {
            Position = new Vector3(0.2f, 0.16f, 0.26f),
            Target = Vector3.Zero,
            Up = Vector3.UnitY,
            FovY = 32f,
            Projection = CameraProjection.Perspective
        };

        for (var i = 0; i < MaxSlots; i++)
        {
            _slotTargets[i] = Raylib.LoadRenderTexture(SlotSize, SlotSize);
            _slotLoaded[i] = true;
        }
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        SyncDisplayOrder(world);
        _spinYawDegrees += SpinSpeedDegrees * dt;
        if (_spinYawDegrees >= 360f)
        {
            _spinYawDegrees -= 360f;
        }
    }

    public override void DrawOffscreen(GameWorld world)
    {
        _activeCount = Math.Min(_displayOrder.Count, MaxSlots);
        if (_activeCount == 0)
        {
            return;
        }

        for (var i = 0; i < _activeCount; i++)
        {
            var itemId = _displayOrder[i];
            var visual = ItemVisualCatalog.Resolve(itemId, world.ActiveLevel);
            TryLoadModel(visual.ModelPath);

            Raylib.BeginTextureMode(_slotTargets[i]);
            Raylib.ClearBackground(Color.Blank);
            Raylib.BeginMode3D(_studioCamera);
            DrawPreview(itemId, visual);
            Raylib.EndMode3D();
            Raylib.EndTextureMode();
        }
    }

    public override void Draw(GameWorld world)
    {
        if (_activeCount == 0 || !world.Ui.IsBegun)
        {
            return;
        }

        var labelBand = 18;
        var baseY = UiFramebuffer.Height - MarginBottom - SlotSize - labelBand;

        for (var i = 0; i < _activeCount; i++)
        {
            var x = MarginLeft + i * (SlotSize + SlotGap);
            var y = baseY;
            var itemId = _displayOrder[i];
            var visual = ItemVisualCatalog.Resolve(itemId, world.ActiveLevel);

            Raylib.DrawRectangle(x, y, SlotSize, SlotSize, SlotBg);
            Raylib.DrawRectangleLines(x, y, SlotSize, SlotSize, SlotBorder);

            // Render textures are Y-flipped in OpenGL.
            var source = new Rectangle(0, 0, SlotSize, -SlotSize);
            var dest = new Rectangle(x, y, SlotSize, SlotSize);
            Raylib.DrawTexturePro(_slotTargets[i].Texture, source, dest, Vector2.Zero, 0f, Color.White);

            var label = visual.Label;
            var fontSize = 12;
            var textW = Raylib.MeasureText(label, fontSize);
            var textX = x + Math.Max(2, (SlotSize - textW) / 2);
            Raylib.DrawText(label, textX, y + SlotSize + 2, fontSize, LabelColor);
        }
    }

    public override void Unload()
    {
        foreach (var handle in _handlesByPath.Values)
        {
            handle.Dispose();
        }

        _handlesByPath.Clear();
        _placeholder.Unload();
        _displayOrder.Clear();
        _activeCount = 0;

        for (var i = 0; i < MaxSlots; i++)
        {
            if (!_slotLoaded[i])
            {
                continue;
            }

            Raylib.UnloadRenderTexture(_slotTargets[i]);
            _slotLoaded[i] = false;
        }
    }

    private void SyncDisplayOrder(GameWorld world)
    {
        for (var i = _displayOrder.Count - 1; i >= 0; i--)
        {
            if (!world.CarriedItemIds.Contains(_displayOrder[i]))
            {
                _displayOrder.RemoveAt(i);
            }
        }

        foreach (var itemId in world.CarriedItemIds)
        {
            if (_displayOrder.Contains(itemId))
            {
                continue;
            }

            _displayOrder.Add(itemId);
        }
    }

    private void DrawPreview(string itemId, ItemVisual visual)
    {
        if (!string.IsNullOrWhiteSpace(visual.ModelPath) &&
            _handlesByPath.TryGetValue(visual.ModelPath, out var handle) &&
            handle.IsLoaded)
        {
            Raylib.DrawModelEx(
                handle.Model,
                Vector3.Zero,
                Vector3.UnitY,
                _spinYawDegrees,
                Vector3.One,
                Color.White);
            return;
        }

        // Same local layout as KeycardsFeature: body at origin, gold chip on the top face.
        _placeholder.Draw(Vector3.Zero, visual.BoxSize, _spinYawDegrees, visual.Color);

        if (!ItemVisualCatalog.IsKeycard(itemId))
        {
            return;
        }

        var yawRad = MathUtil.DegToRad(_spinYawDegrees);
        var chipOffset = Vector3.Transform(
            new Vector3(-visual.BoxSize.X * 0.28f, visual.BoxSize.Y * 0.5f + 0.0015f, visual.BoxSize.Z * 0.12f),
            Matrix4x4.CreateRotationY(yawRad));
        _placeholder.Draw(
            chipOffset,
            new Vector3(0.028f, 0.003f, 0.022f),
            _spinYawDegrees,
            ItemVisualCatalog.KeycardChipColor);
    }

    private void TryLoadModel(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _handlesByPath.ContainsKey(path))
        {
            return;
        }

        if (!File.Exists(path))
        {
            return;
        }

        var handle = new ModelHandle();
        handle.Load(path);
        _handlesByPath[path] = handle;
    }
}
