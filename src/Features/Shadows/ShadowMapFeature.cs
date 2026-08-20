using System.Numerics;
using ColdAudit.Shared.Contracts;
using ColdAudit.Shared.Input;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;
using ColdAudit.Shared.World;
using Raylib_cs;

namespace ColdAudit.Features.Shadows;

/// <summary>
/// Omnidirectional shadow maps: one linear-distance depth cubemap per point light, rendered
/// offscreen before the main pass and sampled by <c>pbr.fs</c>. This is what makes meshes
/// occlude light; <c>LightVisibilityFeature</c>'s portal volumes remain as a cheap reject and
/// as the source of each light's caster set.
/// </summary>
public sealed class ShadowMapFeature : FeatureBase
{
    private const int FaceCount = 6;
    private const int FaceResolution = 512;
    private const float NearPlane = 0.05f;

    /// <summary>
    /// Distance beyond which a light casts no shadow. Also the normalisation range for the
    /// half-float cubemap, so keeping it tight preserves depth precision.
    /// </summary>
    private const float FarPlane = 60f;

    /// <summary>Squared movement before a cached cube is considered stale (2 cm).</summary>
    private const float MoveEpsilonSquared = 0.0004f;

    /// <summary>
    /// Raylib binds and unbinds units 0..MAX_MATERIAL_MAPS-1 (12) around every mesh draw, so
    /// the shadow cubes sit above that range. GL 3.3 guarantees 16 fragment texture units.
    /// </summary>
    private const int FirstTextureSlot = 12;

    // OpenGL cubemap face conventions: +Y/-Y roll differently from the lateral faces, and
    // every face is rendered with an inverted up vector.
    private static readonly Vector3[] FaceForward =
    [
        new(1f, 0f, 0f),
        new(-1f, 0f, 0f),
        new(0f, 1f, 0f),
        new(0f, -1f, 0f),
        new(0f, 0f, 1f),
        new(0f, 0f, -1f)
    ];

    private static readonly Vector3[] FaceUp =
    [
        new(0f, -1f, 0f),
        new(0f, -1f, 0f),
        new(0f, 0f, 1f),
        new(0f, 0f, -1f),
        new(0f, -1f, 0f),
        new(0f, -1f, 0f)
    ];

    private readonly IReadOnlyList<IShadowCaster> _casters;
    private readonly ShadowPass _pass = new();
    private readonly ShadowCube[] _cubes = new ShadowCube[BasicLighting.MaxLights];
    private readonly uint[] _cubemapIds = new uint[BasicLighting.MaxLights];
    private readonly bool[] _lightHasShadow = new bool[BasicLighting.MaxLights];
    private readonly HashSet<string> _casterSectorIds = new(StringComparer.Ordinal);
    private readonly Frustum _faceFrustum = new();

    private bool _resourcesReady;

    public ShadowMapFeature(IReadOnlyList<IShadowCaster> casters)
    {
        _casters = casters;
    }

    /// <summary>Face renders issued last frame, for the debug overlay.</summary>
    public int FacesRenderedLastFrame { get; private set; }

    public override void Load(GameWorld world, EventBus events)
    {
        if (!_pass.Load())
        {
            return;
        }

        for (var i = 0; i < _cubes.Length; i++)
        {
            if (!ShadowCube.TryCreate(FaceResolution, out var cube))
            {
                ReleaseCubes();
                _pass.Unload();
                return;
            }

            _cubes[i] = cube;
            _cubemapIds[i] = cube.CubemapId;
        }

        _resourcesReady = true;
    }

    public override void Update(float dt, GameWorld world, InputState input, EventBus events)
    {
        if (input.ToggleShadowsPressed)
        {
            world.ShadowsEnabled = !world.ShadowsEnabled;
        }
    }

    public override void DrawOffscreen(GameWorld world)
    {
        FacesRenderedLastFrame = 0;
        if (world.Lighting is not { IsLoaded: true } lighting)
        {
            return;
        }

        if (!_resourcesReady)
        {
            return;
        }

        var lights = lighting.Lights;
        for (var i = 0; i < _cubes.Length; i++)
        {
            _lightHasShadow[i] = world.ShadowsEnabled && i < lights.Count;
            if (!_lightHasShadow[i])
            {
                continue;
            }

            RefreshCube(world, _cubes[i], lights[i]);
        }

        // Always bind, even with shadows off: a samplerCube left on unit 0 would clash with
        // the 2D albedo texture there and the driver drops the whole draw.
        lighting.BindShadowCubes(_cubemapIds, _lightHasShadow, FirstTextureSlot);
        lighting.SetShadowParams(FarPlane, FaceResolution);
    }

    public override void Unload()
    {
        ReleaseCubes();
        _pass.Unload();
        _casterSectorIds.Clear();
        _resourcesReady = false;
    }

    private void RefreshCube(GameWorld world, ShadowCube cube, SceneLight light)
    {
        ResolveCasterSectors(world, light);

        var moved = (light.Position - cube.LightPosition).LengthSquared() > MoveEpsilonSquared;
        if (cube.IsValid && !moved && cube.GeometryRevision == world.ShadowGeometryRevision)
        {
            return;
        }

        cube.LightPosition = light.Position;
        cube.GeometryRevision = world.ShadowGeometryRevision;
        cube.IsValid = true;

        _pass.BeginLight(light.Position, FarPlane, _casterSectorIds);

        var projection = Raymath.MatrixPerspective(MathF.PI * 0.5f, 1f, NearPlane, FarPlane);
        Rlgl.EnableDepthTest();
        // Room shells are single-sided from the inside; keep every triangle so walls stay
        // watertight from the light's point of view.
        Rlgl.DisableBackfaceCulling();

        for (var face = 0; face < FaceCount; face++)
        {
            Rlgl.FramebufferAttach(
                cube.FboId,
                cube.CubemapId,
                FramebufferAttachType.ColorChannel0,
                (FramebufferAttachTextureType)face,
                0);

            // FramebufferAttach leaves the default framebuffer bound, so re-bind after every
            // face or the clear and the draws land on the back buffer.
            Rlgl.EnableFramebuffer(cube.FboId);
            Rlgl.ActiveDrawBuffers(1);
            Rlgl.Viewport(0, 0, FaceResolution, FaceResolution);

            // White clears to the far plane, which reads back as "nothing occluding".
            Rlgl.ClearColor(255, 255, 255, 255);
            Rlgl.ClearScreenBuffers();

            if (!FaceSeesAnyCaster(world, light.Position, face))
            {
                continue;
            }

            Rlgl.SetMatrixProjection(projection);
            Rlgl.SetMatrixModelView(Raymath.MatrixLookAt(
                light.Position,
                light.Position + FaceForward[face],
                FaceUp[face]));

            foreach (var caster in _casters)
            {
                caster.DrawDepth(world, _pass);
            }

            Rlgl.DrawRenderBatchActive();
            FacesRenderedLastFrame++;
        }

        Rlgl.EnableBackfaceCulling();
        Rlgl.DisableDepthTest();
        Rlgl.DisableFramebuffer();
        Rlgl.Viewport(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
    }

    /// <summary>
    /// Geometry that can occlude this light: its own room plus every room one doorway away,
    /// matching the reach of the light's portal volumes.
    /// </summary>
    private void ResolveCasterSectors(GameWorld world, SceneLight light)
    {
        _casterSectorIds.Clear();
        if (string.IsNullOrEmpty(light.SectorId))
        {
            // Unmasked light: everything casts.
            foreach (var id in world.Sectors.SectorIds)
            {
                _casterSectorIds.Add(id);
            }

            return;
        }

        _casterSectorIds.Add(light.SectorId);
        foreach (var link in world.Sectors.LinksFrom(light.SectorId))
        {
            _casterSectorIds.Add(link.OtherSectorId);
        }
    }

    private bool FaceSeesAnyCaster(GameWorld world, Vector3 lightPosition, int face)
    {
        _faceFrustum.UpdateFromCamera(
            lightPosition,
            FaceForward[face],
            FaceUp[face],
            90f,
            1f,
            NearPlane,
            FarPlane);

        foreach (var sectorId in _casterSectorIds)
        {
            if (world.Sectors.TryGetBounds(sectorId, out var bounds) &&
                _faceFrustum.IntersectsAabb(bounds))
            {
                return true;
            }
        }

        return false;
    }

    private void ReleaseCubes()
    {
        for (var i = 0; i < _cubes.Length; i++)
        {
            _cubes[i]?.Release();
            _cubes[i] = null!;
            _cubemapIds[i] = 0;
            _lightHasShadow[i] = false;
        }
    }

    private sealed class ShadowCube
    {
        public uint FboId { get; private init; }
        public uint CubemapId { get; private init; }
        public uint DepthId { get; private init; }

        public Vector3 LightPosition { get; set; }
        public int GeometryRevision { get; set; } = -1;
        public bool IsValid { get; set; }

        public static unsafe bool TryCreate(int resolution, out ShadowCube cube)
        {
            cube = null!;

            // Rlgl refuses to allocate empty R32/R32G32B32A32 cubemaps; R32G32B32 lands on
            // RGB16F, whose relative precision is plenty for a normalised distance.
            var cubemapId = Rlgl.LoadTextureCubemap(
                null,
                resolution,
                PixelFormat.UncompressedR32G32B32,
                1);
            if (cubemapId == 0)
            {
                return false;
            }

            var depthId = Rlgl.LoadTextureDepth(resolution, resolution, true);
            var fboId = Rlgl.LoadFramebuffer();
            if (fboId == 0)
            {
                Rlgl.UnloadTexture(cubemapId);
                Rlgl.UnloadTexture(depthId);
                return false;
            }

            Rlgl.FramebufferAttach(
                fboId,
                depthId,
                FramebufferAttachType.Depth,
                FramebufferAttachTextureType.Renderbuffer,
                0);
            Rlgl.FramebufferAttach(
                fboId,
                cubemapId,
                FramebufferAttachType.ColorChannel0,
                FramebufferAttachTextureType.CubemapPositiveX,
                0);

            if (!Rlgl.FramebufferComplete(fboId))
            {
                Rlgl.UnloadFramebuffer(fboId);
                return false;
            }

            cube = new ShadowCube
            {
                FboId = fboId,
                CubemapId = cubemapId,
                DepthId = depthId
            };
            return true;
        }

        public void Release()
        {
            // UnloadFramebuffer also frees the attached texture and renderbuffer.
            Rlgl.UnloadFramebuffer(FboId);
        }
    }
}
