using System;
using System.Windows;
using Xunit;

namespace WinRinglight.Tests
{
    /// <summary>
    /// Der Saum muss dieselbe Form wie der Ring haben und nach innen UND
    /// aussen auslaufen - wie der alte Weichzeichner.
    ///
    /// Entstanden aus drei Fehlversuchen: erst lag der Saum innen, dann
    /// aussen als Rechteck, dann als Band mit falsch liegendem Verlauf.
    /// </summary>
    public class RingHaloTests
    {
        private const double W = 900;
        private const double H = 500;
        private const double T = 60;

        [Theory]
        [InlineData(60, 8.0)]      // Radius = min(40, 24) = 24 -> sigma 8
        [InlineData(10, 1.3333)]   // Radius = 4 -> sigma 1,33
        [InlineData(200, 13.333)]  // Radius = 40 (gedeckelt) -> sigma 13,33
        public void Sigma_EntsprichtDemAltenWeichzeichner(double thickness, double expected)
        {
            Assert.Equal(expected, RingHalo.SigmaFor(thickness), 3);
        }

        [Fact]
        public void Schichten_NehmenNachAussenAb()
        {
            var layers = RingHalo.Layers(8);

            Assert.Equal(9, layers.Length);

            // Aussen liegende Schichten sind schwaecher als innen liegende.
            for (int i = 1; i < layers.Length; i++)
            {
                Assert.True(layers[i].Spread > layers[i - 1].Spread,
                    "Die Schichten muessen nach aussen wandern");
                Assert.True(layers[i].Alpha <= layers[i - 1].Alpha + 1e-9,
                    $"Schicht {i} ist staerker als die innere");
            }

            // Die innerste Schicht muss kraeftig sein - sonst fehlt der Saum
            // direkt an der Kante.
            Assert.True(layers[0].Alpha > 0.4, $"Innerste Schicht nur {layers[0].Alpha:F2}");
        }

        [Fact]
        public void GeschichteteDeckkraft_EntsprichtDerGaußKurve()
        {
            // An der Ringkante muss die Gesamtdeckkraft 0,5 sein (so wie beim
            // Weichzeichner), bei 3 sigma praktisch null.
            var layers = RingHalo.Layers(8);

            double beiKante = 0;
            double rest = 0;

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].Spread >= 0)
                {
                    // Deckkraft aller Schichten, die die Kante ueberdecken
                    rest = rest * (1 - layers[i].Alpha);
                }
            }
            beiKante = 1 - rest;

            Assert.InRange(beiKante, 0.40, 0.60);

            // Nach der letzten Schicht darf nichts mehr kommen.
            Assert.Equal(0.0, layers[^1].Alpha, 2);
        }

        [Fact]
        public void Kontur_LiegtInDerMitteDerRingdicke()
        {
            // Eine gestrichene Kontur mit Dicke T um die Mittellinie ergibt
            // einen Ring von 0..T. Die Mittellinie muss also bei T/2 liegen.
            var geo = RingHalo.Centerline(0, 0, W, H, 110, T);

            Assert.NotNull(geo);
            Assert.False(geo.IsEmpty());

            Rect b = geo.Bounds;
            Assert.Equal(30, b.X, 3);          // T/2
            Assert.Equal(30, b.Y, 3);
            Assert.Equal(W - T, b.Width, 3);   // 840
            Assert.Equal(H - T, b.Height, 3);  // 440
        }

        [Fact]
        public void Kontur_IstEingefroren()
        {
            var geo = RingHalo.Centerline(0, 0, W, H, 110, T);
            Assert.True(geo.IsFrozen);
        }

        [Fact]
        public void ZuKleinerRing_LiefertLeereKontur()
        {
            // Ein Ring, der duenner ist als die Flaeche, hat keine Mittellinie.
            var geo = RingHalo.Centerline(0, 0, 40, 20, 5, T);
            Assert.True(geo.IsEmpty());
        }

        [Theory]
        [InlineData(true, 228.8)]
        [InlineData(false, 3.0)]
        public void Eckenradius_ProStil(bool apple, double expected)
        {
            Assert.Equal(expected, RingHalo.CoreCornerRadius(1040, 1040, apple), 1);
        }

        [Fact]
        public void FertigerSaum_HatNeunSchichtenMitPinselUndStrich()
        {
            var built = RingHalo.Build(
                System.Windows.Media.Color.FromRgb(255, 214, 170), 0, 0, W, H, 110, T);

            Assert.Equal(9, built.Count);

            foreach (var layer in built)
            {
                Assert.NotNull(layer.Geometry);
                Assert.NotNull(layer.Brush);
                Assert.True(layer.Brush.IsFrozen, "Pinsel muss eingefroren sein");

                // Strichdicke = Ringdicke + 2 * Abstand. Die innerste Schicht
                // ist dicker als der Ring, weil sie auch nach innen wirkt.
                Assert.True(layer.StrokeThickness >= T, "Strich duenner als der Ring");
            }

            // Die aeusserste Schicht muss deutlich dicker sein - sie traegt
            // den Saum nach aussen.
            Assert.True(built[^1].StrokeThickness > T + 20,
                $"Aeusserste Schicht nur {built[^1].StrokeThickness:F1}");
        }

        [Fact]
        public void OhneFlaeche_KeinSaum()
        {
            Assert.Empty(RingHalo.Build(
                System.Windows.Media.Color.FromRgb(255, 255, 255), 0, 0, 0, 0, 0, T));
            Assert.Empty(RingHalo.Build(
                System.Windows.Media.Color.FromRgb(255, 255, 255), 0, 0, W, H, 110, 0));
        }

        [Fact]
        public void NormalCdf_IstBeiNullEinHalb()
        {
            Assert.Equal(0.5, RingHalo.NormalCdf(0), 6);
            Assert.Equal(0.8413, RingHalo.NormalCdf(1), 3);
            Assert.Equal(0.0228, RingHalo.NormalCdf(-2), 3);
        }

        [Fact]
        public void Erf_IstSymmetrisch()
        {
            Assert.Equal(-RingHalo.Erf(1.3), RingHalo.Erf(-1.3), 9);
            Assert.Equal(0.0, RingHalo.Erf(0), 9);
        }
    }
}
