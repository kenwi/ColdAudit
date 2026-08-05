// Native->managed shims for the world's debug-shape hooks (b3WorldDef.createDebugShape /
// destroyDebugShape). These are installed at WORLD CREATION (that is the only time box3d reads
// them) and fire lazily during the first b3World_Draw: box3d asks us to bake a drawable handle
// per shape, stores it on the shape, and hands it back through b3DebugDraw.DrawShapeFcn every
// draw. The handle is a GCHandle to a Box3DDebugShapeGeometry; DestroyShape frees it when the
// shape or world dies. Shims must never throw across the native boundary — Bake guards itself.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Box3D.Interop;

namespace Box3D
{
    internal static unsafe class DebugShapeCallbacks
    {
        /// <summary>Install the bake/free hooks into a world definition (pre-creation only).</summary>
        internal static void Install(ref B3WorldDef def)
        {
            def.CreateDebugShape = (IntPtr)(delegate* unmanaged[Cdecl]<B3DebugShape*, void*, void*>)&CreateShape;
            def.DestroyDebugShape = (IntPtr)(delegate* unmanaged[Cdecl]<void*, void*, void>)&DestroyShape;
            def.UserDebugShapeContext = IntPtr.Zero;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void* CreateShape(B3DebugShape* debugShape, void* userContext)
            => (void*)GCHandle.ToIntPtr(GCHandle.Alloc(Box3DDebugShapeGeometry.Bake(debugShape)));

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        static void DestroyShape(void* userShape, void* userContext)
        {
            if (userShape != null)
                GCHandle.FromIntPtr((IntPtr)userShape).Free();
        }
    }
}
