using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using WinRinglight;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--fullscreen-fixture")
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen!;
            using var form = new System.Windows.Forms.Form { Text = "WinRinglight CI fullscreen fixture",
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.None, StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                Bounds = screen.Bounds, TopMost = true, ShowInTaskbar = true };
            form.Shown += (_, _) => { form.Activate(); form.BringToFront(); };
            System.Windows.Forms.Application.Run(form);
            return 0;
        }
        // This test manipulates and saves settings. Never run it on a user's machine.
        if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("SKIP: Settings integration smoke test is restricted to an isolated GitHub Actions runner.");
            return 0;
        }
        IntPtr desktop = OpenInputDesktop(0, false, 1);
        if (desktop == IntPtr.Zero)
        {
            Console.WriteLine("SKIP: No accessible interactive input desktop; cannot run WPF/Composition integration smoke test.");
            return 0;
        }
        CloseDesktop(desktop);
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? window = null;
        try
        {
            window = new MainWindow();
            Config.Current.SelectedMonitors.Clear();
            Config.Current.SelectedMonitors.Add(0);
            Config.Current.IsSpanned = false;
            Config.Current.HideFromCapture = false;
            Config.Current.AutoWebcam = false;
            window.Show();
            Pump();
            var dpi = VisualTreeHelper.GetDpi(window);
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) || screens.Length == 0)
            {
                Console.WriteLine($"SKIP: Composition eligibility not met (screens={screens.Length}, dpi={dpi.DpiScaleX}/{dpi.DpiScaleY}).");
                return 0;
            }
            window.SetRinglightState(true);
            window.SetCompositionExperiment(true);
            if (!window.DevCompositionActive) throw new Exception("Composition failed to activate: " + window.DevCompositionStatus);
            IntPtr compositionHwnd = GetCompositionHwnd(window);
            int extended = GetWindowLong(compositionHwnd, -20);
            if ((extended & 0x00080000) == 0 || (extended & 0x20) == 0 || (extended & 0x00200000) == 0)
                throw new Exception("Composition overlay is missing expected layered, transparent or no-redirection flags.");
            var working = screens[0].WorkingArea;
            IntPtr wpfHwnd = new WindowInteropHelper(window).Handle;
            var testPoint = new Point { X = working.Left + working.Width / 2, Y = working.Top + working.Height / 3 };
            IntPtr hit = WindowFromPoint(testPoint);
            if (hit == compositionHwnd || hit == wpfHwnd)
                throw new Exception("Overlay appears to intercept the test point.");
            Console.WriteLine(hit == IntPtr.Zero
                ? "PARTIAL: Window flags verified; no underlying window found for click-through probe."
                : "PASS: Window flags and cross-window hit-test probe.");
            var grid = (Grid)window.FindName("MainGrid");
            if (grid.Visibility != Visibility.Hidden) throw new Exception("WPF grid not hidden in Composition mode.");
            var mask = (RadialGradientBrush)grid.OpacityMask;
            var children = grid.Children;
            var wpfRing = (Border)children[children.Count - 1];
            var colorBefore = ((SolidColorBrush)wpfRing.BorderBrush).Color;
            Config.Current.AutoTemperature = false;
            Config.Current.Temperature = 6500;
            window.ApplySettingsLive(1, Config.Current.Thickness, 6500);
            if (((SolidColorBrush)wpfRing.BorderBrush).Color != colorBefore)
                throw new Exception("Composition color update changed hidden WPF ring.");
            Config.Current.HideFromCapture = true;
            window.ApplyCaptureAffinity();
            if (!window.DevCompositionActive) throw new Exception("Capture exclusion caused fallback: " + window.DevCompositionStatus);
            if (!GetWindowDisplayAffinity(compositionHwnd, out uint affinity) || affinity != 0x11)
                throw new Exception("Composition window did not report capture exclusion.");
            Config.Current.HideFromCapture = false;
            window.ApplyCaptureAffinity();
            if (!window.DevCompositionActive) throw new Exception("Clearing capture exclusion caused fallback.");
            if (!GetWindowDisplayAffinity(compositionHwnd, out affinity) || affinity != 0)
                throw new Exception("Composition capture exclusion did not clear.");
            window.SetRinglightState(false);
            if (!window.DevCompositionActive) throw new Exception("Turning off caused Composition fallback.");
            window.SetRinglightState(true);
            if (!window.DevCompositionActive) throw new Exception("Turning on caused Composition fallback.");
            Console.WriteLine("PASS: Color isolation, capture API on/off and light on/off retain Composition.");
            var centerBefore = mask.Center;
            var update = typeof(MainWindow).GetMethod("UpdateCursorCutout", BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (GetCursorPos(out Point original) && SetCursorPos(original.X + 10, original.Y + 10) &&
                GetCursorPos(out Point moved) && (original.X != moved.X || original.Y != moved.Y))
            {
                update.Invoke(window, new object?[] { null, EventArgs.Empty });
                if (mask.Center != centerBefore) throw new Exception("Composition mode changed the hidden WPF mask.");
                window.SetCompositionExperiment(false);
                if (window.DevCompositionActive || grid.Visibility != Visibility.Visible) throw new Exception("WPF fallback failed.");
                if (mask.Center == centerBefore) throw new Exception("WPF mask did not resynchronise on fallback.");
                if (((SolidColorBrush)wpfRing.BorderBrush).Color == colorBefore)
                    throw new Exception("WPF ring color did not resynchronise on fallback.");
                SetCursorPos(original.X, original.Y);
                Console.WriteLine("PASS: Composition activation, isolated cursor mask, WPF fallback and cursor resync.");
            }
            else
            {
                window.SetCompositionExperiment(false);
                Console.WriteLine("PARTIAL: Composition and WPF fallback succeeded; automated cursor movement unavailable.");
            }
            window.SetCompositionExperiment(true);
            if (!window.DevCompositionActive) throw new Exception("Composition reactivation failed: " + window.DevCompositionStatus);
            TestSettings(window);
            TestLayouts(window, screens);
            TestFullscreenDetector(window, screens);
            Console.WriteLine("PASS: Composition reactivation and dynamic layouts remain active.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
        finally
        {
            window?.Close();
            application.Shutdown();
        }
    }

    private static void TestSettings(MainWindow window)
    {
        var settings = new SettingsWindow(window);
        try
        {
            settings.Show();
            Pump();
            var slider = (Slider)settings.FindName("SliderTemperature");
            var auto = (CheckBox)settings.FindName("ChkAutoTemp");
            if (!slider.IsEnabled) throw new Exception("Temperature slider disabled.");
            auto.IsChecked = true;
            PumpFor(650);
            if (!window.DevCompositionActive) throw new Exception("Composition fell back during temperature transition: " + window.DevCompositionStatus);
            var before = GetCoreColor(window);
            slider.Value = 3000;
            Pump();
            if (Math.Abs(Config.Current.Temperature - 3000) > 0.01 || GetCoreColor(window) != before)
                throw new Exception("Manual slider changed active automatic color or failed to save value.");
            auto.IsChecked = false;
            PumpFor(650);
            var after = GetCoreColor(window);
            if (after.R != 250 || after.G != 186 || after.B != 95)
                throw new Exception($"Temperature transition ended at wrong color: {after}");
            var fps = (TextBox)settings.FindName("TxtCursorFps");
            fps.Text = "90";
            fps.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            if (Config.Current.CursorMaxFps != 90 || !window.DevCompositionActive)
                throw new Exception("FPS setting did not update or caused fallback.");
            var unlimited = (CheckBox)settings.FindName("ChkUnlimitedFps");
            unlimited.IsChecked = true;
            if (!Config.Current.CursorUnlimitedFps || !window.DevCompositionActive)
                throw new Exception("Unlimited cursor mode did not activate or caused fallback.");
            unlimited.IsChecked = false;
            fps.Text = "60";
            fps.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            if (Config.Current.CursorMaxFps != 60 || Config.Current.CursorUnlimitedFps)
                throw new Exception("Cursor FPS setting did not restore.");
            var style = (ComboBox)settings.FindName("ComboStyle");
            style.SelectedIndex = 1;
            if (!window.DevCompositionActive) throw new Exception("Style change caused fallback: " + window.DevCompositionStatus);
            PumpFor(450);
            if ((bool)GetFirstBackend(window).GetType().GetField("_currentRounded", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(GetFirstBackend(window))!)
                throw new Exception("Composition style did not change.");
            style.SelectedIndex = 0;
            if (!window.DevCompositionActive) throw new Exception("Style change back caused fallback.");
            Console.WriteLine("PASS: Settings UI: automatic/manual temperature transition, slider, cursor FPS and style changes.");
        }
        finally { settings.Close(); }
    }

    private static Windows.UI.Color GetCoreColor(MainWindow window)
    {
        object backend = GetFirstBackend(window);
        var brush = (Windows.UI.Composition.CompositionColorBrush?)backend.GetType().GetField("_coreBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)
            ?? throw new Exception("Composition color brush missing.");
        return brush.Color;
    }

    private static void PumpFor(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static object GetScene(MainWindow window) => typeof(MainWindow).GetField("_compositionBackend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)
        ?? throw new Exception("Composition scene missing.");
    private static System.Collections.IList GetWindows(MainWindow window) =>
        (System.Collections.IList)GetScene(window).GetType().GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(GetScene(window))!;
    private static object GetFirstBackend(MainWindow window) => GetWindows(window)[0]!;

    private static void TestLayouts(MainWindow window, System.Windows.Forms.Screen[] screens)
    {
        var fullscreenField = typeof(MainWindow).GetField("_isFullscreenActive", BindingFlags.Instance | BindingFlags.NonPublic)!;
        fullscreenField.SetValue(window, true);
        window.UpdateMonitorSetup();
        AssertBounds(GetCompositionHwnd(window), screens[0].Bounds);
        if (!window.DevCompositionActive) throw new Exception("Fullscreen caused fallback.");
        fullscreenField.SetValue(window, false);
        window.UpdateMonitorSetup();
        AssertBounds(GetCompositionHwnd(window), screens[0].Bounds);
        Config.Current.IsSpanned = true;
        window.UpdateMonitorSetup();
        if (!window.DevCompositionActive || GetWindows(window).Count != 1) throw new Exception("Spanned mode failed.");
        Config.Current.IsSpanned = false;
        Config.Current.SelectedMonitors.Clear();
        window.UpdateMonitorSetup();
        if (!window.DevCompositionActive || GetWindows(window).Count != 0) throw new Exception("Empty monitor selection failed.");
        Config.Current.SelectedMonitors.Add(0);
        window.UpdateMonitorSetup();
        if (!window.DevCompositionActive || GetWindows(window).Count != 1) throw new Exception("Monitor reselection failed.");

        // Exercise two independent real Composition HWNDs even on a one-monitor runner.
        var scene = GetScene(window);
        var area = screens[0].WorkingArea;
        var left = new System.Drawing.Rectangle(area.Left, area.Top, area.Width / 2, area.Height);
        var right = new System.Drawing.Rectangle(left.Right, area.Top, area.Width - left.Width, area.Height);
        scene.GetType().GetMethod("Configure")!.Invoke(scene, new object[] { new[] { new CompositionLayout.Piece(left, left), new CompositionLayout.Piece(right, right) }, 300f,
            (float)Config.Current.Thickness, true, 4000d, true, true });
        var windows = GetWindows(window);
        if (windows.Count != 2) throw new Exception("Two-window scene was not created.");
        for (int i = 0; i < 2; i++)
        {
            var backend = windows[i]!;
            IntPtr hwnd = (IntPtr)backend.GetType().GetField("_hwnd", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
            AssertBounds(hwnd, i == 0 ? left : right);
            if (!GetWindowDisplayAffinity(hwnd, out uint affinity) || affinity != 0x11)
                throw new Exception("Capture exclusion missing on a multi-window scene.");
        }
        // The same screen cursor must be mapped to each window's local coordinates.
        if (GetCursorPos(out Point original) && SetCursorPos(area.Left + area.Width / 3, area.Top + area.Height / 3) && GetCursorPos(out Point moved))
        {
            scene.GetType().GetMethod("UpdateCursor")!.Invoke(scene, null);
            for (int i = 0; i < 2; i++)
            {
                var backend = windows[i]!;
                var gradient = (Windows.UI.Composition.CompositionRadialGradientBrush)backend.GetType().GetField("_gradient", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
                var bounds = i == 0 ? left : right;
                if (Math.Abs(gradient.EllipseCenter.X - (moved.X - bounds.Left)) > 0.01 || Math.Abs(gradient.EllipseCenter.Y - (moved.Y - bounds.Top)) > 0.01)
                    throw new Exception("Cursor coordinates differ across Composition windows.");
            }
            SetCursorPos(original.X, original.Y);
        }
        else Console.WriteLine("PARTIAL: Multi-window cursor positioning unavailable.");
        // Simulate an affinity failure on one of the two HWNDs. All windows
        // must be removed and WPF restored, not just the failing window.
        IntPtr first = (IntPtr)windows[0]!.GetType().GetField("_hwnd", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[0])!;
        IntPtr second = (IntPtr)windows[1]!.GetType().GetField("_hwnd", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[1])!;
        DestroyWindow(second);
        Config.Current.HideFromCapture = false;
        window.ApplyCaptureAffinity();
        if (window.DevCompositionActive || IsWindow(first) || IsWindow(second) || ((Grid)window.FindName("MainGrid")).Visibility != Visibility.Visible)
            throw new Exception("Capture failure did not clean up the complete scene and restore WPF.");
        window.SetCompositionExperiment(true);
        if (!window.DevCompositionActive || GetWindows(window).Count != 1) throw new Exception("Returning to real monitor layout failed.");
        Console.WriteLine("PASS: Fullscreen/working area, spanning, empty selection, reselection, two-HWND cursor/capture and failure cleanup.");
    }

    private static void TestFullscreenDetector(MainWindow window, System.Windows.Forms.Screen[] screens)
    {
        if (screens.Length != 1 || Environment.ProcessPath == null ||
            System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("SKIP: External fullscreen detector fixture requires a one-monitor apphost.");
            return;
        }
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--fullscreen-fixture")
        { UseShellExecute = false, CreateNoWindow = true }) ?? throw new Exception("Could not start fullscreen fixture.");
        var field = typeof(MainWindow).GetField("_isFullscreenActive", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            for (int i = 0; i < 30 && !(bool)field.GetValue(window)!; i++)
            {
                if (process.HasExited) throw new Exception("Fullscreen fixture exited unexpectedly.");
                PumpFor(100);
            }
            if (!(bool)field.GetValue(window)! || !window.DevCompositionActive)
                throw new Exception("Fullscreen detector did not activate Composition fullscreen layout.");
            AssertBounds(GetCompositionHwnd(window), screens[0].Bounds);
            process.Kill();
            process.WaitForExit();
            for (int i = 0; i < 30 && (bool)field.GetValue(window)!; i++) PumpFor(100);
            if ((bool)field.GetValue(window)! || !window.DevCompositionActive)
                throw new Exception("Fullscreen detector did not restore Composition working area.");
            AssertBounds(GetCompositionHwnd(window), screens[0].Bounds);
            Console.WriteLine("PASS: External fullscreen window triggers real detector and restores working area.");
        }
        finally { if (!process.HasExited) process.Kill(); }
    }

    private static void AssertBounds(IntPtr hwnd, System.Drawing.Rectangle expected)
    {
        if (!GetWindowRect(hwnd, out NativeRect r) || r.Left != expected.Left || r.Top != expected.Top ||
            r.Right != expected.Right || r.Bottom != expected.Bottom)
            throw new Exception($"Composition bounds differ from expected {expected}.");
    }

    private static IntPtr GetCompositionHwnd(MainWindow window)
    {
        object backend = GetFirstBackend(window);
        return (IntPtr)(backend.GetType().GetField("_hwnd", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)
            ?? throw new Exception("Composition HWND missing."));
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
}
