using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media;
using Windows.Graphics;
using WinRT;

// Isolated experiment: a manually implemented WinRT IGeometrySource2D with
// the documented native IGeometrySource2DInterop interface. Not app code.
internal static unsafe class GeometrySource
{
    private static readonly Guid IUnknownId = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IInspectableId = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
    private static readonly Guid IAgileObjectId = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");
    private static readonly Guid SourceId = new("CAFF7902-670C-4181-A624-DA977203B845");
    private static readonly Guid InteropId = new("0657AF73-53FD-47CF-84FF-C8492D2A80A3");
    private static readonly Guid FactoryId = new("06152247-6F50-465A-9245-118BFD3B6007");
    private static readonly IntPtr InspectableVtable = MakeVtable(false);
    private static readonly IntPtr InteropVtable = MakeVtable(true);

    [StructLayout(LayoutKind.Sequential)] private struct Instance
    {
        public IntPtr InspectableVtable, InspectableOwner;
        public IntPtr InteropVtable, InteropOwner;
        public int References;
        public IntPtr Geometry;
    }
    private static Instance* Owner(IntPtr view) => (Instance*)((IntPtr*)view)[1];
    private static IntPtr MakeVtable(bool interop)
    {
        IntPtr* vt = (IntPtr*)NativeMemory.AllocZeroed((nuint)(interop ? 5 : 6), (nuint)sizeof(IntPtr));
        vt[0] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)&QueryInterface;
        vt[1] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&AddRef;
        vt[2] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint>)&Release;
        if (interop)
        {
            vt[3] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&GetGeometry;
            vt[4] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)&TryGetGeometry;
        }
        else
        {
            vt[3] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint*, IntPtr*, int>)&GetIids;
            vt[4] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)&GetRuntimeClassName;
            vt[5] = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, int*, int>)&GetTrustLevel;
        }
        return (IntPtr)vt;
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int QueryInterface(IntPtr self, Guid* iid, IntPtr* result)
    {
        if (iid == null || result == null) return unchecked((int)0x80004003);
        var o = Owner(self);
        *result = IntPtr.Zero;
        if (*iid == IUnknownId || *iid == IInspectableId || *iid == IAgileObjectId || *iid == SourceId) *result = (IntPtr)o;
        else if (*iid == InteropId) *result = (IntPtr)(&o->InteropVtable);
        else return unchecked((int)0x80004002);
        Interlocked.Increment(ref o->References);
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static uint AddRef(IntPtr self) => (uint)Interlocked.Increment(ref Owner(self)->References);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static uint Release(IntPtr self)
    {
        var o = Owner(self);
        int remaining = Interlocked.Decrement(ref o->References);
        if (remaining == 0)
        {
            if (o->Geometry != IntPtr.Zero) Marshal.Release(o->Geometry);
            NativeMemory.Free(o);
        }
        return (uint)remaining;
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetIids(IntPtr self, uint* count, IntPtr* values)
    {
        if (count == null || values == null) return unchecked((int)0x80004003);
        *count = 1;
        *values = Marshal.AllocCoTaskMem(sizeof(Guid));
        *(Guid*)*values = SourceId;
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetRuntimeClassName(IntPtr self, IntPtr* name) { if (name == null) return unchecked((int)0x80004003); *name = IntPtr.Zero; return 0; }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetTrustLevel(IntPtr self, int* trust) { if (trust == null) return unchecked((int)0x80004003); *trust = 0; return 0; }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int GetGeometry(IntPtr self, IntPtr* result)
    {
        if (result == null) return unchecked((int)0x80004003);
        *result = Owner(self)->Geometry;
        Marshal.AddRef(*result);
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int TryGetGeometry(IntPtr self, IntPtr factory, IntPtr* result)
    { if (result == null) return unchecked((int)0x80004003); *result = IntPtr.Zero; return unchecked((int)0x80004001); }

    public static IGeometrySource2D Create(Geometry source, out IntPtr nativeGeometry)
    {
        nativeGeometry = IntPtr.Zero;
        var flattened = source.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
        var loops = new List<Vector2[]>();
        foreach (var figure in flattened.Figures)
        {
            if (!figure.IsClosed) throw new InvalidOperationException("Only closed outline figures are supported.");
            var points = new List<Vector2> { new((float)figure.StartPoint.X, (float)figure.StartPoint.Y) };
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment lineSegment) points.Add(new((float)lineSegment.Point.X, (float)lineSegment.Point.Y));
                else if (segment is PolyLineSegment polyline)
                    foreach (var point in polyline.Points) points.Add(new((float)point.X, (float)point.Y));
                else throw new InvalidOperationException("Unexpected segment after geometry flattening.");
            }
            loops.Add(points.ToArray());
        }
        Guid factoryId = FactoryId;
        Marshal.ThrowExceptionForHR(D2D1CreateFactory(1, &factoryId, IntPtr.Zero, out IntPtr factory));
        IntPtr geometry = IntPtr.Zero;
        try
        {
            var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)(*(IntPtr**)factory)[10];
            Marshal.ThrowExceptionForHR(create(factory, &geometry));
            var open = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)(*(IntPtr**)geometry)[17];
            IntPtr sink = IntPtr.Zero;
            Marshal.ThrowExceptionForHR(open(geometry, &sink));
            try
            {
                var vt = *(IntPtr**)sink;
                var fillMode = (delegate* unmanaged[Stdcall]<IntPtr, int, void>)vt[3];
                var begin = (delegate* unmanaged[Stdcall]<IntPtr, Vector2, int, void>)vt[5];
                var end = (delegate* unmanaged[Stdcall]<IntPtr, int, void>)vt[8];
                var close = (delegate* unmanaged[Stdcall]<IntPtr, int>)vt[9];
                var line = (delegate* unmanaged[Stdcall]<IntPtr, Vector2, void>)vt[10];
                fillMode(sink, 1); // nonzero winding
                foreach (var loop in loops)
                {
                    if (loop.Length < 3) continue;
                    begin(sink, loop[0], 0);
                    for (int i = 1; i < loop.Length; i++) line(sink, loop[i]);
                    end(sink, 1); // closed
                }
                Marshal.ThrowExceptionForHR(close(sink));
            }
            finally { Marshal.Release(sink); }
            Instance* instance = (Instance*)NativeMemory.AllocZeroed((nuint)sizeof(Instance));
            instance->InspectableVtable = InspectableVtable;
            instance->InspectableOwner = (IntPtr)instance;
            instance->InteropVtable = InteropVtable;
            instance->InteropOwner = (IntPtr)instance;
            instance->References = 1;
            instance->Geometry = geometry;
            geometry = IntPtr.Zero;
            try
            {
                var result = MarshalInterface<IGeometrySource2D>.FromAbi((IntPtr)instance);
                Marshal.AddRef(instance->Geometry);
                nativeGeometry = instance->Geometry;
                return result;
            }
            finally { Marshal.Release((IntPtr)instance); }
        }
        finally { if (geometry != IntPtr.Zero) Marshal.Release(geometry); Marshal.Release(factory); }
    }
    [DllImport("d2d1.dll")] private static extern int D2D1CreateFactory(int factoryType, Guid* iid, IntPtr options, out IntPtr factory);
}
