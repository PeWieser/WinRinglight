using System;
using System.Drawing;

namespace WinRinglight;

/// <summary>Conservative circle/viewport test in physical screen coordinates.</summary>
public static class CursorCoverage
{
    public static bool Intersects(Rectangle viewport, double x, double y, double radius)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0 || !double.IsFinite(radius) || radius < 0) return false;
        double nearestX = Math.Clamp(x, (double)viewport.Left, (double)viewport.Right);
        double nearestY = Math.Clamp(y, (double)viewport.Top, (double)viewport.Bottom);
        double dx = x - nearestX, dy = y - nearestY;
        return dx * dx + dy * dy <= radius * radius;
    }
}
