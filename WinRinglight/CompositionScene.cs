using System;
using System.Collections.Generic;
using System.Drawing;

namespace WinRinglight;

/// <summary>All native overlay windows for one experimental session.</summary>
internal sealed class CompositionScene : IDisposable
{
    private readonly List<CompositionBackend> _windows = new();
    private Rectangle[] _layout = Array.Empty<Rectangle>();
    private float _cursorRadius;

    public void Configure(Rectangle[] layout, float cursorRadius, float thickness, bool rounded,
        double kelvin, bool excludeCapture, bool visible)
    {
        if (SameLayout(layout) && _cursorRadius == cursorRadius) return;
        var replacements = new List<CompositionBackend>();
        try
        {
            foreach (var bounds in layout)
                replacements.Add(new CompositionBackend(bounds, thickness, rounded, kelvin, excludeCapture, visible, cursorRadius));
        }
        catch
        {
            foreach (var window in replacements) SafeDispose(window);
            throw;
        }
        foreach (var window in _windows) SafeDispose(window);
        _windows.Clear();
        _windows.AddRange(replacements);
        _layout = (Rectangle[])layout.Clone();
        _cursorRadius = cursorRadius;
    }

    public void InvalidateLayout() => _cursorRadius = -1;

    private bool SameLayout(Rectangle[] layout)
    {
        if (layout.Length != _layout.Length) return false;
        for (int i = 0; i < layout.Length; i++) if (layout[i] != _layout[i]) return false;
        return true;
    }

    public void SetRing(float thickness, bool rounded, double kelvin, bool animateStyle = false)
    {
        foreach (var window in _windows) window.SetRing(thickness, rounded, kelvin, animateStyle);
    }
    public void SetVisible(bool visible) { foreach (var window in _windows) window.SetVisible(visible); }
    public void UpdateCursor() { foreach (var window in _windows) window.UpdateCursor(); }
    public bool SetCaptureExcluded(bool value)
    {
        // Try every window, even if one fails; caller falls back if any failed.
        bool success = true;
        foreach (var window in _windows) success &= window.SetCaptureExcluded(value);
        return success;
    }
    private static void SafeDispose(CompositionBackend window)
    {
        try { window.Dispose(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Composition window cleanup: " + ex); }
    }

    public void Dispose()
    {
        foreach (var window in _windows) SafeDispose(window);
        _windows.Clear();
        _layout = Array.Empty<Rectangle>();
    }
}
