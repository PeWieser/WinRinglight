using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace WinRinglight;

public static class CompositionLayout
{
    public readonly record struct Display(Rectangle Bounds, Rectangle WorkingArea);
    public readonly record struct Piece(Rectangle Viewport, Rectangle Ring);

    // A bounding box alone places an edge outside shorter/misaligned monitors.
    // Prefer the common band across the shorter axis when such a band exists.
    public static Rectangle SpannedBounds(IReadOnlyList<Display> displays)
    {
        if (displays.Count == 0) return Rectangle.Empty;
        Rectangle union = displays[0].WorkingArea;
        int commonLeft = union.Left, commonRight = union.Right, commonTop = union.Top, commonBottom = union.Bottom;
        long minCx = (long)displays[0].Bounds.Left + displays[0].Bounds.Right, maxCx = minCx;
        long minCy = (long)displays[0].Bounds.Top + displays[0].Bounds.Bottom, maxCy = minCy;
        for (int i = 1; i < displays.Count; i++)
        {
            var area = displays[i].WorkingArea;
            union = Rectangle.Union(union, area);
            commonLeft = Math.Max(commonLeft, area.Left); commonRight = Math.Min(commonRight, area.Right);
            commonTop = Math.Max(commonTop, area.Top); commonBottom = Math.Min(commonBottom, area.Bottom);
            var b = displays[i].Bounds;
            long cx = (long)b.Left + b.Right, cy = (long)b.Top + b.Bottom;
            minCx = Math.Min(minCx, cx); maxCx = Math.Max(maxCx, cx);
            minCy = Math.Min(minCy, cy); maxCy = Math.Max(maxCy, cy);
        }
        if (maxCx - minCx >= maxCy - minCy && commonBottom > commonTop)
            return Rectangle.FromLTRB(union.Left, commonTop, union.Right, commonBottom);
        if (maxCy - minCy > maxCx - minCx && commonRight > commonLeft)
            return Rectangle.FromLTRB(commonLeft, union.Top, commonRight, union.Bottom);
        // No rectangular common band exists (e.g. diagonally separated displays).
        return union;
    }

    public static Piece[] Build(IReadOnlyList<Display> displays, bool spanned,
        IReadOnlyCollection<int> selected, IReadOnlySet<int> fullscreen)
    {
        if (displays.Count == 0) return Array.Empty<Piece>();
        if (spanned)
        {
            Rectangle ring = SpannedBounds(displays);
            if (ring.Width <= 0 || ring.Height <= 0) return Array.Empty<Piece>();
            return displays.Where(d => d.Bounds.Width > 0 && d.Bounds.Height > 0)
                .Select(d => new Piece(d.Bounds, ring)).ToArray();
        }
        return selected.Where(i => i >= 0 && i < displays.Count).Distinct()
            .Select(i => new Piece(displays[i].Bounds, fullscreen.Contains(i) ? displays[i].Bounds : displays[i].WorkingArea))
            .Where(p => p.Viewport.Width > 0 && p.Viewport.Height > 0 && p.Ring.Width > 0 && p.Ring.Height > 0).ToArray();
    }
}
