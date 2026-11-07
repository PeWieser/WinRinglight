using System;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WinRinglight;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (TemperatureTransition.At(2000, 6000, 0) != 2000 ||
            TemperatureTransition.At(2000, 6000, 250) != 4000 ||
            TemperatureTransition.At(2000, 6000, 600) != 6000 ||
            TemperatureTransition.At(6000, 2000, -1) != 6000 ||
            TemperatureTransition.At(6000, 2000, 500) != 2000)
            throw new Exception("Temperature transition interpolation failed.");
        var displays = new[] {
            new CompositionLayout.Display(new System.Drawing.Rectangle(-1280, 0, 1280, 1024), new System.Drawing.Rectangle(-1280, 0, 1280, 984)),
            new CompositionLayout.Display(new System.Drawing.Rectangle(0, -200, 1920, 1080), new System.Drawing.Rectangle(0, -200, 1920, 1040))
        };
        var individual = CompositionLayout.Build(displays, false, new[] { 1, 0, 1, -1, 4 }, new HashSet<int>());
        if (individual.Length != 2 || individual[0] != new CompositionLayout.Piece(displays[1].Bounds, displays[1].WorkingArea) || individual[1] != new CompositionLayout.Piece(displays[0].Bounds, displays[0].WorkingArea))
            throw new Exception("Composition selected-monitor layout failed.");
        var fullscreen = CompositionLayout.Build(displays, false, new[] { 0, 1 }, new HashSet<int> { 1 });
        if (fullscreen.Length != 2 || fullscreen[0] != new CompositionLayout.Piece(displays[0].Bounds, displays[0].WorkingArea) || fullscreen[1] != new CompositionLayout.Piece(displays[1].Bounds, displays[1].Bounds))
            throw new Exception("Composition fullscreen layout failed.");
        var span = CompositionLayout.Build(displays, true, Array.Empty<int>(), new HashSet<int> { 0, 1 });
        if (span.Length != 2 || span[0] != new CompositionLayout.Piece(displays[0].Bounds, displays[0].WorkingArea, false, true) ||
            span[1] != new CompositionLayout.Piece(displays[1].Bounds, displays[1].WorkingArea, true, false))
            throw new Exception("Composition full-height spanned layout failed.");
        if (CompositionLayout.Build(displays, false, Array.Empty<int>(), new HashSet<int>()).Length != 0 ||
            CompositionLayout.Build(Array.Empty<CompositionLayout.Display>(), true, Array.Empty<int>(), new HashSet<int>()).Length != 0)
            throw new Exception("Composition empty display layout failed.");
        var z = new[] { new FullscreenPolicy.Entry(0, false), new FullscreenPolicy.Entry(1, true),
            new FullscreenPolicy.Entry(1, false), new FullscreenPolicy.Entry(0, true) };
        var states = FullscreenPolicy.Detect(2, z, null);
        if (!states.SetEquals(new[] { 0 }) || FullscreenPolicy.Detect(2, z, 0).Count != 0 ||
            !FullscreenPolicy.Detect(2, new[] { new FullscreenPolicy.Entry(1, false) }, 0).SetEquals(new[] { 1 }))
            throw new Exception("Per-monitor fullscreen z-order policy failed.");
        var stacked = new[] {
            new CompositionLayout.Display(new System.Drawing.Rectangle(0, -1080, 1920, 1080), new System.Drawing.Rectangle(0, -1080, 1920, 1080)),
            new CompositionLayout.Display(new System.Drawing.Rectangle(200, 0, 1280, 1024), new System.Drawing.Rectangle(200, 0, 1280, 984))
        };
        if (CompositionLayout.SpannedBounds(stacked) != System.Drawing.Rectangle.FromLTRB(200, -1080, 1480, 984))
            throw new Exception("Stacked monitor common band failed.");
        var stackedPieces = CompositionLayout.Build(stacked, true, Array.Empty<int>(), new HashSet<int>());
        if (stackedPieces.Length != 2 || stackedPieces[0].ExtendLeft || stackedPieces[0].ExtendRight ||
            stackedPieces[1].ExtendLeft || stackedPieces[1].ExtendRight)
            throw new Exception("Stacked displays must retain side borders.");
        Console.WriteLine("Composition multi-monitor layout and fullscreen policy checks passed.");
        foreach (double radius in new[] { 3.0, 55.0 })
        {
            var halo = new GlowBorder { Width = 320, Height = 200,
                BorderThickness = new Thickness(30), CornerRadius = new CornerRadius(radius),
                BorderBrush = Brushes.White };
            halo.Measure(new Size(320, 200));
            halo.Arrange(new Rect(0, 0, 320, 200));
            halo.UpdateLayout();
            var image = new RenderTargetBitmap(320, 200, 96, 96, PixelFormats.Pbgra32);
            image.Render(halo);
            byte[] pixels = new byte[320 * 200 * 4];
            image.CopyPixels(pixels, 320 * 4, 0);
            byte Alpha(int x, int y) => pixels[(y * 320 + x) * 4 + 3];
            Console.WriteLine($"radius={radius}, center={Alpha(160, 100)}, ring={Alpha(160, 15)}, inside-glow={Alpha(160, 34)}");
            if (Alpha(160, 100) != 0 || Alpha(160, 34) == 0)
                throw new Exception("Glow rendered unexpectedly: check center, ring and inner glow.");
            if (radius == 55.0)
            {
                // Border uses this same centerline radius. Sample just inside
                // the ring's inner edge along the whole top-left quarter arc.
                double center = 15 + radius;
                double innerRadius = radius - 15;
                byte straight = Alpha(160, 33);
                for (int degrees = 10; degrees <= 80; degrees += 10)
                {
                    double angle = degrees * Math.PI / 180;
                    int x = (int)Math.Round(center - (innerRadius - 3) * Math.Cos(angle));
                    int y = (int)Math.Round(center - (innerRadius - 3) * Math.Sin(angle));
                    byte curved = Alpha(x, y);
                    Console.WriteLine($"inner arc {degrees}°: {curved}, straight: {straight}");
                    if (curved < straight * 0.45 || curved > straight * 1.8)
                        throw new Exception("Inner corner glow diverges from straight edge.");
                }
            }
        }
        foreach (var item in new[] { (30.0, 3.0), (30.0, 55.0), (70.0, 3.0), (70.0, 55.0) })
        {
            byte[] oldPixels = RenderCombined(new ReferenceGlow(), item.Item1, item.Item2);
            byte[] newPixels = RenderCombined(new GlowBorder(), item.Item1, item.Item2);
            long diff = 0;
            int max = 0;
            for (int i = 0; i < oldPixels.Length; i++)
            {
                int d = Math.Abs(oldPixels[i] - newPixels[i]);
                diff += d;
                max = Math.Max(max, d);
            }
            double mean = (double)diff / oldPixels.Length;
            Console.WriteLine($"Visual comparison thickness={item.Item1}, radius={item.Item2}: mean={mean:F3}, max={max}");
            if (mean > 0.5) throw new Exception("Optimized halo differs too much from the reference.");
        }
        Console.WriteLine("Glow geometry checks passed; this is not a visual or GPU performance test.");
        return 0;
    }

    private static byte[] RenderCombined(Border glow, double thickness, double radius)
    {
        var root = new Grid { Width = 320, Height = 200 };
        glow.BorderThickness = new Thickness(thickness);
        glow.CornerRadius = new CornerRadius(radius);
        glow.BorderBrush = Brushes.White;
        glow.Opacity = 0.55;
        root.Children.Add(glow);
        root.Children.Add(new Border { BorderThickness = new Thickness(thickness),
            CornerRadius = new CornerRadius(radius), BorderBrush = Brushes.White });
        root.Measure(new Size(320, 200));
        root.Arrange(new Rect(0, 0, 320, 200));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(320, 200, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixels = new byte[320 * 200 * 4];
        bitmap.CopyPixels(pixels, 320 * 4, 0);
        return pixels;
    }

    private sealed class ReferenceGlow : Border
    {
        protected override void OnRender(DrawingContext dc)
        {
            double t = BorderThickness.Left, half = t / 2;
            double radius = Math.Min(CornerRadius.TopLeft, Math.Min(RenderSize.Width - t, RenderSize.Height - t) / 2);
            var rect = new Rect(half, half, RenderSize.Width - t, RenderSize.Height - t);
            double blur = Math.Min(40, t * 0.4);
            for (int i = 12; i >= 1; i--)
            {
                double f = (double)i / 12;
                byte alpha = (byte)Math.Round(255 * 0.035 * (1 - f * 0.75));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)), t + 2 * blur * f);
                dc.DrawRoundedRectangle(null, pen, rect, radius, radius);
            }
        }
    }
}
