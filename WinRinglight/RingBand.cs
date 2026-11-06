using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

// Das Projekt nutzt WPF UND WinForms. Color und Point sind dadurch mehrdeutig.
// Aliase statt einzelner Qualifizierungen - sonst kommt der Fehler beim
// naechsten neuen Typ zurueck.
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace WinRinglight
{
    /// <summary>Welcher Teil des Ringsaums ist das?</summary>
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
    /// Ein Stueck des Ringsaums. Die vier geraden Stuecke sind Rechtecke, die
    /// vier Ecken sind Viertelringe um <see cref="ArcCenter"/>.
    /// Alle Koordinaten sind ringlokal: (0,0) ist die linke obere Ecke des
    /// Kerns, negative Werte liegen ausserhalb.
    /// </summary>
    public readonly struct BandPiece
    {
        public BandPiece(BandPieceKind kind, Rect bounds, Point arcCenter)
        {
            Kind = kind;
            Bounds = bounds;
            ArcCenter = arcCenter;
        }

        public BandPieceKind Kind { get; }

        /// <summary>Aussenkasten des Stuecks, ringlokal.</summary>
        public Rect Bounds { get; }

        /// <summary>Mittelpunkt des Eckenradius. Nur bei den vier Ecken benutzt.</summary>
        public Point ArcCenter { get; }

        public bool IsCorner =>
            Kind == BandPieceKind.TopLeft || Kind == BandPieceKind.TopRight ||
            Kind == BandPieceKind.BottomRight || Kind == BandPieceKind.BottomLeft;
    }

    /// <summary>
    /// Baut den weichen Ringsaum als konstant breites Band **ausserhalb** der
    /// Kernkante - mit derselben runden Kontur wie der Kern.
    ///
    /// Warum ueberhaupt so: Der alte Ansatz nahm vier gerade Rechtecke. Sobald
    /// der Kern abgerundete Ecken hat, passen gerade Rechtecke nicht mehr dazu -
    /// an den Ecken stehen Leuchtstreifen heraus, und auf den geraden Seiten
    /// liegt der Saum unter dem Kern. Deshalb: acht Stuecke (vier gerade,
    /// vier Ecken), die sich lueckenlos zu einem einzigen Band ergaenzen.
    /// </summary>
    public static class RingBand
    {
        /// <summary>Eckenradius im Stil "eckig".</summary>
        public const double SquareCornerRadius = 3.0;

        /// <summary>Eckenradius im Stil "weich", relativ zur kuerzeren Seite.</summary>
        public const double AppleCornerFactor = 0.22;

        /// <summary>
        /// Eckenradius des Kerns. Einzige Quelle fuer Kern UND Saum - sonst
        /// laufen die beiden Konturen auseinander und es entstehen wieder
        /// Kanten, die nicht zusammenpassen.
        /// </summary>
        public static double CoreCornerRadius(double width, double height, bool isAppleStyle)
        {
            double minSide = Math.Min(width, height);
            if (minSide <= 0) return 0;

            double r = isAppleStyle ? minSide * AppleCornerFactor : SquareCornerRadius;
            return Math.Max(0, Math.Min(r, minSide / 2.0));
        }

        /// <summary>
        /// Die acht Stuecke des Bands. Sie ueberlappen sich nicht und decken
        /// das Band vollstaendig ab.
        /// </summary>
        /// <param name="width">Breite des Kerns in DIP.</param>
        /// <param name="height">Hoehe des Kerns in DIP.</param>
        /// <param name="cornerRadius">Eckenradius des Kerns.</param>
        /// <param name="band">Breite des Saums nach aussen.</param>
        public static BandPiece[] Pieces(double width, double height, double cornerRadius, double band)
        {
            if (width <= 0 || height <= 0 || band <= 0) return Array.Empty<BandPiece>();

            double minSide = Math.Min(width, height);

            // Ein Radius groesser als die halbe Seite ist geometrisch unmoeglich,
            // ein Band breiter als die halbe Seite ebenso unsinnig.
            double r = Math.Max(0, Math.Min(cornerRadius, minSide / 2.0));
            double b = Math.Max(0.01, band);

            double innerW = Math.Max(0, width - 2 * r);
            double innerH = Math.Max(0, height - 2 * r);

            var list = new List<BandPiece>(8)
            {
                // Gerade Stuecke: zwischen den Tangentialpunkten der Ecken.
                new BandPiece(BandPieceKind.Top, new Rect(r, -b, innerW, b), new Point(0, 0)),
                new BandPiece(BandPieceKind.Bottom, new Rect(r, height, innerW, b), new Point(0, 0)),
                new BandPiece(BandPieceKind.Left, new Rect(-b, r, b, innerH), new Point(0, 0)),
                new BandPiece(BandPieceKind.Right, new Rect(width, r, b, innerH), new Point(0, 0)),

                // Ecken: Quadrat mit Kantenlaenge (Radius + Band). Der Schnitt
                // mit dem Band ergibt darin exakt den Viertelring.
                new BandPiece(BandPieceKind.TopLeft,
                    new Rect(-b, -b, r + b, r + b), new Point(r, r)),
                new BandPiece(BandPieceKind.TopRight,
                    new Rect(width - r, -b, r + b, r + b), new Point(width - r, r)),
                new BandPiece(BandPieceKind.BottomRight,
                    new Rect(width - r, height - r, r + b, r + b), new Point(width - r, height - r)),
                new BandPiece(BandPieceKind.BottomLeft,
                    new Rect(-b, height - r, r + b, r + b), new Point(r, height - r)),
            };

            return list.ToArray();
        }

        /// <summary>
        /// Fertiges Stueck: Geometrie plus eingefrorenem Pinsel.
        /// </summary>
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
        /// Baut alle acht Stuecke fertig zum Zeichnen.
        /// </summary>
        /// <param name="color">Farbe des Rings.</param>
        /// <param name="opacity">Gesamtdeckkraft des Saums.</param>
        public static List<PieceVisual> Build(
            Color color,
            double opacity,
            double width,
            double height,
            double cornerRadius,
            double band)
        {
            var result = new List<PieceVisual>(8);
            BandPiece[] pieces = Pieces(width, height, cornerRadius, band);
            if (pieces.Length == 0) return result;

            double r = Math.Max(0, Math.Min(cornerRadius, Math.Min(width, height) / 2.0));
            double b = Math.Max(0.01, band);

            // Das Band als Ganzes: aeusseres abgerundetes Rechteck minus Kern.
            // Damit stimmt die Kontur an jeder Stelle exakt mit dem Kern ueberein.
            var outer = new RectangleGeometry(new Rect(-b, -b, width + 2 * b, height + 2 * b), r + b, r + b);
            var inner = new RectangleGeometry(new Rect(0, 0, width, height), r, r);
            var fullBand = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            fullBand.Freeze();

            foreach (BandPiece piece in pieces)
            {
                var clip = new RectangleGeometry(piece.Bounds);
                var geometry = new CombinedGeometry(GeometryCombineMode.Intersect, fullBand, clip);
                geometry.Freeze();

                Brush brush = piece.IsCorner
                    ? CornerBrush(color, opacity, piece.ArcCenter, r, b)
                    : StraightBrush(color, opacity, piece.Kind);

                result.Add(new PieceVisual(piece, geometry, brush));
            }

            return result;
        }

        /// <summary>
        /// Gerades Stueck: Der Verlauf laeuft exakt ueber die Bandbreite, weil
        /// der Kasten des Stuecks genau das Band ist.
        /// </summary>
        private static Brush StraightBrush(Color color, double opacity, BandPieceKind kind)
        {
            // Startpunkt = deckend, Endpunkt = durchsichtig.
            return kind switch
            {
                // Oben liegt aussen bei y = -b, innen bei y = 0.
                BandPieceKind.Top => RingGlow.Create(color, opacity, new Point(0, 1), new Point(0, 0)),
                BandPieceKind.Bottom => RingGlow.Create(color, opacity, new Point(0, 0), new Point(0, 1)),
                BandPieceKind.Left => RingGlow.Create(color, opacity, new Point(1, 0), new Point(0, 0)),
                _ => RingGlow.Create(color, opacity, new Point(0, 0), new Point(1, 0)),
            };
        }

        /// <summary>
        /// Ecke: radial vom Eckmittelpunkt nach aussen. Der Verlauf beginnt
        /// nicht bei 0, sondern bei Radius R - deshalb werden die Stuetzstellen
        /// auf den Bereich [R, R+b] umgerechnet. Ohne diese Umrechnung laege der
        /// ganze sichtbare Bereich im fast durchsichtigen Teil und der Saum
        /// waere an den Ecken praktisch weg.
        /// </summary>
        private static Brush CornerBrush(Color color, double opacity, Point center, double innerRadius, double band)
        {
            double outerRadius = innerRadius + band;
            if (outerRadius <= 0) outerRadius = 1;

            // Ab hier ist der Verlauf deckend (Kernkante), ab 1.0 durchsichtig.
            double start = innerRadius / outerRadius;

            double[] offsets = RingGlow.DefaultStops;
            double[] alphas = RingGlow.DefaultAlphas;

            var brush = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = center,
                GradientOrigin = center,
                RadiusX = outerRadius,
                RadiusY = outerRadius,
                SpreadMethod = GradientSpreadMethod.Pad,
            };

            double o = opacity;
            if (o < 0) o = 0;
            if (o > 1) o = 1;

            for (int i = 0; i < offsets.Length; i++)
            {
                double position = start + (1.0 - start) * offsets[i];
                if (position < 0) position = 0;
                if (position > 1) position = 1;

                double a = alphas[i] * o;
                if (a < 0) a = 0;
                if (a > 1) a = 1;

                brush.GradientStops.Add(new GradientStop(
                    Color.FromArgb((byte)Math.Round(a * 255.0), color.R, color.G, color.B),
                    position));
            }

            brush.Freeze();
            return brush;
        }
    }
}
