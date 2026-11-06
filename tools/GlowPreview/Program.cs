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
    /// Aufruf:  GlowPreview <ausgabeordner>
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

                byte[] pixels; int pw; int ph; double originX; double originY;
                Render(apple, w, h, out pixels, out pw, out ph, out originX, out originY);
                SavePng(Path2(outDir, name + ".png"), pixels, pw, ph);

                lines.Add($"--- {(apple ? "Apple/Squircle" : "eckig")}: Kern {w} x {h} ---");
                foreach (string m in Measure(pixels, pw, ph, originX, originY, w, h, apple))
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
                RingBand.Build(color, 1.0, coreW, coreH, radius, band, pad, pad);

            foreach (RingBand.PieceVisual piece in pieces)
            {
                canvas.Children.Add(new Path
                {
                    Data = piece.Geometry,
                    Fill = piece.Brush,
                    IsHitTestVisible = false,
                });
            }

            // Kern als Rahmen, damit das Bild dem echten Fenster entspricht.
            var core = new Border
            {
                Width = coreW,
                Height = coreH,
                BorderThickness = new Thickness(Thickness),
                BorderBrush = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(radius),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(core, pad);
            Canvas.SetTop(core, pad);
            canvas.Children.Add(core);

            canvas.Measure(new Size(canvasW, canvasH));
            canvas.Arrange(new Rect(0, 0, canvasW, canvasH));
            canvas.UpdateLayout();

            const double scale = 1.0;
            var rtb = new RenderTargetBitmap(
                (int)Math.Round(canvasW * scale), (int)Math.Round(canvasH * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
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
        /// Prueft, ob der Schein rundherum GLEICHMAESSIG ist: an jeder Stelle
        /// des Bandes muss die gleiche Deckkraft herrschen, egal ob gerade
        /// Kante oder Ecke. Genau das war zweimal kaputt.
        /// </summary>
        private static List<string> Measure(byte[] px, int pw, int ph,
            double ox, double oy, double coreW, double coreH, bool apple)
        {
            var result = new List<string>();

            double band = RingGlow.WidthFor(Thickness);
            double radius = RingBand.CoreCornerRadius(coreW, coreH, apple);

            // Referenz: Mitte der oberen geraden Kante, halbe Bandbreite aussen.
            double refA = SampleAlpha(px, pw, ph,
                ox + coreW / 2, oy - band / 2);
            result.Add($"Deckkraft obere Kante (halbe Bandbreite): {refA,6:F1} / 255");

            if (refA < 20)
            {
                result.Add("FEHLER: Der Saum ist an der geraden Kante zu schwach - Band fehlt.");
            }

            // Die vier Eckpunkte, jeweils auf der Winkelhalbierenden.
            double diag = band / 2 * Math.Sqrt(2);
            var corners = new (string Name, double X, double Y)[]
            {
                ("oben links",     ox + radius - diag, oy + radius - diag),
                ("oben rechts",    ox + coreW - radius + diag, oy + radius - diag),
                ("unten rechts",   ox + coreW - radius + diag, oy + coreH - radius + diag),
                ("unten links",    ox + radius - diag, oy + coreH - radius + diag),
            };

            foreach (var (name, cx, cy) in corners)
            {
                double a = SampleAlpha(px, pw, ph, cx, cy);
                double abw = refA > 0 ? Math.Abs(a - refA) / refA : 1;
                string urteil = abw <= 0.25 ? "ok" : "FEHLER";
                result.Add($"Ecke {name,-13}: {a,6:F1} / 255   Abweichung {abw * 100,5:F1} %   {urteil}");
            }

            // Kein harter Klecks: an der Ecke darf die Deckkraft nicht
            // schlagartig abfallen (dann sitzt der Verlauf falsch).
            double ausserhalb = SampleAlpha(px, pw, ph,
                ox + radius - band * 1.6, oy + radius - band * 1.6);
            result.Add($"weit ausserhalb der Ecke: {ausserhalb,6:F1} / 255 (muss klein sein)");
            if (ausserhalb > refA * 0.5)
            {
                result.Add("FEHLER: Der Saum reicht zu weit nach aussen - Verlauf sitzt falsch.");
            }

            return result;
        }

        private static double SampleAlpha(byte[] px, int pw, int ph, double x, double y)
        {
            int ix = (int)Math.Round(x);
            int iy = (int)Math.Round(y);
            if (ix < 1 || iy < 1 || ix >= pw - 1 || iy >= ph - 1) return 0;

            // 3x3 mitteln gegen Kanten-Rauschen
            double sum = 0;
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int p = ((iy + dy) * pw + (ix + dx)) * 4;
                    // Pbgra32: B,G,R,A
                    byte a = px[p + 3];
                    byte b = px[p];
                    byte g = px[p + 1];
                    byte r = px[p + 2];

                    // "Deckkraft" hier = Helligkeit der Ringfarbe ueber dem
                    // dunklen Hintergrund. Der Kanal Alpha ist durch das
                    // Vektorisieren immer 255, deshalb ueber die Helligkeit.
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
