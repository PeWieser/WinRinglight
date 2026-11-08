using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Media;

namespace WinRinglight;

/// <summary>
/// Isolated geometry prototype, not yet wired into any renderer. An inward
/// offset keeps a stroked contour inside the union of the selected rectangles.
/// Narrow connections can split or vanish; no pixels are invented in gaps.
/// </summary>
public static class InsetDisplayOutline
{
    public static Geometry Create(IEnumerable<Rectangle> displays, double cornerRadius, double inset)
    {
        if (!double.IsFinite(inset) || inset < 0) throw new ArgumentOutOfRangeException(nameof(inset));
        var boundary = RoundedDisplayOutline.Create(displays, cornerRadius);
        return Offset(boundary, inset);
    }

    public static Geometry Offset(Geometry boundary, double inset)
    {
        if (!double.IsFinite(inset) || inset < 0) throw new ArgumentOutOfRangeException(nameof(inset));
        if (boundary.IsEmpty() || inset == 0) return boundary;
        var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, inset * 2) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        var perimeterBand = boundary.GetWidenedPathGeometry(pen, 0.1, ToleranceType.Absolute);
        var inner = Geometry.Combine(boundary, perimeterBand, GeometryCombineMode.Exclude, null, 0.1, ToleranceType.Absolute);
        inner.Freeze();
        return inner;
    }
}
