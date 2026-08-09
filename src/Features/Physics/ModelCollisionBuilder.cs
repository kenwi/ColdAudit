using Box3D;
using Box3D.Interop;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;

namespace ColdAudit.Features.Physics;

/// <summary>
/// Cooks static Box3D triangle meshes from sector, portal, and model-placement collision paths.
/// Mesh data is shared per path; each owner gets its own static body.
/// </summary>
internal static class ModelCollisionBuilder
{
    public static int Build(Box3DWorld world, LevelData level, List<Box3DMesh> ownedMeshes)
    {
        var meshByPath = new Dictionary<string, Box3DMesh?>(StringComparer.Ordinal);
        var bodyCount = 0;

        foreach (var sector in level.Sectors)
        {
            if (!sector.HasCollisionMesh)
            {
                continue;
            }

            if (TryAddBody(
                    world,
                    meshByPath,
                    ownedMeshes,
                    sector.CollisionMeshPath!,
                    sector.Id,
                    position: default,
                    yawDegrees: 0f,
                    scale: 1f))
            {
                bodyCount++;
            }
        }

        foreach (var portal in level.Portals)
        {
            if (!portal.HasCollisionMesh)
            {
                continue;
            }

            if (TryAddBody(
                    world,
                    meshByPath,
                    ownedMeshes,
                    portal.CollisionMeshPath!,
                    portal.Id,
                    position: default,
                    yawDegrees: 0f,
                    scale: 1f))
            {
                bodyCount++;
            }
        }

        foreach (var placement in level.ModelPlacements)
        {
            if (!placement.HasCollisionMesh)
            {
                continue;
            }

            if (TryAddBody(
                    world,
                    meshByPath,
                    ownedMeshes,
                    placement.CollisionMeshPath!,
                    placement.Id,
                    placement.Position,
                    placement.YawDegrees,
                    placement.Scale))
            {
                bodyCount++;
            }
        }

        return bodyCount;
    }

    private static bool TryAddBody(
        Box3DWorld world,
        Dictionary<string, Box3DMesh?> meshByPath,
        List<Box3DMesh> ownedMeshes,
        string path,
        string ownerId,
        System.Numerics.Vector3 position,
        float yawDegrees,
        float scale)
    {
        if (!meshByPath.TryGetValue(path, out var mesh))
        {
            mesh = TryCreateMesh(path);
            meshByPath[path] = mesh;
            if (mesh is not null)
            {
                ownedMeshes.Add(mesh);
            }
        }

        if (mesh is null)
        {
            return false;
        }

        if (scale <= 0f)
        {
            Console.WriteLine($"[Physics] skip collision mesh for '{ownerId}': scale={scale}");
            return false;
        }

        var def = Box3DWorld.DefaultBodyDef();
        def.Type = B3BodyType.Static;
        def.Position = new B3Pos(position.X, position.Y, position.Z);
        def.Rotation = YawDegreesToQuat(yawDegrees);

        var body = world.CreateBody(in def);
        body.AddMesh(mesh, new B3Vec3(scale, scale, scale));
        return true;
    }

    private static Box3DMesh? TryCreateMesh(string path)
    {
        if (!ModelTriangleSoup.TryLoad(path, out var soup))
        {
            Console.WriteLine($"[Physics] failed to load collision mesh: {path}");
            return null;
        }

        var vertices = new B3Vec3[soup.Vertices.Length];
        for (var i = 0; i < soup.Vertices.Length; i++)
        {
            var v = soup.Vertices[i];
            vertices[i] = new B3Vec3(v.X, v.Y, v.Z);
        }

        try
        {
            var mesh = Box3DMesh.Create(
                vertices,
                soup.Indices,
                weldVertices: true,
                identifyEdges: true);
            Console.WriteLine(
                $"[Physics] cooked collision mesh '{Path.GetFileName(path)}' verts={vertices.Length} tris={soup.Indices.Length / 3}");
            return mesh;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Physics] b3CreateMesh failed for '{path}': {ex.Message}");
            return null;
        }
    }

    private static B3Quat YawDegreesToQuat(float yawDegrees)
    {
        var half = MathUtil.DegToRad(yawDegrees) * 0.5f;
        return new B3Quat
        {
            V = new B3Vec3(0f, MathF.Sin(half), 0f),
            S = MathF.Cos(half)
        };
    }
}
