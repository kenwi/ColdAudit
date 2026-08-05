using Box3D;
using Box3D.Interop;
using ColdAudit.Features.LevelLoad;
using ColdAudit.Shared.Math;
using ColdAudit.Shared.Rendering;

namespace ColdAudit.Features.Physics;

/// <summary>
/// Cooks static Box3D triangle meshes from <see cref="ModelPlacementDef.CollisionMeshPath"/>.
/// Mesh data is shared per path; each placement gets its own static body (pose + scale).
/// </summary>
internal static class ModelCollisionBuilder
{
    public static int Build(Box3DWorld world, LevelData level, List<Box3DMesh> ownedMeshes)
    {
        var meshByPath = new Dictionary<string, Box3DMesh?>(StringComparer.Ordinal);
        var bodyCount = 0;

        foreach (var placement in level.ModelPlacements)
        {
            if (!placement.HasCollisionMesh)
            {
                continue;
            }

            var path = placement.CollisionMeshPath!;
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
                continue;
            }

            var scale = placement.Scale;
            if (scale <= 0f)
            {
                Console.WriteLine($"[Physics] skip collision mesh for '{placement.Id}': scale={scale}");
                continue;
            }

            var def = Box3DWorld.DefaultBodyDef();
            def.Type = B3BodyType.Static;
            def.Position = new B3Pos(placement.Position.X, placement.Position.Y, placement.Position.Z);
            def.Rotation = YawDegreesToQuat(placement.YawDegrees);

            var body = world.CreateBody(in def);
            body.AddMesh(mesh, new B3Vec3(scale, scale, scale));
            bodyCount++;
        }

        return bodyCount;
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
