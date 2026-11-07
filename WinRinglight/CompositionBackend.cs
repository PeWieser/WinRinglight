using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Color = Windows.UI.Color;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace WinRinglight;

/// <summary>Standalone Composition overlay experiment; not the production rendering backend.</summary>
internal sealed class CompositionBackend : IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExTopmost = 0x8, WsExTransparent = 0x20, WsExToolWindow = 0x80;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x2;
    private const int WsExNoActivate = 0x08000000, WsExNoRedirectionBitmap = 0x00200000;
    private const uint WdaNone = 0, WdaExcludeFromCapture = 0x11;
    private static readonly Guid DesktopInteropId = new("29E691FA-4567-4DCA-B319-D0F207EB6807");
    private static IntPtr _queueController; // Keep the UI-thread queue alive for the application lifetime.
    private readonly List<CompositionObject> _objects = new();
    private readonly List<CompositionObject> _ringObjects = new();
    private SpriteVisual? _result;
    private readonly List<CompositionColorBrush> _haloBrushes = new();
    private CompositionColorBrush? _coreBrush;
    private double _currentThickness;
    private float _dpiScale = 1;
    private CompositionRoundedRectangleGeometry? _geometry;
    private bool _currentRounded;
    private IntPtr _hwnd;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private ShapeVisual? _ring;
    private CompositionRadialGradientBrush? _gradient;
    private readonly int _left, _top, _width, _height;
    private int _lastX = int.MinValue, _lastY = int.MinValue;

    public CompositionBackend(System.Drawing.Rectangle bounds, float thickness, bool rounded,
        double kelvin, bool excludeCapture, bool visible, float cursorRadius)
    {
        _left = bounds.Left; _top = bounds.Top; _width = bounds.Width; _height = bounds.Height;
        try
        {
            EnsureDispatcherQueue();
            _hwnd = CreateWindowEx(WsExTopmost | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExNoRedirectionBitmap | WsExLayered,
                "static", "Composition prototype overlay", WsPopup, _left, _top, _width, _height,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            // Layered + transparent causes mouse hit-testing to pass through
            // this window, including to windows owned by other processes.
            // This is experimental with a NOREDIRECTIONBITMAP Composition host.
            if (!SetLayeredWindowAttributes(_hwnd, 0, 255, LwaAlpha))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            uint dpi = GetDpiForWindow(_hwnd);
            _dpiScale = dpi > 0 ? dpi / 96f : 1f;

            // Apply exclusion before showing any overlay pixels.
            if (excludeCapture && !SetCaptureExcluded(true))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Capture exclusion failed");
            _compositor = new Compositor();
            _target = CreateTarget(_compositor, _hwnd);
            Vector2 size = new(_width, _height);
            _ring = Keep(_compositor.CreateShapeVisual());
            _ring.Size = size;
            SetRing(thickness, rounded, kelvin);
            var ringSurface = Keep(_compositor.CreateVisualSurface());
            ringSurface.SourceVisual = _ring;
            ringSurface.SourceSize = size;
            var ringBrush = Keep(_compositor.CreateSurfaceBrush(ringSurface));
            ringBrush.Stretch = CompositionStretch.None;

            _gradient = Keep(_compositor.CreateRadialGradientBrush());
            _gradient.MappingMode = CompositionMappingMode.Absolute;
            _gradient.EllipseRadius = new Vector2(cursorRadius);
            _gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(0, Color.FromArgb(0, 255, 255, 255))));
            _gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(0.2f, Color.FromArgb(0, 255, 255, 255))));
            _gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(1, Color.FromArgb(255, 255, 255, 255))));
            var maskVisual = Keep(_compositor.CreateSpriteVisual());
            maskVisual.Size = size;
            maskVisual.Brush = _gradient;
            var maskSurface = Keep(_compositor.CreateVisualSurface());
            maskSurface.SourceVisual = maskVisual;
            maskSurface.SourceSize = size;
            var maskBrush = Keep(_compositor.CreateSurfaceBrush(maskSurface));
            maskBrush.Stretch = CompositionStretch.None;
            var composite = Keep(_compositor.CreateMaskBrush());
            composite.Source = ringBrush;
            composite.Mask = maskBrush;
            _result = Keep(_compositor.CreateSpriteVisual());
            _result.Size = size;
            _result.Brush = composite;
            _result.Opacity = visible ? 1 : 0;
            _target.Root = _result;
            UpdateCursor();
            ShowWindow(_hwnd, 4); // SW_SHOWNOACTIVATE
        }
        catch { Dispose(); throw; }
    }

    private T Keep<T>(T value) where T : CompositionObject { _objects.Add(value); return value; }

    private T KeepRing<T>(T value) where T : CompositionObject { _ringObjects.Add(value); return value; }

    public void SetRing(float thickness, bool rounded, double kelvin, bool animateStyle = false)
    {
        if (_compositor == null || _ring == null) return;
        thickness *= _dpiScale;
        if (_coreBrush != null && _currentThickness == thickness)
        {
            if (_currentRounded != rounded && _geometry != null)
            {
                _currentRounded = rounded;
                SetGeometry(_geometry, thickness, rounded, animateStyle);
            }
            UpdateColor(kelvin);
            return;
        }
        _ring.Shapes.Clear();
        _haloBrushes.Clear();
        _coreBrush = null;
        _currentThickness = thickness;
        _currentRounded = rounded;
        for (int i = _ringObjects.Count - 1; i >= 0; i--) _ringObjects[i].Dispose();
        _ringObjects.Clear();
        var geometry = KeepRing(_compositor.CreateRoundedRectangleGeometry());
        _geometry = geometry;
        SetGeometry(geometry, thickness, rounded, false);
        var color = ToColor(kelvin);
        float blur = Math.Min(40, thickness * 0.4f);
        for (int i = 12; i >= 1; i--)
        {
            float fraction = i / 12f;
            byte alpha = (byte)Math.Round(255 * 0.55 * 0.035 * (1 - fraction * 0.75));
            var brush = KeepRing(_compositor.CreateColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)));
            _haloBrushes.Add(brush);
            var shape = KeepRing(_compositor.CreateSpriteShape(geometry));
            shape.StrokeBrush = brush;
            shape.StrokeThickness = thickness + 2 * blur * fraction;
            _ring.Shapes.Add(shape);
        }
        var core = KeepRing(_compositor.CreateSpriteShape(geometry));
        _coreBrush = KeepRing(_compositor.CreateColorBrush(color));
        core.StrokeBrush = _coreBrush;
        core.StrokeThickness = thickness;
        _ring.Shapes.Add(core);
    }

    private void SetGeometry(CompositionRoundedRectangleGeometry geometry, float thickness, bool rounded, bool animate)
    {
        float inset = rounded ? 20 * _dpiScale : 0;
        float radius = rounded ? Math.Min(_width, _height) * 0.22f : 3 * _dpiScale;
        var offset = new Vector2(inset + thickness / 2);
        var size = new Vector2(Math.Max(0, _width - inset * 2 - thickness), Math.Max(0, _height - inset * 2 - thickness));
        if (!animate || _compositor == null)
        {
            geometry.Offset = offset;
            geometry.Size = size;
        }
        else
        {
            AnimateVector(geometry, "Offset", offset);
            AnimateVector(geometry, "Size", size);
        }
        // The original WPF corner radius changes immediately on a style switch.
        geometry.CornerRadius = new Vector2(radius);
    }

    private void AnimateVector(CompositionObject target, string property, Vector2 to)
    {
        if (_compositor == null) return;
        var animation = _compositor.CreateVector2KeyFrameAnimation();
        var easing = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.455f, 0.03f), new Vector2(0.515f, 0.955f));
        animation.InsertKeyFrame(1, to, easing);
        animation.Duration = TimeSpan.FromSeconds(0.4);
        target.StartAnimation(property, animation);
        animation.Dispose();
        easing.Dispose();
    }

    private static Color ToColor(double kelvin)
    {
        double t = (kelvin - 2000) / 4500.0;
        return Color.FromArgb(255, (byte)(255 - 20 * t), (byte)(170 + 75 * t), (byte)(50 + 205 * t));
    }

    private void UpdateColor(double kelvin)
    {
        var color = ToColor(kelvin);
        if (_coreBrush != null) _coreBrush.Color = color;
        for (int i = 0; i < _haloBrushes.Count; i++)
        {
            byte alpha = _haloBrushes[i].Color.A;
            _haloBrushes[i].Color = Color.FromArgb(alpha, color.R, color.G, color.B);
        }
    }

    public void SetVisible(bool visible)
    {
        if (_result == null || _compositor == null) return;
        var animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, visible ? 1 : 0);
        animation.Duration = TimeSpan.FromSeconds(0.4);
        _result.StartAnimation("Opacity", animation);
        animation.Dispose();
    }

    public void UpdateCursor()
    {
        if (_gradient == null || !GetCursorPos(out Point p) || (p.X == _lastX && p.Y == _lastY)) return;
        _lastX = p.X; _lastY = p.Y;
        _gradient.EllipseCenter = new Vector2(p.X - _left, p.Y - _top);
    }

    public bool SetCaptureExcluded(bool value) => _hwnd != IntPtr.Zero && SetWindowDisplayAffinity(_hwnd, value ? WdaExcludeFromCapture : WdaNone);

    public void Dispose()
    {
        if (_target != null) { _target.Root = null; _target.Dispose(); _target = null; }
        if (_ring != null) _ring.Shapes.Clear();
        for (int i = _ringObjects.Count - 1; i >= 0; i--) _ringObjects[i].Dispose();
        _ringObjects.Clear();
        for (int i = _objects.Count - 1; i >= 0; i--) _objects[i].Dispose();
        _objects.Clear();
        _gradient = null; _ring = null; _result = null; _geometry = null;
        _haloBrushes.Clear(); _coreBrush = null;
        _compositor?.Dispose(); _compositor = null;
        if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }

    private static void EnsureDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null || _queueController != IntPtr.Zero) return;
        var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 2 };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _queueController));
    }

    private static unsafe DesktopWindowTarget CreateTarget(Compositor compositor, IntPtr hwnd)
    {
        IntPtr unknown = MarshalInspectable<Compositor>.FromManaged(compositor);
        try
        {
            Guid id = DesktopInteropId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in id, out IntPtr interop));
            try
            {
                var vtable = *(IntPtr**)interop;
                var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)vtable[3];
                IntPtr target;
                Marshal.ThrowExceptionForHR(create(interop, hwnd, 0, &target));
                try { return DesktopWindowTarget.FromAbi(target); }
                finally { Marshal.Release(target); }
            }
            finally { Marshal.Release(interop); }
        }
        finally { Marshal.Release(unknown); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
