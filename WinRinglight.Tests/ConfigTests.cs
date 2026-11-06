using WinRinglight;
using Xunit;

namespace WinRinglight.Tests
{
    /// <summary>
    /// Die Performance-Schalter kommen aus einer JSON-Datei, die ein Nutzer
    /// von Hand bearbeitet. Kaputte Werte duerfen die App nicht lahmlegen.
    /// </summary>
    public class ConfigTests
    {
        [Fact]
        public void Voreinstellungen_SindDieSchnellenWerte()
        {
            var d = new SettingsData();

            Assert.True(d.ShrinkWindowToUsedArea);
            Assert.Equal(33.0, d.CursorUpdateIntervalMs);
            Assert.Equal(2.0, d.CursorDeadZoneDip);
        }

        [Fact]
        public void AlteSettingsOhneNeueFelder_BekommenVoreinstellungen()
        {
            // Eine settings.json aus Version 1.2.1 kennt die Felder nicht.
            string alt = "{\"Thickness\":60,\"Temperature\":4000,\"HideFromCapture\":true}";
            var d = System.Text.Json.JsonSerializer.Deserialize<SettingsData>(alt);

            Assert.NotNull(d);
            Assert.True(d!.ShrinkWindowToUsedArea);
            Assert.Equal(33.0, d.CursorUpdateIntervalMs);
        }

        [Theory]
        [InlineData(-50.0, 0.0)]
        [InlineData(9999.0, 500.0)]
        [InlineData(double.NaN, 33.0)]
        public void UnsinnigesIntervall_WirdBegrenzt(double input, double expected)
        {
            Config.Current.CursorUpdateIntervalMs = input;
            Assert.Equal(expected, Config.CursorUpdateIntervalMs, 3);
            Config.Current = new SettingsData();   // zuruecksetzen
        }

        [Theory]
        [InlineData(-5.0, 0.0)]
        [InlineData(9999.0, 100.0)]
        [InlineData(double.NaN, 2.0)]
        public void UnsinnigeToteZone_WirdBegrenzt(double input, double expected)
        {
            Config.Current.CursorDeadZoneDip = input;
            Assert.Equal(expected, Config.CursorDeadZoneDip, 3);
            Config.Current = new SettingsData();
        }
    }
}
