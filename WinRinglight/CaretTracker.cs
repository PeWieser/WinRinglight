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
    private readonly AutoResetEvent _wake = new(false);
    private readonly System.Collections.Generic.List<IntPtr> _hooks = new();
    private WinEventCallback? _eventCallback;
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

    public void SetRate(int fps, bool unlimited) => _intervalMs = unlimited ? 0 : Math.Max(1, (int)Math.Round(1000.0 / Math.Clamp(fps, 1, 240)));

    public void SetEnabled(bool enabled)
    {
        Interlocked.Increment(ref _generation);
        _enabled = enabled;
        if (!enabled) { RemoveHooks(); _expiry.Stop(); Publish(null); _wake.Set(); return; }
        InstallHooks();
        _expiry.Start();
        _wake.Set();
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
        long lastQuery = 0;
        while (!_disposed)
        {
            // Accessibility events wake the worker immediately. Some browsers do
            // not emit reliable caret notifications, so sample at the chosen
            // cursor rate too; unchanged positions never invalidate a mask.
            _wake.WaitOne(_intervalMs == 0 ? 16 : _intervalMs);
            if (_enabled)
            {
                double elapsed = lastQuery == 0 ? double.MaxValue : Stopwatch.GetElapsedTime(lastQuery).TotalMilliseconds;
                int remaining = _intervalMs - (int)Math.Min(elapsed, _intervalMs);
                if (remaining > 0) Thread.Sleep(remaining);
                if (!_enabled || _disposed) continue;
                lastQuery = Stopwatch.GetTimestamp();
                int generation = Volatile.Read(ref _generation);
                IntPtr foreground = GetForegroundWindow();
                long started = Stopwatch.GetTimestamp();
                Point? point = null;
                try { point = FindCaret(foreground, foreground == previousForeground ? previous : null); }
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
        if ((_sampleForeground != GetForegroundWindow() || Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastSuccess)) > TimeSpan.FromMilliseconds(Math.Max(1000, _intervalMs * 3 + 100))) && _lastPoint != null)
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

    private static Point? FindCaret(IntPtr foreground, Point? previous)
    {
        if (foreground == IntPtr.Zero) return null;
        // UIA's active caret is more accurate than GUITHREADINFO in browsers:
        // their native caret rectangle can represent an entire text field.
        var focused = AutomationElement.FocusedElement;
        bool belongsToForeground = FocusBelongsToForeground(focused, foreground);
        if (belongsToForeground)
        {
            var modern = FindAutomationCaret(out _);
            if (modern != null) return modern;
            var legacy = FindLegacyCaret(focused, previous);
            if (legacy != null) return legacy;
        }
        // Classic Win32 text controls provide a native caret in client pixels.
        uint thread = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (thread != 0 && GetGUIThreadInfo(thread, ref info) && info.Caret != IntPtr.Zero && info.Focus != IntPtr.Zero &&
            GetAncestor(info.Caret, 2) == foreground && GetAncestor(info.Focus, 2) == foreground)
        {
            var p = new NativePoint { X = info.Rect.Left, Y = (info.Rect.Top + info.Rect.Bottom) / 2 };
            if (ClientToScreen(info.Caret, ref p)) return new Point(p.X, p.Y);
        }
        return null;
    }

    // UIA can temporarily report the element from the previously focused app.
    // Do not let that stale caret mask a newly foregrounded browser/window.
    private static bool FocusBelongsToForeground(AutomationElement? element, IntPtr foreground)
    {
        try
        {
            for (int depth = 0; element != null && depth < 48; depth++)
            {
                int handle = element.Current.NativeWindowHandle;
                if (handle != 0)
                {
                    var root = GetAncestor(new IntPtr(handle), 2);
                    return root == foreground;
                }
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
        }
        catch (Exception ex) when (ex is COMException or ElementNotAvailableException or InvalidOperationException) { }
        return false;
    }

    // TextPattern1 exposes selection but not its active endpoint. For selected
    // text we preserve the end closest to the last caret position; direction
    // cannot be determined reliably from this older provider alone.
    private static Point? FindLegacyCaret(AutomationElement? focused, Point? previous)
    {
        try
        {
            if (focused == null || !focused.TryGetCurrentPattern(TextPattern.Pattern, out object value)) return null;
            var selections = ((TextPattern)value).GetSelection();
            if (selections.Length != 1) return null;
            var selection = selections[0];
            bool collapsed = selection.CompareEndpoints(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End) == 0;
            var direct = selection.GetBoundingRectangles();
            if (!collapsed && direct.Length > 0)
            {
                var first = direct[0];
                var last = direct[direct.Length - 1];
                var a = ToPoint(first.Left, first.Top, first.Height);
                var b = ToPoint(last.Right, last.Top, last.Height);
                if (a == null) return b;
                if (b == null || previous == null) return a;
                double Dist(Point p) => Math.Pow((double)p.X - previous.Value.X, 2) + Math.Pow((double)p.Y - previous.Value.Y, 2);
                return Dist(a.Value) <= Dist(b.Value) ? a : b;
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
            // Some Chromium UIA providers give a fixed location for a
            // degenerate caret range. Only use its rectangle as a last resort
            // when no neighbouring character geometry is available.
            if (direct.Length > 0 && !direct[0].IsEmpty && direct[0].Width <= Math.Max(4, direct[0].Height / 2))
                return ToPoint(direct[0].Left, direct[0].Top, direct[0].Height);
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
            if (rects == null || rects.Length < 4 || !double.IsFinite(rects[2]) || rects[2] > Math.Max(4, rects[3] / 2)) return null;
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

    private void InstallHooks()
    {
        if (_hooks.Count != 0) return;
        _eventCallback ??= OnWinEvent;
        void Add(uint first, uint last)
        {
            IntPtr hook = SetWinEventHook(first, last, IntPtr.Zero, _eventCallback, 0, 0, 0);
            if (hook != IntPtr.Zero) _hooks.Add(hook);
        }
        Add(3, 3);             // foreground
        Add(0x8002, 0x800e);  // caret show/hide, focus, selection, location, value
        Add(0x8014, 0x8014);  // text selection
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
    {
        if (!_enabled) return;
        if (evt == 3) { _wake.Set(); return; }
        if (hwnd == IntPtr.Zero || GetAncestor(hwnd, 2) != GetForegroundWindow()) return;
        if (evt == 0x8005 || evt == 0x8006 || evt == 0x8009 || evt == 0x800e || evt == 0x8014 ||
            (objectId == -8 && (evt == 0x8002 || evt == 0x8003 || evt == 0x800b))) _wake.Set();
    }

    private void RemoveHooks()
    {
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
    }

    public void Dispose() { _enabled = false; _disposed = true; RemoveHooks(); _wake.Set(); _expiry.Stop(); Publish(null); }
    private delegate void WinEventCallback(IntPtr hook, uint evt, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
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
