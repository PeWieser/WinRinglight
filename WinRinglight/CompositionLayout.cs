using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace WinRinglight;

/// <summary>Screen-space viewports and ring bounds in physical pixels.</summary>
public static class CompositionLayout
{
    public readonly record struct Display(Rectangle Bounds, Rectangle WorkingArea);
    public readonly record struct Piece(Rectangle Viewport, Rectangle Ring);

    public static Piece[] Build(IReadOnlyList<Display> displays, bool spanned,
        IReadOnlyCollection<int> selected, bool fullscreen)
    {
        if (displays.Count == 0) return Array.Empty<Piece>();
        if (spanned)
        {
            Rectangle ring = displays[0].WorkingArea;
            for (int i = 1; i < displays.Count; i++) ring = Rectangle.Union(ring, displays[i].WorkingArea);
            if (ring.Width <= 0 || ring.Height <= 0) return Array.Empty<Piece>();
            // Each physical display gets a monitor-sized surface with the SAME
            // global ring geometry. This avoids virtual-desktop-sized textures.
            return displays.Where(d => d.Bounds.Width > 0 && d.Bounds.Height > 0)
                .Select(d => new Piece(d.Bounds, ring)).ToArray();
        }
        return selected.Where(i => i >= 0 && i < displays.Count).Distinct()
            .Select(i => new Piece(displays[i].Bounds, fullscreen ? displays[i].Bounds : displays[i].WorkingArea))
            .Where(p => p.Viewport.Width > 0 && p.Viewport.Height > 0 && p.Ring.Width > 0 && p.Ring.Height > 0).ToArray();
    }
}
