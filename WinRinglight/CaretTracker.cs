using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Threading;

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
    private long _lastSuccess;
    private IntPtr _sampleForeground;
    private Point? _lastPoint;

    public CaretTracker(Dispatcher dispatcher, Action<Point?> changed)
    {
        _dispatcher = dispatcher; _changed = changed;
        _expiry = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => Expire(), dispatcher);
        _expiry.Stop();
    }

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
        while (!_disposed)
        {
            if (_enabled)
            {
                int generation = Volatile.Read(ref _generation);
                IntPtr foreground = GetForegroundWindow();
                Point? point = null;
                try { point = FindCaret(foreground); }
                catch (Exception ex) { Trace.WriteLine("Caret lookup: " + ex.Message); }
                if (!_disposed && !_dispatcher.HasShutdownStarted)
                {
                    try { _dispatcher.BeginInvoke(() => Accept(generation, foreground, point)); }
                    catch (InvalidOperationException) { }
                }
            }
            Thread.Sleep(150);
        }
    }

    private void Accept(int generation, IntPtr foreground, Point? point)
    {
        if (!_enabled || generation != Volatile.Read(ref _generation) || foreground == IntPtr.Zero || foreground != GetForegroundWindow()) return;
        _sampleForeground = foreground;
        _lastSuccess = Stopwatch.GetTimestamp();
        Publish(point);
    }

    private void Expire()
    {
        if (_sampleForeground != GetForegroundWindow() || Stopwatch.GetElapsedTime(_lastSuccess) > TimeSpan.FromSeconds(1)) Publish(null);
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
        // UI Automation works with many modern controls (including browsers).
        // GetCaretRange returns only geometry, not the text around the caret.
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused != null && focused.TryGetCurrentPattern(TextPattern2.Pattern, out object value))
            {
                var range = ((TextPattern2)value).GetCaretRange(out bool active);
                if (active && range != null)
                {
                    var rects = range.GetBoundingRectangles();
                    if (rects.Length > 0 && !rects[0].IsEmpty && double.IsFinite(rects[0].X) && double.IsFinite(rects[0].Y))
                        return new Point((int)Math.Round(rects[0].X), (int)Math.Round(rects[0].Y + rects[0].Height / 2));
                }
                // An inactive range must not leave an old cutout visible.
                return null;
            }
        }
        catch (ElementNotAvailableException) { return null; }
        catch (System.Runtime.InteropServices.COMException) { return null; }
        // Classic Win32 edit controls may expose only the GUI-thread caret.
        uint thread = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (thread == 0 || !GetGUIThreadInfo(thread, ref info) || info.Caret == IntPtr.Zero || info.Focus == IntPtr.Zero) return null;
        var p = new NativePoint { X = info.Rect.Left, Y = (info.Rect.Top + info.Rect.Bottom) / 2 };
        return ClientToScreen(info.Caret, ref p) ? new Point(p.X, p.Y) : null;
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
