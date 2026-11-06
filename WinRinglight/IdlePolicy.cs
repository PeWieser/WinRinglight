using System;

namespace WinRinglight
{
    /// <summary>
    /// Entscheidet, ob Dauerlaeufer (Vollbild-Pruefung, Cursor-Abfrage)
    /// ueberhaupt laufen muessen.
    ///
    /// Hintergrund: <c>FullscreenTimer_Tick</c> feuert heute 20-mal pro Sekunde,
    /// auch wenn das Ringlicht ausgeschaltet ist - und kehrt dann sofort um.
    /// Reine Verschwendung von Timer-Ticks und Aufwecken des UI-Threads.
    ///
    /// Ebenso: bei aktivem Ringlicht wurde der 50-ms-Vollbild-Walk beibehalten.
    /// Der Walk selbst (GetTopWindow-Schleife mit DwmGetWindowAttribute pro
    /// Fenster) ist der teure Teil, nicht der Timer.
    /// </summary>
    public static class IdlePolicy
    {
        /// <summary>Timer-Intervall, wenn das Ringlicht laeuft (ms).</summary>
        public const double ActiveIntervalMs = 50.0;

        /// <summary>
        /// Timer-Intervall, wenn das Ringlicht aus ist (ms).
        /// Die App muss nur wissen, wann sie wieder angehen soll - dafuer
        /// reicht 1x pro Sekunde voellig.
        /// </summary>
        public const double IdleIntervalMs = 1000.0;

        /// <summary>
        /// Wie oft der teure Z-Order-Walk hoechstens laufen darf (ms).
        /// Dazwischen wird nur der billige Vordergrund-Check gemacht.
        /// </summary>
        public const double WalkIntervalMs = 1000.0;

        /// <summary>
        /// Soll der Timer ueberhaupt laufen?
        /// </summary>
        /// <param name="ringlightOn">Ist das Ringlicht eingeschaltet?</param>
        /// <param name="autoWebcam">Soll es sich per Webcam einschalten?</param>
        public static bool ShouldRunTimer(bool ringlightOn, bool autoWebcam)
        {
            // Aus und niemand kann es einschalten -> Timer darf schlafen.
            // (Hotkey funktioniert weiterhin, der kommt als Windows-Nachricht.)
            return ringlightOn || autoWebcam;
        }

        /// <summary>
        /// Passendes Timer-Intervall in Millisekunden.
        /// </summary>
        public static double IntervalFor(bool ringlightOn)
        {
            return ringlightOn ? ActiveIntervalMs : IdleIntervalMs;
        }

        /// <summary>
        /// Darf der teure Z-Order-Walk jetzt laufen?
        /// </summary>
        /// <param name="nowMs">Monotone Zeit in ms.</param>
        /// <param name="lastWalkMs">Zeitpunkt des letzten Walks in ms.</param>
        public static bool ShouldWalk(long nowMs, long lastWalkMs)
        {
            // Noch nie gelaufen (Start bei 0, Uhr bei 0) -> sofort messen.
            // Ohne diese Sonderregel bliebe der Vollbild-Zustand nach dem
            // Start bis zu einer Sekunde lang falsch.
            if (nowMs <= 0 && lastWalkMs <= 0) return true;
            if (lastWalkMs <= 0) return true;

            return nowMs - lastWalkMs >= WalkIntervalMs;
        }
    }
}
