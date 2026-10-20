using ColdAudit.Shared.Assets;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;

namespace ColdAudit.Features.LightMaps;

/// <summary>
/// Loads the level's baked lighting atlas and hands it to the sector drawers.
/// <para>
/// This replaces the realtime chain for the sector geometry rather than competing with it:
/// with an atlas loaded, <c>LevelModelsFeature</c> assigns the lightmap shader
/// instead of <see cref="BasicLighting"/>, so no lights, shadow cubes or light volumes are
/// consulted for the level. Props and doors drawn by other features have no atlas UVs and
/// keep their existing path.
/// </para>
/// </summary>
public sealed class LightMapFeature : FeatureBase
{
    private GameWorld? _world;
    private LightMap? _lightMap;

    public override void Load(GameWorld world, EventBus events)
    {
        _world = world;

        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        var map = new LightMap();
        var image = LevelCatalog.LightMapImagePath(level.LevelId);
        var meta = LevelCatalog.LightMapMetaPath(level.LevelId);
        if (!map.TryLoad(image, meta))
        {
            Console.WriteLine($"[LightMap] disabled: {map.Error}");
            map.Dispose();
            return;
        }

        _lightMap = map;
        world.LightMap = map;
        Console.WriteLine(
            $"[LightMap] {Path.GetFileName(image)} {map.Resolution}x{map.Resolution} " +
            $"uv={map.UvLayer} encodeScale={map.EncodeScale:0.00000}");
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (_lightMap is not { IsLoaded: true } map)
        {
            return;
        }

        if (input.ToggleLightMapPressed)
        {
            map.SetEnabled(!map.Enabled);
        }
    }

    public override void Unload()
    {
        if (_lightMap is null)
        {
            return;
        }

        // LevelModelsFeature unloads first (features unwind in reverse) and has already
        // pointed its materials back at the default shader by now.
        if (_world is not null && ReferenceEquals(_world.LightMap, _lightMap))
        {
            _world.LightMap = null;
        }

        _lightMap.Dispose();
        _lightMap = null;
        _world = null;
    }
}
