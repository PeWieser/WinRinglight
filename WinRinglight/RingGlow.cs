using System;
using System.Windows;
using System.Windows.Media;

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
}
