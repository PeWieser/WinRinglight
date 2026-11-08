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
        var span = CompositionLayout.Build(displays, true, Array.Empty<int>(), new HashSet<int>());
        if (span.Length != 2 || span[0] != new CompositionLayout.Piece(displays[0].Bounds, displays[0].WorkingArea, false, true) ||
            span[1] != new CompositionLayout.Piece(displays[1].Bounds, displays[1].WorkingArea, true, false))
            throw new Exception("Composition full-height spanned layout failed.");
        var spanFullscreen = CompositionLayout.Build(displays, true, Array.Empty<int>(), new HashSet<int> { 1 });
        if (spanFullscreen.Length != 2 || spanFullscreen[0].Ring != displays[0].Bounds || spanFullscreen[1].Ring != displays[1].Bounds)
            throw new Exception("Spanned fullscreen must use every monitor's full bounds.");
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
        // Union outlines: check shared edges disappear, exposed step segments
        // remain, and disconnected monitors never get a connecting line.
        static void CheckOutline(string name, System.Drawing.Rectangle[] screens, params DisplayOutline.Edge[] expected)
        {
            var actual = DisplayOutline.Build(screens);
            if (actual.Length != expected.Length || !new HashSet<DisplayOutline.Edge>(actual).SetEquals(expected))
                throw new Exception($"{name} display outline mismatch: {string.Join(", ", actual)}");
        }
        var T = DisplayOutline.Side.Top; var B = DisplayOutline.Side.Bottom;
        var L = DisplayOutline.Side.Left; var R = DisplayOutline.Side.Right;
        CheckOutline("stacked", new[] { new System.Drawing.Rectangle(0, -100, 200, 100), new System.Drawing.Rectangle(50, 0, 100, 100) },
            new(T, -100, 0, 200), new(B, 0, 0, 50), new(B, 0, 150, 200), new(B, 100, 50, 150),
            new(L, 0, -100, 0), new(L, 50, 0, 100), new(R, 150, 0, 100), new(R, 200, -100, 0));
        CheckOutline("cross", new[] { new System.Drawing.Rectangle(0, 0, 100, 300), new System.Drawing.Rectangle(-100, 100, 300, 100) },
            new(T, 0, 0, 100), new(T, 100, -100, 0), new(T, 100, 100, 200),
            new(B, 200, -100, 0), new(B, 200, 100, 200), new(B, 300, 0, 100),
            new(L, -100, 100, 200), new(L, 0, 0, 100), new(L, 0, 200, 300),
            new(R, 100, 0, 100), new(R, 100, 200, 300), new(R, 200, 100, 200));
        CheckOutline("gap", new[] { new System.Drawing.Rectangle(-100, 0, 100, 100), new System.Drawing.Rectangle(10, 0, 100, 100) },
            new(T, 0, -100, 0), new(T, 0, 10, 110), new(B, 100, -100, 0), new(B, 100, 10, 110),
            new(L, -100, 0, 100), new(L, 10, 0, 100), new(R, 0, 0, 100), new(R, 110, 0, 100));
        CheckOutline("overlap", new[] { new System.Drawing.Rectangle(0, 0, 100, 100), new System.Drawing.Rectangle(50, 0, 100, 100) },
            new(T, 0, 0, 150), new(B, 100, 0, 150), new(L, 0, 0, 100), new(R, 150, 0, 100));
        Console.WriteLine("Display union outlines: stacked, cross, gap and overlap checks passed.");
        var touching = DisplayOutline.BuildLoops(new[] { new System.Drawing.Rectangle(0, 0, 2, 2), new System.Drawing.Rectangle(2, 2, 2, 2) });
        if (touching.Length != 2) throw new Exception("Corner-touching screens must yield distinct boundary loops.");
        var random = new Random(9173);
        for (int n = 0; n < 200; n++)
        {
            var screens = new System.Drawing.Rectangle[random.Next(1, 7)];
            for (int i = 0; i < screens.Length; i++) screens[i] = new System.Drawing.Rectangle(random.Next(-5, 6), random.Next(-5, 6), random.Next(1, 6), random.Next(1, 6));
            bool Occupied(int x, int y) => Array.Exists(screens, r => r.Contains(x, y));
            var expectedUnits = new HashSet<DisplayOutline.Edge>();
            for (int y = -5; y < 11; y++) for (int x = -5; x < 11; x++)
            {
                if (!Occupied(x, y)) continue;
                if (!Occupied(x, y - 1)) expectedUnits.Add(new(T, y, x, x + 1));
                if (!Occupied(x, y + 1)) expectedUnits.Add(new(B, y + 1, x, x + 1));
                if (!Occupied(x - 1, y)) expectedUnits.Add(new(L, x, y, y + 1));
                if (!Occupied(x + 1, y)) expectedUnits.Add(new(R, x + 1, y, y + 1));
            }
            var actualUnits = new HashSet<DisplayOutline.Edge>();
            foreach (var edge in DisplayOutline.Build(screens))
                for (int p = edge.Start; p < edge.End; p++)
                    if (!actualUnits.Add(edge with { Start = p, End = p + 1 })) throw new Exception("Duplicate union outline edge.");
            if (!expectedUnits.SetEquals(actualUnits)) throw new Exception($"Randomized display outline mismatch (case {n}).");
            var tracedUnits = new HashSet<DisplayOutline.Edge>();
            foreach (var loop in DisplayOutline.BuildLoops(screens))
            {
                if (loop.Length < 4) throw new Exception("Display outline loop is too short.");
                for (int i = 0; i < loop.Length; i++)
                {
                    var a = loop[i]; var b = loop[(i + 1) % loop.Length];
                    if ((a.X == b.X) == (a.Y == b.Y)) throw new Exception("Invalid outline loop segment.");
                    if (a.Y == b.Y)
                    {
                        var side = b.X > a.X ? T : B;
                        for (int x = Math.Min(a.X, b.X); x < Math.Max(a.X, b.X); x++)
                            if (!tracedUnits.Add(new(side, a.Y, x, x + 1))) throw new Exception("Duplicate traced horizontal edge.");
                    }
                    else
                    {
                        var side = b.Y > a.Y ? R : L;
                        for (int y = Math.Min(a.Y, b.Y); y < Math.Max(a.Y, b.Y); y++)
                            if (!tracedUnits.Add(new(side, a.X, y, y + 1))) throw new Exception("Duplicate traced vertical edge.");
                    }
                }
            }
            if (!expectedUnits.SetEquals(tracedUnits)) throw new Exception($"Randomized display boundary loop mismatch (case {n}).");
        }
        Console.WriteLine("Display union outline: 200 randomized layouts match independent cell boundaries.");
        var rounded = RoundedDisplayOutline.Create(new[] { new System.Drawing.Rectangle(0, 0, 100, 100) }, 20);
        if (rounded.FillContains(new System.Windows.Point(1, 1)) || !rounded.FillContains(new System.Windows.Point(10, 10)) ||
            !rounded.FillContains(new System.Windows.Point(50, 50)) || rounded.FillContains(new System.Windows.Point(101, 50)))
            throw new Exception("Rounded display outline convex corner failed.");
        var square = RoundedDisplayOutline.Create(new[] { new System.Drawing.Rectangle(0, 0, 100, 100) }, 0);
        if (!square.FillContains(new System.Windows.Point(1, 1))) throw new Exception("Zero-radius outline should be rectangular.");
        var separated = RoundedDisplayOutline.Create(new[] { new System.Drawing.Rectangle(0, 0, 100, 100), new System.Drawing.Rectangle(120, 0, 100, 100) }, 10);
        if (separated.FillContains(new System.Windows.Point(110, 50))) throw new Exception("Rounded outline connected a physical gap.");
        var shortStep = RoundedDisplayOutline.Create(new[] { new System.Drawing.Rectangle(0, 0, 100, 100), new System.Drawing.Rectangle(100, 2, 100, 98) }, 80);
        if (!shortStep.FillContains(new System.Windows.Point(50, 50)) || !shortStep.FillContains(new System.Windows.Point(150, 50)))
            throw new Exception("Rounded outline failed around a short step.");

        var concave = RoundedDisplayOutline.Create(new[] { new System.Drawing.Rectangle(0, 0, 100, 200), new System.Drawing.Rectangle(100, 100, 100, 100) }, 20);
        if (concave.FillContains(new System.Windows.Point(105, 95))) throw new Exception("Concave rounding extended the display union into a gap.");
        var hole = RoundedDisplayOutline.Create(new[] { new System.Drawing.Rectangle(0, 0, 100, 20), new System.Drawing.Rectangle(0, 80, 100, 20),
            new System.Drawing.Rectangle(0, 20, 20, 60), new System.Drawing.Rectangle(80, 20, 20, 60) }, 0);
        if (hole.FillContains(new System.Windows.Point(50, 50)) || !hole.FillContains(new System.Windows.Point(10, 50)))
            throw new Exception("Display outline did not preserve the interior gap.");
        Console.WriteLine("Rounded outline geometry: convex, square, gap and short step checks passed.");
        var crossGeometry = RoundedDisplayOutline.Create(new[] {
            new System.Drawing.Rectangle(100, 0, 100, 300), new System.Drawing.Rectangle(0, 100, 300, 100) }, 10);
        var visualOutline = new DrawingVisual();
        using (var dc = visualOutline.RenderOpen())
            dc.DrawGeometry(null, new Pen(Brushes.White, 8), crossGeometry);
        var outlineBitmap = new RenderTargetBitmap(300, 300, 96, 96, PixelFormats.Pbgra32);
        outlineBitmap.Render(visualOutline);
        var outlinePixels = new byte[300 * 300 * 4];
        outlineBitmap.CopyPixels(outlinePixels, 300 * 4, 0);
        byte OutlineAlpha(int x, int y) => outlinePixels[(y * 300 + x) * 4 + 3];
        if (OutlineAlpha(50, 100) < 200 || OutlineAlpha(250, 200) < 200 || OutlineAlpha(100, 150) != 0 || OutlineAlpha(200, 150) != 0)
            throw new Exception("Rounded outline pixel prototype has missing outer or unexpected inner edges.");
        Console.WriteLine("Rounded cross-outline prototype has exposed edges and no internal side edges (WPF pixel test).");
        var insetSquare = InsetDisplayOutline.Create(new[] { new System.Drawing.Rectangle(0, 0, 100, 100) }, 0, 10);
        if (Math.Abs(insetSquare.Bounds.Left - 10) > .5 || Math.Abs(insetSquare.Bounds.Top - 10) > .5 ||
            Math.Abs(insetSquare.Bounds.Right - 90) > .5 || Math.Abs(insetSquare.Bounds.Bottom - 90) > .5 ||
            insetSquare.FillContains(new System.Windows.Point(5, 50)) || !insetSquare.FillContains(new System.Windows.Point(50, 50)))
            throw new Exception($"Inset contour failed: {insetSquare.Bounds}.");
        var insetHole = InsetDisplayOutline.Create(new[] { new System.Drawing.Rectangle(0, 0, 100, 20), new System.Drawing.Rectangle(0, 80, 100, 20),
            new System.Drawing.Rectangle(0, 20, 20, 60), new System.Drawing.Rectangle(80, 20, 20, 60) }, 0, 5);
        if (insetHole.FillContains(new System.Windows.Point(50, 50)) || !insetHole.FillContains(new System.Windows.Point(10, 50)) ||
            insetHole.FillContains(new System.Windows.Point(2, 50))) throw new Exception("Inset contour hole failed.");
        var disappearing = InsetDisplayOutline.Create(new[] { new System.Drawing.Rectangle(0, 0, 10, 100) }, 0, 6);
        if (!disappearing.IsEmpty()) throw new Exception("Too-narrow inset region should disappear rather than invert.");
        Console.WriteLine("Inset outline prototype: square, hole and narrow-region checks passed.");

        var viewport = new System.Drawing.Rectangle(0, 0, 100, 100);
        if (!CursorCoverage.Intersects(viewport, 50, 50, 10) ||
            !CursorCoverage.Intersects(viewport, -10, 50, 10) ||
            CursorCoverage.Intersects(viewport, -11, 50, 10) ||
            CursorCoverage.Intersects(viewport, -9, -9, 10) ||
            !CursorCoverage.Intersects(viewport, -6, -8, 10) ||
            !CursorCoverage.Intersects(new System.Drawing.Rectangle(-200, -100, 100, 100), -205, -50, 5) ||
            CursorCoverage.Intersects(viewport, 50, 50, -1))
            throw new Exception("Cursor/monitor coverage failed.");
        Console.WriteLine("Cursor coverage boundary and negative-coordinate checks passed.");
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
