using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace WinRinglight;

public static class CompositionLayout
{
    public readonly record struct Display(Rectangle Bounds, Rectangle WorkingArea);
    public readonly record struct Piece(Rectangle Viewport, Rectangle Ring, bool ExtendLeft = false, bool ExtendRight = false);

    // A bounding box alone places an edge outside shorter/misaligned monitors.
    // Prefer the common band across the shorter axis when such a band exists.
    public static Rectangle SpannedBounds(IReadOnlyList<Display> displays, bool fullscreen = false)
    {
        if (displays.Count == 0) return Rectangle.Empty;
        Rectangle union = fullscreen ? displays[0].Bounds : displays[0].WorkingArea;
        int commonLeft = union.Left, commonRight = union.Right, commonTop = union.Top, commonBottom = union.Bottom;
        long minCx = (long)displays[0].Bounds.Left + displays[0].Bounds.Right, maxCx = minCx;
        long minCy = (long)displays[0].Bounds.Top + displays[0].Bounds.Bottom, maxCy = minCy;
        for (int i = 1; i < displays.Count; i++)
        {
            var area = fullscreen ? displays[i].Bounds : displays[i].WorkingArea;
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
            bool useFullBounds = fullscreen.Count > 0;
            var pieces = new List<Piece>();
            for (int i = 0; i < displays.Count; i++)
            {
                var display = displays[i];
                if (display.Bounds.Width <= 0 || display.Bounds.Height <= 0 ||
                    display.WorkingArea.Width <= 0 || display.WorkingArea.Height <= 0) continue;
                bool left = false, right = false;
                for (int j = 0; j < displays.Count; j++)
                {
                    if (i == j) continue;
                    var other = displays[j].Bounds;
                    // Only suppress an inner side when the displays overlap
                    // vertically; stacked displays keep their side borders.
                    if (Math.Max(display.Bounds.Top, other.Top) >= Math.Min(display.Bounds.Bottom, other.Bottom)) continue;
                    left |= other.Right <= display.Bounds.Left;
                    right |= other.Left >= display.Bounds.Right;
                }
                pieces.Add(new Piece(display.Bounds, useFullBounds ? display.Bounds : display.WorkingArea, left, right));
            }
            return pieces.ToArray();
        }
        return selected.Where(i => i >= 0 && i < displays.Count).Distinct()
            .Select(i => new Piece(displays[i].Bounds, fullscreen.Contains(i) ? displays[i].Bounds : displays[i].WorkingArea))
            .Where(p => p.Viewport.Width > 0 && p.Viewport.Height > 0 && p.Ring.Width > 0 && p.Ring.Height > 0).ToArray();
    }
}
