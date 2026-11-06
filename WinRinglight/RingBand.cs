using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

// Das Projekt nutzt WPF UND WinForms. Color, Point, Brush und Geometry sind
// dadurch mehrdeutig. Aliase statt einzelner Qualifizierungen - sonst kommt
// der Fehler beim naechsten neuen Typ zurueck.
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Geometry = System.Windows.Media.Geometry;

namespace WinRinglight
{
    /// <summary>Auf welcher Seite einer Kante liegt das Stueck?</summary>
    public enum BandSide
    {
        /// <summary>Ausserhalb - leuchtet auf den Desktop.</summary>
        Outward,

        /// <summary>Innerhalb - leuchtet in das Loch hinein.</summary>
        Inward,
    }

    /// <summary>Welcher Teil des Bands ist das?</summary>
    public enum BandPieceKind
    {
        Top,
        Right,
        Bottom,
        Left,
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft,
    }

    /// <summary>
    /// Ein Stueck des Saums. Die vier geraden Stuecke sind Rechtecke, die vier
    /// Ecken sind Viertelringe um <see cref="ArcCenter"/>.
    /// </summary>
    public readonly struct BandPiece
    {
        public BandPiece(BandPieceKind kind, BandSide side, Rect bounds, Point arcCenter)
        {
            Kind = kind;
            Side = side;
            Bounds = bounds;
            ArcCenter = arcCenter;
        }

        public BandPieceKind Kind { get; }
        public BandSide Side { get; }

        /// <summary>Aussenkasten des Stuecks.</summary>
        public Rect Bounds { get; }

        /// <summary>Mittelpunkt des Eckenradius. Nur bei den vier Ecken benutzt.</summary>
        public Point ArcCenter { get; }

        public bool IsCorner =>
            Kind == BandPieceKind.TopLeft || Kind == BandPieceKind.TopRight ||
            Kind == BandPieceKind.BottomRight || Kind == BandPieceKind.BottomLeft;
    }

    /// <summary>
    /// Baut den weichen Saum um die Ringkanten herum.
    ///
    /// Warum auf BEIDEN Seiten: Der fruehere <c>BlurEffect</c> sass auf einem
    /// Element in Form des Rings. Ein Blur wirkt beidseitig - nach aussen auf
    /// den Desktop UND nach innen in das Loch. Nur aussen zu bauen laesst den
    /// Ring innen hart aussehen.
    ///
    /// Warum 0,5 an der Kante: Der Blur einer harten Kante ergibt ein
    /// Fehlerintegral. An der Kante selbst ist es genau 0,5 - die Kante ist
    /// halb durchsichtig, nicht deckend. Wer mit 1,0 beginnt, bekommt einen
    /// doppelt so starken Saum.
    ///
    /// Die Werte: (1/2) * erfc(d / (sigma * sqrt(2))), sigma = Bandbreite / 3.
    /// </summary>
    public static class RingBand
    {
        /// <summary>Eckenradius im Stil "eckig".</summary>
        public const double SquareCornerRadius = 3.0;

        /// <summary>Eckenradius im Stil "weich", relativ zur kuerzeren Seite.</summary>
        public const double AppleCornerFactor = 0.22;

        /// <summary>Stuetzstellen, relativ zur Bandbreite.</summary>
        public static readonly double[] DefaultStops = { 0.0, 0.20, 0.45, 0.70, 1.0 };

        /// <summary>
        /// Deckkraft **nach aussen**, bezogen auf die Deckkraft des Rings.
        /// Faellt von 0,5 an der Kante auf praktisch null.
        /// </summary>
        public static readonly double[] OutwardAlphas = { 0.5000, 0.2743, 0.0885, 0.0179, 0.0013 };

        /// <summary>
        /// Deckkraft **nach innen** - das Spiegelbild. Steigt von 0,5 an der
        /// Kante auf fast 1,0, weil diesseits der Kante der ganze Ring liegt.
        /// </summary>
        public static readonly double[] InwardAlphas = { 0.5000, 0.7257, 0.9115, 0.9821, 0.9987 };

        /// <summary>
        /// Eckenradius des Kerns. Einzige Quelle fuer Kern UND Saum - sonst
        /// laufen die beiden Konturen auseinander und es entstehen Kanten,
        /// die nicht zusammenpassen.
        /// </summary>
        public static double CoreCornerRadius(double width, double height, bool isAppleStyle)
        {
            double minSide = Math.Min(width, height);
            if (minSide <= 0) return 0;

            double r = isAppleStyle ? minSide * AppleCornerFactor : SquareCornerRadius;
            return Math.Max(0, Math.Min(r, minSide / 2.0));
        }

        /// <summary>
        /// Die acht Stuecke eines Bands um ein abgerundetes Rechteck.
        /// </summary>
        /// <param name="rect">Die Kante, um die das Band liegt.</param>
        /// <param name="radius">Eckenradius dieser Kante.</param>
        /// <param name="band">Breite des Bands.</param>
        public static BandPiece[] Pieces(Rect rect, double radius, double band, BandSide side)
        {
            if (rect.Width <= 0 || rect.Height <= 0 || band <= 0) return Array.Empty<BandPiece>();

            double minSide = Math.Min(rect.Width, rect.Height);
            double r = Math.Max(0, Math.Min(radius, minSide / 2.0));
            double b = Math.Max(0.01, band);

            double x0 = rect.X, y0 = rect.Y, w = rect.Width, h = rect.Height;
            double innerW = Math.Max(0, w - 2 * r);
            double innerH = Math.Max(0, h - 2 * r);

            return new[]
            {
                // Gerade Stuecke: zwischen den Tangentialpunkten der Ecken.
                new BandPiece(BandPieceKind.Top,    side, new Rect(x0 + r, y0 - b, innerW, b), new Point(0, 0)),
                new BandPiece(BandPieceKind.Bottom, side, new Rect(x0 + r, y0 + h, innerW, b), new Point(0, 0)),
                new BandPiece(BandPieceKind.Left,   side, new Rect(x0 - b, y0 + r, b, innerH), new Point(0, 0)),
                new BandPiece(BandPieceKind.Right,  side, new Rect(x0 + w, y0 + r, b, innerH), new Point(0, 0)),

                // Ecken: Quadrat mit Kantenlaenge (Radius + Band). Der Schnitt
                // mit dem Band ergibt darin exakt den Viertelring.
                new BandPiece(BandPieceKind.TopLeft, side,
                    new Rect(x0 - b, y0 - b, r + b, r + b), new Point(x0 + r, y0 + r)),
                new BandPiece(BandPieceKind.TopRight, side,
                    new Rect(x0 + w - r, y0 - b, r + b, r + b), new Point(x0 + w - r, y0 + r)),
                new BandPiece(BandPieceKind.BottomRight, side,
                    new Rect(x0 + w - r, y0 + h - r, r + b, r + b), new Point(x0 + w - r, y0 + h - r)),
                new BandPiece(BandPieceKind.BottomLeft, side,
                    new Rect(x0 - b, y0 + h - r, r + b, r + b), new Point(x0 + r, y0 + h - r)),
            };
        }

        /// <summary>
        /// Die Bandflaeche als Ganzes: aeusseres abgerundetes Rechteck minus
        /// das Rechteck selbst. Damit stimmt die Kontur ueberall exakt.
        /// </summary>
        public static Geometry BandGeometry(Rect rect, double radius, double band)
        {
            double minSide = Math.Min(rect.Width, rect.Height);
            double r = Math.Max(0, Math.Min(radius, minSide / 2.0));
            double b = Math.Max(0.01, band);

            var outer = new RectangleGeometry(
                new Rect(rect.X - b, rect.Y - b, rect.Width + 2 * b, rect.Height + 2 * b),
                r + b, r + b);
            var inner = new RectangleGeometry(rect, r, r);

            Geometry g = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            g.Freeze();
            return g;
        }

        /// <summary>Fertiges Stueck: Geometrie plus eingefrorenem Pinsel.</summary>
        public sealed class PieceVisual
        {
            public PieceVisual(BandPiece piece, Geometry geometry, Brush brush)
            {
                Piece = piece;
                Geometry = geometry;
                Brush = brush;
            }

            public BandPiece Piece { get; }
            public Geometry Geometry { get; }
            public Brush Brush { get; }
        }

        /// <summary>
        /// Baut beide Saeume: nach aussen um die Aussenkante, nach innen um die
        /// Innenkante des Rings.
        /// </summary>
        /// <param name="width">Aussenbreite des Kerns.</param>
        /// <param name="height">Aussenenhoehe des Kerns.</param>
        /// <param name="thickness">Ringdicke.</param>
        /// <param name="cornerRadius">Eckenradius der Aussenkante.</param>
        /// <param name="band">Breite des Saums.</param>
        /// <param name="offsetX">Versatz ins Zielkoordinatensystem.</param>
        /// <param name="offsetY">Versatz ins Zielkoordinatensystem.</param>
        public static List<PieceVisual> Build(
            Color color,
            double opacity,
            double width,
            double height,
            double thickness,
            double cornerRadius,
            double band,
            double offsetX = 0,
            double offsetY = 0)
        {
            var result = new List<PieceVisual>(16);
            if (width <= 0 || height <= 0 || band <= 0 || thickness <= 0) return result;

            double r = Math.Max(0, Math.Min(cornerRadius, Math.Min(width, height) / 2.0));

            // Nach innen hoechstens bis zur Ringmitte - darueber hinaus gibt es
            // keine Kante mehr, von der aus der Schein kommen koennte.
            double inwardBand = Math.Max(0.01, Math.Min(band, thickness));

            AddBand(result, color, opacity, new Rect(0, 0, width, height), r, band,
                BandSide.Outward, offsetX, offsetY);

            var innerRect = new Rect(thickness, thickness,
                Math.Max(0.01, width - 2 * thickness), Math.Max(0.01, height - 2 * thickness));
            double innerRadius = Math.Max(0, r - thickness);

            AddBand(result, color, opacity, innerRect, innerRadius, inwardBand,
                BandSide.Inward, offsetX, offsetY);

            return result;
        }

        private static void AddBand(List<PieceVisual> result, Color color, double opacity,
            Rect rect, double radius, double band, BandSide side, double offsetX, double offsetY)
        {
            Geometry whole = BandGeometry(rect, radius, band);

            foreach (BandPiece piece in Pieces(rect, radius, band, side))
            {
                var clip = new RectangleGeometry(piece.Bounds);
                Geometry geometry = new CombinedGeometry(GeometryCombineMode.Intersect, whole, clip);

                // Versatz ins Zielkoordinatensystem. Damit liegen alle
                // Koordinaten im positiven Bereich - ein Path mit rein
                // negativen Koordinaten wird von WPF nicht zuverlaessig
                // gezeichnet.
                if (offsetX != 0 || offsetY != 0)
                {
                    var shift = new TranslateTransform(offsetX, offsetY);
                    shift.Freeze();
                    geometry.Transform = shift;
                }

                geometry.Freeze();

                Brush brush = piece.IsCorner
                    ? CornerBrush(color, opacity, piece.ArcCenter, radius, band, side, offsetX, offsetY)
                    : StraightBrush(color, opacity, piece.Kind, side);

                result.Add(new PieceVisual(piece, geometry, brush));
            }
        }

        /// <summary>
        /// Gerades Stueck: Der Verlauf laeuft ueber die Bandbreite, weil der
        /// Kasten des Stuecks genau das Band ist. Startpunkt = an der Kante.
        /// </summary>
        private static Brush StraightBrush(Color color, double opacity, BandPieceKind kind, BandSide side)
        {
            double[] alphas = side == BandSide.Outward ? OutwardAlphas : InwardAlphas;

            return kind switch
            {
                BandPieceKind.Top => Gradient(color, opacity, alphas, new Point(0, 1), new Point(0, 0)),
                BandPieceKind.Bottom => Gradient(color, opacity, alphas, new Point(0, 0), new Point(0, 1)),
                BandPieceKind.Left => Gradient(color, opacity, alphas, new Point(1, 0), new Point(0, 0)),
                _ => Gradient(color, opacity, alphas, new Point(0, 0), new Point(1, 0)),
            };
        }

        private static Brush Gradient(Color color, double opacity, double[] alphas, Point from, Point to)
        {
            var brush = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                StartPoint = from,
                EndPoint = to,
                SpreadMethod = GradientSpreadMethod.Pad,
            };

            double o = Clamp01(opacity);
            for (int i = 0; i < DefaultStops.Length; i++)
            {
                brush.GradientStops.Add(new GradientStop(WithAlpha(color, alphas[i] * o), DefaultStops[i]));
            }

            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Ecke: radial vom Eckmittelpunkt. Der sichtbare Bereich liegt
        /// zwischen Radius R und R+b - die Stuetzstellen werden deshalb auf
        /// [R/(R+b), 1] umgerechnet, sonst laege er im fast durchsichtigen
        /// Teil und der Saum waere an den Ecken weg.
        ///
        /// Achtung: MappingMode.Absolute bezieht sich auf das ELEMENT, nicht
        /// auf die Geometrie. Der Mittelpunkt muss den Versatz mitmachen,
        /// sonst sitzt der Verlauf neben der Ecke und es entsteht ein harter
        /// Klops.
        /// </summary>
        private static Brush CornerBrush(Color color, double opacity, Point center,
            double innerRadius, double band, BandSide side, double offsetX, double offsetY)
        {
            double outerRadius = innerRadius + band;
            if (outerRadius <= 0) outerRadius = 1;

            double start = innerRadius / outerRadius;
            double[] alphas = side == BandSide.Outward ? OutwardAlphas : InwardAlphas;

            var brush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = new Point(center.X + offsetX, center.Y + offsetY),
                GradientOrigin = new Point(center.X + offsetX, center.Y + offsetY),
                RadiusX = outerRadius,
                RadiusY = outerRadius,
                SpreadMethod = GradientSpreadMethod.Pad,
            };

            double o = Clamp01(opacity);
            for (int i = 0; i < DefaultStops.Length; i++)
            {
                double position = start + (1.0 - start) * DefaultStops[i];
                if (position < 0) position = 0;
                if (position > 1) position = 1;

                brush.GradientStops.Add(new GradientStop(
                    WithAlpha(color, alphas[i] * o), position));
            }

            brush.Freeze();
            return brush;
        }

        private static Color WithAlpha(Color color, double alpha)
        {
            return Color.FromArgb((byte)Math.Round(Clamp01(alpha) * 255.0), color.R, color.G, color.B);
        }

        private static double Clamp01(double v)
        {
            if (double.IsNaN(v)) return 0;
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }
    }
}
