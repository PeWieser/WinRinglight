using System;
using System.Windows;
using System.Windows.Media;
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
            if (Alpha(160, 100) != 0 || Alpha(160, 15) == 0 || Alpha(160, 34) == 0)
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
        Console.WriteLine("Glow geometry checks passed; this is not a visual or GPU performance test.");
        return 0;
    }
}
