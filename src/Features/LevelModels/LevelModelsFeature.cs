using System.Numerics;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Features.Physics;
using ColdAudit.Shared.Assets;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.LevelModels;

public sealed class LevelModelsFeature : FeatureBase
{
    private const float SurfaceUvTiles = 4f;
    private const int SurfaceMeshSlices = 16;

    private static readonly Color PortalTint = new(220, 230, 220, 255);
    private static float TileMeters => DebugSectorLayout.Extent / SurfaceUvTiles;

    private readonly Dictionary<string, ModelHandle> _handles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missingSectorIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _sectorIndexById = new(StringComparer.Ordinal);
    private Camera3D _camera;

    private Mesh _surfaceMesh;
    private bool _surfaceMeshLoaded;

    private Texture2D _floorTexture;
    private bool _floorTextureLoaded;
    private Material _floorMaterial;
    private bool _floorMaterialLoaded;

    private Texture2D _ceilingTexture;
    private bool _ceilingTextureLoaded;
    private Material _ceilingMaterial;
    private bool _ceilingMaterialLoaded;

    public override void Load(GameWorld world, EventBus events)
    {
        _camera = new Camera3D
        {
            Position = world.PlayerPosition,
            Target = world.PlayerPosition + Vector3.UnitZ,
            Up = Vector3.UnitY,
            FovY = 70f,
            Projection = CameraProjection.Perspective
        };

        LoadPlaceholderSurfaces(world);

        var level = world.ActiveLevel;
        if (level is null)
        {
            return;
        }

        for (var i = 0; i < level.Sectors.Count; i++)
        {
            var sector = level.Sectors[i];
            _sectorIndexById[sector.Id] = i;

            if (string.IsNullOrWhiteSpace(sector.ModelPath) || !File.Exists(sector.ModelPath))
            {
                _missingSectorIds.Add(sector.Id);
                continue;
            }

            var handle = new ModelHandle();
            handle.Load(sector.ModelPath);
            world.Lighting?.ApplyToModel(handle);
            _handles[sector.Id] = handle;
        }
    }

    public override void Draw(GameWorld world)
    {
        if (world.ActiveLevel is null)
        {
            return;
        }

        var forward = MathUtil.ForwardFromYawPitch(world.PlayerYaw, world.PlayerPitch);
        _camera.Position = world.PlayerPosition;
        _camera.Target = world.PlayerPosition + forward;

        Raylib.BeginMode3D(_camera);

        var sectors = world.ActiveLevel.Sectors;
        for (var i = 0; i < sectors.Count; i++)
        {
            var sector = sectors[i];
            if (!sector.RenderEnabled || !IsSectorDrawn(world, sector.Id))
            {
                continue;
            }

            if (_handles.TryGetValue(sector.Id, out var handle) && handle.IsLoaded)
            {
                Raylib.DrawModel(handle.Model, Vector3.Zero, 1f, Color.White);
                continue;
            }

            if (_missingSectorIds.Contains(sector.Id))
            {
                var origin = DebugSectorLayout.Origin(i);
                DrawFloor(origin);
                DrawCeiling(origin);
            }
        }

        DrawPortalPlaceholders(world, world.ActiveLevel);

        Raylib.EndMode3D();
    }

    public override void Unload()
    {
        foreach (var handle in _handles.Values)
        {
            BasicLighting.DetachFromModel(handle);
            handle.Dispose();
        }

        _handles.Clear();
        _missingSectorIds.Clear();
        _sectorIndexById.Clear();
        UnloadPlaceholderSurfaces();
    }

    private void LoadPlaceholderSurfaces(GameWorld world)
    {
        UnloadPlaceholderSurfaces();

        _surfaceMesh = Raylib.GenMeshPlane(
            DebugSectorLayout.Extent,
            DebugSectorLayout.Extent,
            SurfaceMeshSlices,
            SurfaceMeshSlices);
        // UVs are rewritten in world space before each draw so portals can match.
        Raylib.UploadMesh(ref _surfaceMesh, false);
        _surfaceMeshLoaded = true;

        TryLoadSurfaceMaterial(
            world,
            TextureCatalog.FloorCarpetPath,
            out _floorTexture,
            out _floorTextureLoaded,
            out _floorMaterial,
            out _floorMaterialLoaded);

        TryLoadSurfaceMaterial(
            world,
            TextureCatalog.CeilingTilesPath,
            out _ceilingTexture,
            out _ceilingTextureLoaded,
            out _ceilingMaterial,
            out _ceilingMaterialLoaded);
    }

    private static void TryLoadSurfaceMaterial(
        GameWorld world,
        string path,
        out Texture2D texture,
        out bool textureLoaded,
        out Material material,
        out bool materialLoaded)
    {
        texture = default;
        textureLoaded = false;
        material = default;
        materialLoaded = false;

        if (!File.Exists(path))
        {
            return;
        }

        texture = Raylib.LoadTexture(path);
        Raylib.SetTextureWrap(texture, TextureWrap.Repeat);
        Raylib.SetTextureFilter(texture, TextureFilter.Bilinear);
        textureLoaded = true;

        material = Raylib.LoadMaterialDefault();
        Raylib.SetMaterialTexture(ref material, MaterialMapIndex.Albedo, texture);
        world.Lighting?.ApplyToMaterial(ref material);
        materialLoaded = true;
    }

    private void UnloadPlaceholderSurfaces()
    {
        UnloadMaterial(ref _floorMaterial, ref _floorMaterialLoaded, _floorTextureLoaded);
        UnloadMaterial(ref _ceilingMaterial, ref _ceilingMaterialLoaded, _ceilingTextureLoaded);

        if (_surfaceMeshLoaded)
        {
            Raylib.UnloadMesh(_surfaceMesh);
            _surfaceMeshLoaded = false;
        }

        if (_floorTextureLoaded)
        {
            Raylib.UnloadTexture(_floorTexture);
            _floorTextureLoaded = false;
        }

        if (_ceilingTextureLoaded)
        {
            Raylib.UnloadTexture(_ceilingTexture);
            _ceilingTextureLoaded = false;
        }
    }

    private static void UnloadMaterial(ref Material material, ref bool materialLoaded, bool hasOwnedTexture)
    {
        if (!materialLoaded)
        {
            return;
        }

        // Keep texture ownership here; clear the map so UnloadMaterial does not free it.
        if (hasOwnedTexture)
        {
            Raylib.SetMaterialTexture(ref material, MaterialMapIndex.Albedo, default);
        }

        // UnloadMaterial frees non-default shaders; detach shared lighting first.
        BasicLighting.DetachFromMaterial(ref material);

        Raylib.UnloadMaterial(material);
        materialLoaded = false;
    }

    private void DrawFloor(Vector3 origin)
    {
        if (!_surfaceMeshLoaded || !_floorMaterialLoaded)
        {
            Raylib.DrawPlane(
                origin,
                new Vector2(DebugSectorLayout.Extent, DebugSectorLayout.Extent),
                new Color(40, 48, 58, 255));
            return;
        }

        WriteWorldSpaceUvs(origin);
        // Raylib matrices are column-major; System.Numerics.CreateTranslation is not.
        var transform = Raymath.MatrixTranslate(origin.X, origin.Y, origin.Z);
        Raylib.DrawMesh(_surfaceMesh, _floorMaterial, transform);
    }

    private void DrawCeiling(Vector3 origin)
    {
        var ceilingY = origin.Y + LevelCollisionBuilder.WallHeight;
        if (!_surfaceMeshLoaded || !_ceilingMaterialLoaded)
        {
            Raylib.DrawPlane(
                new Vector3(origin.X, ceilingY, origin.Z),
                new Vector2(DebugSectorLayout.Extent, DebugSectorLayout.Extent),
                new Color(200, 200, 205, 255));
            return;
        }

        WriteWorldSpaceUvs(origin);
        // Same +Y plane as the floor, raised to wall height. Disable culling so the
        // underside is visible from inside the room (normals stay +Y for lighting).
        var transform = Raymath.MatrixTranslate(origin.X, ceilingY, origin.Z);
        Rlgl.DisableBackfaceCulling();
        Raylib.DrawMesh(_surfaceMesh, _ceilingMaterial, transform);
        Rlgl.EnableBackfaceCulling();
    }

    private void DrawPortalPlaceholders(GameWorld world, LevelData level)
    {
        foreach (var portal in level.Portals)
        {
            if (!_sectorIndexById.TryGetValue(portal.FromSectorId, out var fromIndex) ||
                !_sectorIndexById.TryGetValue(portal.ToSectorId, out var toIndex))
            {
                continue;
            }

            var fromSector = level.Sectors[fromIndex];
            var toSector = level.Sectors[toIndex];
            var fromDrawn = fromSector.RenderEnabled && IsSectorDrawn(world, portal.FromSectorId);
            var toDrawn = toSector.RenderEnabled && IsSectorDrawn(world, portal.ToSectorId);
            if (!fromDrawn && !toDrawn)
            {
                continue;
            }

            var from = DebugSectorLayout.Origin(fromIndex);
            var to = DebugSectorLayout.Origin(toIndex);
            var center = (from + to) * 0.5f;
            var delta = to - from;

            Vector2 size;
            if (System.MathF.Abs(delta.X) >= System.MathF.Abs(delta.Z))
            {
                size = new Vector2(DebugSectorLayout.PortalGap, DebugSectorLayout.PortalWidth);
            }
            else
            {
                size = new Vector2(DebugSectorLayout.PortalWidth, DebugSectorLayout.PortalGap);
            }

            // Floor strip across the portal gap.
            Raylib.DrawPlane(center, size, PortalTint);

            // Ceiling soffit with world-space UVs so tiles continue through the doorway.
            DrawPortalCeiling(world, center, size);
        }
    }

    private void DrawPortalCeiling(GameWorld world, Vector3 center, Vector2 size)
    {
        var y = LevelCollisionBuilder.WallHeight;
        var halfX = size.X * 0.5f;
        var halfZ = size.Y * 0.5f;

        var bl = new Vector3(center.X - halfX, y, center.Z - halfZ);
        var br = new Vector3(center.X + halfX, y, center.Z - halfZ);
        var tr = new Vector3(center.X + halfX, y, center.Z + halfZ);
        var tl = new Vector3(center.X - halfX, y, center.Z + halfZ);

        var lighting = world.Lighting is { IsLoaded: true } lit ? lit : null;
        var useLighting = lighting is not null && lighting.TryBeginShaderMode();

        if (_ceilingTextureLoaded)
        {
            Rlgl.SetTexture(_ceilingTexture.Id);
        }
        else
        {
            Rlgl.SetTexture(0);
        }

        Rlgl.DisableBackfaceCulling();
        DrawWorldUvCeilingTriangle(bl, br, tr);
        DrawWorldUvCeilingTriangle(bl, tr, tl);
        Rlgl.EnableBackfaceCulling();
        Rlgl.SetTexture(0);

        if (useLighting)
        {
            lighting!.EndShaderMode();
        }
    }

    private static void DrawWorldUvCeilingTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        var tile = TileMeters;
        Rlgl.Begin(DrawMode.Triangles);
        Rlgl.Color4ub(255, 255, 255, 255);
        Rlgl.Normal3f(0f, 1f, 0f);

        Rlgl.TexCoord2f(a.X / tile, a.Z / tile);
        Rlgl.Vertex3f(a.X, a.Y, a.Z);

        Rlgl.TexCoord2f(b.X / tile, b.Z / tile);
        Rlgl.Vertex3f(b.X, b.Y, b.Z);

        Rlgl.TexCoord2f(c.X / tile, c.Z / tile);
        Rlgl.Vertex3f(c.X, c.Y, c.Z);
        Rlgl.End();
    }

    /// <summary>
    /// Bake world-space XZ UVs so sector and portal ceilings share one tiling space.
    /// </summary>
    private unsafe void WriteWorldSpaceUvs(Vector3 origin)
    {
        if (!_surfaceMeshLoaded || _surfaceMesh.TexCoords == null || _surfaceMesh.Vertices == null)
        {
            return;
        }

        var tile = TileMeters;
        var positions = _surfaceMesh.VerticesAs<Vector3>();
        var uvs = _surfaceMesh.TexCoordsAs<Vector2>();
        for (var i = 0; i < _surfaceMesh.VertexCount; i++)
        {
            var worldX = positions[i].X + origin.X;
            var worldZ = positions[i].Z + origin.Z;
            uvs[i] = new Vector2(worldX / tile, worldZ / tile);
        }

        Raylib.UpdateMeshBuffer(_surfaceMesh, Mesh.VboIdIndexTexCoords, uvs, 0);
    }

    private static bool IsSectorDrawn(GameWorld world, string sectorId) =>
        !world.SectorCullEnabled || world.VisibleSectorIds.Contains(sectorId);
}
