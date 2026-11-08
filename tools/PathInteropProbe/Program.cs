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
            var loops = DisplayOutline.BuildLoops(new[] { new Rectangle(100, 20, 120, 160), new Rectangle(20, 60, 280, 80) });
            var source = GeometrySource.Create(loops);
            var path = new CompositionPath(source);
            using var geometry = compositor.CreatePathGeometry(path);
            using var brush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            using var shape = compositor.CreateSpriteShape(geometry);
            shape.StrokeBrush = brush; shape.StrokeThickness = 8;
            ring.Shapes.Clear(); ring.Shapes.Add(shape);
            Pump(1000); DwmFlush();
            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                uint outer = GetPixel(dc, bounds.Left + 50, bounds.Top + 60);
                uint upper = GetPixel(dc, bounds.Left + 160, bounds.Top + 20);
                uint inner = GetPixel(dc, bounds.Left + 100, bounds.Top + 100);
                static int Sum(uint p) => (int)(p & 255) + (int)((p >> 8) & 255) + (int)((p >> 16) & 255);
                if (Sum(outer) < 600 || Sum(upper) < 600 || Sum(inner) > 60)
                    throw new Exception($"Composition path pixels invalid: outer={outer:X}, upper={upper:X}, inner={inner:X}.");
                Console.WriteLine("PASS: Native Direct2D union geometry is stroked by Composition with no internal edge.");
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            ring.Shapes.Clear();
            GC.KeepAlive(source);
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
