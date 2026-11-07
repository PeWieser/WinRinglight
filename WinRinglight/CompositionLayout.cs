using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace WinRinglight;

/// <summary>Layout in physical screen coordinates, including negative monitor origins.</summary>
public static class CompositionLayout
{
    public readonly record struct Display(Rectangle Bounds, Rectangle WorkingArea);

    public static Rectangle[] Build(IReadOnlyList<Display> displays, bool spanned,
        IReadOnlyCollection<int> selected, bool fullscreen)
    {
        if (displays.Count == 0) return Array.Empty<Rectangle>();
        if (spanned)
        {
            // Match the existing WPF spanned-mode behaviour: use working areas,
            // even when the fullscreen detector is active.
            Rectangle union = displays[0].WorkingArea;
            for (int i = 1; i < displays.Count; i++) union = Rectangle.Union(union, displays[i].WorkingArea);
            return union.Width > 0 && union.Height > 0 ? new[] { union } : Array.Empty<Rectangle>();
        }
        return selected.Where(i => i >= 0 && i < displays.Count).Distinct()
            .Select(i => fullscreen ? displays[i].Bounds : displays[i].WorkingArea)
            .Where(r => r.Width > 0 && r.Height > 0).ToArray();
    }
}
