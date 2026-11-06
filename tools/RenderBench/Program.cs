using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace RenderBench
{
    /// <summary>
    /// Misst auf einem echten Windows-Rechner, wie teuer das Neuzeichnen des
    /// Ringlichts ist - und zwar fuer die Aufbauten, die im echten Code stehen.
    ///
    /// Hintergrund: Phase 3 sollte den BlurEffect durch Verlaeufe ersetzen.
    /// Die Annahme "Blur ist teuer" muss geprueft werden, denn die Borders haben
    /// ein BitmapCache - dann wird der Blur womoeglich nur einmal gerechnet.
    /// Hier wird deshalb gemessen statt geraten.
    ///
    /// Gemessen wird das Rastern des Visual-Trees in eine Bitmap. Das ist
    /// deterministisch, braucht keine GPU und laeuft auf jedem CI-Runner.
    /// Es zeigt genau den Unterschied, um den es geht: kostet die Maske bzw.
    /// der Blur pro Bild Zeit oder nicht.
    /// </summary>
    internal static class Program
    {
        private const double Thickness = 60;
        private const int Frames = 30;

        [STAThread]
        private static int Main()
        {
            Console.WriteLine("RenderBench - Zeichenzeit pro Bild, niedriger ist besser");
            Console.WriteLine();

            var setups = new (string Name, int W, int H)[]
            {
                ("1 Monitor 1080p", 1920, 1080),
                ("1 von 3 (1080p)", 2016, 1080),
                ("1 von 2 (4K @150%)", 3984, 2160),
                ("2 gespannt (4K @150%)", 7680, 2160),
            };

            foreach (var s in setups)
            {
                Console.WriteLine($"--- {s.Name}: {s.W} x {s.H} ---");

                double baseline = Measure(s.W, s.H, useMask: false, useBlur: false, useCache: false);

                Print("ohne Maske, ohne Blur", Measure(s.W, s.H, false, false, false), baseline);
                Print("ohne Maske, mit Blur ", Measure(s.W, s.H, false, true, false), baseline);
                Print("mit Maske, ohne Blur ", Measure(s.W, s.H, true, false, false), baseline);
                Print("mit Maske, mit Blur  ", Measure(s.W, s.H, true, true, false), baseline);
                Print("mit Maske+Blur+Cache ", Measure(s.W, s.H, true, true, true), baseline);
                Print("mit Maske+VERLAUF   ", Measure(s.W, s.H, true, false, false, useGradientGlow: true), baseline);

                Console.WriteLine();
            }

            return 0;
        }

        private static void Print(string label, double ms, double baseline)
        {
            double factor = baseline > 0.001 ? ms / baseline : 0;
            Console.WriteLine($"  {label} : {ms,8:F2} ms/Bild  ({factor,5:F2}x Basis)");
        }

        private static double Measure(int w, int h, bool useMask, bool useBlur, bool useCache,
            bool useGradientGlow = false)
        {
            var root = new Grid { Width = w, Height = h };

            var maskBrush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                SpreadMethod = GradientSpreadMethod.Pad,
                Center = new Point(w / 2.0, h / 2.0),
                GradientOrigin = new Point(w / 2.0, h / 2.0),
                RadiusX = h * 0.40,
                RadiusY = h * 0.40,
            };
            maskBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0));
            maskBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.2));
            maskBrush.GradientStops.Add(new GradientStop(Colors.White, 1.0));

            if (useMask) root.OpacityMask = maskBrush;

            var color = Color.FromRgb(255, 214, 170);

            // Glow-Rand (mit ggf. Blur/Cache) und Kern, wie im echten Code.
            var glow = new Border
            {
                Width = w,
                Height = h,
                BorderThickness = new Thickness(Thickness),
                BorderBrush = new SolidColorBrush(color),
                Opacity = 0.55,
            };

            if (useBlur)
            {
                if (useCache)
                {
                    glow.CacheMode = new BitmapCache
                    {
                        EnableClearType = false,
                        SnapsToDevicePixels = false,
                        RenderAtScale = 0.25,
                    };
                }

                glow.Effect = new BlurEffect
                {
                    Radius = 15,
                    RenderingBias = RenderingBias.Performance,
                };
            }

            if (useGradientGlow)
            {
                // Genau der neue Aufbau: 4 Rechtecke mit LinearGradientBrush
                // an den Kanten, kein Effekt, kein Cache.
                var canvas = new Canvas { Width = w, Height = h, Opacity = 0.55 };
                double band = Math.Max(6.0, Math.Min(40.0, Thickness * 0.4) / 3.0 * 3.0);
                double edge = Thickness;
                double outer = Thickness + band;

                void Strip(double x, double y, double sw, double sh, Point from, Point to)
                {
                    var b = new LinearGradientBrush
                    {
                        MappingMode = BrushMappingMode.RelativeToBoundingBox,
                        StartPoint = from,
                        EndPoint = to,
                        SpreadMethod = GradientSpreadMethod.Pad,
                    };
                    // Profil wie RingGlow.DefaultAlphas
                    b.GradientStops.Add(new GradientStop(Color.FromArgb(255, color.R, color.G, color.B), 0.0));
                    b.GradientStops.Add(new GradientStop(Color.FromArgb(213, color.R, color.G, color.B), 0.20));
                    b.GradientStops.Add(new GradientStop(Color.FromArgb(103, color.R, color.G, color.B), 0.45));
                    b.GradientStops.Add(new GradientStop(Color.FromArgb(28, color.R, color.G, color.B), 0.70));
                    b.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0));
                    b.Freeze();

                    var r = new System.Windows.Shapes.Rectangle
                    {
                        Width = sw, Height = sh, Fill = b, IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(r, x);
                    Canvas.SetTop(r, y);
                    canvas.Children.Add(r);
                }

                Strip(edge, 0, w - 2 * edge, band, new Point(0, 0), new Point(0, 1));
                Strip(edge, h - outer, w - 2 * edge, band, new Point(0, 1), new Point(0, 0));
                Strip(0, edge, band, h - 2 * edge, new Point(0, 0), new Point(1, 0));
                Strip(w - outer, edge, band, h - 2 * edge, new Point(1, 0), new Point(0, 0));

                root.Children.Add(canvas);
            }
            else
            {
                root.Children.Add(glow);
            }

            root.Children.Add(new Border
            {
                Width = w,
                Height = h,
                BorderThickness = new Thickness(Thickness),
                BorderBrush = new SolidColorBrush(color),
            });

            root.Measure(new Size(w, h));
            root.Arrange(new Rect(0, 0, w, h));
            root.UpdateLayout();

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);

            // Aufwaermen: erste Rasterung baut Ressourcen auf.
            rtb.Render(root);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Frames; i++)
            {
                // Genau die Aenderung, die im echten Code pro Frame passiert.
                double x = (w / 2.0) + (i * 11) - (Frames * 11 / 2.0);
                maskBrush.Center = new Point(x, h / 2.0 + (i % 5));
                maskBrush.GradientOrigin = new Point(x, h / 2.0 + (i % 5));

                rtb.Render(root);
            }
            sw.Stop();

            return sw.Elapsed.TotalMilliseconds / Frames;
        }
    }
}
