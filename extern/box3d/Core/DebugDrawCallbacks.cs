// Native->managed shims for b3World_Draw. Same conventions as QueryCallbacks: static
// [UnmanagedCallersOnly] methods (works on .NET, IL2CPP, and Unity 6 Mono). Unlike the query
// shims, output size is unknowable up front, so the context pointer carries a GCHandle to a
// managed Box3DDebugSnapshot instead of a stack buffer — debug draw is allowed to allocate.
// Color parameters are b3HexColor (a C enum: 4 bytes, 0xRRGGBB) received as uint.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Box3D.Interop;

namespace Box3D
{
    internal static unsafe class DebugDrawCallbacks
    {
        /// <summary>Point every callback in <paramref name="draw"/> at the shims below.</summary>
        internal static void Install(ref B3DebugDraw draw)
        {
            draw.DrawShapeFcn = (IntPtr)(delegate* unmanaged[Cdecl]<void*, B3WorldTransform, uint, void*, byte>)&DrawShape;
            draw.DrawSegmentFcn = (IntPtr)(delegate* unmanaged[Cdecl]<B3Pos, B3Pos, uint, void*, void>)&DrawSegment;
            draw.DrawTransformFcn = (IntPtr)(delegate* unmanaged[Cdecl]<B3WorldTransform, void*, void>)&DrawTransform;
            draw.DrawPointFcn = (IntPtr)(delegate* unmanaged[Cdecl]<B3Pos, float, uint, void*, void>)&DrawPoint;
            draw.DrawSphereFcn = (IntPtr)(delegate* unmanaged[Cdecl]<B3Pos, float, uint, float, void*, void>)&DrawSphere;
            draw.DrawCapsuleFcn = (IntPtr)(delegate* unmanaged[Cdecl]<B3Pos, B3Pos, float, uint, float, void*, void>)&DrawCapsule;
            draw.DrawBoundsFcn = (IntPtr)(delegate* unmanaged[Cdecl]<B3AABB, uint, void*, void>)&DrawBounds;
            draw.DrawBoxFcn = (IntPtr)(delegate* unmanaged[Cdecl]<B3Vec3, B3WorldTransform, uint, void*, void>)&DrawBox;
            draw.DrawStringFcn = (IntPtr)(delegate* unmanaged[Cdecl]<B3Pos, byte*, uint, void*, void>)&DrawString;
        }

        static Box3DDebugSnapshot Target(void* context)
            => (Box3DDebugSnapshot)GCHandle.FromIntPtr((IntPtr)context).Target;

        // Shape geometry arrives ONLY through this callback: userShape is the GCHandle to the
        // Box3DDebugShapeGeometry baked by DebugShapeCallbacks.CreateShape (worlds created with
        // debugShapes: true). Worlds without the hooks never reach here — they draw no shapes.
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static byte DrawShape(void* userShape, B3WorldTransform transform, uint color, void* context)
        {
            if (userShape != null && GCHandle.FromIntPtr((IntPtr)userShape).Target is Box3DDebugShapeGeometry geometry)
                Target(context).AddShapeGeometry(geometry, in transform, color);
            return 1;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void DrawSegment(B3Pos p1, B3Pos p2, uint color, void* context)
            => Target(context).AddSegment(p1, p2, color);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void DrawTransform(B3WorldTransform transform, void* context)
            => Target(context).AddTransform(in transform);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void DrawPoint(B3Pos p, float size, uint color, void* context)
            => Target(context).AddPoint(p, size, color);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void DrawSphere(B3Pos p, float radius, uint color, float alpha, void* context)
            => Target(context).AddSphere(p, radius, color, alpha);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void DrawCapsule(B3Pos p1, B3Pos p2, float radius, uint color, float alpha, void* context)
            => Target(context).AddCapsule(p1, p2, radius, color, alpha);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void DrawBounds(B3AABB aabb, uint color, void* context)
            => Target(context).AddBounds(in aabb, color);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void DrawBox(B3Vec3 extents, B3WorldTransform transform, uint color, void* context)
            => Target(context).AddBox(extents, in transform, color);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void DrawString(B3Pos p, byte* s, uint color, void* context)
            => Target(context).AddLabel(p, Marshal.PtrToStringAnsi((IntPtr)s), color);
    }
}
