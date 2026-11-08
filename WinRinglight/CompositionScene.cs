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
    private bool _outlineMode;
    private bool _directRaster;
    private DisplayOutlineLayers.Layers? _outline;
    public float DpiScale => _windows.Count > 0 ? _windows[0].DpiScale : 1;
    private bool _useTiles = true;
    private Point? _caret;
    private Rectangle? _selection;
    public bool UsingTiles { get; private set; }
    public bool UsingSoftwareRaster => _windows.Exists(window => window.DirectRasterUsesSoftware);
    public string? TileFallbackReason { get; private set; }
    public string? RasterFallbackReason { get; private set; }
    public event Action<string>? RasterFailed;

    public void Configure(CompositionLayout.Piece[] layout, float cursorRadius, float thickness, bool rounded,
        double kelvin, bool excludeCapture, bool visible, bool outlineMode = false)
    {
        bool sharedGeometry = layout.Length > 1;
        for (int i = 1; i < layout.Length; i++) sharedGeometry &= layout[i].Ring == layout[0].Ring;
        if (SameViewports(layout) && _cursorRadius == cursorRadius && sharedGeometry == _sharedGeometry && outlineMode == _outlineMode)
        {
            for (int i = 0; i < layout.Length; i++) _windows[i].SetLayout(layout[i].Ring, true);
            _layout = (CompositionLayout.Piece[])layout.Clone();
            return;
        }
        List<CompositionBackend> CreateWindows(bool tiled, bool directRaster)
        {
            var created = new List<CompositionBackend>();
            try
            {
                foreach (var piece in layout)
                {
                    float? sharedScale = (sharedGeometry || outlineMode) && created.Count > 0 ? created[0].DpiScale : null;
                    var window = new CompositionBackend(piece.Viewport, piece.Ring, thickness, rounded, kelvin, excludeCapture, visible,
                        cursorRadius, sharedScale, piece.ExtendLeft, piece.ExtendRight, 1f, tiled, directRaster);
                    window.DirectRasterFailed += message => { RasterFallbackReason = message; RasterFailed?.Invoke(message); };
                    created.Add(window);
                    if (outlineMode && _outline != null) window.SetOutline(_outline);
                }
                return created;
            }
            catch
            {
                foreach (var window in created) SafeDispose(window);
                throw;
            }
        }
        List<CompositionBackend> CreateWithRasterFallback(bool tiled)
        {
            try { return CreateWindows(tiled, _directRaster); }
            catch (Exception ex) when (_directRaster)
            {
                RasterFallbackReason = ex.Message;
                System.Diagnostics.Trace.WriteLine("Direct2D initialization failed; retrying vector Composition: " + ex);
                return CreateWindows(tiled, false);
            }
        }
        List<CompositionBackend> replacements;
        TileFallbackReason = null;
        RasterFallbackReason = null;
        try
        {
            replacements = CreateWithRasterFallback(_useTiles);
            UsingTiles = _useTiles;
        }
        catch (Exception ex) when (_useTiles)
        {
            System.Diagnostics.Trace.WriteLine("Tiled composition failed; retrying original Composition backend: " + ex);
            TileFallbackReason = ex.Message;
            replacements = CreateWithRasterFallback(false);
            UsingTiles = false;
        }
        foreach (var window in _windows) SafeDispose(window);
        _windows.Clear();
        _windows.AddRange(replacements);
        foreach (var window in _windows) window.UpdateTextCutout(_caret, _selection);
        _layout = (CompositionLayout.Piece[])layout.Clone();
        _cursorRadius = cursorRadius;
        _sharedGeometry = sharedGeometry;
        _outlineMode = outlineMode;
    }

    public void SetOutline(DisplayOutlineLayers.Layers? outline)
    {
        if (Nullable.Equals(_outline, outline)) return;
        foreach (var window in _windows) window.SetOutline(outline);
        _outline = outline;
    }

    public void SetTiling(bool enabled)
    {
        if (_useTiles == enabled) return;
        _useTiles = enabled;
        InvalidateLayout();
    }

    public void SetDirectRaster(bool enabled)
    {
        if (_directRaster == enabled) return;
        _directRaster = enabled;
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
        // SetRing restores the base geometry when thickness or style changes.
        // Invalidate our cached path so the caller installs a matching one.
        if (_outline != null && _windows.Exists(window => !window.HasOutline)) _outline = null;
    }
    public void SetVisible(bool visible) { foreach (var window in _windows) window.SetVisible(visible); }
    public void UpdateCursor() { foreach (var window in _windows) window.UpdateCursor(); }
    public void UpdateCursorAt(int x, int y) { foreach (var window in _windows) window.UpdateCursorAt(x, y); }
    public void UpdateCaret(Point? caret) => UpdateTextCutout(caret, null);
    public void UpdateTextCutout(Point? caret, Rectangle? selection) { _caret = caret; _selection = selection; foreach (var window in _windows) window.UpdateTextCutout(caret, selection); }
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
