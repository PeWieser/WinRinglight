using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace WinRinglight;

// Accessibility providers run in other processes and can hang. Keep all UIA
// calls on one background thread, never on the rendering/dispatcher thread.
// Only caret geometry is read; no text, keystrokes or document contents.
internal sealed class CaretTracker : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<Point?> _changed;
    private readonly DispatcherTimer _expiry;
    private Thread? _worker;
    private volatile bool _enabled, _disposed;
    private int _generation;
    private volatile int _intervalMs = 16;
    private int _refreshVersion;
    private long _lastSuccess;
    private IntPtr _sampleForeground;
    private Point? _lastPoint;

    public CaretTracker(Dispatcher dispatcher, Action<Point?> changed)
    {
        _dispatcher = dispatcher; _changed = changed;
        _expiry = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => Expire(), dispatcher);
        _expiry.Stop();
    }

    public void SetRate(int fps, bool unlimited) => _intervalMs = Math.Max(1, (int)Math.Round(1000.0 / (unlimited ? 240 : Math.Clamp(fps, 1, 240))));

    public void SetEnabled(bool enabled)
    {
        Interlocked.Increment(ref _generation);
        _enabled = enabled;
        if (!enabled) { _expiry.Stop(); Publish(null); return; }
        _expiry.Start();
        if (_worker != null) return;
        _worker = new Thread(Work) { IsBackground = true, Name = "Text caret position" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    private void Work()
    {
        Point? previous = null;
        IntPtr previousForeground = IntPtr.Zero;
        int previousGeneration = -1, previousRefresh = -1;
        while (!_disposed)
        {
            if (_enabled)
            {
                int generation = Volatile.Read(ref _generation);
                IntPtr foreground = GetForegroundWindow();
                long started = Stopwatch.GetTimestamp();
                Point? point = null;
                try { point = FindCaret(foreground); }
                catch (Exception ex) { Trace.WriteLine("Caret lookup: " + ex.Message); }
                if (!_disposed && _enabled && generation == Volatile.Read(ref _generation) && foreground != IntPtr.Zero &&
                    foreground == GetForegroundWindow() && Stopwatch.GetElapsedTime(started) <= TimeSpan.FromSeconds(1))
                {
                    Interlocked.Exchange(ref _lastSuccess, Stopwatch.GetTimestamp());
                    // No dispatcher work or mask invalidation for an unchanged caret.
                    int refresh = Volatile.Read(ref _refreshVersion);
                    if (point != previous || foreground != previousForeground || generation != previousGeneration || refresh != previousRefresh)
                    {
                        previous = point; previousForeground = foreground; previousGeneration = generation; previousRefresh = refresh;
                        if (!_dispatcher.HasShutdownStarted)
                        {
                            try { _dispatcher.BeginInvoke(() => Accept(generation, foreground, started, point)); }
                            catch (InvalidOperationException) { }
                        }
                    }
                }
            }
            Thread.Sleep(_enabled ? _intervalMs : 150);
        }
    }

    private void Accept(int generation, IntPtr foreground, long started, Point? point)
    {
        if (!_enabled || generation != Volatile.Read(ref _generation) || Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(1) || foreground == IntPtr.Zero || foreground != GetForegroundWindow()) return;
        _sampleForeground = foreground;
        Publish(point);
    }

    private void Expire()
    {
        if ((_sampleForeground != GetForegroundWindow() || Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastSuccess)) > TimeSpan.FromSeconds(1)) && _lastPoint != null)
        {
            Interlocked.Increment(ref _refreshVersion);
            Publish(null);
        }
    }

    private void Publish(Point? point)
    {
        if (_lastPoint == point) return;
        _lastPoint = point;
        _changed(point);
    }

    private static Point? FindCaret(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero) return null;
        // Native GUI carets are cheap and are available even when a UIA
        // provider advertises an empty caret rectangle.
        uint thread = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (thread != 0 && GetGUIThreadInfo(thread, ref info) && info.Caret != IntPtr.Zero && info.Focus != IntPtr.Zero)
        {
            var p = new NativePoint { X = info.Rect.Left, Y = (info.Rect.Top + info.Rect.Bottom) / 2 };
            if (ClientToScreen(info.Caret, ref p)) return new Point(p.X, p.Y);
        }
        var modern = FindAutomationCaret(out bool inactive);
        if (modern != null || inactive) return modern;
        return FindLegacyCaret();
    }

    // Some providers support only TextPattern, not TextPattern2. Inspect only
    // collapsed selection geometry; never ask the provider for text content.
    private static Point? FindLegacyCaret()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null || !focused.TryGetCurrentPattern(TextPattern.Pattern, out object value)) return null;
            var selections = ((TextPattern)value).GetSelection();
            if (selections.Length != 1) return null;
            var selection = selections[0];
            if (selection.CompareEndpoints(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End) != 0) return null;
            var direct = selection.GetBoundingRectangles();
            if (direct.Length > 0 && !direct[0].IsEmpty)
            {
                var position = ToPoint(direct[0].Left, direct[0].Top, direct[0].Height);
                if (position != null) return position;
            }
            var forward = selection.Clone();
            if (forward.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1) != 0)
            {
                var rects = forward.GetBoundingRectangles();
                if (rects.Length > 0 && !rects[0].IsEmpty) return ToPoint(rects[0].Left, rects[0].Top, rects[0].Height);
            }
            var backward = selection.Clone();
            if (backward.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1) != 0)
            {
                var rects = backward.GetBoundingRectangles();
                if (rects.Length > 0 && !rects[rects.Length - 1].IsEmpty)
                {
                    var rect = rects[rects.Length - 1];
                    return ToPoint(rect.Right, rect.Top, rect.Height);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or ElementNotAvailableException or InvalidOperationException) { }
        return null;
    }

    private static Point? ToPoint(double x, double y, double height) =>
        double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(height) && height > 0 &&
        x >= int.MinValue && x <= int.MaxValue && y >= int.MinValue && y + height <= int.MaxValue
            ? new Point((int)Math.Round(x), (int)Math.Round(y + height / 2)) : null;

    private static Point? FindAutomationCaret(out bool inactive)
    {
        inactive = false;
        object? automationObject = null;
        IAutomationElement? element = null;
        ITextPattern2? pattern = null;
        ITextRange? range = null;
        try
        {
            automationObject = new CUIAutomation8();
            ((IAutomation)automationObject).GetFocusedElement(out element);
            if (element == null) return null;
            Guid iid = typeof(ITextPattern2).GUID;
            element.GetCurrentPatternAs(10024, ref iid, out pattern);
            if (pattern == null) return null;
            pattern.GetCaretRange(out int active, out range);
            if (active == 0) { inactive = true; return null; }
            if (range == null) return null;
            range.GetBoundingRectangles(out double[] rects);
            if (rects == null || rects.Length < 4) return null;
            return ToPoint(rects[0], rects[1], rects[3]);
        }
        catch (COMException) { return null; }
        finally
        {
            if (range != null) Marshal.ReleaseComObject(range);
            if (pattern != null) Marshal.ReleaseComObject(pattern);
            if (element != null) Marshal.ReleaseComObject(element);
            if (automationObject != null) Marshal.ReleaseComObject(automationObject);
        }
    }

    // Method order follows the Windows SDK UIAutomationClient.h vtables.
    [ComImport, Guid("e22ad333-b25f-460c-83d0-0581107395c9")] private class CUIAutomation8 { }
    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAutomation
    {
        void CompareElements(); void CompareRuntimeIds(); void GetRootElement(); void ElementFromHandle(); void ElementFromPoint();
        void GetFocusedElement(out IAutomationElement element);
    }
    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAutomationElement
    {
        void SetFocus(); void GetRuntimeId(); void FindFirst(); void FindAll(); void FindFirstBuildCache();
        void FindAllBuildCache(); void BuildUpdatedCache(); void GetCurrentPropertyValue(); void GetCurrentPropertyValueEx();
        void GetCachedPropertyValue(); void GetCachedPropertyValueEx();
        void GetCurrentPatternAs(int patternId, ref Guid iid, out ITextPattern2 pattern);
    }
    [ComImport, Guid("506a921a-fcc9-409f-b23b-37eb74106872"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITextPattern2
    {
        void RangeFromPoint(); void RangeFromChild(); void GetSelection(); void GetVisibleRanges();
        void GetDocumentRange(); void GetSupportedTextSelection(); void RangeFromAnnotation();
        void GetCaretRange(out int active, out ITextRange range);
    }
    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITextRange
    {
        void Clone(); void Compare(); void CompareEndpoints(); void ExpandToEnclosingUnit(); void FindAttribute(); void FindText(); void GetAttributeValue();
        void GetBoundingRectangles([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] out double[] rectangles);
    }

    public void Dispose() { _enabled = false; _disposed = true; _expiry.Stop(); Publish(null); }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public NativeRect Rect;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
}
