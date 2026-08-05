using System;
using Box3D.Interop;

namespace Box3D
{
    /// <summary>
    /// Reusable triangle-mesh collision data (wraps the opaque native b3MeshData). Build once,
    /// attach to any number of bodies via <see cref="Box3DBody.AddMesh(Box3DMesh, B3Vec3)"/>,
    /// each with its own scale. Dispose after every shape referencing it is gone.
    /// </summary>
    public sealed unsafe class Box3DMesh : IDisposable
    {
        private IntPtr _handle;

        /// <summary>Native b3MeshData pointer.</summary>
        public IntPtr Handle => _handle;

        public bool IsCreated => _handle != IntPtr.Zero;

        private Box3DMesh(IntPtr handle) => _handle = handle;

        ~Box3DMesh() => DestroyNative();

        public void Dispose()
        {
            DestroyNative();
            GC.SuppressFinalize(this);
        }

        private void DestroyNative()
        {
            if (_handle != IntPtr.Zero)
            {
                B3Api.b3DestroyMesh(_handle);
                _handle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Build a collision mesh from a triangle soup. Indices are 3 per triangle.
        /// <paramref name="identifyEdges"/> computes triangle adjacency for smoother
        /// collision across shared edges (recommended for terrain/world geometry).
        /// </summary>
        public static Box3DMesh Create(ReadOnlySpan<B3Vec3> vertices, ReadOnlySpan<int> indices,
            bool weldVertices = false, float weldTolerance = 0.005f,
            bool useMedianSplit = false, bool identifyEdges = true)
        {
            if (vertices.Length < 3)
                throw new ArgumentException("A mesh needs at least 3 vertices.", nameof(vertices));
            if (indices.Length < 3 || indices.Length % 3 != 0)
                throw new ArgumentException("Indices must contain 3 entries per triangle.", nameof(indices));

            fixed (B3Vec3* v = vertices)
            fixed (int* i = indices)
            {
                var def = new B3MeshDef
                {
                    Vertices = v,
                    Indices = i,
                    MaterialIndices = null,
                    WeldTolerance = weldTolerance,
                    VertexCount = vertices.Length,
                    TriangleCount = indices.Length / 3,
                    WeldVertices = weldVertices ? (byte)1 : (byte)0,
                    UseMedianSplit = useMedianSplit ? (byte)1 : (byte)0,
                    IdentifyEdges = identifyEdges ? (byte)1 : (byte)0,
                };

                IntPtr handle = B3Api.b3CreateMesh(in def, null, 0);
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException("b3CreateMesh failed (degenerate or invalid input mesh).");
                return new Box3DMesh(handle);
            }
        }

        /// <summary>Hollow box mesh (6 quads) — useful for test rooms.</summary>
        public static Box3DMesh CreateBox(B3Vec3 center, B3Vec3 extent, bool identifyEdges = true)
        {
            IntPtr handle = B3Api.b3CreateBoxMesh(center, extent, identifyEdges ? (byte)1 : (byte)0);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("b3CreateBoxMesh failed.");
            return new Box3DMesh(handle);
        }

        /// <summary>Flat grid mesh on the XZ plane — useful ground for tests and prototypes.</summary>
        public static Box3DMesh CreateGrid(int xCount, int zCount, float cellWidth, int materialCount = 1, bool identifyEdges = true)
        {
            IntPtr handle = B3Api.b3CreateGridMesh(xCount, zCount, cellWidth, materialCount, identifyEdges ? (byte)1 : (byte)0);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("b3CreateGridMesh failed.");
            return new Box3DMesh(handle);
        }
    }
}
