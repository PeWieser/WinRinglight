using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.UI.Composition;
using WinRinglight;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        if (work.Width < 600 || work.Height < 400) { Console.WriteLine("SKIP: Desktop too small for Composition path probe."); return 0; }
        var bounds = new Rectangle(work.Left + 60, work.Top + 60, 320, 200);
        var background = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize,
            Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height, Topmost = true, Background = System.Windows.Media.Brushes.Black };
        object? backend = null;
        try
        {
            background.Show(); Pump(200);
            var type = typeof(MainWindow).Assembly.GetType("WinRinglight.CompositionBackend")!;
            backend = Activator.CreateInstance(type, new object?[] { bounds, bounds, 30f, true, 4000d, false, true, 2f, null, false, false, 1f, false })!;
            type.GetMethod("UpdateCursorAt")!.Invoke(backend, new object[] { bounds.Right + 100, bounds.Bottom + 100 });
            var compositor = (Compositor)type.GetField("_compositor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(backend)!;
            var ring = (ShapeVisual)type.GetField("_ring", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(backend)!;
            var outline = RoundedDisplayOutline.Create(new[] { new Rectangle(100, 20, 120, 160), new Rectangle(20, 60, 280, 80) }, 20);
            var source = GeometrySource.Create(outline, out IntPtr nativeGeometry);
            var path = new CompositionPath(source);
            using var geometry = compositor.CreatePathGeometry(path);
            using var brush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            using var shape = compositor.CreateSpriteShape(geometry);
            shape.StrokeBrush = brush; shape.StrokeThickness = 8;
            ring.Shapes.Clear(); ring.Shapes.Add(shape);
            source = null!;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Pump(1000); DwmFlush();
            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                uint outer = GetPixel(dc, bounds.Left + 50, bounds.Top + 60);
                uint upper = GetPixel(dc, bounds.Left + 160, bounds.Top + 20);
                uint inner = GetPixel(dc, bounds.Left + 100, bounds.Top + 100);
                uint corner = GetPixel(dc, bounds.Left + 100, bounds.Top + 20);
                static int Sum(uint p) => (int)(p & 255) + (int)((p >> 8) & 255) + (int)((p >> 16) & 255);
                if (Sum(outer) < 600 || Sum(upper) < 600 || Sum(inner) > 60 || Sum(corner) > 60)
                    throw new Exception($"Composition path pixels invalid: outer={outer:X}, upper={upper:X}, inner={inner:X}, rounded-corner={corner:X}.");
                Console.WriteLine("PASS: Rounded Direct2D union geometry is stroked by Composition with no internal edge after GC.");
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            type.GetMethod("UpdateCursorAt")!.Invoke(backend, new object[] { bounds.Left + 50, bounds.Top + 60 });
            Pump(500); DwmFlush();
            dc = GetDC(IntPtr.Zero);
            uint cut;
            try { cut = GetPixel(dc, bounds.Left + 50, bounds.Top + 60); }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            if ((cut & 0xffffff) > 0x202020) throw new Exception($"Cursor mask did not cut Composition path: {cut:X}.");
            type.GetMethod("UpdateCursorAt")!.Invoke(backend, new object[] { bounds.Right + 100, bounds.Bottom + 100 });
            Pump(500); DwmFlush();
            dc = GetDC(IntPtr.Zero);
            uint restored;
            try { restored = GetPixel(dc, bounds.Left + 50, bounds.Top + 60); }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            if ((restored & 0xffffff) < 0xc0c0c0) throw new Exception($"Composition path did not restore after cursor moved: {restored:X}.");
            Console.WriteLine("PASS: Composition path respects cursor mask and restores after movement.");
            try
            {
                // Independent 2x Direct2D path surface, displayed at 1x, uses
                // the exact same geometry object as the Composition prototype.
                using var raster = new Direct2DRasterProbe(compositor, bounds.Width, bounds.Height, 2, pathGeometry: nativeGeometry);
                using var surfaceBrush = compositor.CreateSurfaceBrush(raster.Surface);
                surfaceBrush.Stretch = CompositionStretch.Fill;
                surfaceBrush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
                var result = (SpriteVisual)type.GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
                result.Brush = surfaceBrush;
                Pump(800); DwmFlush();
                dc = GetDC(IntPtr.Zero);
                try
                {
                    uint edge = GetPixel(dc, bounds.Left + 50, bounds.Top + 60);
                    uint voidPixel = GetPixel(dc, bounds.Left + 100, bounds.Top + 100);
                    uint roundedCorner = GetPixel(dc, bounds.Left + 100, bounds.Top + 20);
                    if ((edge & 0xffffff) < 0xc0c0c0 || (voidPixel & 0xffffff) > 0x202020 || (roundedCorner & 0xffffff) > 0x202020)
                        throw new Exception($"Direct2D 2x path pixels invalid: edge={edge:X}, inner={voidPixel:X}, corner={roundedCorner:X}.");
                    Console.WriteLine("PASS: Direct2D 2x surface renders the same rounded union path without inner edge.");
                }
                finally { ReleaseDC(IntPtr.Zero, dc); }
            }
            finally { Marshal.Release(nativeGeometry); }
            ring.Shapes.Clear();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally { if (backend is IDisposable d) d.Dispose(); background.Close(); app.Shutdown(); }
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
