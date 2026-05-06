using System.Collections.Generic;
using System.Globalization;

namespace WinRinglight
{
    public static class Config
    {
        // --- Developer Settings (Fine-tuning) ---
        public static double MaxBrightness = 1.0;
        public static double MinBrightness = 0.0;
        public static double MaxTemperatureKelvin = 6500;
        public static double MinTemperatureKelvin = 2000;
        public static double MaxRinglightWidthPercent = 0.05;
        public static double MinRinglightWidthPercent = 0.005;
        public static double CursorCutoutRadiusPercent = 0.05;
        public static double CursorBlurRadiusPercent = 0.1;

        // --- Localization Engine ---
        // Automatically detects the Windows system language (e.g., "de", "en", "fr")
        public static string CurrentLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();

        public static string GetText(string key)
        {
            if (Texts.ContainsKey(key) && Texts[key].ContainsKey(CurrentLang))
                return Texts[key][CurrentLang];

            // Fallback to English if the system language is not available
            if (Texts.ContainsKey(key) && Texts[key].ContainsKey("en"))
                return Texts[key]["en"];

            // Fallback to German if English is missing (just in case)
            if (Texts.ContainsKey(key) && Texts[key].ContainsKey("de"))
                return Texts[key]["de"];

            return key; // Fallback if key doesn't exist
        }

        // --- Dictionary ---
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new Dictionary<string, Dictionary<string, string>>()
        {
            { "AppName", new Dictionary<string, string> {
                { "de", "WinRinglight" },
                { "en", "WinRinglight" }
            }},
            { "SettingsMenu", new Dictionary<string, string> {
                { "de", "Einstellungen" },
                { "en", "Settings" }
            }},
            { "ExitMenu", new Dictionary<string, string> {
                { "de", "Beenden" },
                { "en", "Exit" }
            }},
            { "Brightness", new Dictionary<string, string> {
                { "de", "Helligkeit (Intensität)" },
                { "en", "Brightness (Intensity)" }
            }},
            { "ChangeRinglightSize", new Dictionary<string, string> {
                { "de", "Ringlight Dicke" },
                { "en", "Ringlight Thickness" }
            }},
            { "ColorTemperature", new Dictionary<string, string> {
                { "de", "Farbtemperatur (Warm -> Kalt)" },
                { "en", "Color Temperature (Warm -> Cold)" }
            }},
            { "VisualTheme", new Dictionary<string, string> {
                { "de", "Visuelles Design" },
                { "en", "Visual Theme" }
            }}
        };
    }
}