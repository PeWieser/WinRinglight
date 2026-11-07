using System;
using System.Collections.Generic;
using System.Drawing;

namespace WinRinglight;

/// <summary>All native overlay windows for one experimental session.</summary>
internal sealed class CompositionScene : IDisposable
{
    private readonly List<CompositionBackend> _windows = new();
    private CompositionLayout.Piece[] _layout = Array.Empty<CompositionLayout.Piece>();
    private float _cursorRadius;

    public void Configure(CompositionLayout.Piece[] layout, float cursorRadius, float thickness, bool rounded,
        double kelvin, bool excludeCapture, bool visible)
    {
        if (SameViewports(layout) && _cursorRadius == cursorRadius)
        {
            for (int i = 0; i < layout.Length; i++) _windows[i].SetLayout(layout[i].Ring, true);
            _layout = (CompositionLayout.Piece[])layout.Clone();
            return;
        }
        var replacements = new List<CompositionBackend>();
        try
        {
            foreach (var piece in layout)
                replacements.Add(new CompositionBackend(piece.Viewport, piece.Ring, thickness, rounded, kelvin, excludeCapture, visible, cursorRadius));
        }
        catch
        {
            foreach (var window in replacements) SafeDispose(window);
            throw;
        }
        foreach (var window in _windows) SafeDispose(window);
        _windows.Clear();
        _windows.AddRange(replacements);
        _layout = (CompositionLayout.Piece[])layout.Clone();
        _cursorRadius = cursorRadius;
    }

    public void InvalidateLayout() => _cursorRadius = -1;

    private bool SameViewports(CompositionLayout.Piece[] layout)
    {
        if (layout.Length != _layout.Length) return false;
        for (int i = 0; i < layout.Length; i++) if (layout[i].Viewport != _layout[i].Viewport) return false;
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
        _layout = Array.Empty<CompositionLayout.Piece>();
    }
}
