using System;
using System.Collections.Generic;
using System.Windows;
using WinRinglight;
using Xunit;

namespace WinRinglight.Tests
{
    public class ScreenLayoutTests
    {
        private const double Pad = ScreenLayout.GlowPaddingDip;

        private static MonitorInfo Mon(double x, double y, double w, double h, double taskbar = 40)
            => new MonitorInfo(
                new Rect(x, y, w, h),
                new Rect(x, y, w, h - taskbar));

        /// <summary>Ein 1080p-Monitor, 100 % Skalierung, nur dieser Monitor.</summary>
        [Fact]
        public void EinMonitor_100Prozent_FensterIstEtwasGroesserAlsDerMonitor()
        {
            var monitors = new[] { Mon(0, 0, 1920, 1080) };

            var l = ScreenLayout.Compute(monitors, new[] { 0 }, false, false, 1.0);

            Assert.False(l.IsFallback);
            Assert.Single(l.Rings);
            // Fenster = Arbeitsfläche + 2 * Rand
            Assert.Equal(1920 + 2 * Pad, l.WindowPixels.Width, 3);
            Assert.Equal(1040 + 2 * Pad, l.WindowPixels.Height, 3);
            // Der Ring liegt genau um den Rand eingerückt.
            Assert.Equal(Pad, l.Rings[0].X, 3);
            Assert.Equal(Pad, l.Rings[0].Y, 3);
            Assert.Equal(1920, l.Rings[0].Width, 3);
            Assert.Equal(1040, l.Rings[0].Height, 3);
        }

        /// <summary>Der Kern der Phase: 1 von 3 Monitoren -> Fenster nur so gross wie 1 Monitor.</summary>
        [Fact]
        public void EinVonDreiMonitoren_FensterIstDeutlichKleinerAlsDerDesktop()
        {
            var monitors = new[]
            {
                Mon(0, 0, 1920, 1080),
                Mon(1920, 0, 1920, 1080),
                Mon(3840, 0, 1920, 1080),
            };
            double desktopArea = 1920.0 * 3 * 1080;

            var l = ScreenLayout.Compute(monitors, new[] { 1 }, false, false, 1.0);

            double windowArea = l.WindowPixels.Width * l.WindowPixels.Height;
            Assert.True(windowArea < desktopArea * 0.40,
                $"Fenster {windowArea:F0} px^2 ist nicht kleiner als 40 % des Desktops ({desktopArea:F0}).");
        }

        /// <summary>Garantie: das Fenster ist nie groesser als der ganze Desktop.</summary>
        [Fact]
        public void FensterIstNieGroesserAlsDerDesktop()
        {
            var monitors = new[]
            {
                Mon(0, 0, 2560, 1440),
                Mon(2560, 0, 3840, 2160),
            };

            var l = ScreenLayout.Compute(monitors, new[] { 0 }, false, false, 1.0);

            // Der Glow-Rand darf links/oben ueber den Monitor hinausragen.
            Assert.True(l.WindowPixels.Left >= -Pad);
            Assert.True(l.WindowPixels.Top >= -Pad);
            Assert.True(l.WindowPixels.Right <= 2560 + 3840);
        }

        /// <summary>Zwei Monitore nebeneinander, gespannt: ein Ring ueber beide.</summary>
        [Fact]
        public void Gespannt_EinRingUeberAlleMonitore()
        {
            var monitors = new[]
            {
                Mon(-1920, 0, 1920, 1080),
                Mon(0, 0, 1920, 1080),
            };

            var l = ScreenLayout.Compute(monitors, new[] { 0, 1 }, true, false, 1.0);

            Assert.Single(l.Rings);
            Assert.Equal(3840, l.Rings[0].Width, 3);
            Assert.Equal(1040, l.Rings[0].Height, 3);
            // Negativer Monitor: Fenster muss bei -1920-Pad beginnen.
            Assert.Equal(-1920 - Pad, l.WindowPixels.X, 3);
        }

        /// <summary>Zwei Monitore, nicht gespannt: zwei getrennte Ringe.</summary>
        [Fact]
        public void NichtGespannt_ZweiRinge()
        {
            var monitors = new[]
            {
                Mon(0, 0, 1920, 1080),
                Mon(1920, 0, 1920, 1080),
            };

            var l = ScreenLayout.Compute(monitors, new[] { 0, 1 }, false, false, 1.0);

            Assert.Equal(2, l.Rings.Count);
            Assert.Equal(Pad, l.Rings[0].X, 3);
            Assert.Equal(Pad + 1920, l.Rings[1].X, 3);
        }

        /// <summary>150 % Skalierung: Fensterpixel wachsen, DIP-Ringe bleiben logisch gleich.</summary>
        [Fact]
        public void Skalierung150Prozent_RingeBleibenInDipGleich()
        {
            // taskbar: 0 -> Arbeitsflaeche = volle Hoehe, sonst waere die
            // DIP-Hoehe (1620-40)/1.5 = 1053.33.
            var monitors = new[] { Mon(0, 0, 2880, 1620, taskbar: 0) };
            const double scale = 1.5;

            var l = ScreenLayout.Compute(monitors, new[] { 0 }, false, false, scale);

            Assert.Equal(2880 + 2 * Pad * scale, l.WindowPixels.Width, 3);
            Assert.Equal(1920, l.Rings[0].Width, 3); // 2880 / 1.5
            Assert.Equal(1080, l.Rings[0].Height, 3);
        }

        /// <summary>Keine Auswahl -> kein Absturz, Fallback = ganzer Desktop.</summary>
        [Fact]
        public void LeereAuswahl_KeinAbsturzMitFallback()
        {
            var monitors = new[] { Mon(0, 0, 1920, 1080), Mon(1920, 0, 1920, 1080) };

            var l = ScreenLayout.Compute(monitors, Array.Empty<int>(), false, false, 1.0);

            Assert.True(l.IsFallback);
            Assert.Empty(l.Rings);
            Assert.Equal(3840, l.WindowPixels.Width, 3);
        }

        /// <summary>Ungueltiger Index (Monitor wurde abgesteckt) -> kein Absturz.</summary>
        [Fact]
        public void UngueltigerMonitorIndex_KeinAbsturz()
        {
            var monitors = new[] { Mon(0, 0, 1920, 1080) };

            var l = ScreenLayout.Compute(monitors, new[] { 5 }, false, false, 1.0);

            Assert.True(l.IsFallback);
        }

        /// <summary>Kaputte Skalierung (0 oder NaN) darf keine Division durch 0 erzeugen.</summary>
        [Theory]
        [InlineData(0.0)]
        [InlineData(double.NaN)]
        [InlineData(-1.0)]
        public void KaputteSkalierung_WirdAuf1Korrigiert(double scale)
        {
            var monitors = new[] { Mon(0, 0, 1920, 1080) };

            var l = ScreenLayout.Compute(monitors, new[] { 0 }, false, false, scale);

            Assert.False(l.IsFallback);
            Assert.Equal(1.0, l.Scale, 6);
            Assert.Equal(1920, l.Rings[0].Width, 3);
        }

        /// <summary>Vollbild: es wird Bounds statt WorkingArea verwendet (Taskleiste ignoriert).</summary>
        [Fact]
        public void Vollbild_NutztBoundsStattWorkingArea()
        {
            var monitors = new[] { Mon(0, 0, 1920, 1080, taskbar: 40) };

            var windowed = ScreenLayout.Compute(monitors, new[] { 0 }, false, false, 1.0);
            var full = ScreenLayout.Compute(monitors, new[] { 0 }, false, true, 1.0);

            Assert.Equal(1040, windowed.Rings[0].Height, 3);
            Assert.Equal(1080, full.Rings[0].Height, 3);
        }

        /// <summary>Cursor-Umrechnung: physische Pixel -> DIP relativ zur Fensterecke.</summary>
        [Fact]
        public void Cursor_WirdKorrektInDipUmgerechnet()
        {
            var window = new Rect(-48, -48, 2016, 1136);

            var p = ScreenLayout.ToWindowDip(new System.Windows.Point(0, 0), window, 1.0);
            Assert.Equal(48, p.X, 3);
            Assert.Equal(48, p.Y, 3);

            var q = ScreenLayout.ToWindowDip(new System.Windows.Point(960, 540), window, 1.5);
            Assert.Equal((960 + 48) / 1.5, q.X, 3);
            Assert.Equal((540 + 48) / 1.5, q.Y, 3);
        }

        /// <summary>Der Cursor landet immer innerhalb des Fensters, wenn er auf dem Monitor ist.</summary>
        [Fact]
        public void CursorLiegtImFenster()
        {
            var monitors = new[] { Mon(-1920, -200, 1920, 1080) };
            var l = ScreenLayout.Compute(monitors, new[] { 0 }, false, false, 1.0);

            var p = ScreenLayout.ToWindowDip(new System.Windows.Point(-1000, 100), l.WindowPixels, l.Scale);

            Assert.InRange(p.X, 0, l.WindowPixels.Width);
            Assert.InRange(p.Y, 0, l.WindowPixels.Height);
        }
    }
}
