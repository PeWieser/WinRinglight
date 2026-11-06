using System;
using System.Windows;
using System.Windows.Media;
using WinRinglight;
using Xunit;

// Siehe RingGlow.cs: WPF + WinForms im selben Projekt machen Color/Point mehrdeutig.
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace WinRinglight.Tests
{
    public class RingGlowTests
    {
        private static readonly Color White = Colors.White;

        [Fact]
        public void Verlauf_HatGleicheAnzahlStuetzstellen()
        {
            var b = RingGlow.Create(White, 1.0, new Point(0, 0), new Point(1, 0));
            Assert.Equal(RingGlow.DefaultStops.Length, b.GradientStops.Count);
        }

        [Fact]
        public void Verlauf_IstVonInnenDeckendNachAussenTransparent()
        {
            var b = RingGlow.Create(White, 1.0, new Point(0, 0), new Point(1, 0));

            Assert.Equal(255, b.GradientStops[0].Color.A);              // innen voll
            Assert.Equal(0, b.GradientStops[^1].Color.A);               // aussen weg

            // monoton fallend
            for (int i = 1; i < b.GradientStops.Count; i++)
            {
                Assert.True(b.GradientStops[i].Color.A <= b.GradientStops[i - 1].Color.A,
                    $"Alpha steigt bei Stuetzstelle {i}");
            }
        }

        [Fact]
        public void Verlauf_StuetzstellenSindStrengAufsteigend()
        {
            for (int i = 1; i < RingGlow.DefaultStops.Length; i++)
            {
                Assert.True(RingGlow.DefaultStops[i] > RingGlow.DefaultStops[i - 1]);
            }
            Assert.Equal(0.0, RingGlow.DefaultStops[0]);
            Assert.Equal(1.0, RingGlow.DefaultStops[^1]);
        }

        [Fact]
        public void Deckkraft_WirdKorrektSkaliert()
        {
            var full = RingGlow.Create(White, 1.0, new Point(0, 0), new Point(1, 0));
            var half = RingGlow.Create(White, 0.5, new Point(0, 0), new Point(1, 0));

            Assert.Equal(255, full.GradientStops[0].Color.A);
            Assert.Equal(128, half.GradientStops[0].Color.A);   // 0.5 * 255 = 127.5 -> 128
        }

        [Theory]
        [InlineData(-1.0)]
        [InlineData(2.0)]
        [InlineData(double.NaN)]
        public void UnsinnigeDeckkraft_WirdBegrenzt(double opacity)
        {
            var b = RingGlow.Create(White, opacity, new Point(0, 0), new Point(1, 0));
            byte a = b.GradientStops[0].Color.A;
            Assert.InRange(a, 0, 255);
        }

        [Fact]
        public void Farbe_BleibtErhalten()
        {
            var color = Color.FromRgb(255, 214, 170);
            var b = RingGlow.Create(color, 1.0, new Point(0, 0), new Point(1, 0));

            foreach (var stop in b.GradientStops)
            {
                Assert.Equal(255, stop.Color.R);
                Assert.Equal(214, stop.Color.G);
                Assert.Equal(170, stop.Color.B);
            }
        }

        [Fact]
        public void Verlauf_IstEingefroren()
        {
            // Freeze -> WPF muss den Pinsel nicht bei jeder Aenderung neu pruefen.
            var b = RingGlow.Create(White, 1.0, new Point(0, 0), new Point(1, 0));
            Assert.True(b.IsFrozen);
        }

        [Fact]
        public void UngueltigeStuetzstellen_WerdenAbgelehnt()
        {
            Assert.Throws<ArgumentException>(() =>
                RingGlow.Create(White, 1.0, new Point(0, 0), new Point(1, 0),
                    new double[] { 0.0 }, new double[] { 1.0 }));

            Assert.Throws<ArgumentException>(() =>
                RingGlow.Create(White, 1.0, new Point(0, 0), new Point(1, 0),
                    new double[] { 0.0, 1.0 }, new double[] { 1.0 }));
        }

        [Theory]
        [InlineData(60, 24)]     // Standarddicke: min(40,24) = 24 -> Breite 24
        [InlineData(10, 6)]      // duenn: 10*0.4 = 4 -> unter Minimum 6
        [InlineData(200, 40)]    // dick: min(40,80) = 40 -> Breite 40
        public void GlowBreite_FolgtDerRingdicke(double thickness, double expected)
        {
            Assert.Equal(expected, RingGlow.WidthFor(thickness), 3);
        }

        [Fact]
        public void GlowBreite_IstImmerPositiv()
        {
            Assert.True(RingGlow.WidthFor(0) > 0);
            Assert.True(RingGlow.WidthFor(-50) > 0);
            Assert.True(RingGlow.WidthFor(10000) <= 40);
        }

        /// <summary>
        /// Das Profil muss ungefaehr einem Gauß mit sigma = radius/3 folgen,
        /// sonst sieht der Rand anders aus als vorher.
        /// </summary>
        [Fact]
        public void Profil_EntsprichtUngefaehrDemAltenBlur()
        {
            // WPF-Blur: sigma = Radius / 3. Bei Dicke 60 ist der Radius 24,
            // also sigma = 8 px. Die Stuetzstellen muessen auf der Glowbreite
            // (= 24 px) genau diesem Gauß folgen.
            double width = RingGlow.WidthFor(60);
            const double sigma = 8.0;

            for (int i = 0; i < RingGlow.DefaultStops.Length; i++)
            {
                double d = RingGlow.DefaultStops[i] * width;
                double gauss = Math.Exp(-(d * d) / (2 * sigma * sigma));
                double actual = RingGlow.DefaultAlphas[i];

                Assert.True(Math.Abs(gauss - actual) < 0.05,
                    $"Stuetzstelle {i}: Verlauf {actual:F2}, Gauß {gauss:F2}, " +
                    $"Abweichung zu gross");
            }
        }
    }
}
