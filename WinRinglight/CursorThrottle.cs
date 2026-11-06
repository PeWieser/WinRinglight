using System;

namespace WinRinglight
{
    /// <summary>
    /// Entscheidet, ob eine Cursor-Bewegung tatsächlich neu gezeichnet werden muss.
    ///
    /// Hintergrund: Das Loch um die Maus liegt als OpacityMask auf dem Wurzel-Grid.
    /// Jede Änderung setzt deshalb das ganze Fenster neu zusammen. Die einzige
    /// Stellschraube, die ohne Umbau bleibt, ist: **wie oft** das passiert.
    ///
    /// Diese Klasse kapselt genau diese Entscheidung, damit sie ohne WPF
    /// getestet werden kann.
    /// </summary>
    public sealed class CursorThrottle
    {
        private double _minIntervalMs;
        private double _deadZoneDip;

        private double _lastX = double.NaN;
        private double _lastY = double.NaN;
        private long _lastUpdateTicks;

        public CursorThrottle(double minIntervalMs = 33.0, double deadZoneDip = 2.0)
        {
            MinIntervalMs = minIntervalMs;
            DeadZoneDip = deadZoneDip;
        }

        /// <summary>Mindestabstand zwischen zwei Aktualisierungen in Millisekunden.</summary>
        public double MinIntervalMs
        {
            get => _minIntervalMs;
            set
            {
                if (value < 0 || double.IsNaN(value)) value = 0;
                if (value > 500) value = 500;
                _minIntervalMs = value;
            }
        }

        /// <summary>
        /// Mindestbewegung in DIP. Kleine Zitterbewegungen lösen kein Neuzeichnen aus.
        /// </summary>
        public double DeadZoneDip
        {
            get => _deadZoneDip;
            set
            {
                if (value < 0 || double.IsNaN(value)) value = 0;
                if (value > 100) value = 100;
                _deadZoneDip = value;
            }
        }

        /// <summary>Wie viele Aktualisierungen bisher stattgefunden haben (Diagnose).</summary>
        public long UpdateCount { get; private set; }

        /// <summary>Wie viele Aufrufe verworfen wurden (Diagnose).</summary>
        public long SkippedCount { get; private set; }

        /// <summary>
        /// Erzwingt beim nächsten Aufruf ein Update (z. B. wenn das Ringlicht
        /// gerade eingeschaltet wurde und das Loch sofort sitzen muss).
        /// </summary>
        public void Reset()
        {
            _lastX = double.NaN;
            _lastY = double.NaN;
            _lastUpdateTicks = 0;
        }

        /// <summary>
        /// Prüft, ob die Position (in DIP) ein Neuzeichnen rechtfertigt.
        /// </summary>
        /// <param name="xDip">Cursor-X in DIP relativ zur Fensterecke.</param>
        /// <param name="yDip">Cursor-Y in DIP relativ zur Fensterecke.</param>
        /// <param name="nowMs">Monotone Zeit in Millisekunden.</param>
        /// <returns>True, wenn neu gezeichnet werden soll.</returns>
        public bool ShouldUpdate(double xDip, double yDip, long nowMs)
        {
            if (double.IsNaN(xDip) || double.IsNaN(yDip) ||
                double.IsInfinity(xDip) || double.IsInfinity(yDip))
            {
                SkippedCount++;
                return false;
            }

            bool first = double.IsNaN(_lastX) || double.IsNaN(_lastY);

            if (!first)
            {
                // 1) Zeitdrossel
                if (nowMs - _lastUpdateTicks < _minIntervalMs)
                {
                    SkippedCount++;
                    return false;
                }

                // 2) Tote Zone.
                //    Exakte Gleichheit ist immer ein Skip, auch bei deadZone = 0 -
                //    sonst wuerde ein stehender Cursor endlos neu gezeichnet.
                double dx = xDip - _lastX;
                double dy = yDip - _lastY;
                double distSq = (dx * dx) + (dy * dy);
                double deadSq = _deadZoneDip * _deadZoneDip;

                if (distSq == 0 || distSq <= deadSq)
                {
                    SkippedCount++;
                    return false;
                }
            }

            _lastX = xDip;
            _lastY = yDip;
            _lastUpdateTicks = nowMs;
            UpdateCount++;
            return true;
        }
    }
}
