using WinRinglight;
using Xunit;

namespace WinRinglight.Tests
{
    public class CursorThrottleTests
    {
        [Fact]
        public void ErsteBewegung_WirdImmerGezeichnet()
        {
            var t = new CursorThrottle();
            Assert.True(t.ShouldUpdate(100, 100, nowMs: 1000));
            Assert.Equal(1, t.UpdateCount);
        }

        [Fact]
        public void ZweiteBewegungZuSchnell_WirdVerworfen()
        {
            var t = new CursorThrottle(minIntervalMs: 33);
            t.ShouldUpdate(100, 100, 1000);

            Assert.False(t.ShouldUpdate(300, 300, 1010));   // nur 10 ms später
            Assert.Equal(1, t.UpdateCount);
            Assert.Equal(1, t.SkippedCount);
        }

        [Fact]
        public void NachAblaufDesIntervalls_WirdWiederGezeichnet()
        {
            var t = new CursorThrottle(minIntervalMs: 33);
            t.ShouldUpdate(100, 100, 1000);

            Assert.True(t.ShouldUpdate(300, 300, 1040));
            Assert.Equal(2, t.UpdateCount);
        }

        [Fact]
        public void ToteZone_VerwirftKleineBewegung()
        {
            var t = new CursorThrottle(minIntervalMs: 0, deadZoneDip: 2.0);
            t.ShouldUpdate(100, 100, 1000);

            Assert.False(t.ShouldUpdate(101, 101, 2000));   // 1.41 DIP < 2
            Assert.True(t.ShouldUpdate(103, 100, 3000));    // 3 DIP > 2
        }

        [Fact]
        public void ToteZone_ErlaubtGenauDieGrenzeNicht()
        {
            var t = new CursorThrottle(minIntervalMs: 0, deadZoneDip: 2.0);
            t.ShouldUpdate(0, 0, 0);

            Assert.False(t.ShouldUpdate(2, 0, 1000));       // exakt 2 -> keine Bewegung
            Assert.True(t.ShouldUpdate(2.5, 0, 2000));
        }

        [Fact]
        public void Stillstand_LoestNichtsAus()
        {
            var t = new CursorThrottle(minIntervalMs: 0, deadZoneDip: 0);
            t.ShouldUpdate(500, 500, 0);

            for (int i = 1; i <= 100; i++)
            {
                Assert.False(t.ShouldUpdate(500, 500, i * 16));
            }
            Assert.Equal(1, t.UpdateCount);
        }

        [Theory]
        [InlineData(double.NaN, 10)]
        [InlineData(10, double.NaN)]
        [InlineData(double.PositiveInfinity, 10)]
        public void UngueltigeWerte_WerdenVerworfen(double x, double y)
        {
            var t = new CursorThrottle();
            Assert.False(t.ShouldUpdate(x, y, 1000));
            Assert.Equal(0, t.UpdateCount);
        }

        [Fact]
        public void Reset_ErzwingtSofortigeAktualisierung()
        {
            var t = new CursorThrottle(minIntervalMs: 100, deadZoneDip: 50);
            t.ShouldUpdate(100, 100, 1000);
            Assert.False(t.ShouldUpdate(101, 101, 1010));

            t.Reset();
            Assert.True(t.ShouldUpdate(101, 101, 1011));
        }

        [Fact]
        public void KaputteParameter_WerdenBegrenzt()
        {
            var t = new CursorThrottle(minIntervalMs: -5, deadZoneDip: -5);
            Assert.Equal(0, t.MinIntervalMs);
            Assert.Equal(0, t.DeadZoneDip);

            t.MinIntervalMs = 99999;
            t.DeadZoneDip = 99999;
            Assert.Equal(500, t.MinIntervalMs);
            Assert.Equal(100, t.DeadZoneDip);

            t.MinIntervalMs = double.NaN;
            Assert.Equal(0, t.MinIntervalMs);
        }

        /// <summary>
        /// Der eigentliche Zweck: bei 60 Eingaben pro Sekunde sollen nur noch
        /// ~30 Neuzeichnungen übrig bleiben (halbe Arbeit).
        /// </summary>
        [Fact]
        public void SechzigBewegungenProSekunde_WerdenHalbiert()
        {
            var t = new CursorThrottle(minIntervalMs: 33, deadZoneDip: 0);

            for (int i = 0; i < 60; i++)
            {
                // 60 Ereignisse in einer Sekunde, Cursor wandert langsam
                t.ShouldUpdate(100 + i * 5, 100 + i * 5, i * 16);
            }

            Assert.InRange(t.UpdateCount, 25, 32);
        }

        /// <summary>Bei 30 Hz Eingabe darf nichts verloren gehen.</summary>
        [Fact]
        public void DreissigHertzEingabe_BleibtVollstaendig()
        {
            var t = new CursorThrottle(minIntervalMs: 33, deadZoneDip: 0);

            for (int i = 0; i < 30; i++)
            {
                t.ShouldUpdate(100 + i * 10, 100, i * 33);
            }

            Assert.Equal(30, t.UpdateCount);
        }
    }
}
