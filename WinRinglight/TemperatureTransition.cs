using System;

namespace WinRinglight
{
    /// <summary>Monotonic, bounded interpolation for a short temperature transition.</summary>
    public static class TemperatureTransition
    {
        public const double DurationMilliseconds = 500;

        public static double At(double from, double to, double elapsedMilliseconds)
        {
            double t = Math.Clamp(elapsedMilliseconds / DurationMilliseconds, 0, 1);
            double eased = t * t * (3 - 2 * t);
            return from + (to - from) * eased;
        }
    }
}
