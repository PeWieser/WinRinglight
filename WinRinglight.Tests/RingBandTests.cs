using System;
using System.Windows;
using Xunit;

namespace WinRinglight.Tests
{
    /// <summary>
    /// Der Saum muss ein konstant breites Band **ausserhalb** der Kernkante
    /// sein und dieselbe runde Kontur haben wie der Kern.
    ///
    /// Diese Tests sind aus zwei echten Fehlern entstanden:
    /// 1. Der Saum lag INNEN (unter dem Kern) - auf den geraden Seiten
    ///    unsichtbar, an den runden Ecken als eckige Leuchtstreifen sichtbar.
    /// 2. Es gab nur vier gerade Rechtecke - die passen nicht zu einem
    ///    abgerundeten Kern.
    /// </summary>
    public class RingBandTests
    {
        private const double W = 1900;
        private const double H = 1040;

        [Fact]
        public void AchtStuecke_VierGeradeVierEcken()
        {
            var pieces = RingBand.Pieces(new Rect(0, 0, W, H), 228, 24, BandSide.Outward);

            Assert.Equal(8, pieces.Length);
            Assert.Equal(4, Array.FindAll(pieces, p => !p.IsCorner).Length);
            Assert.Equal(4, Array.FindAll(pieces, p => p.IsCorner).Length);
        }

        [Fact]
        public void BandLiegtAusserhalbDerKernkante()
        {
            // Das war Fehler 1: Der Saum begann bei der Kernmitte statt an der
            // Aussenkante und lag damit unter dem Kern.
            var pieces = RingBand.Pieces(new Rect(0, 0, W, H), 228, 24, BandSide.Outward);

            // Das obere Stueck liegt vollstaendig ueber dem Kern (y <= 0).
            var top = Array.Find(pieces, p => p.Kind == BandPieceKind.Top);
            Assert.True(top.Bounds.Bottom <= 0.001, $"Oben liegt bei {top.Bounds.Bottom}, muss <= 0 sein");

            // Das untere vollstaendig darunter.
            var bottom = Array.Find(pieces, p => p.Kind == BandPieceKind.Bottom);
            Assert.True(bottom.Bounds.Top >= H - 0.001, "Unten liegt im Kern");

            // Links und rechts ebenso.
            var left = Array.Find(pieces, p => p.Kind == BandPieceKind.Left);
            Assert.True(left.Bounds.Right <= 0.001, "Links liegt im Kern");
            var right = Array.Find(pieces, p => p.Kind == BandPieceKind.Right);
            Assert.True(right.Bounds.Left >= W - 0.001, "Rechts liegt im Kern");
        }

        [Fact]
        public void BandIstUeberallGleichBreit()
        {
            var pieces = RingBand.Pieces(new Rect(0, 0, W, H), 228, 24, BandSide.Outward);

            foreach (var p in pieces)
            {
                if (p.IsCorner) continue;

                double breite = p.Kind == BandPieceKind.Top || p.Kind == BandPieceKind.Bottom
                    ? p.Bounds.Height : p.Bounds.Width;

                Assert.Equal(24, breite, 3);
            }
        }

        [Fact]
        public void EckenReichenBisInDieEcke()
        {
            var pieces = RingBand.Pieces(new Rect(0, 0, W, H), 228, 24, BandSide.Outward);

            var tl = Array.Find(pieces, p => p.Kind == BandPieceKind.TopLeft);
            Assert.Equal(-24, tl.Bounds.X, 3);
            Assert.Equal(-24, tl.Bounds.Y, 3);

            var br = Array.Find(pieces, p => p.Kind == BandPieceKind.BottomRight);
            Assert.Equal(W, br.Bounds.Right - 24, 3);
            Assert.Equal(H, br.Bounds.Bottom - 24, 3);
        }

        [Fact]
        public void EckenStueckeHabenIhrenMittelpunktAmRadius()
        {
            double r = 228;
            var pieces = RingBand.Pieces(new Rect(0, 0, W, H), r, 24, BandSide.Outward);

            var tl = Array.Find(pieces, p => p.Kind == BandPieceKind.TopLeft);
            Assert.Equal(r, tl.ArcCenter.X, 3);
            Assert.Equal(r, tl.ArcCenter.Y, 3);

            var br = Array.Find(pieces, p => p.Kind == BandPieceKind.BottomRight);
            Assert.Equal(W - r, br.ArcCenter.X, 3);
            Assert.Equal(H - r, br.ArcCenter.Y, 3);
        }

        [Fact]
        public void GeradeStueckeSindTangentenlang()
        {
            // Zwischen den beiden Ecken bleibt genau W - 2r bzw. H - 2r.
            double r = 228;
            var pieces = RingBand.Pieces(new Rect(0, 0, W, H), r, 24, BandSide.Outward);

            var top = Array.Find(pieces, p => p.Kind == BandPieceKind.Top);
            Assert.Equal(W - 2 * r, top.Bounds.Width, 3);
            Assert.Equal(r, top.Bounds.X, 3);
        }

        [Fact]
        public void KeinStueckIstLeer()
        {
            var pieces = RingBand.Pieces(new Rect(0, 0, W, H), 228, 24, BandSide.Outward);

            foreach (var p in pieces)
            {
                Assert.True(p.Bounds.Width > 0, $"{p.Kind} hat Breite 0");
                Assert.True(p.Bounds.Height > 0, $"{p.Kind} hat Hoehe 0");
            }
        }

        [Fact]
        public void OhneGroesse_GibtEsKeineStuecke()
        {
            Assert.Empty(RingBand.Pieces(new Rect(0, 0, 0, 0), 0, 24, BandSide.Outward));
            Assert.Empty(RingBand.Pieces(new Rect(0, 0, W, H), 228, 0, BandSide.Outward));
        }

        [Theory]
        [InlineData(true, 228.8)]    // Apple: 1040 * 0.22 = 228.8
        [InlineData(false, 3)]       // eckig
        public void Eckenradius_ProStil(bool apple, double erwartet)
        {
            double r = RingBand.CoreCornerRadius(W, H, apple);
            Assert.Equal(erwartet, r, 1);
        }

        [Fact]
        public void EckenradiusWirdNieGroesserAlsDieHalbeSeite()
        {
            // Kleine Ringe (z. B. ein stark verkleinertes Fenster) duerfen
            // keinen Radius bekommen, der groesser als die Flaeche ist.
            double r = RingBand.CoreCornerRadius(100, 40, true);
            Assert.True(r <= 20, $"Radius {r} ist groesser als die halbe Hoehe");
        }

        [Fact]
        public void FertigeStuecke_HabenPinselUndGeometrie()
        {
            var built = RingBand.Build(System.Windows.Media.Color.FromRgb(255, 200, 140), 1.0, W, H, 60, 228, 24);

            Assert.Equal(8, built.Count);

            foreach (var p in built)
            {
                Assert.NotNull(p.Geometry);
                Assert.NotNull(p.Brush);
                Assert.True(p.Geometry.IsFrozen, "Geometrie muss eingefroren sein");
                Assert.True(p.Brush.IsFrozen, "Pinsel muss eingefroren sein");
                Assert.False(p.Geometry.IsEmpty(), "Geometrie darf nicht leer sein");
            }
        }

        [Fact]
        public void BandIstNichtNurEinPunkt()
        {
            // Fehler 2 war: an den Ecken ein gerades Rechteck. Ein Band mit
            // Radius muss dort eine RUNDE Begrenzung haben, also eine Geometrie
            // mit Bogen und nicht nur ein Rechteck.
            var built = RingBand.Build(System.Windows.Media.Color.FromRgb(255, 200, 140), 1.0, W, H, 60, 228, 24);

            var ecke = built.Find(p => p.Piece.Kind == BandPieceKind.TopLeft);
            Assert.NotNull(ecke);
            Assert.True(ecke!.Geometry.Bounds.Width > 10, "Ecke ist zu klein");
            Assert.True(ecke.Geometry.Bounds.Height > 10, "Ecke ist zu klein");
        }
    }

    /// <summary>
    /// Der eigentliche Beweis: Das Band muss die Flaeche zwischen Kern und
    /// Aussenkontur VOLLSTAENDIG fuellen - ohne Luecke und ohne Ueberlappung.
    ///
    /// Eine Luecke waere ein sichtbarer Spalt, eine Ueberlappung ein hellerer
    /// Streifen (zwei halbtransparente Flaechen uebereinander). Beides ist
    /// genau die Art Fehler, die auf dem Bildschirm auffaellt.
    /// </summary>
    public class RingBandAbdeckungTests
    {
        private const double W = 800;
        private const double H = 400;
        private const double BAND = 24;

        [Theory]
        [InlineData(120.0)]   // Apple-artig
        [InlineData(3.0)]     // eckig
        [InlineData(0.0)]     // ganz ohne Rundung
        public void BandIstLueckenlosUndUeberlapptNicht(double radius)
        {
            var pieces = RingBand.Pieces(new Rect(0, 0, W, H), radius, BAND, BandSide.Outward);

            // Punkte auf dem Band testen: Abstand zur Kernkontur zwischen 0 und BAND.
            // Ein Punkt "gehoert zum Band", wenn er ausserhalb des Kerns liegt
            // und hoechstens BAND von ihm entfernt ist.
            int drin = 0, daneben = 0;

            for (double x = -BAND; x <= W + BAND; x += 6.0)
            {
                for (double y = -BAND; y <= H + BAND; y += 6.0)
                {
                    // Liegt der Punkt im Band?
                    double d = AbstandZurKontur(x, y, W, H, radius);

                    bool imBand = d > 0.5 && d < BAND - 0.5;
                    if (!imBand) continue;

                    drin++;

                    // Wichtig: mit Toleranz pruefen. Die Stuecke stossen an den
                    // Nahtstellen exakt aneinander - ein Punkt genau auf der
                    // Naht liegt geometrisch in keinem oder in beiden. Das ist
                    // keine Luecke und keine Ueberlappung, sondern die Naht.
                    //
                    // Echte Luecke   = Punkt liegt auch mit EPS daneben nirgends.
                    // Echte Ueberlappung = Punkt liegt mit -EPS in zwei Stuecken.
                    const double EPS = 0.01;

                    int aussen = 0, innen = 0;
                    foreach (var p in pieces)
                    {
                        if (Enthaelt(p.Bounds, x, y, EPS)) aussen++;
                        if (Enthaelt(p.Bounds, x, y, -EPS)) innen++;
                    }

                    if (aussen == 0) daneben++;      // Luecke
                    if (innen > 1) daneben++;        // Ueberlappung (heller Streifen)
                }
            }

            Assert.True(drin > 100, $"Zu wenige Testpunkte ({drin}) - Test misst nichts");
            Assert.Equal(0, daneben);
        }

        private static bool Enthaelt(Rect r, double x, double y, double eps)
        {
            return x >= r.X - eps && x <= r.Right + eps &&
                   y >= r.Y - eps && y <= r.Bottom + eps;
        }

        /// <summary>
        /// Vorzeichenbehafteter Abstand zur Kernkontur: positiv = ausserhalb.
        /// Fuer abgerundete Rechtecke: Abstand zur naechsten Kante bzw. zum
        /// naechsten Eckmittelpunkt.
        /// </summary>
        private static double AbstandZurKontur(double x, double y, double w, double h, double r)
        {
            // Im Kern?
            bool imKern = x > 0 && x < w && y > 0 && y < h;
            if (r > 0)
            {
                // Ecken: ausserhalb des Viertelkreises?
                double[] cx = { r, w - r, w - r, r };
                double[] cy = { r, r, h - r, h - r };
                for (int i = 0; i < 4; i++)
                {
                    bool inEckQuadrant = (cx[i] == r ? x < r : x > w - r) && (cy[i] == r ? y < r : y > h - r);
                    if (!inEckQuadrant) continue;

                    double dx = x - cx[i];
                    double dy = y - cy[i];
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    return dist - r;
                }
            }

            if (imKern) return -1;

            // Abstand zur naechsten Rechteckkante
            double aussen = Math.Max(Math.Max(-x, x - w), Math.Max(-y, y - h));
            return aussen;
        }
    }

}
