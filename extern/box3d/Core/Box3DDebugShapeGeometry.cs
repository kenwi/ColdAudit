using System;
using System.Collections.Generic;
using Box3D.Interop;

namespace Box3D
{
    /// <summary>
    /// The baked drawable representation of one shape, produced once by the world's
    /// CreateDebugShape hook and re-drawn every <see cref="Box3DWorld.Draw"/> by transforming the
    /// local-space segments with the owning body's world transform. This is box3d's intended debug
    /// pipeline: shape geometry is only ever drawn via a user-baked handle, never decomposed by the
    /// engine itself (the primitive callbacks serve contacts/joints/bounds).
    ///
    /// Coverage: spheres and capsules tessellate exactly; hulls bake their local-space AABB — exact
    /// for box hulls (all of them, today), an envelope for arbitrary hulls (the half-edge walk needs
    /// hull internals the public headers don't define; upgrade here when non-box hulls appear).
    /// Mesh / compound / height-field shapes bake nothing yet.
    /// </summary>
    public sealed class Box3DDebugShapeGeometry
    {
        public struct LocalSegment
        {
            public B3Vec3 A, B;
        }

        const int CircleSegments = 16;
        const int ArcSegments = 8;

        public List<LocalSegment> Segments { get; } = new List<LocalSegment>(32);

        /// <summary>Bake from the callback's shape description. Must not throw (native boundary);
        /// on any failure the geometry comes back empty and the shape simply draws as nothing.</summary>
        public static unsafe Box3DDebugShapeGeometry Bake(B3DebugShape* shape)
        {
            var geometry = new Box3DDebugShapeGeometry();
            try
            {
                switch (shape->Type)
                {
                    case B3ShapeType.Sphere:
                    {
                        var sphere = (B3Sphere*)shape->Geometry;
                        geometry.BakeSphere(sphere->Center, sphere->Radius);
                        break;
                    }
                    case B3ShapeType.Capsule:
                    {
                        var capsule = (B3Capsule*)shape->Geometry;
                        geometry.BakeCapsule(capsule->Center1, capsule->Center2, capsule->Radius);
                        break;
                    }
                    case B3ShapeType.Hull:
                    {
                        var hull = (B3HullData*)shape->Geometry;
                        geometry.BakeAabb(hull->Aabb);
                        break;
                    }
                    // Mesh / Compound / Height: not baked yet — drawn as nothing (see class doc).
                }
            }
            catch
            {
                geometry.Segments.Clear();
            }
            return geometry;
        }

        void Add(B3Vec3 a, B3Vec3 b) => Segments.Add(new LocalSegment { A = a, B = b });

        void BakeSphere(B3Vec3 center, float radius)
        {
            BakeCircle(center, new B3Vec3(1, 0, 0), new B3Vec3(0, 1, 0), radius);
            BakeCircle(center, new B3Vec3(1, 0, 0), new B3Vec3(0, 0, 1), radius);
            BakeCircle(center, new B3Vec3(0, 1, 0), new B3Vec3(0, 0, 1), radius);
        }

        void BakeCapsule(B3Vec3 p1, B3Vec3 p2, float radius)
        {
            B3Vec3 axis = Sub(p2, p1);
            float len = Length(axis);
            B3Vec3 n = len > 1e-9f ? Scale(axis, 1f / len) : new B3Vec3(0, 1, 0);
            Basis(n, out B3Vec3 u, out B3Vec3 v);

            BakeCircle(p1, u, v, radius);
            BakeCircle(p2, u, v, radius);

            Add(AddVec(p1, Scale(u, radius)), AddVec(p2, Scale(u, radius)));
            Add(AddVec(p1, Scale(u, -radius)), AddVec(p2, Scale(u, -radius)));
            Add(AddVec(p1, Scale(v, radius)), AddVec(p2, Scale(v, radius)));
            Add(AddVec(p1, Scale(v, -radius)), AddVec(p2, Scale(v, -radius)));

            B3Vec3 nNeg = Scale(n, -1f);
            BakeArc(p1, u, nNeg, radius);
            BakeArc(p1, v, nNeg, radius);
            BakeArc(p2, u, n, radius);
            BakeArc(p2, v, n, radius);
        }

        void BakeAabb(in B3AABB aabb)
        {
            Span<B3Vec3> corners = stackalloc B3Vec3[8];
            for (int i = 0; i < 8; i++)
                corners[i] = new B3Vec3(
                    (i & 1) != 0 ? aabb.UpperBound.X : aabb.LowerBound.X,
                    (i & 2) != 0 ? aabb.UpperBound.Y : aabb.LowerBound.Y,
                    (i & 4) != 0 ? aabb.UpperBound.Z : aabb.LowerBound.Z);

            Add(corners[0], corners[1]); Add(corners[0], corners[2]); Add(corners[0], corners[4]);
            Add(corners[1], corners[3]); Add(corners[1], corners[5]); Add(corners[2], corners[3]);
            Add(corners[2], corners[6]); Add(corners[3], corners[7]); Add(corners[4], corners[5]);
            Add(corners[4], corners[6]); Add(corners[5], corners[7]); Add(corners[6], corners[7]);
        }

        void BakeCircle(B3Vec3 center, B3Vec3 u, B3Vec3 v, float radius)
        {
            B3Vec3 prev = AddVec(center, Scale(u, radius));
            for (int i = 1; i <= CircleSegments; i++)
            {
                float t = i * (2f * MathF.PI / CircleSegments);
                B3Vec3 next = AddVec(center, AddVec(Scale(u, radius * MathF.Cos(t)), Scale(v, radius * MathF.Sin(t))));
                Add(prev, next);
                prev = next;
            }
        }

        void BakeArc(B3Vec3 center, B3Vec3 u, B3Vec3 w, float radius)
        {
            B3Vec3 prev = AddVec(center, Scale(u, radius));
            for (int i = 1; i <= ArcSegments; i++)
            {
                float t = i * (MathF.PI / ArcSegments);
                B3Vec3 next = AddVec(center, AddVec(Scale(u, radius * MathF.Cos(t)), Scale(w, radius * MathF.Sin(t))));
                Add(prev, next);
                prev = next;
            }
        }

        static B3Vec3 Sub(in B3Vec3 a, in B3Vec3 b) => new B3Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        static B3Vec3 AddVec(in B3Vec3 a, in B3Vec3 b) => new B3Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        static B3Vec3 Scale(in B3Vec3 v, float s) => new B3Vec3(v.X * s, v.Y * s, v.Z * s);

        static float Length(in B3Vec3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

        static B3Vec3 Cross(in B3Vec3 a, in B3Vec3 b)
            => new B3Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        static void Basis(in B3Vec3 n, out B3Vec3 u, out B3Vec3 v)
        {
            B3Vec3 reference = MathF.Abs(n.Y) < 0.99f ? new B3Vec3(0, 1, 0) : new B3Vec3(1, 0, 0);
            B3Vec3 c = Cross(reference, n);
            u = Scale(c, 1f / Length(c));
            v = Cross(n, u);
        }
    }
}
