using WinRinglight;
using Xunit;

namespace WinRinglight.Tests
{
    public class IdlePolicyTests
    {
        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]   // einziger Fall, in dem gespart wird
        public void TimerLaeuftNurWennErEtwasBewirkenKann(bool on, bool webcam, bool expected)
        {
            Assert.Equal(expected, IdlePolicy.ShouldRunTimer(on, webcam));
        }

        [Fact]
        public void Aus_ZwoPzigProSekundeWerdenEine()
        {
            // Vorher: 20 Ticks/s, alle folgenlos.
            // Nachher: 1 Tick/s im Leerlauf.
            double before = 1000.0 / IdlePolicy.ActiveIntervalMs;   // 20
            double after = 1000.0 / IdlePolicy.IntervalFor(false);  // 1

            Assert.Equal(20, before);
            Assert.Equal(1, after);
            Assert.Equal(20, before / after);
        }

        [Fact]
        public void Aktiv_BleibtDasIntervallUnveraendert()
        {
            Assert.Equal(50, IdlePolicy.IntervalFor(true));
            Assert.Equal(50, IdlePolicy.ActiveIntervalMs);
        }

        [Fact]
        public void WalkImLeerlauf_WirdGebremst()
        {
            // Bei 50-ms-Ticks darf der Walk nur 1x/s laufen statt 20x/s.
            long lastWalk = 0;
            int walks = 0;

            for (long now = 0; now <= 1000; now += 50)
            {
                if (IdlePolicy.ShouldWalk(now, lastWalk))
                {
                    walks++;
                    lastWalk = now;
                }
            }

            Assert.InRange(walks, 1, 2);
        }

        [Fact]
        public void WalkSofortAmAnfang_IstErlaubt()
        {
            Assert.True(IdlePolicy.ShouldWalk(0, 0));
        }

        [Fact]
        public void WalkKurzDanach_IstVerboten()
        {
            Assert.False(IdlePolicy.ShouldWalk(500, 0));
            Assert.True(IdlePolicy.ShouldWalk(1000, 0));
        }
    }
}
