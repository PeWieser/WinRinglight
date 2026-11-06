using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WinRinglight;

// WPF + WinForms => diese Namen sind mehrdeutig.
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Path = System.Windows.Shapes.Path;
using Brushes = System.Windows.Media.Brushes;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace GlowPreview
{
    /// <summary>
    /// Vergleichs- und Messwerkzeug fuer den Ringsaum.
    ///
    /// Kernidee: Der REFERENZRENDER ist die alte Darstellung - eine Border mit
    /// BlurEffect, exakt die Einstellungen von vor dem Umbau. Dagegen wird der
    /// neue Aufbau gemessen. Ohne diese Referenz habe ich mehrfach "passend"
    /// gebaut, was auf dem Bildschirm nicht passte.
    ///
    /// Aufruf:  GlowPreview &lt;ausgabeordner&gt;
    /// Ergebnis: alte-squircle.png, alte-eckig.png,
    ///           neu-squircle.png, neu-eckig.png, messwerte.txt
    /// Exitcode 1, wenn der neue Aufbau von der alten Darstellung abweicht.
    /// </summary>
    internal static class Program
    {
        private const double CoreW = 900;
        private const double CoreH = 500;
        private const double Thickness = 60;
        private const double Inset = 20;
        private const double Pad = 60;           // Platz fuer den Schein im Bild
        private const double FadeOpacity = 0.55;

        [STAThread]
        private static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(outDir);

            var lines = new List<string>();
            bool ok = true;

            foreach (bool apple in new[] { true, false })
            {
                string style = apple ? "squircle" : "eckig";

                double w = apple ? CoreW - 40 : CoreW;
                double h = apple ? CoreH - 40 : CoreH;
                double radius = RingHalo.CoreCornerRadius(w, h, apple);

                double[] alt = RenderAlt(w, h, radius);
                double[] neu = RenderNeu(w, h, radius);

                SavePng(System.IO.Path.Combine(outDir, $"alte-{style}.png"), alt);
                SavePng(System.IO.Path.Combine(outDir, $"neu-{style}.png"), neu);

                lines.Add($"=== {(apple ? "Squircle (Apple)" : "Eckig")} : Kern {w:F0} x {h:F0}, Radius {radius:F1} ===");

                // Messung entlang der Kontur, aussen und innen.
                foreach (double depth in new[] { -1.0, -0.5, 0.5, 1.0, 1.5, 2.0, 3.0 })
                {
                    double a = ContourAverage(alt, w, h, radius, depth, out double amin, out double amax);
                    double n = ContourAverage(neu, w, h, radius, depth, out double nmin, out double nmax);

                    double abw = a > 3 ? Math.Abs(n - a) / a : 0;
                    string urteil = abw <= 0.25 ? "ok" : "ABWEICHUNG";
                    if (abw > 0.25) ok = false;

                    lines.Add(string.Format(CultureInfo.InvariantCulture,
                        "  {0,5:F1} sigma: alt {1,6:F1} [{2,5:F1}..{3,5:F1}]   neu {4,6:F1} [{5,5:F1}..{6,5:F1}]   {7,5:F1} %   {8}",
                        depth, a, amin, amax, n, nmin, nmax, abw * 100, urteil));
                }
                lines.Add("");
            }

            File.WriteAllLines(System.IO.Path.Combine(outDir, "messwerte.txt"), lines);
            foreach (string l in lines) Console.WriteLine(l);
            Console.WriteLine(ok ? "SAUM STIMMT MIT DER ALTEN DARSTELLUNG UEBEREIN" : "SAUM WEICHT AB");
            return ok ? 0 : 1;
        }

        // ---------------------------------------------------------------- alt

        /// <summary>Die alte Darstellung: Border mit BlurEffect, wie vor dem Umbau.</summary>
        private static double[] RenderAlt(double w, double h, double radius)
        {
            var canvas = NewCanvas();

            var glow = new Border
            {
                Width = w,
                Height = h,
                BorderThickness = new Thickness(Thickness),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 214, 170)),
                CornerRadius = new CornerRadius(radius),
                Opacity = FadeOpacity,
                CacheMode = new BitmapCache { EnableClearType = false, SnapsToDevicePixels = false, RenderAtScale = 0.25 },
                Effect = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = Math.Min(40, Thickness * 0.4),
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
                },
            };
            Canvas.SetLeft(glow, Pad);
            Canvas.SetTop(glow, Pad);
            canvas.Children.Add(glow);

            return Raster(canvas);
        }

        // ---------------------------------------------------------------- neu

        /// <summary>Der neue Aufbau: gestrichene Kontur, mehrere Schichten.</summary>
        private static double[] RenderNeu(double w, double h, double radius)
        {
            var canvas = NewCanvas();
            canvas.Opacity = FadeOpacity;

            List<RingHalo.LayerVisual> layers =
                RingHalo.Build(Color.FromRgb(255, 214, 170), Pad, Pad, w, h, radius, Thickness);

            foreach (RingHalo.LayerVisual layer in layers)
            {
                canvas.Children.Add(new Path
                {
                    Data = layer.Geometry,
                    Stroke = layer.Brush,
                    StrokeThickness = layer.StrokeThickness,
                    IsHitTestVisible = false,
                });
            }

            // Kern als Ringpfad (EvenOdd) - genau wie im echten Fenster.
            // Als Border wuerde er das Loch zudecken.
            var core = new Path
            {
                Data = CoreRing(w, h, Thickness, radius),
                Fill = new SolidColorBrush(Color.FromRgb(255, 214, 170)),
                IsHitTestVisible = false,
            };
            var shift = new TranslateTransform(Pad, Pad);
            shift.Freeze();
            core.RenderTransform = shift;
            canvas.Children.Add(core);

            return Raster(canvas);
        }

        /// <summary>Ring als Pfad mit EvenOdd - Kopie aus MainWindow.</summary>
        private static Geometry CoreRing(double w, double h, double t, double r)
        {
            r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2));
            var fig = new PathFigure { IsClosed = true, IsFilled = true };
            AddRect(fig, 0, 0, w, h, r);
            double iw = Math.Max(0.01, w - 2 * t);
            double ih = Math.Max(0.01, h - 2 * t);
            double ir = Math.Max(0, Math.Min(r - t, Math.Min(iw, ih) / 2));
            AddRect(fig, t, t, iw, ih, ir);
            var geo = new PathGeometry { FillRule = FillRule.EvenOdd };
            geo.Figures.Add(fig);
            geo.Freeze();
            return geo;
        }

        private static void AddRect(PathFigure f, double x, double y, double w, double h, double r)
        {
            if (w <= 0 || h <= 0) return;
            r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2));
            f.Segments.Add(new LineSegment(new Point(x + r, y), true));
            f.Segments.Add(new LineSegment(new Point(x + w - r, y), true));
            f.Segments.Add(new ArcSegment(new Point(x + w, y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(new Point(x + w, y + h - r), true));
            f.Segments.Add(new ArcSegment(new Point(x + w - r, y + h), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(new Point(x + r, y + h), true));
            f.Segments.Add(new ArcSegment(new Point(x, y + h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            f.Segments.Add(new LineSegment(new Point(x, y + r), true));
            f.Segments.Add(new ArcSegment(new Point(x + r, y), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        }

        // ------------------------------------------------------------- Helfer

        private static Canvas NewCanvas()
        {
            double size = Math.Max(CoreW, CoreH) + 2 * Pad;
            return new Canvas
            {
                Width = size,
                Height = size,
                Background = new SolidColorBrush(Color.FromRgb(20, 20, 24)),
            };
        }

        private static int _size;

        private static double[] Raster(Canvas canvas)
        {
            _size = (int)Math.Round(canvas.Width);
            canvas.Measure(new Size(canvas.Width, canvas.Height));
            canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
            canvas.UpdateLayout();

            var rtb = new RenderTargetBitmap(_size, (int)Math.Round(canvas.Height), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(canvas);

            var px = new byte[_size * rtb.PixelHeight];
            rtb.CopyPixels(px, _size * 4, 0);

            var bright = new double[_size * rtb.PixelHeight];
            for (int i = 0; i < bright.Length; i++)
            {
                int p = i * 4;
                bright[i] = (px[p] + px[p + 1] + px[p + 2]) / 3.0;
            }
            return bright;
        }

        private static void SavePng(string path, double[] bright)
        {
            int h = bright.Length / _size;
            var px = new byte[_size * h * 4];
            for (int i = 0; i < bright.Length; i++)
            {
                byte v = (byte)Math.Max(0, Math.Min(255, bright[i]));
                int p = i * 4;
                px[p] = v; px[p + 1] = v; px[p + 2] = v; px[p + 3] = 255;
            }
            var bmp = BitmapSource.Create(_size, h, 96, 96, PixelFormats.Bgra32, null, px, _size * 4);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using FileStream fs = File.Create(path);
            enc.Save(fs);
        }

        /// <summary>
        /// Mittelwert der Helligkeit entlang der ganzen Ringkontur, im Abstand
        /// <paramref name="depth"/> sigma ausserhalb (positiv) bzw. innerhalb
        /// (negativ) der Ringkante.
        /// </summary>
        private static double ContourAverage(double[] img, double w, double h, double radius,
            double depth, out double min, out double max)
        {
            double sigma = RingHalo.SigmaFor(Thickness);
            double offset = depth * sigma;

            // Konturpunkte: gerade Kanten und Eckenboegen, jeweils mit
            // Aussennormale.
            var pts = new List<(Point P, Point N)>();
            for (int i = 0; i <= 4; i++)
            {
                double t = i / 4.0;
                double x = radius + (w - 2 * radius) * t;
                pts.Add((new Point(x, 0), new Point(0, -1)));
                pts.Add((new Point(x, h), new Point(0, 1)));
            }
            for (int i = 1; i < 4; i++)
            {
                double t = i / 4.0;
                double y = radius + (h - 2 * radius) * t;
                pts.Add((new Point(0, y), new Point(-1, 0)));
                pts.Add((new Point(w, y), new Point(1, 0)));
            }
            var corners = new (double cx, double cy, double from)[]
            {
                (radius, radius, 180),
                (w - radius, radius, 270),
                (w - radius, h - radius, 0),
                (radius, h - radius, 90),
            };
            foreach (var (cx, cy, from) in corners)
            {
                for (int i = 0; i <= 8; i++)
                {
                    double a = (from + 90.0 * i / 8) * Math.PI / 180.0;
                    pts.Add((new Point(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)),
                             new Point(Math.Cos(a), Math.Sin(a))));
                }
            }

            double sum = 0;
            min = double.MaxValue;
            max = 0;
            int n = 0;

            foreach (var (p, nrm) in pts)
            {
                double x = Pad + p.X + nrm.X * offset;
                double y = Pad + p.Y + nrm.Y * offset;

                int ix = (int)Math.Round(x);
                int iy = (int)Math.Round(y);
                if (ix < 1 || iy < 1 || ix >= _size - 1) continue;

                double v = img[iy * _size + ix];
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
                n++;
            }

            return n > 0 ? sum / n : 0;
        }
    }
}
