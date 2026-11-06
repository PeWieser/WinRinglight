using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinRinglight
{
    /// <summary>
    /// A subtle halo made from stroked contours instead of a full-window blur.
    /// The normal Border above this element supplies the opaque ring.
    /// </summary>
    public sealed class GlowBorder : Border
    {
        protected override void OnRender(DrawingContext dc)
        {
            if (BorderBrush is not SolidColorBrush source) return;
            double thickness = BorderThickness.Left;
            double width = RenderSize.Width;
            double height = RenderSize.Height;
            if (thickness <= 0 || width <= thickness || height <= thickness) return;

            double half = thickness / 2;
            var rect = new Rect(half, half, width - thickness, height - thickness);
            double radius = Math.Max(0, CornerRadius.TopLeft - half);
            radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            double blurRadius = Math.Min(40, thickness * 0.4);
            // Layer opacity is intentionally low; these contours are underneath
            // the normal solid ring, so only their inner and outer edges show.
            const int layers = 12;
            for (int i = layers; i >= 1; i--)
            {
                double fraction = (double)i / layers;
                double spread = blurRadius * fraction;
                double alpha = 0.035 * (1 - fraction * 0.75);
                var color = source.Color;
                color.A = (byte)Math.Round(color.A * alpha * source.Opacity);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                var pen = new System.Windows.Media.Pen(brush, thickness + 2 * spread);
                pen.Freeze();
                dc.DrawRoundedRectangle(null, pen, rect, radius, radius);
            }
        }
    }
}
