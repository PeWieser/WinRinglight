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
}
