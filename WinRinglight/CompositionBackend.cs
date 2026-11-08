using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Color = Windows.UI.Color;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace WinRinglight;

/// <summary>Standalone Composition overlay experiment; not the production rendering backend.</summary>
internal sealed class CompositionBackend : IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExTopmost = 0x8, WsExTransparent = 0x20, WsExToolWindow = 0x80;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x2;
    private const int WsExNoActivate = 0x08000000, WsExNoRedirectionBitmap = 0x00200000;
    private const uint WdaNone = 0, WdaExcludeFromCapture = 0x11;
    private static readonly Guid DesktopInteropId = new("29E691FA-4567-4DCA-B319-D0F207EB6807");
    private static IntPtr _queueController; // Keep the UI-thread queue alive for the application lifetime.
    private readonly List<CompositionObject> _objects = new();
    private readonly List<CompositionObject> _ringObjects = new();
    private SpriteVisual? _result; // Legacy whole-monitor mask fallback.
    private ContainerVisual? _tileRoot;
    private readonly List<MaskTile> _tiles = new();
    private DispatcherTimer? _tileSettleTimer;
    private DispatcherTimer? _rasterTimer;
    private Direct2DRingRaster? _directRaster;
    public bool DirectRasterUsesSoftware => _directRaster?.UsingSoftwareDevice == true;
    private CompositionVisualSurface? _ringVectorSurface;
    private CompositionDrawingSurface? _ringRasterSurface;
    private bool _directRasterActive;
    private bool _directRasterFailed;
    public event Action<string>? DirectRasterFailed;
    private const int RasterPadding = 0;
    private sealed class MaskTile
    {
        public required System.Drawing.Rectangle Bounds;
        public required SpriteVisual Visual;
        public required CompositionRadialGradientBrush Gradient;
        public required CompositionSurfaceBrush RingBrush;
        public required CompositionVisualSurface VectorSurface;
        public CompositionDrawingSurface? RasterSurface;
        public required CompositionMaskBrush MaskBrush;
        public bool MaskApplied;
        public ExtraMask? CaretMask;
    }
    private sealed class ExtraMask
    {
        public required CompositionRadialGradientBrush Gradient;
        public required CompositionMaskBrush Brush;
        public required SpriteVisual SourceVisual;
    }
    private ExtraMask? _caretMask;
    private System.Drawing.Point? _caret;
    private System.Drawing.Rectangle? _selection;
    private TextCutoutEllipse? _textEllipse;
    private CompositionSurfaceBrush? _ringBrush;
    private CompositionMaskBrush? _maskedBrush;
    private bool _maskApplied;
    private readonly float _cursorRadius;
    private readonly List<CompositionColorBrush> _haloBrushes = new();
    private CompositionColorBrush? _coreBrush;
    private double _currentThickness;
    private float _dpiScale = 1;
    private float _rasterScale = 1;
    public float DpiScale => _dpiScale;
    private CompositionRoundedRectangleGeometry? _geometry;
    private CompositionRoundedRectangleGeometry? _outerGeometry, _innerGeometry;
    private bool _currentRounded;
    private IntPtr _hwnd;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private ShapeVisual? _ring;
    private CompositionRadialGradientBrush? _gradient;
    private readonly int _left, _top, _width, _height;
    private System.Drawing.Rectangle _ringBounds;
    private readonly bool _extendLeft, _extendRight;
    private int _lastX = int.MinValue, _lastY = int.MinValue;

    public CompositionBackend(System.Drawing.Rectangle bounds, System.Drawing.Rectangle ringBounds, float thickness, bool rounded,
        double kelvin, bool excludeCapture, bool visible, float cursorRadius, float? sharedDpiScale = null,
        bool extendLeft = false, bool extendRight = false, float? sampleScaleOverride = null, bool tiled = false)
        : this(bounds, ringBounds, thickness, rounded, kelvin, excludeCapture, visible, cursorRadius, sharedDpiScale,
            extendLeft, extendRight, sampleScaleOverride, tiled, false) { }

    public CompositionBackend(System.Drawing.Rectangle bounds, System.Drawing.Rectangle ringBounds, float thickness, bool rounded,
        double kelvin, bool excludeCapture, bool visible, float cursorRadius, float? sharedDpiScale,
        bool extendLeft, bool extendRight, float? sampleScaleOverride, bool tiled, bool directRaster)
    {
        _left = bounds.Left; _top = bounds.Top; _width = bounds.Width; _height = bounds.Height;
        _ringBounds = ringBounds;
        _cursorRadius = cursorRadius;
        _extendLeft = extendLeft; _extendRight = extendRight;
        try
        {
            EnsureDispatcherQueue();
            _hwnd = CreateWindowEx(WsExTopmost | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExNoRedirectionBitmap | WsExLayered,
                "static", "Composition prototype overlay", WsPopup, _left, _top, _width, _height,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            // Layered + transparent causes mouse hit-testing to pass through
            // this window, including to windows owned by other processes.
            // This is experimental with a NOREDIRECTIONBITMAP Composition host.
            if (!SetLayeredWindowAttributes(_hwnd, 0, 255, LwaAlpha))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            uint dpi = GetDpiForWindow(_hwnd);
            _dpiScale = sharedDpiScale ?? (dpi > 0 ? dpi / 96f : 1f);

            // Apply exclusion before showing any overlay pixels.
            if (excludeCapture && !SetCaptureExcluded(true))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Capture exclusion failed");
            _compositor = new Compositor();
            _target = CreateTarget(_compositor, _hwnd);
            Vector2 size = new(_width, _height);
            // Rasterise the vector ring at higher resolution before downsampling.
            // Bound each surface dimension to avoid excessive allocation on 4K+
            // displays; the cursor mask stays at native resolution.
            _rasterScale = Math.Max(1f, Math.Min(sampleScaleOverride ?? 1f, 4096f / Math.Max(_width, _height)));
            _ring = Keep(_compositor.CreateShapeVisual());
            _ring.Size = size * _rasterScale;
            SetRing(thickness, rounded, kelvin);
            if (tiled)
            {
                InitializeTiles(size, visible);
            }
            else
            {
                var ringSurface = Keep(_compositor.CreateVisualSurface());
                _ringVectorSurface = ringSurface;
                ringSurface.SourceVisual = _ring;
                ringSurface.SourceSize = size * _rasterScale;
                _ringBrush = Keep(_compositor.CreateSurfaceBrush(ringSurface));
                _ringBrush.Stretch = CompositionStretch.Fill;
                _ringBrush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;

                _gradient = Keep(_compositor.CreateRadialGradientBrush());
                _gradient.MappingMode = CompositionMappingMode.Absolute;
                _gradient.EllipseRadius = new Vector2(cursorRadius);
                _gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(0, Color.FromArgb(0, 255, 255, 255))));
                _gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(0.2f, Color.FromArgb(0, 255, 255, 255))));
                _gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(1, Color.FromArgb(255, 255, 255, 255))));
                var maskVisual = Keep(_compositor.CreateSpriteVisual());
                maskVisual.Size = size;
                maskVisual.Brush = _gradient;
                var maskSurface = Keep(_compositor.CreateVisualSurface());
                maskSurface.SourceVisual = maskVisual;
                maskSurface.SourceSize = size;
                var maskBrush = Keep(_compositor.CreateSurfaceBrush(maskSurface));
                maskBrush.Stretch = CompositionStretch.None;
                _maskedBrush = Keep(_compositor.CreateMaskBrush());
                _maskedBrush.Source = _ringBrush;
                _maskedBrush.Mask = maskBrush;
                _result = Keep(_compositor.CreateSpriteVisual());
                _result.Size = size;
                _result.Brush = _maskedBrush;
                _maskApplied = true;
                _result.Opacity = visible ? 1 : 0;
                _target.Root = _result;
            }
            if (directRaster)
            {
                // Whole-monitor surfaces are capped; tiled surfaces remain small.
                if (!tiled && Math.Max(_width, _height) + 2 * RasterPadding > 2048)
                    throw new InvalidOperationException("Direct2D high-resolution whole-monitor surface exceeds the 4096-pixel limit; enable tiling.");
                _directRaster = new Direct2DRingRaster(_compositor);
                _rasterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                _rasterTimer.Tick += (_, _) =>
                {
                    _rasterTimer?.Stop();
                    try { RefreshTileVisibility(); RefreshDirectRaster(); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine("Direct2D raster failed; retaining Composition vector renderer: " + ex);
                        RestoreVectorSurfaces();
                        _directRasterFailed = true;
                        DirectRasterFailed?.Invoke(ex.Message);
                    }
                };
                RefreshDirectRaster();
            }
            UpdateCursor();
            ShowWindow(_hwnd, 4); // SW_SHOWNOACTIVATE
        }
        catch { Dispose(); throw; }
    }

    private void InitializeTiles(Vector2 size, bool visible)
    {
        if (_compositor == null || _ring == null || _target == null) throw new InvalidOperationException("Composition not initialized.");
        _tileRoot = Keep(_compositor.CreateContainerVisual());
        _tileRoot.Size = size;
        _tileRoot.Opacity = visible ? 1 : 0;
        // Keep object count bounded for very large monitors while maintaining
        // smaller dirty regions on common Full HD / QHD setups.
        int tileSize = Math.Max(256, (int)Math.Ceiling(Math.Max(_width, _height) / 12.0 / 64) * 64);
        for (int y = 0; y < _height; y += tileSize)
        for (int x = 0; x < _width; x += tileSize)
        {
            int width = Math.Min(tileSize, _width - x), height = Math.Min(tileSize, _height - y);
            Vector2 tileSizeVector = new(width, height);
            var ringSurface = Keep(_compositor.CreateVisualSurface());
            ringSurface.SourceVisual = _ring;
            ringSurface.SourceOffset = new Vector2(x, y) * _rasterScale;
            ringSurface.SourceSize = tileSizeVector * _rasterScale;
            var ringBrush = Keep(_compositor.CreateSurfaceBrush(ringSurface));
            ringBrush.Stretch = CompositionStretch.Fill;
            ringBrush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
            var gradient = Keep(_compositor.CreateRadialGradientBrush());
            gradient.MappingMode = CompositionMappingMode.Absolute;
            gradient.EllipseRadius = new Vector2(_cursorRadius);
            gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(0, Color.FromArgb(0, 255, 255, 255))));
            gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(0.2f, Color.FromArgb(0, 255, 255, 255))));
            gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(1, Color.FromArgb(255, 255, 255, 255))));
            var maskVisual = Keep(_compositor.CreateSpriteVisual());
            maskVisual.Size = tileSizeVector;
            maskVisual.Brush = gradient;
            var maskSurface = Keep(_compositor.CreateVisualSurface());
            maskSurface.SourceVisual = maskVisual;
            maskSurface.SourceSize = tileSizeVector;
            var maskBrush = Keep(_compositor.CreateSurfaceBrush(maskSurface));
            maskBrush.Stretch = CompositionStretch.None;
            var composite = Keep(_compositor.CreateMaskBrush());
            composite.Source = ringBrush;
            composite.Mask = maskBrush;
            var visual = Keep(_compositor.CreateSpriteVisual());
            visual.Size = tileSizeVector;
            visual.Offset = new Vector3(x, y, 0);
            visual.Brush = ringBrush;
            _tileRoot.Children.InsertAtTop(visual);
            _tiles.Add(new MaskTile { Bounds = new System.Drawing.Rectangle(_left + x, _top + y, width, height),
                Visual = visual, Gradient = gradient, RingBrush = ringBrush, VectorSurface = ringSurface, MaskBrush = composite });
        }
        _tileSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _tileSettleTimer.Tick += (_, _) => { _tileSettleTimer?.Stop(); RefreshTileVisibility(); };
        _target.Root = _tileRoot;
        RefreshTileVisibility();
    }

    private void PrepareTilesForAnimation()
    {
        if (_tiles.Count == 0) return;
        foreach (var tile in _tiles)
        {
            tile.Visual.IsVisible = true;
            UpdateTileCursor(tile, _lastX, _lastY);
        }
        _tileSettleTimer?.Stop();
        _tileSettleTimer?.Start();
    }

    private void RefreshTileVisibility()
    {
        if (_tiles.Count == 0 || _geometry == null) return;
        float thickness = (float)_currentThickness;
        float inset = _currentRounded ? 20 * _dpiScale : 0;
        float radius = _geometry.CornerRadius.X / _rasterScale;
        float extension = radius + inset + thickness + 50 * _dpiScale;
        float left = _ringBounds.Left + inset - (_extendLeft ? extension + Math.Max(0, _ringBounds.Left - _left) : 0);
        float right = _ringBounds.Right - inset + (_extendRight ? extension + Math.Max(0, _left + _width - _ringBounds.Right) : 0);
        float top = _ringBounds.Top + inset, bottom = _ringBounds.Bottom - inset;
        float glow = Math.Min(40, thickness * 0.4f) + 2 * _dpiScale + 2;
        float innerLeft = left + thickness + glow, innerRight = right - thickness - glow;
        float innerTop = top + thickness + glow, innerBottom = bottom - thickness - glow;
        float innerRadius = Math.Max(0, radius - thickness / 2 - glow);
        innerRadius = Math.Min(innerRadius, Math.Max(0, Math.Min(innerRight - innerLeft, innerBottom - innerTop) / 2));
        bool InHole(float x, float y)
        {
            if (innerRight <= innerLeft || innerBottom <= innerTop || x < innerLeft || x > innerRight || y < innerTop || y > innerBottom) return false;
            float cx = Math.Clamp(x, innerLeft + innerRadius, innerRight - innerRadius);
            float cy = Math.Clamp(y, innerTop + innerRadius, innerBottom - innerRadius);
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= innerRadius * innerRadius;
        }
        foreach (var tile in _tiles)
        {
            var b = tile.Bounds;
            bool outside = b.Right < left - glow || b.Left > right + glow || b.Bottom < top - glow || b.Top > bottom + glow;
            bool insideHole = InHole(b.Left, b.Top) && InHole(b.Right, b.Top) && InHole(b.Left, b.Bottom) && InHole(b.Right, b.Bottom);
            bool isVisible = !outside && !insideHole;
            tile.Visual.IsVisible = isVisible;
            if (isVisible) UpdateTileCursor(tile, _lastX, _lastY);
        }
    }

    // Restore the live, compositor-animated vector before any geometry/color
    // change. Never redraw in cursor/caret callbacks. The settle timer switches
    // back only after the last update and after the 400 ms geometry animation.
    private void ScheduleDirectRaster()
    {
        if (_directRaster == null || _directRasterFailed) return;
        RestoreVectorSurfaces();
        _rasterTimer?.Stop();
        _rasterTimer?.Start();
    }

    private static void ConfigureVectorBrush(CompositionSurfaceBrush brush, CompositionVisualSurface surface)
    {
        brush.Surface = surface;
        brush.Stretch = CompositionStretch.Fill;
        brush.Scale = Vector2.One;
        brush.Offset = Vector2.Zero;
    }

    private static void ConfigureRasterBrush(CompositionSurfaceBrush brush, CompositionDrawingSurface surface)
    {
        brush.Surface = surface;
        // Source is exactly twice the tile size. Fill performs the same
        // 2x-to-1x linear sampling used in the independent quality probe.
        brush.Stretch = CompositionStretch.Fill;
        brush.Scale = Vector2.One;
        brush.Offset = Vector2.Zero;
    }

    private void RestoreVectorSurfaces()
    {
        if (_ringBrush != null && _ringVectorSurface != null) ConfigureVectorBrush(_ringBrush, _ringVectorSurface);
        foreach (var tile in _tiles) ConfigureVectorBrush(tile.RingBrush, tile.VectorSurface);
        _directRasterActive = false;
        _ringRasterSurface?.Dispose(); _ringRasterSurface = null;
        foreach (var tile in _tiles) { tile.RasterSurface?.Dispose(); tile.RasterSurface = null; }
    }

    private void RefreshDirectRaster()
    {
        if (_directRaster == null || _geometry == null || _coreBrush == null || _directRasterFailed) return;
        // Compute target geometry from the layout, not animated property reads.
        // Layout changes intentionally preserve the current corner radius.
        float thickness = (float)_currentThickness;
        float radius = _geometry.CornerRadius.X / _rasterScale;
        float inset = _currentRounded ? 20 * _dpiScale : 0;
        float extension = radius + inset + thickness + 50 * _dpiScale;
        float extraLeft = _extendLeft ? extension + Math.Max(0, _ringBounds.Left - _left) : 0;
        float extraRight = _extendRight ? extension + Math.Max(0, _left + _width - _ringBounds.Right) : 0;
        float left = _ringBounds.Left - _left + inset + thickness / 2 - extraLeft;
        float top = _ringBounds.Top - _top + inset + thickness / 2;
        float width = Math.Max(0, _ringBounds.Width + extraLeft + extraRight - inset * 2 - thickness);
        float height = Math.Max(0, _ringBounds.Height - inset * 2 - thickness);
        var color = _coreBrush.Color;
        CompositionDrawingSurface Draw(int x, int y, int w, int h) => _directRaster.Draw(
            w + 2 * RasterPadding, h + 2 * RasterPadding, 2, x - RasterPadding, y - RasterPadding,
            left, top, left + width, top + height, radius, thickness, _dpiScale, color);
        // Draw all replacements first. Failure leaves every brush on its
        // existing vector surface, rather than a partially updated ring.
        var created = new List<(MaskTile Tile, CompositionDrawingSurface Surface)>();
        CompositionDrawingSurface? full = null;
        try
        {
            if (_ringBrush != null) full = Draw(0, 0, _width, _height);
            foreach (var tile in _tiles)
                if (tile.Visual.IsVisible)
                    created.Add((tile, Draw(tile.Bounds.Left - _left, tile.Bounds.Top - _top, tile.Bounds.Width, tile.Bounds.Height)));
            if (full != null) { ConfigureRasterBrush(_ringBrush!, full); _ringRasterSurface = full; full = null; }
            foreach (var item in created) { ConfigureRasterBrush(item.Tile.RingBrush, item.Surface); item.Tile.RasterSurface = item.Surface; }
            created.Clear();
            _directRasterActive = true;
        }
        finally
        {
            full?.Dispose();
            foreach (var item in created) item.Surface.Dispose();
        }
    }

    private ExtraMask CreateCaretMask(Vector2 size)
    {
        if (_compositor == null) throw new InvalidOperationException();
        var gradient = Keep(_compositor.CreateRadialGradientBrush());
        gradient.MappingMode = CompositionMappingMode.Absolute;
        gradient.EllipseRadius = new Vector2(_cursorRadius);
        gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(0, Color.FromArgb(0, 255, 255, 255))));
        gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(0.2f, Color.FromArgb(0, 255, 255, 255))));
        gradient.ColorStops.Add(Keep(_compositor.CreateColorGradientStop(1, Color.FromArgb(255, 255, 255, 255))));
        var visual = Keep(_compositor.CreateSpriteVisual());
        visual.Size = size;
        visual.Brush = gradient;
        var surface = Keep(_compositor.CreateVisualSurface());
        surface.SourceVisual = visual;
        surface.SourceSize = size;
        var brush = Keep(_compositor.CreateSurfaceBrush(surface));
        brush.Stretch = CompositionStretch.None;
        // MaskBrush.Source accepts a surface brush, not another MaskBrush.
        // Rasterize the already mouse-masked result into a local visual surface
        // before applying the second (caret) mask.
        var sourceVisual = Keep(_compositor.CreateSpriteVisual());
        sourceVisual.Size = size;
        var sourceSurface = Keep(_compositor.CreateVisualSurface());
        sourceSurface.SourceVisual = sourceVisual;
        sourceSurface.SourceSize = size;
        var sourceBrush = Keep(_compositor.CreateSurfaceBrush(sourceSurface));
        sourceBrush.Stretch = CompositionStretch.None;
        var mask = Keep(_compositor.CreateMaskBrush());
        mask.Source = sourceBrush;
        mask.Mask = brush;
        return new ExtraMask { Gradient = gradient, Brush = mask, SourceVisual = sourceVisual };
    }

    private void UpdateTileCursor(MaskTile tile, int x, int y)
    {
        bool mouse = x != int.MinValue && CursorCoverage.Intersects(tile.Bounds, x, y, _cursorRadius);
        if (mouse) tile.Gradient.EllipseCenter = new Vector2(x - tile.Bounds.Left, y - tile.Bounds.Top);
        tile.MaskApplied = mouse;
        CompositionBrush source = mouse ? tile.MaskBrush : tile.RingBrush;
        if (_textEllipse is TextCutoutEllipse ellipse && ellipse.Intersects(tile.Bounds))
        {
            tile.CaretMask ??= CreateCaretMask(new Vector2(tile.Bounds.Width, tile.Bounds.Height));
            SetTextGradient(tile.CaretMask.Gradient, ellipse, tile.Bounds.Left, tile.Bounds.Top);
            if (tile.CaretMask.SourceVisual.Brush != source) tile.CaretMask.SourceVisual.Brush = source;
            source = tile.CaretMask.Brush;
        }
        if (tile.Visual.Brush != source) tile.Visual.Brush = source;
    }

    public void UpdateCaret(System.Drawing.Point? caret) => UpdateTextCutout(caret, null);

    public void UpdateTextCutout(System.Drawing.Point? caret, System.Drawing.Rectangle? selection)
    {
        if (_caret == caret && _selection == selection) return;
        _caret = caret; _selection = selection;
        _textEllipse = BuildTextEllipse();
        ApplyCursor();
    }

    private TextCutoutEllipse? BuildTextEllipse()
    {
        if (_caret is not System.Drawing.Point p) return null;
        var r = _selection.GetValueOrDefault();
        return TextCutoutEllipse.Create(p.X, p.Y, _cursorRadius, r.Left, r.Top, r.Right, r.Bottom, _selection.HasValue);
    }

    private static void SetTextGradient(CompositionRadialGradientBrush gradient, TextCutoutEllipse ellipse, int left, int top)
    {
        var center = new Vector2((float)(ellipse.X - left), (float)(ellipse.Y - top));
        var radius = new Vector2((float)ellipse.RadiusX, (float)ellipse.RadiusY);
        if (gradient.EllipseCenter != center) gradient.EllipseCenter = center;
        if (gradient.EllipseRadius != radius) gradient.EllipseRadius = radius;
        if (gradient.ColorStops[1].Offset != (float)ellipse.InnerStop) gradient.ColorStops[1].Offset = (float)ellipse.InnerStop;
    }

    private T Keep<T>(T value) where T : CompositionObject { _objects.Add(value); return value; }

    private T KeepRing<T>(T value) where T : CompositionObject { _ringObjects.Add(value); return value; }

    public void SetRing(float thickness, bool rounded, double kelvin, bool animateStyle = false)
    {
        if (_compositor == null || _ring == null) return;
        thickness *= _dpiScale;
        if (_coreBrush != null && _currentThickness == thickness)
        {
            bool changed = _currentRounded != rounded || !_coreBrush.Color.Equals(ToColor(kelvin));
            if (changed) ScheduleDirectRaster();
            if (_currentRounded != rounded && _geometry != null)
            {
                _currentRounded = rounded;
                if (animateStyle) PrepareTilesForAnimation();
                SetGeometry(_geometry, thickness, rounded, animateStyle);
                if (!animateStyle) RefreshTileVisibility();
            }
            UpdateColor(kelvin);
            return;
        }
        ScheduleDirectRaster();
        _ring.Shapes.Clear();
        _haloBrushes.Clear();
        _coreBrush = null;
        _currentThickness = thickness;
        _currentRounded = rounded;
        for (int i = _ringObjects.Count - 1; i >= 0; i--) _ringObjects[i].Dispose();
        _ringObjects.Clear();
        var geometry = KeepRing(_compositor.CreateRoundedRectangleGeometry());
        _geometry = geometry;
        _outerGeometry = KeepRing(_compositor.CreateRoundedRectangleGeometry());
        _innerGeometry = KeepRing(_compositor.CreateRoundedRectangleGeometry());
        SetGeometry(geometry, thickness, rounded, false);
        var color = ToColor(kelvin);
        float blur = Math.Min(40, thickness * 0.4f);
        const int layers = 12;
        for (int i = layers; i >= 1; i--)
        {
            float fraction = (float)i / layers;
            byte alpha = (byte)Math.Round(255 * 0.55 * 0.035 * (1 - fraction * 0.75));
            var brush = KeepRing(_compositor.CreateColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)));
            _haloBrushes.Add(brush);
            // Shade only the two visible edges instead of repainting the full
            // thick ring for every halo layer during a style animation.
            foreach (var contour in new[] { _outerGeometry, _innerGeometry })
            {
                var shape = KeepRing(_compositor.CreateSpriteShape(contour));
                shape.StrokeBrush = brush;
                shape.StrokeThickness = 2 * blur * fraction * _rasterScale;
                _ring.Shapes.Add(shape);
            }
        }
        // Keep the previously tested inexpensive edge-softening contours.
        foreach (var edge in new[] { (spread: 3f, alpha: (byte)30), (spread: 2f, alpha: (byte)65), (spread: 1f, alpha: (byte)110) })
        {
            var brush = KeepRing(_compositor.CreateColorBrush(Color.FromArgb(edge.alpha, color.R, color.G, color.B)));
            _haloBrushes.Add(brush);
            foreach (var contour in new[] { _outerGeometry, _innerGeometry })
            {
                var shape = KeepRing(_compositor.CreateSpriteShape(contour));
                shape.StrokeBrush = brush;
                shape.StrokeThickness = edge.spread * _dpiScale * _rasterScale;
                _ring.Shapes.Add(shape);
            }
        }
        var core = KeepRing(_compositor.CreateSpriteShape(geometry));
        _coreBrush = KeepRing(_compositor.CreateColorBrush(color));
        core.StrokeBrush = _coreBrush;
        core.StrokeThickness = thickness * _rasterScale;
        _ring.Shapes.Add(core);
        RefreshTileVisibility();
    }

    private void SetGeometry(CompositionRoundedRectangleGeometry geometry, float thickness, bool rounded, bool animate)
    {
        float inset = rounded ? 20 * _dpiScale : 0;
        float radius = rounded ? Math.Min(_ringBounds.Width, _ringBounds.Height) * 0.22f : 3 * _dpiScale;
        // Move internal side/corners entirely outside this monitor's clipped
        // viewport, leaving straight top/bottom strokes at the seam.
        float extension = radius + inset + thickness + 50 * _dpiScale;
        float extraLeft = _extendLeft ? extension + Math.Max(0, _ringBounds.Left - _left) : 0;
        float extraRight = _extendRight ? extension + Math.Max(0, _left + _width - _ringBounds.Right) : 0;
        var offset = new Vector2(_ringBounds.Left - _left + inset + thickness / 2 - extraLeft,
                                 _ringBounds.Top - _top + inset + thickness / 2);
        var size = new Vector2(Math.Max(0, _ringBounds.Width + extraLeft + extraRight - inset * 2 - thickness), Math.Max(0, _ringBounds.Height - inset * 2 - thickness));
        offset *= _rasterScale;
        size *= _rasterScale;
        radius *= _rasterScale;
        thickness *= _rasterScale;
        SetShapeGeometry(geometry, offset, size, new Vector2(radius), animate);
        float half = thickness / 2;
        if (_outerGeometry != null)
            SetShapeGeometry(_outerGeometry, offset - new Vector2(half), size + new Vector2(thickness), new Vector2(radius + half), animate);
        if (_innerGeometry != null)
            SetShapeGeometry(_innerGeometry, offset + new Vector2(half),
                new Vector2(Math.Max(0, size.X - thickness), Math.Max(0, size.Y - thickness)),
                new Vector2(Math.Max(0, radius - half)), animate);
    }

    private void SetShapeGeometry(CompositionRoundedRectangleGeometry geometry, Vector2 offset, Vector2 size, Vector2 radius, bool animate)
    {
        if (!animate || _compositor == null)
        {
            geometry.Offset = offset;
            geometry.Size = size;
        }
        else
        {
            AnimateVector(geometry, "Offset", offset);
            AnimateVector(geometry, "Size", size);
        }
        geometry.CornerRadius = radius;
    }

    public void SetLayout(System.Drawing.Rectangle ringBounds, bool animate)
    {
        if (_ringBounds == ringBounds) return;
        ScheduleDirectRaster();
        _ringBounds = ringBounds;
        if (_geometry != null)
        {
            if (animate) PrepareTilesForAnimation();
            // WPF only changes the corner radius on style changes, not during
            // a working-area/fullscreen layout transition.
            Vector2 oldRadius = _geometry.CornerRadius;
            Vector2 oldOuterRadius = _outerGeometry?.CornerRadius ?? default;
            Vector2 oldInnerRadius = _innerGeometry?.CornerRadius ?? default;
            SetGeometry(_geometry, (float)_currentThickness, _currentRounded, animate);
            _geometry.CornerRadius = oldRadius;
            if (_outerGeometry != null) _outerGeometry.CornerRadius = oldOuterRadius;
            if (_innerGeometry != null) _innerGeometry.CornerRadius = oldInnerRadius;
            if (!animate) RefreshTileVisibility();
        }
    }

    // QuadraticEase(EaseInOut), exactly as the WPF animations: two quadratic
    // segments. These Bezier control points represent x=t, y=t² and y=2t−t².
    private CubicBezierEasingFunction QuadraticSegment(bool easeIn) => _compositor!.CreateCubicBezierEasingFunction(
        easeIn ? new Vector2(1f / 3, 0) : new Vector2(1f / 3, 2f / 3),
        easeIn ? new Vector2(2f / 3, 1f / 3) : new Vector2(2f / 3, 1));

    private void AnimateVector(CompositionObject target, string property, Vector2 to)
    {
        if (_compositor == null) return;
        using var animation = _compositor.CreateVector2KeyFrameAnimation();
        using var easeIn = QuadraticSegment(true);
        using var easeOut = QuadraticSegment(false);
        animation.SetVector2Parameter("target", to);
        animation.InsertExpressionKeyFrame(0.5f, "this.StartingValue + (target - this.StartingValue) * 0.5", easeIn);
        animation.InsertKeyFrame(1, to, easeOut);
        animation.Duration = TimeSpan.FromSeconds(0.4);
        target.StartAnimation(property, animation);
    }

    private static Color ToColor(double kelvin)
    {
        double t = (kelvin - 2000) / 4500.0;
        return Color.FromArgb(255, (byte)(255 - 20 * t), (byte)(170 + 75 * t), (byte)(50 + 205 * t));
    }

    private void UpdateColor(double kelvin)
    {
        var color = ToColor(kelvin);
        if (_coreBrush != null) _coreBrush.Color = color;
        for (int i = 0; i < _haloBrushes.Count; i++)
        {
            byte alpha = _haloBrushes[i].Color.A;
            _haloBrushes[i].Color = Color.FromArgb(alpha, color.R, color.G, color.B);
        }
    }

    public void SetVisible(bool visible)
    {
        Visual? root = (Visual?)_tileRoot ?? _result;
        if (root == null || _compositor == null) return;
        float target = visible ? 1 : 0;
        using var animation = _compositor.CreateScalarKeyFrameAnimation();
        using var easeIn = QuadraticSegment(true);
        using var easeOut = QuadraticSegment(false);
        animation.SetScalarParameter("target", target);
        animation.InsertExpressionKeyFrame(0.5f, "this.StartingValue + (target - this.StartingValue) * 0.5", easeIn);
        animation.InsertKeyFrame(1, target, easeOut);
        animation.Duration = TimeSpan.FromSeconds(0.4);
        root.StartAnimation("Opacity", animation);
    }

    public void UpdateCursor()
    {
        if (GetCursorPos(out Point p)) UpdateCursorAt(p.X, p.Y);
    }

    public void UpdateCursorAt(int x, int y)
    {
        if (x == _lastX && y == _lastY) return;
        _lastX = x; _lastY = y;
        ApplyCursor();
    }

    private void ApplyCursor()
    {
        if (_tileRoot != null)
        {
            foreach (var tile in _tiles) if (tile.Visual.IsVisible) UpdateTileCursor(tile, _lastX, _lastY);
            return;
        }
        if (_gradient == null || _result == null || _ringBrush == null || _maskedBrush == null) return;
        var bounds = new System.Drawing.Rectangle(_left, _top, _width, _height);
        bool mouse = _lastX != int.MinValue && CursorCoverage.Intersects(bounds, _lastX, _lastY, _cursorRadius);
        if (mouse) _gradient.EllipseCenter = new Vector2(_lastX - _left, _lastY - _top);
        _maskApplied = mouse;
        CompositionBrush source = mouse ? _maskedBrush : _ringBrush;
        if (_textEllipse is TextCutoutEllipse ellipse && ellipse.Intersects(bounds))
        {
            _caretMask ??= CreateCaretMask(new Vector2(_width, _height));
            SetTextGradient(_caretMask.Gradient, ellipse, _left, _top);
            if (_caretMask.SourceVisual.Brush != source) _caretMask.SourceVisual.Brush = source;
            source = _caretMask.Brush;
        }
        if (_result.Brush != source) _result.Brush = source;
    }

    public bool SetCaptureExcluded(bool value) => _hwnd != IntPtr.Zero && SetWindowDisplayAffinity(_hwnd, value ? WdaExcludeFromCapture : WdaNone);

    public void Dispose()
    {
        // A failure while releasing one COM object must never leave this or
        // another overlay HWND visible after a failed scene reconfiguration.
        try
        {
            _tileSettleTimer?.Stop();
            _tileSettleTimer = null;
            _rasterTimer?.Stop();
            _rasterTimer = null;
            try { RestoreVectorSurfaces(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            _directRaster?.Dispose(); _directRaster = null;
            _tiles.Clear();
            _tileRoot = null;
            if (_target != null)
            {
                try { _target.Root = null; } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
                DisposeSafely(_target);
                _target = null;
            }
            if (_ring != null)
            {
                try { _ring.Shapes.Clear(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            }
            for (int i = _ringObjects.Count - 1; i >= 0; i--) DisposeSafely(_ringObjects[i]);
            _ringObjects.Clear();
            for (int i = _objects.Count - 1; i >= 0; i--) DisposeSafely(_objects[i]);
            _objects.Clear();
            _gradient = null; _ring = null; _result = null; _ringBrush = null; _maskedBrush = null; _maskApplied = false; _geometry = null; _outerGeometry = null; _innerGeometry = null;
            _haloBrushes.Clear(); _coreBrush = null;
            if (_compositor != null) DisposeSafely(_compositor);
            _compositor = null;
        }
        finally
        {
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        }
    }

    private static void DisposeSafely(IDisposable value)
    {
        try { value.Dispose(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Composition cleanup: " + ex); }
    }

    private static void EnsureDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null || _queueController != IntPtr.Zero) return;
        var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 2 };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _queueController));
    }

    private static unsafe DesktopWindowTarget CreateTarget(Compositor compositor, IntPtr hwnd)
    {
        IntPtr unknown = MarshalInspectable<Compositor>.FromManaged(compositor);
        try
        {
            Guid id = DesktopInteropId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in id, out IntPtr interop));
            try
            {
                var vtable = *(IntPtr**)interop;
                var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)vtable[3];
                IntPtr target;
                Marshal.ThrowExceptionForHR(create(interop, hwnd, 0, &target));
                try { return DesktopWindowTarget.FromAbi(target); }
                finally { Marshal.Release(target); }
            }
            finally { Marshal.Release(interop); }
        }
        finally { Marshal.Release(unknown); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
