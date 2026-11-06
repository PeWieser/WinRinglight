using System;
using System.Collections.Generic;
using System.Windows;

namespace WinRinglight
{
    /// <summary>
    /// Ein Monitor, wie ihn <see cref="System.Windows.Forms.Screen"/> liefert.
    /// ALLE Werte sind physische Pixel im Koordinatensystem des virtuellen Desktops.
    /// </summary>
    public readonly struct MonitorInfo
    {
        public MonitorInfo(Rect bounds, Rect workingArea)
        {
            Bounds = bounds;
            WorkingArea = workingArea;
        }

        /// <summary>Ganzer Monitor (physische Pixel).</summary>
        public Rect Bounds { get; }

        /// <summary>Monitor ohne Taskleiste (physische Pixel).</summary>
        public Rect WorkingArea { get; }
    }

    /// <summary>
    /// Ergebnis der Layout-Rechnung. Trennt reine Mathematik von der UI,
    /// damit es ohne Fenster getestet werden kann.
    /// </summary>
    public sealed class WindowLayout
    {
        public WindowLayout(Rect windowPixels, IReadOnlyList<Rect> rings, double scale, bool isFallback)
        {
            WindowPixels = windowPixels;
            Rings = rings;
            Scale = scale;
            IsFallback = isFallback;
        }

        /// <summary>
        /// Fensterrechteck in physischen Pixeln (= was SetWindowPos bekommt).
        /// </summary>
        public Rect WindowPixels { get; }

        /// <summary>
        /// Ring-Rechtecke in DIPs, relativ zur linken oberen Fensterecke.
        /// Das ist genau das, was die Borders als Margin bekommen.
        /// </summary>
        public IReadOnlyList<Rect> Rings { get; }

        /// <summary>DPI-Faktor, mit dem gerechnet wurde (1.0 = 100 %).</summary>
        public double Scale { get; }

        /// <summary>
        /// True, wenn keine gültige Monitorauswahl vorlag und deshalb
        /// das alte Verhalten (ganzer Desktop, keine Ringe) verwendet wird.
        /// </summary>
        public bool IsFallback { get; }
    }

    public static class ScreenLayout
    {
        /// <summary>
        /// Rand um die Ringe in DIP. Platz für den weichen Außenrand (Blur/Glow),
        /// damit er am Fensterrand nicht abgeschnitten wird.
        /// </summary>
        public const double GlowPaddingDip = 48.0;

        /// <summary>
        /// Berechnet Fenstergröße und Ringpositionen.
        /// </summary>
        /// <param name="monitors">Alle Monitore, physische Pixel.</param>
        /// <param name="selected">Indizes der Monitore, die leuchten sollen.</param>
        /// <param name="spanned">Ein einziger Ring über alle Monitore.</param>
        /// <param name="fullscreen">True = ganzer Monitor, False = nur Arbeitsfläche.</param>
        /// <param name="scale">DPI-Faktor des Fensters (1.0 = 100 %).</param>
        public static WindowLayout Compute(
            IReadOnlyList<MonitorInfo> monitors,
            IReadOnlyList<int> selected,
            bool spanned,
            bool fullscreen,
            double scale)
        {
            if (monitors == null || monitors.Count == 0)
            {
                return new WindowLayout(new Rect(0, 0, 0, 0), Array.Empty<Rect>(), 1.0, true);
            }

            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            {
                scale = 1.0;
            }

            // 1) Ziel-Rechtecke in physischen Pixeln bestimmen.
            List<Rect> targets = new List<Rect>();

            if (spanned)
            {
                // Wie bisher: alle Monitore, immer Arbeitsfläche.
                Rect union = monitors[0].WorkingArea;
                for (int i = 1; i < monitors.Count; i++)
                {
                    union = Rect.Union(union, monitors[i].WorkingArea);
                }
                targets.Add(union);
            }
            else if (selected != null)
            {
                foreach (int index in selected)
                {
                    if (index < 0 || index >= monitors.Count) continue;
                    MonitorInfo m = monitors[index];
                    targets.Add(fullscreen ? m.Bounds : m.WorkingArea);
                }
            }

            // 2) Keine gültige Auswahl -> altes Verhalten, aber ohne Absturz.
            if (targets.Count == 0)
            {
                Rect all = monitors[0].Bounds;
                for (int i = 1; i < monitors.Count; i++)
                {
                    all = Rect.Union(all, monitors[i].Bounds);
                }
                return new WindowLayout(all, Array.Empty<Rect>(), scale, true);
            }

            // 3) Fenster = Bounding-Box der Ziele, plus Rand für den Glow.
            Rect box = targets[0];
            for (int i = 1; i < targets.Count; i++)
            {
                box = Rect.Union(box, targets[i]);
            }

            double pad = GlowPaddingDip * scale;
            Rect window = new Rect(
                box.X - pad,
                box.Y - pad,
                box.Width + (2 * pad),
                box.Height + (2 * pad));

            // 4) Ringe in DIP, relativ zur Fensterecke.
            List<Rect> rings = new List<Rect>(targets.Count);
            foreach (Rect t in targets)
            {
                rings.Add(new Rect(
                    (t.X - window.X) / scale,
                    (t.Y - window.Y) / scale,
                    t.Width / scale,
                    t.Height / scale));
            }

            return new WindowLayout(window, rings, scale, false);
        }

        /// <summary>
        /// Rechnet einen Cursorpunkt (physische Pixel, virtueller Desktop)
        /// in DIP relativ zur Fensterecke um.
        /// </summary>
        public static System.Windows.Point ToWindowDip(System.Windows.Point cursorPixels, Rect windowPixels, double scale)
        {
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            {
                scale = 1.0;
            }
            return new System.Windows.Point(
                (cursorPixels.X - windowPixels.X) / scale,
                (cursorPixels.Y - windowPixels.Y) / scale);
        }
    }
}
