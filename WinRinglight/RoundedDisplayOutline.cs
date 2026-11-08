using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace WinRinglight;

/// <summary>Prototype: rounded geometry of display-boundary loops.
/// This is not yet used by the renderers. Coordinates are physical pixels.</summary>
public static class RoundedDisplayOutline
{
    public static StreamGeometry Create(IEnumerable<Rectangle> displays, double radius)
    {
        if (!double.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            foreach (var loop in DisplayOutline.BuildLoops(displays))
            {
                // Ignore split points which exist only to disambiguate contacts.
                var corners = new List<Point>();
                for (int i = 0; i < loop.Length; i++)
                {
                    var previous = loop[(i + loop.Length - 1) % loop.Length];
                    var current = loop[i]; var next = loop[(i + 1) % loop.Length];
                    if ((previous.X == current.X && current.X == next.X) || (previous.Y == current.Y && current.Y == next.Y)) continue;
                    corners.Add(new Point(current.X, current.Y));
                }
                if (corners.Count < 4) continue;
                var incoming = new Point[corners.Count];
                var outgoing = new Point[corners.Count];
                var radii = new double[corners.Count];
                var clockwise = new bool[corners.Count];
                for (int i = 0; i < corners.Count; i++)
                {
                    var a = corners[(i + corners.Count - 1) % corners.Count];
                    var b = corners[i]; var c = corners[(i + 1) % corners.Count];
                    var before = b - a; var after = c - b;
                    double lengthBefore = before.Length, lengthAfter = after.Length;
                    var r = Math.Min(radius, Math.Min(lengthBefore, lengthAfter) / 2);
                    before.Normalize(); after.Normalize();
                    radii[i] = r;
                    incoming[i] = b - before * r;
                    outgoing[i] = b + after * r;
                    clockwise[i] = before.X * after.Y - before.Y * after.X > 0;
                }
                context.BeginFigure(outgoing[0], true, true);
                for (int step = 1; step <= corners.Count; step++)
                {
                    int i = step % corners.Count;
                    context.LineTo(incoming[i], true, false);
                    if (radii[i] > 0)
                        context.ArcTo(outgoing[i], new Size(radii[i], radii[i]), 0, false,
                            clockwise[i] ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }
}
