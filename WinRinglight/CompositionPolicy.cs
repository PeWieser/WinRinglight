using System;
using System.Collections.Generic;

namespace WinRinglight
{
    /// <summary>Pure policy for the temporary, single-display Composition path.</summary>
    public static class CompositionPolicy
    {
        public static bool CanUse(bool apiAvailable, int monitorCount, bool spanned,
            IReadOnlyCollection<int> selected, bool fullscreen, double dpiX, double dpiY)
        {
            if (!apiAvailable || monitorCount != 1 || fullscreen ||
                !double.IsFinite(dpiX) || !double.IsFinite(dpiY) ||
                Math.Abs(dpiX - 1) > 0.001 || Math.Abs(dpiY - 1) > 0.001)
                return false;
            if (spanned) return true; // One monitor: spanning is equivalent to selecting it.
            if (selected.Count != 1) return false;
            foreach (int index in selected) return index == 0;
            return false;
        }
    }
}
