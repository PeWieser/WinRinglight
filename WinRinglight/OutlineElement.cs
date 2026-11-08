using System;
using System.Windows;
using System.Windows.Media;

namespace WinRinglight;

/// <summary>WPF fallback for the same physical outline used by Composition.</summary>
internal sealed class OutlineElement : FrameworkElement
{
    public DisplayOutlineLayers.Layers? Layers { get; set; }
    public Matrix DeviceToLocal { get; set; } = Matrix.Identity;
    public double Thickness { get; set; }
    public double DpiScale { get; set; } = 1;
    public Color Color { get; set; }
    public bool DisableGlow { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        if (Layers is not DisplayOutlineLayers.Layers layers || Thickness <= 0) return;
        dc.PushTransform(new MatrixTransform(DeviceToLocal));
        void Stroke(Geometry geometry, double width, byte alpha)
        {
            if (geometry.IsEmpty() || width <= 0) return;
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, Color.R, Color.G, Color.B));
            brush.Freeze();
            var pen = new Pen(brush, width);
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
        if (!DisableGlow)
        {
            double blur = Math.Min(40, Thickness * 0.4);
            for (int i = 12; i >= 1; i--)
            {
                double fraction = i / 12.0;
                byte alpha = (byte)Math.Round(255 * 0.55 * 0.035 * (1 - fraction * 0.75));
                Stroke(layers.Outer, 2 * blur * fraction, alpha);
                Stroke(layers.Inner, 2 * blur * fraction, alpha);
            }
            foreach (var edge in new[] { (width: 3.0, alpha: (byte)30), (width: 2.0, alpha: (byte)65), (width: 1.0, alpha: (byte)110) })
            {
                Stroke(layers.Outer, edge.width * DpiScale, edge.alpha);
                Stroke(layers.Inner, edge.width * DpiScale, edge.alpha);
            }
        }
        Stroke(layers.Core, Thickness, 255);
        dc.Pop();
    }
}
