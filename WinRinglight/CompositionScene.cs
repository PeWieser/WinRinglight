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
    private bool _sharedGeometry;
    private float _maximumRasterScale = 1;

    public void Configure(CompositionLayout.Piece[] layout, float cursorRadius, float thickness, bool rounded,
        double kelvin, bool excludeCapture, bool visible)
    {
        bool sharedGeometry = layout.Length > 1;
        for (int i = 1; i < layout.Length; i++) sharedGeometry &= layout[i].Ring == layout[0].Ring;
        if (SameViewports(layout) && _cursorRadius == cursorRadius && sharedGeometry == _sharedGeometry)
        {
            for (int i = 0; i < layout.Length; i++) _windows[i].SetLayout(layout[i].Ring, true);
            _layout = (CompositionLayout.Piece[])layout.Clone();
            return;
        }
        var replacements = new List<CompositionBackend>();
        try
        {
            foreach (var piece in layout)
            {
                // A single spanned ring must use one scale on all pieces or its
                // outlines will split at a mixed-DPI monitor boundary.
                float? sharedScale = sharedGeometry && replacements.Count > 0 ? replacements[0].DpiScale : null;
                replacements.Add(new CompositionBackend(piece.Viewport, piece.Ring, thickness, rounded, kelvin, excludeCapture, visible, cursorRadius, sharedScale, piece.ExtendLeft, piece.ExtendRight, _maximumRasterScale));
            }
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
        _sharedGeometry = sharedGeometry;
    }

    public void SetSupersampling(bool enabled)
    {
        float value = enabled ? 2 : 1;
        if (_maximumRasterScale == value) return;
        _maximumRasterScale = value;
        InvalidateLayout();
    }

    public void InvalidateLayout() => _cursorRadius = -1;

    private bool SameViewports(CompositionLayout.Piece[] layout)
    {
        if (layout.Length != _layout.Length) return false;
        for (int i = 0; i < layout.Length; i++) if (layout[i].Viewport != _layout[i].Viewport || layout[i].ExtendLeft != _layout[i].ExtendLeft || layout[i].ExtendRight != _layout[i].ExtendRight) return false;
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
