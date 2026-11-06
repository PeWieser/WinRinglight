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

// Das Projekt nutzt WPF UND WinForms - diese Namen sind mehrdeutig.
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Path = System.Windows.Shapes.Path;

namespace GlowPreview
{
    /// <summary>
    /// Rendert den Ringsaum mit dem ECHTEN WPF-Renderer und prueft die Optik.
    ///
    /// Warum: Zweimal war die Geometrie "rechnerisch richtig" und auf dem
    /// Bildschirm falsch. Ein Bild sagt mehr als eine Formel - und die
    /// Gleichmaessigkeit laesst sich daraus messen statt schaetzen.
    ///
    /// Gemessen wird ENTLANG DER KONTUR: die Kontur des Kerns wird abgelaufen
    /// und jeweils um einen Teil der Bandbreite nach aussen getastet. An jeder
    /// Stelle muss dieselbe Deckkraft herrschen - gerade Kante wie Ecke.
    ///
    /// Aufruf:  GlowPreview &lt;ausgabeordner&gt;
    /// Ergebnis: glow-apple.png, glow-eckig.png, messwerte.txt
    /// Exitcode 1, wenn die Optik nicht gleichmaessig ist.
    /// </summary>
    internal static class Program
    {
        private const double CoreW = 900;
        private const double CoreH = 500;
        private const double Thickness = 60;
        private const double Inset = 20;          // Apple-Innenabstand
        private const double Pad = 48;            // RinglightGlowPad
        private const double Opacity = 0.55;

        [STAThread]
        private static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(outDir);

            bool ok = true;
            var lines = new List<string>();

            foreach (bool apple in new[] { true, false })
            {
                string name = apple ? "glow-apple" : "glow-eckig";
                (double w, double h) = apple ? (CoreW - 40, CoreH - 40) : (CoreW, CoreH);

                Render(apple, w, h, out byte[] pixels, out int pw, out int ph, out double ox, out double oy);
                SavePng(Path2(outDir, name + ".png"), pixels, pw, ph);

                lines.Add($"--- {(apple ? "Apple/Squircle" : "eckig")}: Kern {w} x {h} ---");
                foreach (string m in Measure(pixels, pw, ph, ox, oy, w, h, apple))
                {
                    lines.Add("  " + m);
                    if (m.Contains("FEHLER")) ok = false;
                }
                lines.Add("");
            }

            File.WriteAllLines(Path2(outDir, "messwerte.txt"), lines);
            foreach (string l in lines) Console.WriteLine(l);

            Console.WriteLine(ok ? "OPTIK OK" : "OPTIK FEHLERHAFT");
            return ok ? 0 : 1;
        }

        private static string Path2(string a, string b) => System.IO.Path.Combine(a, b);

        /// <summary>Ringpfad mit EvenOdd - dieselbe Logik wie MainWindow.CoreRingGeometry.</summary>
        private static Geometry CoreRing(double w, double h, double t, double r)
        {
            var fig = new PathFigure { IsClosed = true, IsFilled = true };
            AddRect(fig, 0, 0, w, h, r);
            double iw = Math.Max(0.01, w - 2 * t);
            double ih = Math.Max(0.01, h - 2 * t);
            AddRect(fig, t, t, iw, ih, Math.Max(0, Math.Min(r - t, Math.Min(iw, ih) / 2)));

            var g = new PathGeometry { FillRule = FillRule.EvenOdd };
            g.Figures.Add(fig);
            g.Freeze();
            return g;
        }

        private static void AddRect(PathFigure fig, double x, double y, double w, double h, double r)
        {
            r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2));
            if (r <= 0.01)
            {
                fig.Segments.Add(new LineSegment(new Point(x, y), true));
                fig.Segments.Add(new LineSegment(new Point(x + w, y), true));
                fig.Segments.Add(new LineSegment(new Point(x + w, y + h), true));
                fig.Segments.Add(new LineSegment(new Point(x, y + h), true));
                return;
            }
            fig.Segments.Add(new LineSegment(new Point(x + r, y), true));
            fig.Segments.Add(new ArcSegment(new Point(x + w, y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(new Point(x + w, y + h - r), true));
            fig.Segments.Add(new ArcSegment(new Point(x + w - r, y + h), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(new Point(x + r, y + h), true));
            fig.Segments.Add(new ArcSegment(new Point(x, y + h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(new Point(x, y + r), true));
            fig.Segments.Add(new ArcSegment(new Point(x + r, y), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        }

        /// <summary>
        /// Baut den Saum genau wie MainWindow (gleiche Masse, gleicher Aufbau)
        /// und rastert ihn.
        /// </summary>
        private static void Render(bool apple, double coreW, double coreH,
            out byte[] pixels, out int pw, out int ph, out double originX, out double originY)
        {
            double inset = apple ? Inset : 0;
            double pad = Pad + inset;

            double canvasW = coreW + 2 * pad;
            double canvasH = coreH + 2 * pad;

            var canvas = new Canvas
            {
                Width = canvasW,
                Height = canvasH,
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 28)),
                Opacity = Opacity,
            };

            double band = RingGlow.WidthFor(Thickness);
            double radius = RingBand.CoreCornerRadius(coreW, coreH, apple);
            Color color = Color.FromRgb(255, 214, 170);

            List<RingBand.PieceVisual> pieces =
                RingBand.Build(color, Opacity, coreW, coreH, Thickness, radius, band, pad, pad);

            foreach (RingBand.PieceVisual piece in pieces)
            {
                canvas.Children.Add(new Path
                {
                    Data = piece.Geometry,
                    Fill = piece.Brush,
                    IsHitTestVisible = false,
                });
            }

            // Grundflaeche des Lochs: was der fruehere Blur hineinstreute.
            canvas.Children.Add(new Path
            {
                Data = new RectangleGeometry(new Rect(pad + Thickness, pad + Thickness,
                    Math.Max(0.01, coreW - 2 * Thickness), Math.Max(0.01, coreH - 2 * Thickness))),
                Fill = RingGlow.CoreInside(color, Opacity,
                    Math.Max(0.01, coreW - 2 * Thickness), Math.Max(0.01, coreH - 2 * Thickness), band),
                IsHitTestVisible = false,
            });

            // Kern als Ringpfad - dieselbe Konstruktion wie in MainWindow.
            canvas.Children.Add(new Path
            {
                Data = CoreRing(coreW, coreH, Thickness, radius),
                Fill = new SolidColorBrush(color),
                IsHitTestVisible = false,
            });

            canvas.Measure(new Size(canvasW, canvasH));
            canvas.Arrange(new Rect(0, 0, canvasW, canvasH));
            canvas.UpdateLayout();

            var rtb = new RenderTargetBitmap(
                (int)Math.Round(canvasW), (int)Math.Round(canvasH),
                96, 96, PixelFormats.Pbgra32);
            rtb.Render(canvas);

            int stride = rtb.PixelWidth * 4;
            pixels = new byte[stride * rtb.PixelHeight];
            rtb.CopyPixels(pixels, stride, 0);
            pw = rtb.PixelWidth;
            ph = rtb.PixelHeight;

            originX = pad;
            originY = pad;
        }

        /// <summary>
        /// Ein Punkt auf der Kernkontur plus die nach aussen zeigende Normale.
        /// </summary>
        private readonly struct ContourPoint
        {
            public ContourPoint(Point p, Point normal, string label)
            {
                P = p;
                Normal = normal;
                Label = label;
            }

            public Point P { get; }
            public Point Normal { get; }
            public string Label { get; }
        }

        /// <summary>
        /// Laeuft die Kontur des Kerns ab - gerade Kanten und Eckenboegen -
        /// und liefert jeweils Punkt und Aussennormale.
        /// </summary>
        private static List<ContourPoint> Contour(double w, double h, double r, int perCorner = 9)
        {
            var list = new List<ContourPoint>();

            // Gerade Kanten (jeweils 5 Stuetzstellen)
            for (int i = 0; i <= 4; i++)
            {
                double t = i / 4.0;
                double x = r + (w - 2 * r) * t;
                list.Add(new ContourPoint(new Point(x, 0), new Point(0, -1), "oben"));
                list.Add(new ContourPoint(new Point(x, h), new Point(0, 1), "unten"));
            }
            for (int i = 1; i < 4; i++)
            {
                double t = i / 4.0;
                double y = r + (h - 2 * r) * t;
                list.Add(new ContourPoint(new Point(0, y), new Point(-1, 0), "links"));
                list.Add(new ContourPoint(new Point(w, y), new Point(1, 0), "rechts"));
            }

            // Eckenboegen
            var corners = new (double cx, double cy, double from, string name)[]
            {
                (r, r, 180, "Ecke oben links"),
                (w - r, r, 270, "Ecke oben rechts"),
                (w - r, h - r, 0, "Ecke unten rechts"),
                (r, h - r, 90, "Ecke unten links"),
            };

            foreach (var (cx, cy, from, name) in corners)
            {
                for (int i = 0; i <= perCorner; i++)
                {
                    double a = (from + 90.0 * i / perCorner) * Math.PI / 180.0;
                    double nx = Math.Cos(a);
                    double ny = Math.Sin(a);
                    list.Add(new ContourPoint(
                        new Point(cx + r * nx, cy + r * ny),
                        new Point(nx, ny),
                        name));
                }
            }

            return list;
        }

        /// <summary>
        /// Laesst die Kontur ablaufen und vergleicht die Deckkraft an jeder
        /// Stelle mit der geraden Kante. Genau dieser Vergleich war zweimal
        /// negativ - und ich hatte es nicht gemessen.
        /// </summary>
        private static List<string> Measure(byte[] px, int pw, int ph,
            double ox, double oy, double coreW, double coreH, bool apple)
        {
            var result = new List<string>();

            double band = RingGlow.WidthFor(Thickness);
            double radius = RingBand.CoreCornerRadius(coreW, coreH, apple);

            // Tiefen im Band, jeweils als Anteil der Bandbreite.
            double[] depths = { 0.25, 0.5, 0.75 };

            // Referenz: gerade Kante, obere Kante, mittig.
            var reference = new double[depths.Length];
            for (int i = 0; i < depths.Length; i++)
            {
                reference[i] = Sample(px, pw, ph, ox + coreW / 2 + 0.37, oy - band * depths[i]);
            }

            result.Add("Deckkraft auf der geraden Kante (Helligkeit 0..255):");
            for (int i = 0; i < depths.Length; i++)
            {
                result.Add($"   bei {depths[i] * 100,3:F0} % der Bandbreite: {reference[i],6:F1}");
            }

            if (reference[0] < 40)
            {
                result.Add("FEHLER: Der Saum ist an der geraden Kante zu schwach - Band fehlt.");
            }

            // Jetzt die ganze Kontur.
            List<ContourPoint> contour = Contour(coreW, coreH, radius);

            var proGruppe = new Dictionary<string, List<double>>();
            double maxAbw = 0;
            string maxOrt = "";
            int luecken = 0;

            foreach (ContourPoint cp in contour)
            {
                for (int i = 0; i < depths.Length; i++)
                {
                    double x = ox + cp.P.X + cp.Normal.X * band * depths[i];
                    double y = oy + cp.P.Y + cp.Normal.Y * band * depths[i];
                    double a = Sample(px, pw, ph, x, y);

                    if (a < reference[i] * 0.5) luecken++;

                    double abw = reference[i] > 1 ? Math.Abs(a - reference[i]) / reference[i] : 0;
                    if (abw > maxAbw)
                    {
                        maxAbw = abw;
                        maxOrt = $"{cp.Label} bei {depths[i] * 100:F0} %";
                    }

                    string g = cp.Label;
                    if (!proGruppe.TryGetValue(g, out List<double>? vals))
                    {
                        vals = new List<double>();
                        proGruppe[g] = vals;
                    }
                    if (i == 1) vals.Add(a);
                }
            }

            result.Add("");
            result.Add("Deckkraft entlang der Kontur (bei 50 % der Bandbreite):");
            foreach (var kv in proGruppe)
            {
                double sum = 0, min = double.MaxValue, max = 0;
                foreach (double v in kv.Value)
                {
                    sum += v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
                double avg = sum / kv.Value.Count;
                result.Add(string.Format(CultureInfo.InvariantCulture,
                    "   {0,-17}: Mittel {1,6:F1}   min {2,6:F1}   max {3,6:F1}",
                    kv.Key, avg, min, max));
            }

            result.Add("");
            result.Add($"Groesste Abweichung zur geraden Kante: {maxAbw * 100,5:F1} % bei {maxOrt}");
            if (maxAbw > 0.25)
            {
                result.Add("FEHLER: Der Saum ist an dieser Stelle deutlich anders als auf der Kante.");
            }
            if (luecken > 0)
            {
                result.Add($"FEHLER: {luecken} Messpunkte sind auffaellig schwach - Luecke im Band.");
            }

            // Nichts darf weit ausserhalb des Bands noch sichtbar sein.
            double weit = 0;
            foreach (ContourPoint cp in contour)
            {
                double x = ox + cp.P.X + cp.Normal.X * band * 2.2;
                double y = oy + cp.P.Y + cp.Normal.Y * band * 2.2;
                weit = Math.Max(weit, Sample(px, pw, ph, x, y));
            }
            result.Add($"Aussen bei 220 % der Bandbreite: {weit,6:F1} (muss klein sein)");
            if (weit > reference[2] * 1.2 + 5)
            {
                result.Add("FEHLER: Der Saum reicht zu weit nach aussen - Verlauf sitzt falsch.");
            }

            return result;
        }

        /// <summary>
        /// Helligkeit eines Punktes, 3x3 gemittelt gegen Kantenrauschen.
        /// Alpha ist nach dem Vektorisieren immer 255, deshalb ueber die
        /// Helligkeit gemessen.
        /// </summary>
        private static double Sample(byte[] px, int pw, int ph, double x, double y)
        {
            int ix = (int)Math.Round(x);
            int iy = (int)Math.Round(y);
            if (ix < 1 || iy < 1 || ix >= pw - 1 || iy >= ph - 1) return -1;

            double sum = 0;
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int p = ((iy + dy) * pw + (ix + dx)) * 4;
                    byte b = px[p];
                    byte g = px[p + 1];
                    byte r = px[p + 2];
                    sum += (r + g + b) / 3.0;
                    n++;
                }
            }
            return sum / n;
        }

        private static void SavePng(string path, byte[] pixels, int pw, int ph)
        {
            var bmp = BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Pbgra32, null, pixels, pw * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using FileStream fs = File.Create(path);
            encoder.Save(fs);
        }
    }
}
