using System;
using System.Drawing;
using System.Collections.Generic;
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
        var haloObjects = new List<CompositionObject>();
        try
        {
            background.Show(); Pump(200);
            var type = typeof(MainWindow).Assembly.GetType("WinRinglight.CompositionBackend")!;
            backend = Activator.CreateInstance(type, new object?[] { bounds, bounds, 30f, true, 4000d, false, true, 25f, null, false, false, 1f, false })!;
            type.GetMethod("UpdateCursorAt")!.Invoke(backend, new object[] { bounds.Right + 100, bounds.Bottom + 100 });
            var compositor = (Compositor)type.GetField("_compositor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(backend)!;
            var ring = (ShapeVisual)type.GetField("_ring", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(backend)!;
            var layers = DisplayOutlineLayers.Create(new[] { new Rectangle(100, 20, 120, 160), new Rectangle(20, 60, 280, 80) }, 20, 1, 8);
            var outline = layers.Core;
            var source = CompositionGeometryInterop.Create(outline);
            var path = new CompositionPath(source);
            using var geometry = compositor.CreatePathGeometry(path);
            using var brush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            using var shape = compositor.CreateSpriteShape(geometry);
            shape.StrokeBrush = brush; shape.StrokeThickness = 8;
            ring.Shapes.Clear();
            using var outerGeometry = compositor.CreatePathGeometry(new CompositionPath(CompositionGeometryInterop.Create(layers.Outer)));
            using var innerGeometry = compositor.CreatePathGeometry(new CompositionPath(CompositionGeometryInterop.Create(layers.Inner)));
            void AddContours(float width, byte opacity)
            {
                var haloBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(opacity, 255, 255, 255));
                haloObjects.Add(haloBrush);
                foreach (var contour in new[] { outerGeometry, innerGeometry })
                {
                    var halo = compositor.CreateSpriteShape(contour);
                    halo.StrokeBrush = haloBrush; halo.StrokeThickness = width;
                    haloObjects.Add(halo); ring.Shapes.Add(halo);
                }
            }
            for (int i = 12; i >= 1; i--)
            {
                float fraction = i / 12f;
                byte opacity = (byte)Math.Round(255 * 0.55 * 0.035 * (1 - fraction * 0.75));
                AddContours(2 * 3.2f * fraction, opacity);
            }
            AddContours(3, 30); AddContours(2, 65); AddContours(1, 110);
            ring.Shapes.Add(shape);
            source = null!;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Pump(1000); DwmFlush();
            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                uint outer = GetPixel(dc, bounds.Left + 50, bounds.Top + 65);
                uint upper = GetPixel(dc, bounds.Left + 160, bounds.Top + 25);
                uint inner = GetPixel(dc, bounds.Left + 100, bounds.Top + 100);
                uint corner = GetPixel(dc, bounds.Left + 105, bounds.Top + 25);
                static int Sum(uint p) => (int)(p & 255) + (int)((p >> 8) & 255) + (int)((p >> 16) & 255);
                if (Sum(outer) < 600 || Sum(upper) < 600 || Sum(inner) > 60 || Sum(corner) > 60)
                    throw new Exception($"Composition path pixels invalid: outer={outer:X}, upper={upper:X}, inner={inner:X}, rounded-corner={corner:X}.");
                Console.WriteLine("PASS: Inset rounded Composition path with separate core/glow contours has no internal edge after GC.");
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            type.GetMethod("UpdateCursorAt")!.Invoke(backend, new object[] { bounds.Left + 50, bounds.Top + 65 });
            Pump(500); DwmFlush();
            dc = GetDC(IntPtr.Zero);
            uint cut;
            try { cut = GetPixel(dc, bounds.Left + 50, bounds.Top + 65); }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            if ((cut & 0xffffff) > 0x202020) throw new Exception($"Cursor mask did not cut Composition path: {cut:X}.");
            type.GetMethod("UpdateCursorAt")!.Invoke(backend, new object[] { bounds.Right + 100, bounds.Bottom + 100 });
            Pump(500); DwmFlush();
            dc = GetDC(IntPtr.Zero);
            uint restored;
            try { restored = GetPixel(dc, bounds.Left + 50, bounds.Top + 65); }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            if ((restored & 0xffffff) < 0xc0c0c0) throw new Exception($"Composition path did not restore after cursor moved: {restored:X}.");
            Console.WriteLine("PASS: Composition path respects cursor mask and restores after movement.");
            var textCutout = type.GetMethod("UpdateTextCutout")!;
            void VerifySelection()
            {
                textCutout.Invoke(backend, new object?[] { new System.Drawing.Point(bounds.Left + 250, bounds.Top + 65),
                    new Rectangle(bounds.Left + 40, bounds.Top + 57, 240, 16) });
                Pump(500); DwmFlush();
                IntPtr selectionDc = GetDC(IntPtr.Zero);
                uint left, right;
                try { left = GetPixel(selectionDc, bounds.Left + 50, bounds.Top + 65); right = GetPixel(selectionDc, bounds.Left + 250, bounds.Top + 65); }
                finally { ReleaseDC(IntPtr.Zero, selectionDc); }
                if ((left & 0xffffff) > 0x202020 || (right & 0xffffff) > 0x202020)
                    throw new Exception($"Path selection mask failed: {left:X}/{right:X}.");
                textCutout.Invoke(backend, new object?[] { null, null });
                Pump(500); DwmFlush();
            }
            VerifySelection();
            Console.WriteLine("PASS: Composition path respects the full text-selection mask.");
            try
            {
                // Independent 2x Direct2D path surface, displayed at 1x, uses
                // the same path data as the Composition prototype, rebuilt for the drawing factory.
                using var raster = new Direct2DRasterProbe(compositor, bounds.Width, bounds.Height, 2, pathFactory: factory => CompositionGeometryInterop.CreateNativeSet(factory, layers.Core, layers.Outer, layers.Inner));
                var ringBrush = (CompositionSurfaceBrush)type.GetField("_ringBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
                ringBrush.Surface = raster.Surface;
                ringBrush.Stretch = CompositionStretch.Fill;
                ringBrush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
                Pump(800); DwmFlush();
                dc = GetDC(IntPtr.Zero);
                try
                {
                    uint edge = GetPixel(dc, bounds.Left + 50, bounds.Top + 65);
                    uint voidPixel = GetPixel(dc, bounds.Left + 100, bounds.Top + 100);
                    uint roundedCorner = GetPixel(dc, bounds.Left + 105, bounds.Top + 25);
                    if ((edge & 0xffffff) < 0xc0c0c0 || (voidPixel & 0xffffff) > 0x202020 || (roundedCorner & 0xffffff) > 0x202020)
                        throw new Exception($"Direct2D 2x path pixels invalid: edge={edge:X}, inner={voidPixel:X}, corner={roundedCorner:X}.");
                    Console.WriteLine("PASS: Direct2D 2x surface renders inset rounded core/glow paths without inner edge.");
                }
                finally { ReleaseDC(IntPtr.Zero, dc); }
                VerifySelection();
                Console.WriteLine("PASS: Direct2D 2x path respects the full text-selection mask.");
            }
            finally { }
            ring.Shapes.Clear();
            ((IDisposable)backend).Dispose(); backend = null;
            // Two actual overlay HWNDs simulate a monitor seam. The same
            // global contour is translated into each viewport, then selection
            // masking is verified across both halves.
            var pieces = new List<object>(); var pathObjects = new List<CompositionObject>();
            try
            {
                for (int part = 0; part < 2; part++)
                {
                    var viewport = new Rectangle(bounds.Left + part * 160, bounds.Top, 160, 200);
                    object piece = Activator.CreateInstance(type, new object?[] { viewport, bounds, 30f, true, 4000d,
                        false, true, 25f, null, false, false, 1f, false })!;
                    pieces.Add(piece);
                    var local = outline.Clone();
                    local.Transform = new TranslateTransform(-part * 160, 0);
                    var comp = (Compositor)type.GetField("_compositor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(piece)!;
                    var pieceRing = (ShapeVisual)type.GetField("_ring", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(piece)!;
                    var pathGeometry = comp.CreatePathGeometry(new CompositionPath(CompositionGeometryInterop.Create(local)));
                    var white = comp.CreateColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                    var sprite = comp.CreateSpriteShape(pathGeometry);
                    sprite.StrokeBrush = white; sprite.StrokeThickness = 8;
                    pathObjects.Add(pathGeometry); pathObjects.Add(white); pathObjects.Add(sprite);
                    pieceRing.Shapes.Clear(); pieceRing.Shapes.Add(sprite);
                    type.GetMethod("UpdateCursorAt")!.Invoke(piece, new object[] { bounds.Right + 100, bounds.Bottom + 100 });
                }
                Pump(900); DwmFlush();
                IntPtr seamDc = GetDC(IntPtr.Zero);
                uint left, right, center;
                try
                {
                    left = GetPixel(seamDc, bounds.Left + 158, bounds.Top + 25);
                    right = GetPixel(seamDc, bounds.Left + 161, bounds.Top + 25);
                    center = GetPixel(seamDc, bounds.Left + 160, bounds.Top + 100);
                }
                finally { ReleaseDC(IntPtr.Zero, seamDc); }
                if ((left & 0xffffff) < 0xc0c0c0 || (right & 0xffffff) < 0xc0c0c0 || (center & 0xffffff) > 0x202020)
                    throw new Exception($"Contour across synthetic monitor seam failed: {left:X}/{right:X}/{center:X}.");
                foreach (var piece in pieces)
                    textCutout.Invoke(piece, new object?[] { new System.Drawing.Point(bounds.Left + 170, bounds.Top + 25),
                        new Rectangle(bounds.Left + 130, bounds.Top + 17, 60, 16) });
                Pump(500); DwmFlush();
                seamDc = GetDC(IntPtr.Zero);
                try { left = GetPixel(seamDc, bounds.Left + 158, bounds.Top + 25); right = GetPixel(seamDc, bounds.Left + 161, bounds.Top + 25); }
                finally { ReleaseDC(IntPtr.Zero, seamDc); }
                if ((left & 0xffffff) > 0x202020 || (right & 0xffffff) > 0x202020)
                    throw new Exception($"Contour selection across synthetic monitor seam failed: {left:X}/{right:X}.");
                Console.WriteLine("PASS: Shared Composition contour and selection span two synthetic monitor windows.");
            }
            finally
            {
                foreach (var piece in pieces) ((IDisposable)piece).Dispose();
                for (int i = pathObjects.Count - 1; i >= 0; i--) pathObjects[i].Dispose();
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        finally
        {
            if (backend is IDisposable d) d.Dispose();
            for (int i = haloObjects.Count - 1; i >= 0; i--) haloObjects[i].Dispose();
            background.Close(); app.Shutdown();
        }
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
