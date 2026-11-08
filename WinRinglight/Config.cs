using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace WinRinglight
{

    // NEW: Data structure for persistent settings
    public class SettingsData
    {
        public string Version { get; } = "1.2.1";
        public double Thickness { get; set; } = 60;
        public double Temperature { get; set; } = 4000;
        public bool AutoTemperature { get; set; } = false;
        public int CursorMaxFps { get; set; } = 60;
        public bool CursorUnlimitedFps { get; set; } = false;
        public bool TextCaretCutout { get; set; } = false;
        public int VisualStyleIndex { get; set; } = 0;
        public bool IsSpanned { get; set; } = false;
        // List of monitor indices that should show a ringlight
        public List<int> SelectedMonitors { get; set; } = new List<int> { 0 };
        public bool AutoWebcam { get; set; } = false;
        public string HotkeyText { get; set; } = "Ctrl + Alt + R";
        public bool HideFromCapture { get; set; } = true;
        public string SupportUrl { get; } = "https://ko-fi.com/melo04";
    }

    public static class Config
    {
        public static SettingsData Current = new SettingsData();

        private static string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinRinglight",
            "settings.json"
        );

        // --- Developer Settings (Fine-tuning) ---
        public static double MaxTemperatureKelvin = 6500;
        public static double MinTemperatureKelvin = 2000;

        public static double MaxRinglightWidthPercent = 0.15;
        public static double MinRinglightWidthPercent = 0.05;

        public static double CursorCutoutRadiusPercent = 0.40;
        public static double CursorBlurRadiusPercent = 0.80;

        // --- Save Settings ---
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        // --- Load Settings ---
        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var data = JsonSerializer.Deserialize<SettingsData>(json);
                    if (data != null) Current = data;
                }
            }
            catch { /* Fallback to defaults */ }
        }

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
            { "DevComposition", new Dictionary<string, string> {
                { "de", "Composition-Renderer testen (experimentell)" },
                { "en", "Test Composition renderer (experimental)" }
            }},
            { "DevTiling", new Dictionary<string, string> {
                { "de", "Composition: Cursor-Maske in Kacheln aufteilen (experimentell)" },
                { "en", "Composition: tile cursor mask (experimental)" }
            }},
            { "DevDirectRaster", new Dictionary<string, string> {
                { "de", "Composition: Direct2D-2×-Kurvenglättung testen (experimentell, mehr GPU-Speicher)" },
                { "en", "Composition: test Direct2D 2x curve smoothing (experimental, more GPU memory)" }
            }},
            { "DevTitle", new Dictionary<string, string> {
                { "de", "Temporäre Rendering-Experimente" }, { "en", "Temporary rendering experiments" }
            }},
            { "DevDescription", new Dictionary<string, string> {
                { "de", "Zum Vergleichen bei gleicher Ringdicke und Cursor-FPS. Zuerst alle aus, dann Optionen einzeln testen." },
                { "en", "Compare with the same ring thickness and cursor FPS. Start with all off, then test each option separately." }
            }},
            { "DevElementCache", new Dictionary<string, string> {
                { "de", "Ring und Glow einzeln zwischenspeichern" }, { "en", "Cache ring and glow separately" }
            }},
            { "DevGroupCache", new Dictionary<string, string> {
                { "de", "Gesamte Ringgruppe zwischenspeichern" }, { "en", "Cache the entire ring group" }
            }},
            { "DevNoGlow", new Dictionary<string, string> {
                { "de", "Glow abschalten (nur Diagnose)" }, { "en", "Disable glow (diagnostic only)" }
            }},
            { "DevWarning", new Dictionary<string, string> {
                { "de", "Experimente können Leistung oder Darstellung verschlechtern und mehr GPU-Speicher verbrauchen. Sie werden beim Beenden zurückgesetzt." },
                { "en", "Experiments may worsen performance or appearance and use more GPU memory. They reset when the app exits." }
            }},
            { "TabPerformance", new Dictionary<string, string> {
                { "de", "Leistung" },
                { "en", "Performance" }
            }},
            { "TextCaretCutout", new Dictionary<string, string> {
                { "de", "Zusätzliche Aussparung am Textcursor (wenn verfügbar)" },
                { "en", "Additional cutout at the text caret (when available)" }
            }},
            { "CursorFps", new Dictionary<string, string> {
                { "de", "Cursor-Aussparung: maximale FPS (1–240)" },
                { "en", "Cursor cutout: maximum FPS (1–240)" }
            }},
            { "CursorFpsUnlimited", new Dictionary<string, string> {
                { "de", "Unbegrenzt (WPF-Render-Takt)" },
                { "en", "Unlimited (WPF render rate)" }
            }},
            { "CursorFpsWarning", new Dictionary<string, string> {
                { "de", "Warnung: Unbegrenzt kann CPU- und GPU-Last sowie Energieverbrauch erhöhen. Die tatsächliche Rate hängt von WPF ab." },
                { "en", "Warning: Unlimited can increase CPU, GPU and power usage. The actual rate depends on WPF." }
            }},
            { "AppName", new Dictionary<string, string> { 
                { "de", "WinRinglight" }, 
                { "en", "WinRinglight" } 
            }},
            { "Author", new Dictionary<string, string> { 
                { "de", "Autor" }, 
                { "en", "Author" } 
            }},
            { "Version", new Dictionary<string, string> { 
                { "de", "Version" }, 
                { "en", "Version" } 
            }},
            { "SystemInfo", new Dictionary<string, string> { 
                { "de", "Info über das Programm" }, 
                { "en", "About the program" } 
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
            }},
            { "Warm", new Dictionary<string, string> {
                { "de", "Warm" },
                { "en", "Warm" }
            }},
            { "Cold", new Dictionary<string, string> {
                { "de", "Kalt" },
                { "en", "Cold" }
            }},
            { "SupportProject", new Dictionary<string, string> {
                { "de", "💖 Unterstütze dieses Projekt" },
                { "en", "💖 Support this project" }
            }},
            { "TabAppearance", new Dictionary<string, string> {
                { "de", "Aussehen" },
                { "en", "Appearance" }
            }},
            { "TabSystem", new Dictionary<string, string> {
                { "de", "System" },
                { "en", "System" }
            }},
            { "StartWithWindows", new Dictionary<string, string> {
                { "de", "Mit Windows starten (Autostart)" },
                { "en", "Start with Windows (Autostart)" }
            }},
            { "StartWithWebcam", new Dictionary<string, string> {
                { "de", "Automatisch einschalten, wenn Webcam aktiv ist" },
                { "en", "Turn on automatically when webcam is active" }
            }},
            { "HotkeyToggle", new Dictionary<string, string> {
                { "de", "Tastenkombination (Ein-/Ausschalten)" },
                { "en", "Toggle Hotkey (On/Off)" }
            }},
            { "PressHotkey", new Dictionary<string, string> {
                { "de", "Tastenkombination festlegen" },
                { "en", "Set Hotkey" }
            }},
            { "WaitingForHotkey", new Dictionary<string, string> {
                { "de", "Warten auf Tastenkombination..." },
                { "en", "Waiting for hotkey..." }
            }},
            { "SelectMonitor", new Dictionary<string, string> {
                { "de", "Monitor auswählen" },
                { "en", "Select Monitor" }
            }},
            { "AllMonitors", new Dictionary<string, string> {
                { "de", "Alle Monitore (Gespant)" },
                { "en", "All Monitors (Spanned)" }
            }},
            { "AutoTemp", new Dictionary<string, string> {
                { "de", "Automatisch an Tageszeit anpassen" },
                { "en", "Auto-adjust to time of day" }
            }},
            { "HideCapture", new Dictionary<string, string> {
                { "de", "Auf Screenshots / in Screenshares verstecken" },
                { "en", "Hide in screenshots and screen shares" }
            }},
        };
    }
}