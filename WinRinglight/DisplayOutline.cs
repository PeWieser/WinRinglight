using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace WinRinglight;

/// <summary>
/// Exact axis-aligned boundary of the union of display rectangles. This is a
/// geometry primitive only: rendering, insets and rounded joins are separate.
/// Rectangles that do not touch stay separate; no line is invented across a gap.
/// </summary>
public static class DisplayOutline
{
    public enum Side { Top, Right, Bottom, Left }
    public readonly record struct Edge(Side Side, int Fixed, int Start, int End);

    public static Edge[] Build(IEnumerable<Rectangle> source)
    {
        var rectangles = source.Where(r => r.Width > 0 && r.Height > 0).ToArray();
        if (rectangles.Length == 0) return Array.Empty<Edge>();
        var xs = rectangles.SelectMany(r => new[] { r.Left, r.Right }).Distinct().Order().ToArray();
        var ys = rectangles.SelectMany(r => new[] { r.Top, r.Bottom }).Distinct().Order().ToArray();
        var occupied = new bool[xs.Length - 1, ys.Length - 1];
        for (int x = 0; x < xs.Length - 1; x++)
            for (int y = 0; y < ys.Length - 1; y++)
                occupied[x, y] = rectangles.Any(r => r.Left <= xs[x] && r.Right >= xs[x + 1] && r.Top <= ys[y] && r.Bottom >= ys[y + 1]);
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < xs.Length - 1 && y < ys.Length - 1 && occupied[x, y];
        var edges = new List<Edge>();
        for (int x = 0; x < xs.Length - 1; x++)
            for (int y = 0; y < ys.Length - 1; y++)
            {
                if (!occupied[x, y]) continue;
                if (!Inside(x, y - 1)) edges.Add(new(Side.Top, ys[y], xs[x], xs[x + 1]));
                if (!Inside(x, y + 1)) edges.Add(new(Side.Bottom, ys[y + 1], xs[x], xs[x + 1]));
                if (!Inside(x - 1, y)) edges.Add(new(Side.Left, xs[x], ys[y], ys[y + 1]));
                if (!Inside(x + 1, y)) edges.Add(new(Side.Right, xs[x + 1], ys[y], ys[y + 1]));
            }
        // Merge consecutive collinear pieces while preserving the outward side.
        var result = new List<Edge>();
        foreach (var edge in edges.OrderBy(e => e.Side).ThenBy(e => e.Fixed).ThenBy(e => e.Start))
        {
            if (result.Count > 0)
            {
                var last = result[^1];
                if (last.Side == edge.Side && last.Fixed == edge.Fixed && last.End == edge.Start)
                {
                    result[^1] = last with { End = edge.End };
                    continue;
                }
            }
            result.Add(edge);
        }
        return result.ToArray();
    }
    /// <summary>Trace directed boundary loops; monitor material stays on the right.
    /// At corner-only contacts, prefer a right turn so distinct regions do not
    /// become one self-intersecting path. Holes have the opposite winding.</summary>
    public static Point[][] BuildLoops(IEnumerable<Rectangle> rectangles)
    {
        var edges = Build(rectangles);
        var endpoints = new HashSet<Point>();
        foreach (var edge in edges)
        {
            if (edge.Side is Side.Top or Side.Bottom)
            {
                endpoints.Add(new Point(edge.Start, edge.Fixed));
                endpoints.Add(new Point(edge.End, edge.Fixed));
            }
            else
            {
                endpoints.Add(new Point(edge.Fixed, edge.Start));
                endpoints.Add(new Point(edge.Fixed, edge.End));
            }
        }
        var segments = new List<(Point From, Point To, int Direction)>();
        foreach (var edge in edges)
        {
            bool horizontal = edge.Side is Side.Top or Side.Bottom;
            var cuts = endpoints.Where(p => horizontal ? p.Y == edge.Fixed && p.X >= edge.Start && p.X <= edge.End
                                                       : p.X == edge.Fixed && p.Y >= edge.Start && p.Y <= edge.End)
                .Select(p => horizontal ? p.X : p.Y).Distinct().Order().ToArray();
            bool forward = edge.Side is Side.Top or Side.Right;
            int direction = edge.Side switch { Side.Top => 0, Side.Right => 1, Side.Bottom => 2, _ => 3 };
            for (int i = 0; i + 1 < cuts.Length; i++)
            {
                var a = horizontal ? new Point(cuts[i], edge.Fixed) : new Point(edge.Fixed, cuts[i]);
                var b = horizontal ? new Point(cuts[i + 1], edge.Fixed) : new Point(edge.Fixed, cuts[i + 1]);
                segments.Add(forward ? (a, b, direction) : (b, a, direction));
            }
        }
        var byStart = new Dictionary<Point, List<int>>();
        for (int i = 0; i < segments.Count; i++)
        {
            var start = segments[i].From;
            if (!byStart.TryGetValue(start, out var outgoing)) byStart[start] = outgoing = new List<int>();
            outgoing.Add(i);
        }
        var used = new bool[segments.Count];
        var loops = new List<Point[]>();
        for (int first = 0; first < segments.Count; first++)
        {
            if (used[first]) continue;
            var points = new List<Point>();
            int current = first;
            Point origin = segments[first].From;
            while (true)
            {
                if (used[current]) throw new InvalidOperationException("Boundary traversal reused an edge.");
                used[current] = true;
                var segment = segments[current];
                points.Add(segment.From);
                if (segment.To == origin) break;
                if (!byStart.TryGetValue(segment.To, out var outgoing)) throw new InvalidOperationException("Open display boundary.");
                int next = -1, best = int.MaxValue;
                foreach (int candidate in outgoing)
                {
                    if (used[candidate]) continue;
                    int turn = (segments[candidate].Direction - segment.Direction + 4) % 4;
                    int rank = turn switch { 1 => 0, 0 => 1, 3 => 2, _ => 3 };
                    if (rank < best) { best = rank; next = candidate; }
                }
                if (next < 0) throw new InvalidOperationException("Display boundary did not close.");
                current = next;
            }
            loops.Add(points.ToArray());
        }
        return loops.ToArray();
    }

}
