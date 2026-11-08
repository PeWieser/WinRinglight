using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Media;

namespace WinRinglight;

/// <summary>
/// Shared physical-pixel geometry for the spanned outline's core and glow contours.
/// </summary>
public static class DisplayOutlineLayers
{
    public readonly record struct Layers(Geometry Core, Geometry Outer, Geometry Inner);

    // Coordinates and dimensions are physical pixels. The core is centered
    // half a stroke inside the visible inset; the outer and inner glow lines
    // sit on the corresponding core edges, as in CompositionBackend.
    public static Layers Create(IEnumerable<Rectangle> displays, double centerRadius, double edgeInset, double thickness)
    {
        if (!double.IsFinite(centerRadius) || centerRadius < 0) throw new ArgumentOutOfRangeException(nameof(centerRadius));
        if (!double.IsFinite(edgeInset) || edgeInset < 0) throw new ArgumentOutOfRangeException(nameof(edgeInset));
        if (!double.IsFinite(thickness) || thickness <= 0) throw new ArgumentOutOfRangeException(nameof(thickness));
        double centerInset = edgeInset + thickness / 2;
        var boundary = RoundedDisplayOutline.Create(displays, centerRadius + centerInset);
        var outer = InsetDisplayOutline.Offset(boundary, edgeInset);
        var core = InsetDisplayOutline.Offset(boundary, centerInset);
        var inner = InsetDisplayOutline.Offset(boundary, edgeInset + thickness);
        return new Layers(core, outer, inner);
    }
}
