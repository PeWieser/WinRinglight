using System;
using System.Windows;
using System.Windows.Media;

// Das Projekt nutzt WPF UND WinForms. Diese Namen sind dadurch mehrdeutig -
// Aliase statt einzelner Qualifizierungen, sonst kommt der Fehler zurueck.
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Geometry = System.Windows.Media.Geometry;

namespace WinRinglight
{
    /// <summary>
    /// Eine Schicht des Scheins. <see cref="Spread"/> ist der Abstand, um den
    /// die Kontur gegenueber dem Ring nach aussen (und innen) waechst.
    /// </summary>
    public readonly struct HaloLayer
    {
        public HaloLayer(double spread, double alpha)
        {
            Spread = spread;
            Alpha = alpha;
        }

        public double Spread { get; }
        public double Alpha { get; }
    }

    /// <summary>
    /// Erzeugt den weichen Rand als **gestrichene Kontur des Rings selbst** -
    /// nicht als vier Rechtecke, nicht als Band ausserhalb.
    ///
    /// Warum dieser Weg:
    /// Der alte Glow war ein <c>BlurEffect</c> auf einer <c>Border</c>, die
    /// genau die Ringform hatte. Ein Weichzeichner verschiebt nichts und
    /// aendert keine Form - er verteilt die vorhandene Form nach innen UND
    /// aussen. Genau das fehlte bei meinen Versuchen: der Schein lag nur
    /// aussen, war zu stark und passte an den Ecken nicht.
    ///
    /// Eine gestrichene Kontur hat dieselbe Form wie der Ring
    /// (abgerundetes Rechteck, gleicher Radius) und waechst gleichmaessig
    /// nach beiden Seiten. Mehrere solche Konturen uebereinander ergeben
    /// das Gauß-Profil des Weichzeichners.
    ///
    /// Rechenweg: Der Weichzeichner erzeugt an der Ringkante die Deckkraft
    /// 0,5 und faellt nach aussen wie die Normalverteilung ab. Schicht k
    /// deckt alles bis zum Abstand d_k ab. Damit die geschichtete Deckkraft
    /// genau dieser Kurve entspricht, wird die Alpha-Kette so gewaehlt, dass
    /// 1 - Produkt(1 - a_j) = A_k gilt. Ohne diese Korrektur waere der Saum
    /// an der Kante zu stark.
    /// </summary>
    public static class RingHalo
    {
        /// <summary>Deckkraft des Scheins - wie vor dem Umbau (Border.Opacity).</summary>
        public const double ElementOpacity = 0.55;

        /// <summary>Eckenradius im Stil "eckig".</summary>
        public const double SquareCornerRadius = 3.0;

        /// <summary>Eckenradius im Stil "weich", relativ zur kuerzeren Seite.</summary>
        public const double AppleCornerFactor = 0.22;

        /// <summary>
        /// Abstande der Schichten, als Vielfaches von sigma.
        /// Bei 3,1 sigma ist der Gauß praktisch bei null (0,1 %).
        /// </summary>
        private static readonly double[] Zs = { 0.35, 0.70, 1.00, 1.35, 1.70, 2.05, 2.40, 2.75, 3.10 };

        /// <summary>
        /// Eckenradius des Kerns. Einzige Quelle fuer Kern UND Schein -
        /// sonst laufen die Konturen auseinander.
        /// </summary>
        public static double CoreCornerRadius(double width, double height, bool isAppleStyle)
        {
            double minSide = Math.Min(width, height);
            if (minSide <= 0) return 0;

            double r = isAppleStyle ? minSide * AppleCornerFactor : SquareCornerRadius;
            return Math.Max(0, Math.Min(r, minSide / 2.0));
        }

        /// <summary>
        /// sigma des alten Weichzeichners. Er hatte Radius =
        /// min(40, Dicke * 0,4); WPF rechnet Radius in sigma = Radius / 3 um.
        /// </summary>
        public static double SigmaFor(double thickness)
        {
            if (thickness < 1) thickness = 1;
            if (thickness > 400) thickness = 400;

            double radius = Math.Min(40.0, thickness * 0.4);
            return radius / 3.0;
        }

        /// <summary>
        /// Die Schichten des Scheins. Aussen nach innen sortiert.
        /// </summary>
        public static HaloLayer[] Layers(double sigma)
        {
            if (sigma <= 0) return Array.Empty<HaloLayer>();

            int n = Zs.Length;

            // D[i] = Deckkraft, die am AUSSENRAND von Schicht i noch stehen muss.
            // Der Weichzeichner faellt nach aussen wie die Normalverteilung ab.
            var D = new double[n + 1];
            for (int i = 0; i < n; i++)
            {
                D[i] = NormalCdf(-Zs[i]);
            }
            D[n] = 0.0;   // hinter der aeussersten Schicht: nichts mehr

            // Von aussen nach innen, damit die Kette aufgeht:
            // Schicht i liegt ueber Schicht i+1 (weiter aussen).
            // Gewuenscht: am Rand von i steht D[i].
            //   D[i] = a_i + D[i+1] * (1 - a_i)
            //   => a_i = (D[i] - D[i+1]) / (1 - D[i+1])
            var layers = new HaloLayer[n];
            for (int i = 0; i < n; i++)
            {
                double innen = 1.0 - D[i + 1];
                double a = innen <= 1e-12 ? D[i] : (D[i] - D[i + 1]) / innen;
                if (a < 0) a = 0;
                if (a > 1) a = 1;

                layers[i] = new HaloLayer(Zs[i] * sigma, a);
            }

            return layers;
        }

        /// <summary>
        /// Die Mittellinie des Rings: ein abgerundetes Rechteck genau in der
        /// Mitte der Ringdicke. Wird es mit der Ringdicke gestrichen, entsteht
        /// exakt der Ring (aussen Radius R, innen R - Dicke).
        /// </summary>
        public static Geometry Centerline(double x, double y, double width, double height,
            double cornerRadius, double thickness)
        {
            double half = thickness / 2.0;
            double w = width - thickness;
            double h = height - thickness;

            if (w <= 0 || h <= 0)
            {
                return Geometry.Empty;
            }

            double r = Math.Max(0, Math.Min(cornerRadius - half, Math.Min(w, h) / 2.0));

            var geo = new RectangleGeometry(new Rect(x + half, y + half, w, h), r, r);
            geo.Freeze();
            return geo;
        }

        /// <summary>
        /// Baut die fertigen Schichten zum Zeichnen.
        /// </summary>
        /// <param name="color">Farbe des Rings.</param>
        /// <param name="x">Linke Kante der Ringflaeche (Kernkasten), im Ziel-Koordinatensystem.</param>
        /// <param name="y">Obere Kante der Ringflaeche.</param>
        /// <param name="width">Breite der Ringflaeche (Kernkasten, ohne Schein).</param>
        /// <param name="height">Hoehe der Ringflaeche.</param>
        /// <param name="cornerRadius">Eckenradius des Kerns.</param>
        /// <param name="thickness">Ringdicke.</param>
        public static System.Collections.Generic.List<LayerVisual> Build(
            Color color, double x, double y, double width, double height,
            double cornerRadius, double thickness, double elementOpacity = ElementOpacity)
        {
            var result = new System.Collections.Generic.List<LayerVisual>();
            if (width <= 0 || height <= 0 || thickness <= 0) return result;

            Geometry centerline = Centerline(x, y, width, height, cornerRadius, thickness);
            if (centerline == Geometry.Empty) return result;

            double sigma = SigmaFor(thickness);

            if (elementOpacity < 0) elementOpacity = 0;
            if (elementOpacity > 1) elementOpacity = 1;

            foreach (HaloLayer layer in Layers(sigma))
            {
                // Die 0,55 des alten Aufbaus sassen auf dem ganzen Glow-Element.
                // Hier wird der Wert in jede Schicht gerechnet, damit der Kern
                // deckend bleiben kann (er lag frueher in einem eigenen Element).
                var brush = new SolidColorBrush(
                    Color.FromArgb(PremultiplyAlpha(layer.Alpha * elementOpacity), color.R, color.G, color.B));
                brush.Freeze();

                result.Add(new LayerVisual(centerline, brush, thickness + 2 * layer.Spread));
            }

            return result;
        }

        private static byte PremultiplyAlpha(double a)
        {
            if (a < 0) a = 0;
            if (a > 1) a = 1;
            return (byte)Math.Round(a * 255.0);
        }

        /// <summary>Fertige Schicht: Kontur, Pinsel, Strichdicke.</summary>
        public sealed class LayerVisual
        {
            public LayerVisual(Geometry geometry, Brush brush, double strokeThickness)
            {
                Geometry = geometry;
                Brush = brush;
                StrokeThickness = strokeThickness;
            }

            public Geometry Geometry { get; }
            public Brush Brush { get; }
            public double StrokeThickness { get; }
        }

        /// <summary>Standardnormalverteilung, kumuliert.</summary>
        public static double NormalCdf(double z) => 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0)));

        /// <summary>
        /// Fehlerfunktion nach Abramowitz und Stegun 7.1.26.
        /// Genauigkeit ca. 1e-7 - fuer einen Farbverlauf mehr als genug.
        /// </summary>
        public static double Erf(double x)
        {
            double ax = Math.Abs(x);
            double t = 1.0 / (1.0 + 0.3275911 * ax);
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t
                              - 0.284496736) * t + 0.254829592) * t * Math.Exp(-ax * ax);
            return x >= 0 ? y : -y;
        }
    }
}
