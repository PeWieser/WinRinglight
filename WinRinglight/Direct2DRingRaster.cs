using System;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.Graphics.DirectX;
using WinRT;

// Device-backed Direct2D surfaces for the experimental Composition ring.
namespace WinRinglight;

internal sealed unsafe class Direct2DRingRaster : IDisposable
{
    private static readonly Guid DxgiDeviceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid CompositorInteropId = new("25297d5c-3ad4-4c9c-b5cf-e36a38512330");
    private static readonly Guid SurfaceInteropId = new("fd04e6e3-fe0c-4c3c-ab19-a07601a576ee");
    private static readonly Guid DeviceContextId = new("e8f7fe7a-191c-466d-ad95-975678bda998");
    private IntPtr _d3d, _context, _dxgi, _d2d;
    private CompositionGraphicsDevice? _device;
    public bool UsingSoftwareDevice { get; private set; }

    public Direct2DRingRaster(Compositor compositor)
    {
        try
        {
            // D3D11_CREATE_DEVICE_BGRA_SUPPORT. Hosted runners may need WARP.
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out _d3d, out _, out _context);
            if (hr < 0)
            {
                UsingSoftwareDevice = true;
                hr = D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out _d3d, out _, out _context);
            }
            Marshal.ThrowExceptionForHR(hr);
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(_d3d, in DxgiDeviceId, out _dxgi));
            Marshal.ThrowExceptionForHR(D2D1CreateDevice(_dxgi, IntPtr.Zero, out _d2d));
            IntPtr unk = MarshalInspectable<Compositor>.FromManaged(compositor);
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unk, in CompositorInteropId, out IntPtr interop));
                try
                {
                    var vtable = *(IntPtr**)interop;
                    var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)vtable[5];
                    IntPtr ptr = IntPtr.Zero;
                    Marshal.ThrowExceptionForHR(create(interop, _d2d, &ptr));
                    try { _device = CompositionGraphicsDevice.FromAbi(ptr); }
                    finally { if (ptr != IntPtr.Zero) Marshal.Release(ptr); }
                }
                finally { Marshal.Release(interop); }
            }
            finally { Marshal.Release(unk); }

        }
        catch { Dispose(); throw; }
    }

    public CompositionDrawingSurface Draw(int width, int height, int scale, float originX, float originY,
        float left, float top, float right, float bottom, float radius, float thickness, float dpi, Windows.UI.Color color)
    {
        var surface = _device!.CreateDrawingSurface(new Windows.Foundation.Size(width * scale, height * scale),
            DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
        try
        {
            IntPtr unk = MarshalInspectable<CompositionDrawingSurface>.FromManaged(surface);
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unk, in SurfaceInteropId, out IntPtr interop));
                try
                {
                    var vtable = *(IntPtr**)interop;
                    var begin = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, NativePoint*, int>)vtable[3];
                    var end = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtable[4];
                    var iid = DeviceContextId; IntPtr dc = IntPtr.Zero; NativePoint offset = default;
                    Marshal.ThrowExceptionForHR(begin(interop, IntPtr.Zero, &iid, &dc, &offset));
                    try
                    {
                        var vt = *(IntPtr**)dc;
                        var clear = (delegate* unmanaged[Stdcall]<IntPtr, ColorF*, void>)vt[47];
                        var transform = (delegate* unmanaged[Stdcall]<IntPtr, Matrix*, void>)vt[30];
                        ColorF transparent = default; clear(dc, &transparent);
                        Matrix matrix = new(scale, 0, 0, scale, offset.X - originX * scale, offset.Y - originY * scale);
                        transform(dc, &matrix);
                        float half = thickness / 2, blur = Math.Min(40, thickness * 0.4f);
                        var outer = new RoundedRect(left - half, top - half, right + half, bottom + half, radius + half);
                        var inner = new RoundedRect(left + half, top + half, Math.Max(left + half, right - half), Math.Max(top + half, bottom - half), Math.Max(0, radius - half));
                        for (int i = 12; i >= 1; i--)
                        {
                            float fraction = i / 12f;
                            float alpha = (float)Math.Round(255 * 0.55 * 0.035 * (1 - fraction * 0.75)) / 255f;
                            StrokePair(dc, outer, inner, 2 * blur * fraction, alpha, color);
                        }
                        foreach (var edge in new[] { (width: 3f, alpha: 30), (width: 2f, alpha: 65), (width: 1f, alpha: 110) })
                        {
                            StrokePair(dc, outer, inner, edge.width * dpi, edge.alpha / 255f, color);
                        }
                        Stroke(dc, new RoundedRect(left, top, right, bottom, radius), thickness, 1, color);
                    }
                    finally { if (dc != IntPtr.Zero) Marshal.Release(dc); Marshal.ThrowExceptionForHR(end(interop)); }
                }
                finally { Marshal.Release(interop); }
            }
            finally { Marshal.Release(unk); }
            return surface;
        }
        catch { surface.Dispose(); throw; }
    }

    // Both contours have the same color and opacity. Reuse one device brush
    // for the pair; this changes neither geometry nor draw order.
    private static void StrokePair(IntPtr dc, RoundedRect outer, RoundedRect inner, float width, float alpha, Windows.UI.Color ringColor)
    {
        var vt = *(IntPtr**)dc;
        var create = (delegate* unmanaged[Stdcall]<IntPtr, ColorF*, IntPtr, IntPtr*, int>)vt[8];
        var draw = (delegate* unmanaged[Stdcall]<IntPtr, RoundedRect*, IntPtr, float, IntPtr, void>)vt[18];
        ColorF color = new(ringColor.R / 255f, ringColor.G / 255f, ringColor.B / 255f, alpha);
        IntPtr brush = IntPtr.Zero;
        Marshal.ThrowExceptionForHR(create(dc, &color, IntPtr.Zero, &brush));
        try { draw(dc, &outer, brush, width, IntPtr.Zero); draw(dc, &inner, brush, width, IntPtr.Zero); }
        finally { if (brush != IntPtr.Zero) Marshal.Release(brush); }
    }

    private static void Stroke(IntPtr dc, RoundedRect rect, float width, float alpha, Windows.UI.Color ringColor)
    {
        var vt = *(IntPtr**)dc;
        var create = (delegate* unmanaged[Stdcall]<IntPtr, ColorF*, IntPtr, IntPtr*, int>)vt[8];
        var draw = (delegate* unmanaged[Stdcall]<IntPtr, RoundedRect*, IntPtr, float, IntPtr, void>)vt[18];
        ColorF color = new(ringColor.R / 255f, ringColor.G / 255f, ringColor.B / 255f, alpha);
        IntPtr brush = IntPtr.Zero;
        Marshal.ThrowExceptionForHR(create(dc, &color, IntPtr.Zero, &brush));
        try { draw(dc, &rect, brush, width, IntPtr.Zero); }
        finally { if (brush != IntPtr.Zero) Marshal.Release(brush); }
    }

    public void Dispose()
    {
        _device?.Dispose(); _device = null;
        if (_d2d != IntPtr.Zero) { Marshal.Release(_d2d); _d2d = IntPtr.Zero; }
        if (_dxgi != IntPtr.Zero) { Marshal.Release(_dxgi); _dxgi = IntPtr.Zero; }
        if (_context != IntPtr.Zero) { Marshal.Release(_context); _context = IntPtr.Zero; }
        if (_d3d != IntPtr.Zero) { Marshal.Release(_d3d); _d3d = IntPtr.Zero; }
    }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativePoint { public readonly int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Matrix(float M11, float M12, float M21, float M22, float M31, float M32);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct ColorF(float R, float G, float B, float A);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct RoundedRect(float Left, float Top, float Right, float Bottom, float RadiusX, float RadiusY)
    {
        public RoundedRect(float l, float t, float r, float b, float radius) : this(l, t, r, b, radius, radius) { }
    }
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software, uint flags, IntPtr levels, uint count, uint sdk, out IntPtr device, out int level, out IntPtr context);
    [DllImport("d2d1.dll")] private static extern int D2D1CreateDevice(IntPtr dxgi, IntPtr creationProperties, out IntPtr d2dDevice);
}
