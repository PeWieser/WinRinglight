using System;
using System.Windows;
using System.Windows.Media;

// Das Projekt nutzt WPF UND WinForms. Dadurch sind Color und Point mehrdeutig
// (System.Drawing vs. System.Windows). Aliase statt einzelner Qualifizierungen -
// sonst kommt der Fehler beim naechsten neuen Typ zurueck.
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;

namespace WinRinglight
{
    /// <summary>
    /// Erzeugt den weichen Rand des Ringlichts als Farbverlauf statt als
    /// <c>BlurEffect</c>.
    ///
    /// Warum: Ein <c>BlurEffect</c> wirkt in WPF auf die **Bounds** des Elements.
    /// Eine <c>Border</c>, die den Monitor umrahmt, ist so gross wie der Monitor -
    /// es werden also alle Pixel verwischt, obwohl nur ein 60 px breiter Streifen
    /// sichtbar ist. Im CI-Benchmark kostete das 16x so viel wie der Rest
    /// zusammen, und der <c>BitmapCache</c> half nicht (sondern machte es
    /// 18 % teurer).
    ///
    /// Ein Verlauf wird dagegen pro Pixel gerechnet, ohne Zwischenflaeche -
    /// und liefert fuer einen geraden Streifen dieselbe Optik.
    /// </summary>
    public static class RingGlow
    {
        /// <summary>
        /// Standard-Abstufung relativ zur Ringdicke.
        /// Entspricht einem Gauß mit sigma = Radius/3 (WPF-Standard), das
        /// Profil ist bei 100 % Breite praktisch auf null abgefallen.
        /// </summary>
        public static readonly double[] DefaultStops = { 0.0, 0.20, 0.45, 0.70, 1.0 };

        /// <summary>
        /// Deckkraft an den Stützstellen. Aussen (1.0) = 0, innen (0.0) = voll.
        /// Die Werte folgen dem Gauß-Profil aus dem Benchmark.
        /// </summary>
        public static readonly double[] DefaultAlphas = { 1.0, 0.835, 0.402, 0.110, 0.0 };

        /// <summary>
        /// Baut einen Verlauf, der von <paramref name="color"/> nach transparent
        /// ausläuft. Richtung wird über Start/Endpunkt gesteuert.
        /// </summary>
        /// <param name="color">Farbe des Rings.</param>
        /// <param name="opacity">Gesamtdeckkraft (früher Border.Opacity).</param>
        /// <param name="from">Startpunkt im Element (0..1 relativ).</param>
        /// <param name="to">Endpunkt im Element (0..1 relativ).</param>
        public static LinearGradientBrush Create(
            Color color,
            double opacity,
            Point from,
            Point to)
        {
            return Create(color, opacity, from, to, DefaultStops, DefaultAlphas);
        }

        public static LinearGradientBrush Create(
            Color color,
            double opacity,
            Point from,
            Point to,
            double[] offsets,
            double[] alphas)
        {
            if (offsets == null || alphas == null || offsets.Length != alphas.Length || offsets.Length < 2)
            {
                throw new ArgumentException("Verlauf braucht mindestens zwei passende Stuetzstellen.");
            }

            if (opacity < 0) opacity = 0;
            if (opacity > 1) opacity = 1;

            var brush = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                StartPoint = from,
                EndPoint = to,
                SpreadMethod = GradientSpreadMethod.Pad,
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                double a = alphas[i] * opacity;
                if (a < 0) a = 0;
                if (a > 1) a = 1;

                byte alpha = (byte)Math.Round(a * 255.0);
                brush.GradientStops.Add(new GradientStop(
                    Color.FromArgb(alpha, color.R, color.G, color.B),
                    offsets[i]));
            }

            brush.Freeze();
            return brush;
        }


        /// <summary>
        /// Fuellung fuer das Loch: was der fruehere Blur aus dem Kern heraus
        /// in das Innere streute. Ueber die volle Lochbreite aufgetragen,
        /// damit ein einzelner Verlauf von Kante zu Kante reicht.
        /// </summary>
        /// <param name="color">Farbe des Rings.</param>
        /// <param name="opacity">Deckkraft des Saums.</param>
        /// <param name="width">Lochbreite.</param>
        /// <param name="height">Lochhoehe.</param>
        /// <param name="band">Breite des Saums.</param>
        public static Brush CoreInside(Color color, double opacity,
            double width, double height, double band)
        {
            if (width <= 0 || height <= 0) return Brushes.Transparent;

            var brush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
                SpreadMethod = GradientSpreadMethod.Pad,
            };

            double halfMin = Math.Min(width, height) / 2.0;
            double loch = Math.Max(0.01, Math.Min(1.0, band / halfMin));

            // Ab hier ist das Loch voll mit Ringlicht (fast deckend) - das ist
            // der Anteil, den der Blur nicht erreichte.
            double o = opacity;
            if (o < 0) o = 0;
            if (o > 1) o = 1;

            // Innen (nahe dem Rand) fast voll, zur Mitte hin auf 0.
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb((byte)Math.Round(0.99 * o * 255), color.R, color.G, color.B), 0.0));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb((byte)Math.Round(0.98 * o * 255), color.R, color.G, color.B), 1.0 - 4 * loch));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb((byte)Math.Round(0.91 * o * 255), color.R, color.G, color.B), 1.0 - 3 * loch));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb((byte)Math.Round(0.72 * o * 255), color.R, color.G, color.B), 1.0 - 2 * loch));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb((byte)Math.Round(0.50 * o * 255), color.R, color.G, color.B), 1.0 - loch));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0, color.R, color.G, color.B), 1.0));

            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Wie weit der Glow nach innen reicht, in DIP, bei gegebener Ringdicke.
        /// Das ist die Breite, die der Verlauf braucht.
        /// </summary>
        public static double WidthFor(double thickness)
        {
            if (thickness < 1) thickness = 1;
            if (thickness > 400) thickness = 400;

            // Frueher: BlurEffect.Radius = min(40, thickness * 0.4).
            // WPF rechnet sigma = Radius / 3. Ein Gauß ist nach 3*sigma
            // praktisch bei null (0.1 %). Das ist die Breite, die der
            // Verlauf abdecken muss - NICHT das Dreifache.
            double blurRadius = Math.Min(40.0, thickness * 0.4);
            double sigma = blurRadius / 3.0;
            return Math.Max(6.0, sigma * 3.0);
        }
    }

    /// <summary>
    /// Merkt sich, wofuer der Glow zuletzt gebaut wurde. Damit laesst sich
    /// verhindern, dass er bei jeder Mausbewegung neu entsteht - das waere
    /// genau das Ruckeln, das vermieden werden soll.
    /// </summary>
    public sealed class GlowState
    {
        public GlowState(double width, double height, double thickness, double kelvin)
        {
            Width = width;
            Height = height;
            Thickness = thickness;
            Kelvin = kelvin;
        }

        public double Width { get; }
        public double Height { get; }
        public double Thickness { get; }
        public double Kelvin { get; }
    }

    public static class GlowRebuildPolicy
    {
        /// <summary>Ab dieser Kelvin-Differenz ist die Aenderung sichtbar.</summary>
        public const double KelvinTolerance = 50.0;

        /// <summary>Ab dieser Groessendifferenz in DIP lohnt sich ein Neubau.</summary>
        public const double SizeTolerance = 1.0;

        public static bool NeedsRebuild(GlowState? last, GlowState now)
        {
            if (now == null) return false;
            if (last == null) return true;

            if (System.Math.Abs(now.Width - last.Width) > SizeTolerance) return true;
            if (System.Math.Abs(now.Height - last.Height) > SizeTolerance) return true;
            if (System.Math.Abs(now.Thickness - last.Thickness) > SizeTolerance) return true;
            if (System.Math.Abs(now.Kelvin - last.Kelvin) > KelvinTolerance) return true;

            return false;
        }
    }

}