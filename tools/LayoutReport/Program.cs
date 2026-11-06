using System;
using System.Collections.Generic;
using WinRinglight;

// Diagnose-Werkzeug fuer die CI: zeigt schwarz auf weiss, wie gross das
// Overlay-Fenster fuer typische Monitor-Setups ist - vorher (ganzer Desktop)
// und nachher (nur genutzte Flaeche).
internal static class Program
{
    private static void Main()
    {
        var cases = new (string Name, double[] Sizes, int SelectedIndex, bool Spanned, double Scale)[]
        {
            ("1 Monitor 1080p @100%",          new double[] { 1920, 1080 },                 0, false, 1.0),
            ("1 von 2 Monitoren (1080p)",      new double[] { 1920, 1080, 1920, 1080 },      0, false, 1.0),
            ("1 von 3 Monitoren (1080p)",      new double[] { 1920, 1080, 1920, 1080, 1920, 1080 }, 1, false, 1.0),
            ("1 von 2 Monitoren (4K @150%)",   new double[] { 3840, 2160, 3840, 2160 },      0, false, 1.5),
            ("2 Monitore gespannt (4K @150%)", new double[] { 3840, 2160, 3840, 2160 },      0, true,  1.5),
        };

        Console.WriteLine("| Setup | Fenster vorher | Fenster nachher | Flaeche vorher | Flaeche nachher | Faktor |");
        Console.WriteLine("|---|---|---|---|---|---|");

        foreach (var c in cases)
        {
            var monitors = new List<MonitorInfo>();
            for (int i = 0; i < c.Sizes.Length; i += 2)
            {
                monitors.Add(new MonitorInfo(
                    new System.Windows.Rect(i / 2 * c.Sizes[i], 0, c.Sizes[i], c.Sizes[i + 1]),
                    new System.Windows.Rect(i / 2 * c.Sizes[i], 0, c.Sizes[i], c.Sizes[i + 1] - 40)));
            }

            List<int> selected = new List<int>();
            if (c.Spanned)
            {
                for (int i = 0; i < monitors.Count; i++) selected.Add(i);
            }
            else
            {
                selected.Add(c.SelectedIndex);
            }

            // Vorher: ganzes virtuelles Desktop-Rechteck.
            var all = monitors[0].Bounds;
            for (int i = 1; i < monitors.Count; i++) all = System.Windows.Rect.Union(all, monitors[i].Bounds);
            double before = all.Width * all.Height;

            // Nachher:
            var layout = ScreenLayout.Compute(monitors, selected, c.Spanned, false, c.Scale);
            double after = layout.WindowPixels.Width * layout.WindowPixels.Height;

            Console.WriteLine($"| {c.Name} | {all.Width:F0} x {all.Height:F0} | " +
                              $"{layout.WindowPixels.Width:F0} x {layout.WindowPixels.Height:F0} | " +
                              $"{before / 1e6:F1} MPx | {after / 1e6:F1} MPx | " +
                              $"{(before / after):F2}x |");
        }
    }
}
