using System;
using System.Windows;
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
        int[] selected = { 0 };
        if (!CompositionPolicy.CanUse(true, 1, false, selected, false, 1, 1) ||
            !CompositionPolicy.CanUse(true, 1, true, Array.Empty<int>(), false, 1, 1) ||
            CompositionPolicy.CanUse(false, 1, false, selected, false, 1, 1) ||
            CompositionPolicy.CanUse(true, 2, false, selected, false, 1, 1) ||
            CompositionPolicy.CanUse(true, 1, false, selected, true, 1, 1) ||
            CompositionPolicy.CanUse(true, 1, false, selected, false, 1.25, 1) ||
            CompositionPolicy.CanUse(true, 1, false, Array.Empty<int>(), false, 1, 1) ||
            CompositionPolicy.CanUse(true, 1, false, new[] { 1 }, false, 1, 1))
            throw new Exception("Composition eligibility policy failed.");
        Console.WriteLine("Composition eligibility checks passed.");
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
