using System.Numerics;
using Raylib_cs;

namespace ColdAudit.Shared.Rendering;

/// <summary>
/// Extracts a flat triangle soup from a Raylib-loaded model (e.g. GLB) for physics cooking.
/// </summary>
public static class ModelTriangleSoup
{
    public readonly struct Result
    {
        public Result(Vector3[] vertices, int[] indices)
        {
            Vertices = vertices;
            Indices = indices;
        }

        public Vector3[] Vertices { get; }
        public int[] Indices { get; }
    }

    /// <summary>Load a GLB/glTF, extract triangles, unload the GPU model.</summary>
    public static bool TryLoad(string path, out Result soup)
    {
        soup = default;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var model = Raylib.LoadModel(path);
        try
        {
            return TryExtract(model, out soup);
        }
        finally
        {
            Raylib.UnloadModel(model);
        }
    }

    public static unsafe bool TryExtract(Model model, out Result soup)
    {
        soup = default;
        if (model.MeshCount <= 0 || model.Meshes == null)
        {
            return false;
        }

        var vertices = new List<Vector3>(256);
        var indices = new List<int>(512);
        var modelXform = model.Transform;
        var meshes = model.MeshesAsSpan();

        for (var meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
        {
            ref var mesh = ref meshes[meshIndex];
            if (mesh.VertexCount < 3 || mesh.Vertices == null)
            {
                continue;
            }

            var baseVertex = vertices.Count;
            var positions = mesh.VerticesAs<Vector3>();
            for (var i = 0; i < mesh.VertexCount; i++)
            {
                vertices.Add(Vector3.Transform(positions[i], modelXform));
            }

            if (mesh.Indices != null && mesh.TriangleCount > 0)
            {
                var meshIndices = mesh.IndicesAs<ushort>();
                var indexCount = mesh.TriangleCount * 3;
                for (var i = 0; i < indexCount; i++)
                {
                    indices.Add(baseVertex + meshIndices[i]);
                }
            }
            else
            {
                // Non-indexed triangle list.
                for (var i = 0; i < mesh.VertexCount; i++)
                {
                    indices.Add(baseVertex + i);
                }
            }
        }

        if (vertices.Count < 3 || indices.Count < 3 || indices.Count % 3 != 0)
        {
            return false;
        }

        soup = new Result(vertices.ToArray(), indices.ToArray());
        return true;
    }
}
