using WinRinglight;
using Xunit;

namespace WinRinglight.Tests
{
    /// <summary>
    /// Sichert die Fluessigkeits-Eigenschaft ab: Der Glow darf NICHT bei
    /// Mausbewegungen neu gebaut werden, nur wenn sich etwas Sichtbares andert.
    /// </summary>
    public class GlowRebuildPolicyTests
    {
        [Fact]
        public void GleicheWerte_KeinNeubau()
        {
            var last = new GlowState(1920, 1080, 60, 4000);
            var now = new GlowState(1920, 1080, 60, 4000);

            Assert.False(GlowRebuildPolicy.NeedsRebuild(last, now));
        }

        [Fact]
        public void AndereGroesse_Neubau()
        {
            var last = new GlowState(1920, 1080, 60, 4000);
            var now = new GlowState(2560, 1440, 60, 4000);

            Assert.True(GlowRebuildPolicy.NeedsRebuild(last, now));
        }

        [Fact]
        public void AndereDicke_Neubau()
        {
            var last = new GlowState(1920, 1080, 60, 4000);
            var now = new GlowState(1920, 1080, 80, 4000);

            Assert.True(GlowRebuildPolicy.NeedsRebuild(last, now));
        }

        [Fact]
        public void WinzigeFarbaenderung_KeinNeubau()
        {
            // 4000 K -> 4005 K ist unsichtbar, darf nichts kosten.
            var last = new GlowState(1920, 1080, 60, 4000);
            var now = new GlowState(1920, 1080, 60, 4005);

            Assert.False(GlowRebuildPolicy.NeedsRebuild(last, now));
        }

        [Fact]
        public void SichtbareFarbaenderung_Neubau()
        {
            var last = new GlowState(1920, 1080, 60, 4000);
            var now = new GlowState(1920, 1080, 60, 4200);

            Assert.True(GlowRebuildPolicy.NeedsRebuild(last, now));
        }

        [Fact]
        public void ErsterAufbau_IstImmerEinNeubau()
        {
            Assert.True(GlowRebuildPolicy.NeedsRebuild(null, new GlowState(1920, 1080, 60, 4000)));
        }

        [Fact]
        public void Positionsaenderung_Alleine_LoestNichtsAus()
        {
            // Das ist der Kern: eine reine Positionsänderung (Mausbewegung)
            // erzeugt KEINEN Neubau, weil die Groesse gleich bleibt.
            var last = new GlowState(1920, 1080, 60, 4000);
            var now = new GlowState(1920, 1080, 60, 4000);

            Assert.False(GlowRebuildPolicy.NeedsRebuild(last, now));
        }
    }
}
