using System;
using System.Drawing;

namespace WinRinglight;

// An enclosing soft elliptical cutout. A selected rectangle, including all
// its corners, lies inside the fully transparent part of the mask.
internal readonly record struct TextCutoutEllipse(double X, double Y, double RadiusX, double RadiusY, double InnerStop)
{
    public bool Intersects(Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0 || RadiusX <= 0 || RadiusY <= 0) return false;
        double x = Math.Clamp(X, (double)area.Left, (double)area.Right);
        double y = Math.Clamp(Y, (double)area.Top, (double)area.Bottom);
        double dx = (X - x) / RadiusX, dy = (Y - y) / RadiusY;
        return dx * dx + dy * dy <= 1;
    }

    public static TextCutoutEllipse Create(double x, double y, double radius, double left, double top, double right, double bottom, bool hasSelection)
    {
        radius = Math.Max(1, radius);
        if (!hasSelection || right <= left || bottom <= top)
            return new TextCutoutEllipse(x, y, radius, radius, 0.2);
        // Include the active caret even if a provider clips selection bounds.
        left = Math.Min(left, x); right = Math.Max(right, x);
        top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        double halfWidth = (right - left) / 2, halfHeight = (bottom - top) / 2;
        double rx = Math.Max(radius, halfWidth * Math.Sqrt(2) + radius);
        double ry = Math.Max(radius, halfHeight * Math.Sqrt(2) + radius);
        double stop = Math.Max(0.2, Math.Sqrt(Math.Pow(halfWidth / rx, 2) + Math.Pow(halfHeight / ry, 2)));
        return new TextCutoutEllipse((left + right) / 2, (top + bottom) / 2, rx, ry, Math.Min(0.999999, stop));
    }
}
