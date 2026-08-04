using System;
using System.Collections.Generic;
using Box3D.Interop;

namespace Box3D
{
    /// <summary>Options for <see cref="Box3DWorld.Draw"/>. Start from <see cref="Default"/>.</summary>
    public struct Box3DDebugDrawOptions
    {
        /// <summary>Draw the collision shapes themselves (the usual thing you want).</summary>
        public bool DrawShapes;

        /// <summary>Also draw each shape's broadphase AABB.</summary>
        public bool DrawBounds;

        /// <summary>Category filter for what gets drawn (matches shape filter categoryBits).</summary>
        public ulong MaskBits;

        /// <summary>Optional world-space cull box; leave null to use box3d's default drawing bounds.</summary>
        public B3AABB? DrawingBounds;

        public static Box3DDebugDrawOptions Default => new Box3DDebugDrawOptions
        {
            DrawShapes = true,
            DrawBounds = false,
            MaskBits = ulong.MaxValue,
            DrawingBounds = null,
        };
    }

    /// <summary>
    /// Engine-free sink for <see cref="Box3DWorld.Draw"/>: every debug-draw callback box3d emits
    /// (segments, boxes, spheres, capsules, bounds, points, transforms) is tessellated into plain
    /// line <see cref="Segments"/> in world space, double precision. Strings land in
    /// <see cref="Labels"/>. Renderers (e.g. <c>Box3DDebugLineMesh</c> in Box3D.Unity) consume the
    /// lists; headless code can assert on them directly. Reuse one instance across captures via
    /// <see cref="Clear"/> — the lists keep their capacity.
    /// </summary>
    public sealed class Box3DDebugSnapshot
    {
        /// <summary>A world-space line, color as 0xRRGGBB + alpha 0..1.</summary>
        public struct Segment
        {
            public B3Pos A, B;
            public uint Rgb;
            public float Alpha;
        }

        /// <summary>A world-space text label (from b3DebugDraw's DrawString).</summary>
        public struct Label
        {
            public B3Pos Position;
            public string Text;
            public uint Rgb;
        }

        const int CircleSegments = 16;
        const int ArcSegments = 8;

        public List<Segment> Segments { get; } = new List<Segment>(1024);
        public List<Label> Labels { get; } = new List<Label>();

        public void Clear()
        {
            Segments.Clear();
            Labels.Clear();
        }

        // ---------------- primitive appenders (called by the DebugDrawCallbacks shims) ----------------

        public void AddSegment(B3Pos a, B3Pos b, uint rgb, float alpha = 1f)
            => Segments.Add(new Segment { A = a, B = b, Rgb = rgb, Alpha = alpha });

        public void AddLabel(B3Pos p, string text, uint rgb)
            => Labels.Add(new Label { Position = p, Text = text, Rgb = rgb });

        /// <summary>Oriented box from half-extents + world transform → 12 edges.</summary>
        public void AddBox(B3Vec3 halfExtents, in B3WorldTransform xf, uint rgb, float alpha = 1f)
        {
            Span<B3Pos> corners = stackalloc B3Pos[8];
            for (int i = 0; i < 8; i++)
            {
                var local = new B3Vec3(
                    (i & 1) != 0 ? halfExtents.X : -halfExtents.X,
                    (i & 2) != 0 ? halfExtents.Y : -halfExtents.Y,
                    (i & 4) != 0 ? halfExtents.Z : -halfExtents.Z);
                corners[i] = Add(xf.P, Rotate(xf.Q, local));
            }

            // Edges connect corner indices differing in exactly one axis bit.
            AddEdge(corners, 0, 1, rgb, alpha); AddEdge(corners, 0, 2, rgb, alpha); AddEdge(corners, 0, 4, rgb, alpha);
            AddEdge(corners, 1, 3, rgb, alpha); AddEdge(corners, 1, 5, rgb, alpha); AddEdge(corners, 2, 3, rgb, alpha);
            AddEdge(corners, 2, 6, rgb, alpha); AddEdge(corners, 3, 7, rgb, alpha); AddEdge(corners, 4, 5, rgb, alpha);
            AddEdge(corners, 4, 6, rgb, alpha); AddEdge(corners, 5, 7, rgb, alpha); AddEdge(corners, 6, 7, rgb, alpha);
        }

        /// <summary>Axis-aligned bounds → 12 edges.</summary>
        public void AddBounds(in B3AABB aabb, uint rgb, float alpha = 1f)
        {
            Span<B3Pos> corners = stackalloc B3Pos[8];
            for (int i = 0; i < 8; i++)
                corners[i] = new B3Pos(
                    (i & 1) != 0 ? aabb.UpperBound.X : aabb.LowerBound.X,
                    (i & 2) != 0 ? aabb.UpperBound.Y : aabb.LowerBound.Y,
                    (i & 4) != 0 ? aabb.UpperBound.Z : aabb.LowerBound.Z);

            AddEdge(corners, 0, 1, rgb, alpha); AddEdge(corners, 0, 2, rgb, alpha); AddEdge(corners, 0, 4, rgb, alpha);
            AddEdge(corners, 1, 3, rgb, alpha); AddEdge(corners, 1, 5, rgb, alpha); AddEdge(corners, 2, 3, rgb, alpha);
            AddEdge(corners, 2, 6, rgb, alpha); AddEdge(corners, 3, 7, rgb, alpha); AddEdge(corners, 4, 5, rgb, alpha);
            AddEdge(corners, 4, 6, rgb, alpha); AddEdge(corners, 5, 7, rgb, alpha); AddEdge(corners, 6, 7, rgb, alpha);
        }

        /// <summary>Sphere → three world-axis great circles.</summary>
        public void AddSphere(B3Pos center, float radius, uint rgb, float alpha = 1f)
        {
            AddCircle(center, new B3Vec3(1, 0, 0), new B3Vec3(0, 1, 0), radius, rgb, alpha);
            AddCircle(center, new B3Vec3(1, 0, 0), new B3Vec3(0, 0, 1), radius, rgb, alpha);
            AddCircle(center, new B3Vec3(0, 1, 0), new B3Vec3(0, 0, 1), radius, rgb, alpha);
        }

        /// <summary>Capsule → end rings, four side lines, and hemisphere arcs.</summary>
        public void AddCapsule(B3Pos p1, B3Pos p2, float radius, uint rgb, float alpha = 1f)
        {
            var axis = new B3Vec3((float)(p2.X - p1.X), (float)(p2.Y - p1.Y), (float)(p2.Z - p1.Z));
            float len = MathF.Sqrt(axis.X * axis.X + axis.Y * axis.Y + axis.Z * axis.Z);
            B3Vec3 n = len > 1e-9f ? Scale(axis, 1f / len) : new B3Vec3(0, 1, 0);
            Basis(n, out B3Vec3 u, out B3Vec3 v);

            AddCircle(p1, u, v, radius, rgb, alpha);
            AddCircle(p2, u, v, radius, rgb, alpha);

            AddSegment(Add(p1, Scale(u, radius)), Add(p2, Scale(u, radius)), rgb, alpha);
            AddSegment(Add(p1, Scale(u, -radius)), Add(p2, Scale(u, -radius)), rgb, alpha);
            AddSegment(Add(p1, Scale(v, radius)), Add(p2, Scale(v, radius)), rgb, alpha);
            AddSegment(Add(p1, Scale(v, -radius)), Add(p2, Scale(v, -radius)), rgb, alpha);

            B3Vec3 nNeg = Scale(n, -1f);
            AddArc(p1, u, nNeg, radius, rgb, alpha);
            AddArc(p1, v, nNeg, radius, rgb, alpha);
            AddArc(p2, u, n, radius, rgb, alpha);
            AddArc(p2, v, n, radius, rgb, alpha);
        }

        /// <summary>Point → three-axis cross of the given size.</summary>
        public void AddPoint(B3Pos p, float size, uint rgb, float alpha = 1f)
        {
            float h = size * 0.5f;
            AddSegment(new B3Pos(p.X - h, p.Y, p.Z), new B3Pos(p.X + h, p.Y, p.Z), rgb, alpha);
            AddSegment(new B3Pos(p.X, p.Y - h, p.Z), new B3Pos(p.X, p.Y + h, p.Z), rgb, alpha);
            AddSegment(new B3Pos(p.X, p.Y, p.Z - h), new B3Pos(p.X, p.Y, p.Z + h), rgb, alpha);
        }

        /// <summary>
        /// Baked shape wireframe (local space) posed by the owning body's world transform. The
        /// color arrives packed by b3MakeDebugColor — material preset in the high byte, RGB in the
        /// low 24 bits — and the material bits are simply ignored here.
        /// </summary>
        public void AddShapeGeometry(Box3DDebugShapeGeometry geometry, in B3WorldTransform xf, uint rgb, float alpha = 1f)
        {
            var segments = geometry.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                Box3DDebugShapeGeometry.LocalSegment s = segments[i];
                AddSegment(Add(xf.P, Rotate(xf.Q, s.A)), Add(xf.P, Rotate(xf.Q, s.B)), rgb & 0xFFFFFF, alpha);
            }
        }

        /// <summary>Transform → RGB axis triad, 0.5 m long.</summary>
        public void AddTransform(in B3WorldTransform xf)
        {
            const float L = 0.5f;
            AddSegment(xf.P, Add(xf.P, Rotate(xf.Q, new B3Vec3(L, 0, 0))), 0xFF0000);
            AddSegment(xf.P, Add(xf.P, Rotate(xf.Q, new B3Vec3(0, L, 0))), 0x00FF00);
            AddSegment(xf.P, Add(xf.P, Rotate(xf.Q, new B3Vec3(0, 0, L))), 0x0000FF);
        }

        // ---------------- math helpers ----------------

        void AddEdge(Span<B3Pos> corners, int a, int b, uint rgb, float alpha)
            => AddSegment(corners[a], corners[b], rgb, alpha);

        void AddCircle(B3Pos center, B3Vec3 u, B3Vec3 v, float radius, uint rgb, float alpha)
        {
            B3Pos prev = Add(center, Scale(u, radius));
            for (int i = 1; i <= CircleSegments; i++)
            {
                float t = i * (2f * MathF.PI / CircleSegments);
                B3Pos next = Add(center, AddVec(Scale(u, radius * MathF.Cos(t)), Scale(v, radius * MathF.Sin(t))));
                AddSegment(prev, next, rgb, alpha);
                prev = next;
            }
        }

        // Half circle from +u through +w back to -u (hemisphere silhouette in the u-w plane).
        void AddArc(B3Pos center, B3Vec3 u, B3Vec3 w, float radius, uint rgb, float alpha)
        {
            B3Pos prev = Add(center, Scale(u, radius));
            for (int i = 1; i <= ArcSegments; i++)
            {
                float t = i * (MathF.PI / ArcSegments);
                B3Pos next = Add(center, AddVec(Scale(u, radius * MathF.Cos(t)), Scale(w, radius * MathF.Sin(t))));
                AddSegment(prev, next, rgb, alpha);
                prev = next;
            }
        }

        static B3Pos Add(in B3Pos p, in B3Vec3 v) => new B3Pos(p.X + v.X, p.Y + v.Y, p.Z + v.Z);

        static B3Vec3 AddVec(in B3Vec3 a, in B3Vec3 b) => new B3Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        static B3Vec3 Scale(in B3Vec3 v, float s) => new B3Vec3(v.X * s, v.Y * s, v.Z * s);

        static B3Vec3 Cross(in B3Vec3 a, in B3Vec3 b)
            => new B3Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        // v' = v + 2 * cross(q.V, cross(q.V, v) + q.S * v)
        static B3Vec3 Rotate(in B3Quat q, in B3Vec3 v)
        {
            B3Vec3 t = Cross(q.V, AddVec(Cross(q.V, v), Scale(v, q.S)));
            return AddVec(v, Scale(t, 2f));
        }

        // Orthonormal basis (u, v) perpendicular to unit vector n.
        static void Basis(in B3Vec3 n, out B3Vec3 u, out B3Vec3 v)
        {
            B3Vec3 reference = MathF.Abs(n.Y) < 0.99f ? new B3Vec3(0, 1, 0) : new B3Vec3(1, 0, 0);
            B3Vec3 c = Cross(reference, n);
            float len = MathF.Sqrt(c.X * c.X + c.Y * c.Y + c.Z * c.Z);
            u = Scale(c, 1f / len);
            v = Cross(n, u);
        }
    }
}
