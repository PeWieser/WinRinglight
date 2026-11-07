using System;
using System.Reflection;
using System.Linq;
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
        if (args.Length == 1 && args[0] == "--wpf-caret-fixture")
        {
            var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            var app = new System.Windows.Application();
            var input = new System.Windows.Controls.TextBox { Text = "WPF caret fixture", Margin = new Thickness(10) };
            var readOnly = new System.Windows.Controls.TextBox { Text = "Read only", IsReadOnly = true, Margin = new Thickness(10) };
            var container = new StackPanel(); container.Children.Add(input); container.Children.Add(readOnly);
            var form = new Window { Title = "WPF caret fixture", Left = area.Left + 100, Top = area.Top + 280,
                Width = 400, Height = 180, Topmost = true, Content = container };
            form.ContentRendered += (_, _) => { form.Activate(); input.Focus(); input.CaretIndex = input.Text.Length; };
            app.Run(form);
            return 0;
        }
        if (args.Length == 1 && args[0] == "--caret-fixture")
        {
            var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            using var form = new System.Windows.Forms.Form { Text = "WinRinglight CI caret fixture", StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                Bounds = new System.Drawing.Rectangle(area.Left + 100, area.Top + 100, 400, 160), TopMost = true };
            var input = new System.Windows.Forms.TextBox { Left = 20, Top = 20, Width = 250, Text = "Caret fixture" };
            form.Controls.Add(input);
            form.Shown += (_, _) => { form.Activate(); input.Focus(); input.SelectionStart = input.Text.Length; };
            System.Windows.Forms.Application.Run(form);
            return 0;
        }
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
            // Preserve existing legacy-backend regression coverage; tiled mode
            // is exercised separately below, including real pixels and seams.
            window.SetDevTiling(false);
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
            TestFullscreenDetector(window, screens, false);
            TestFullscreenDetector(window, screens, true);
            TestTiled(window, screens[0]);
            TestCaretMasks(window, screens[0]);
            TestCaretTracking(window, screens[0], "--caret-fixture");
            TestCaretTracking(window, screens[0], "--wpf-caret-fixture");
            TestCaretSwitch(window);
            TestCaretWatchdog();
            TestChromeAddressCaret(window);
            window.SetDevTiling(true);
            if (!window.DevCompositionActive || !(bool)GetScene(window).GetType().GetProperty("UsingTiles")!.GetValue(GetScene(window))!)
                throw new Exception("Could not activate tiled renderer for fullscreen tests: " + window.DevCompositionStatus);
            TestFullscreenDetector(window, screens, false);
            TestFullscreenDetector(window, screens, true);
            if (!window.DevCompositionActive || !(bool)GetScene(window).GetType().GetProperty("UsingTiles")!.GetValue(GetScene(window))!)
                throw new Exception("Tiled renderer did not remain active after fullscreen tests: " + window.DevCompositionStatus);
            window.SetDevTiling(false);
            TestRasterQuality(window, screens[0]);
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
            var caretOption = (CheckBox)settings.FindName("ChkTextCaret");
            caretOption.IsChecked = true;
            string configPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinRinglight", "settings.json");
            using (var saved = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(configPath)))
                if (!Config.Current.TextCaretCutout || !saved.RootElement.GetProperty("TextCaretCutout").GetBoolean())
                    throw new Exception("Caret option was not enabled and persisted.");
            caretOption.IsChecked = false;
            if (Config.Current.TextCaretCutout) throw new Exception("Caret option did not disable.");
            Console.WriteLine("PASS: Text caret setting can be enabled, saved and disabled.");
            var fps = (TextBox)settings.FindName("TxtCursorFps");
            fps.Text = "90";
            fps.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            if (Config.Current.CursorMaxFps != 90 || !window.DevCompositionActive)
                throw new Exception("FPS setting did not update or caused fallback.");
            object caretTracker = typeof(MainWindow).GetField("_caretTracker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var caretInterval = caretTracker.GetType().GetField("_intervalMs", BindingFlags.Instance | BindingFlags.NonPublic)!;
            if ((int)caretInterval.GetValue(caretTracker)! != 11) throw new Exception("Caret rate did not follow the cursor FPS setting.");
            var unlimited = (CheckBox)settings.FindName("ChkUnlimitedFps");
            unlimited.IsChecked = true;
            if (!Config.Current.CursorUnlimitedFps || !window.DevCompositionActive)
                throw new Exception("Unlimited cursor mode did not activate or caused fallback.");
            if ((int)caretInterval.GetValue(caretTracker)! != 0) throw new Exception("Unlimited caret rate was not enabled.");
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
            var quality = (CheckBox)settings.FindName("ChkDevSupersampling");
            quality.IsChecked = true;
            if (!window.DevCompositionActive || !window.DevSupersampling) throw new Exception("Supersampling toggle failed.");
            object highBackend = GetFirstBackend(window);
            float qualityScale = (float)highBackend.GetType().GetField("_rasterScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(highBackend)!;
            if (qualityScale < 1 || qualityScale > 2) throw new Exception("Supersampling scale out of range.");
            quality.IsChecked = false;
            float nativeScale = (float)GetFirstBackend(window).GetType().GetField("_rasterScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(GetFirstBackend(window))!;
            if (!window.DevCompositionActive || window.DevSupersampling || nativeScale != 1) throw new Exception("Native resolution toggle failed.");
            Console.WriteLine($"PASS: Optional supersampling toggles safely; scale={qualityScale:F2} then {nativeScale:F2}.");
            var monitorItems = (ItemsControl)settings.FindName("MonitorItemsControl");
            object? sourceBefore = monitorItems.ItemsSource;
            int selectionCount = Config.Current.SelectedMonitors.Count;
            typeof(MainWindow).GetMethod("DisplaySettingsChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object?[] { null, EventArgs.Empty });
            Pump();
            if (ReferenceEquals(sourceBefore, monitorItems.ItemsSource) || monitorItems.Items.Count != System.Windows.Forms.Screen.AllScreens.Length || Config.Current.SelectedMonitors.Count != selectionCount)
                throw new Exception("Open settings did not refresh monitors or changed the saved selection.");
            Console.WriteLine("PASS: Open settings refreshes monitor list on a display event without changing selection.");
            Console.WriteLine("PASS: Settings UI: automatic/manual temperature transition, slider, cursor/caret FPS and style changes.");
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
        var fullscreenSet = (System.Collections.Generic.HashSet<string>)typeof(MainWindow).GetField("_fullscreenMonitors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        fullscreenSet.Add(screens[0].DeviceName);
        window.UpdateMonitorSetup();
        AssertBounds(GetCompositionHwnd(window), screens[0].Bounds);
        if (!window.DevCompositionActive) throw new Exception("Fullscreen caused fallback.");
        fullscreenSet.Clear();
        window.UpdateMonitorSetup();
        AssertBounds(GetCompositionHwnd(window), screens[0].Bounds);
        Config.Current.IsSpanned = true;
        window.UpdateMonitorSetup();
        if (!window.DevCompositionActive || GetWindows(window).Count != 1) throw new Exception("Spanned mode failed.");
        AssertRingBounds(window, screens[0].WorkingArea);
        fullscreenSet.Add(screens[0].DeviceName);
        window.UpdateMonitorSetup();
        if (!window.DevCompositionActive) throw new Exception("Spanned fullscreen caused fallback.");
        AssertRingBounds(window, screens[0].Bounds);
        fullscreenSet.Clear();
        window.UpdateMonitorSetup();
        AssertRingBounds(window, screens[0].WorkingArea);
        Console.WriteLine("PASS: Spanned mode enters and exits fullscreen bounds.");
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
        scene.GetType().GetMethod("Configure")!.Invoke(scene, new object[] { new[] { new CompositionLayout.Piece(left, area), new CompositionLayout.Piece(right, area) }, 300f,
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
        var g0 = (Windows.UI.Composition.CompositionRoundedRectangleGeometry)windows[0]!.GetType().GetField("_geometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[0])!;
        var g1 = (Windows.UI.Composition.CompositionRoundedRectangleGeometry)windows[1]!.GetType().GetField("_geometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[1])!;
        float raster0 = (float)windows[0]!.GetType().GetField("_rasterScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[0])!;
        float raster1 = (float)windows[1]!.GetType().GetField("_rasterScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[1])!;
        if (Math.Abs((g0.Offset.X / raster0 + left.Left) - (g1.Offset.X / raster1 + right.Left)) > 0.01 ||
            Math.Abs((g0.Offset.Y / raster0 + left.Top) - (g1.Offset.Y / raster1 + right.Top)) > 0.01 || g0.Size / raster0 != g1.Size / raster1 || g0.CornerRadius / raster0 != g1.CornerRadius / raster1)
            throw new Exception("Spanned pieces do not share the same global ring geometry.");
        IntPtr firstBeforeAnimation = (IntPtr)windows[0]!.GetType().GetField("_hwnd", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[0])!;
        var changedRing = new System.Drawing.Rectangle(area.Left, area.Top, area.Width, area.Height - 30);
        scene.GetType().GetMethod("Configure")!.Invoke(scene, new object[] {
            new[] { new CompositionLayout.Piece(left, changedRing), new CompositionLayout.Piece(right, changedRing) }, 300f,
            (float)Config.Current.Thickness, true, 4000d, true, true });
        if ((IntPtr)windows[0]!.GetType().GetField("_hwnd", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[0])! != firstBeforeAnimation ||
            g0.TryGetAnimationController("Offset") == null || g0.TryGetAnimationController("Size") == null || g1.TryGetAnimationController("Size") == null)
            throw new Exception("Layout change replaced windows or failed to start geometry animations.");
        var visual = (Windows.UI.Composition.SpriteVisual)windows[0]!.GetType().GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[0])!;
        scene.GetType().GetMethod("SetVisible")!.Invoke(scene, new object[] { false });
        if (visual.TryGetAnimationController("Opacity") == null) throw new Exception("Fade animation did not start.");
        scene.GetType().GetMethod("SetVisible")!.Invoke(scene, new object[] { true });
        if (visual.TryGetAnimationController("Opacity") == null) throw new Exception("Reversed fade animation did not start.");
        Console.WriteLine("PASS: Spanned pieces share global geometry and layout/fade animation controllers start without window recreation.");
        // Probe actual desktop pixels on both sides of the synthetic seam, with
        // capture exclusion disabled. Some hosted desktops cannot be sampled.
        window.ApplyCaptureAffinity();
        if (GetCursorPos(out Point beforeProbe))
        {
            SetCursorPos(area.Left + area.Width / 2, area.Bottom - 1);
            scene.GetType().GetMethod("UpdateCursor")!.Invoke(scene, null);
            Pump();
            System.Threading.Thread.Sleep(650);
            DwmFlush();
            float scale = (float)windows[0]!.GetType().GetProperty("DpiScale")!.GetValue(windows[0])!;
            int y = area.Top + (int)(20 * scale + Config.Current.Thickness * scale / 2);
            int x1 = left.Right - area.Width / 8, x2 = left.Right + area.Width / 8;
            IntPtr dc = GetDC(IntPtr.Zero);
            uint p1 = GetPixel(dc, x1, y), p2 = GetPixel(dc, x2, y);
            ReleaseDC(IntPtr.Zero, dc);
            static bool RingColor(uint p) => p != 0xffffffff && Math.Abs((int)(p & 255) - 246) < 16 && Math.Abs((int)((p >> 8) & 255) - 203) < 16 && Math.Abs((int)((p >> 16) & 255) - 141) < 16;
            Console.WriteLine(RingColor(p1) && RingColor(p2)
                ? "PASS: Desktop pixel probe saw the shared ring on both sides of the synthetic seam."
                : $"PARTIAL: Desktop pixel probe inconclusive (left=0x{p1:X8}, right=0x{p2:X8}); visible ring not established.");
            SetCursorPos(beforeProbe.X, beforeProbe.Y);
        }
        else Console.WriteLine("PARTIAL: Desktop pixel probe could not position cursor.");
        // Model different-height neighbouring monitors with no inner sides.
        var shortRight = new System.Drawing.Rectangle(right.Left, right.Top + 40, right.Width, right.Height - 100);
        scene.GetType().GetMethod("Configure")!.Invoke(scene, new object[] {
            new[] { new CompositionLayout.Piece(left, left, false, true), new CompositionLayout.Piece(right, shortRight, true, false) },
            300f, (float)Config.Current.Thickness, true, 4000d, false, true });
        windows = GetWindows(window);
        var leftBackend = windows[0]!; var rightBackend = windows[1]!;
        var gl = (Windows.UI.Composition.CompositionRoundedRectangleGeometry)leftBackend.GetType().GetField("_geometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(leftBackend)!;
        var gr = (Windows.UI.Composition.CompositionRoundedRectangleGeometry)rightBackend.GetType().GetField("_geometry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rightBackend)!;
        float leftRaster = (float)leftBackend.GetType().GetField("_rasterScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(leftBackend)!;
        float rightRaster = (float)rightBackend.GetType().GetField("_rasterScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rightBackend)!;
        if ((gl.Offset.X + gl.Size.X) / leftRaster <= left.Width || gr.Offset.X >= 0 ||
            gl.Offset.Y / leftRaster == gr.Offset.Y / rightRaster)
            throw new Exception("Full-height spanned geometry has a seam border or did not respect the individual height.");
        if (GetCursorPos(out Point savedCursor))
        {
            SetCursorPos(area.Left + area.Width / 2, area.Top);
            scene.GetType().GetMethod("UpdateCursor")!.Invoke(scene, null);
            Pump(); System.Threading.Thread.Sleep(650); DwmFlush();
            float scale = (float)leftBackend.GetType().GetProperty("DpiScale")!.GetValue(leftBackend)!;
            int inset = (int)(20 * scale + Config.Current.Thickness * scale / 2);
            IntPtr dc = GetDC(IntPtr.Zero);
            uint bottomLeft = GetPixel(dc, left.Left + left.Width / 2, left.Bottom - inset);
            uint bottomRight = GetPixel(dc, right.Left + right.Width / 2, shortRight.Bottom - inset);
            uint seamLeft = GetPixel(dc, left.Right - 4, area.Top + area.Height / 2);
            uint seamRight = GetPixel(dc, right.Left + 4, area.Top + area.Height / 2);
            ReleaseDC(IntPtr.Zero, dc);
            static bool IsRing(uint p) => p != 0xffffffff && Math.Abs((int)(p & 255) - 246) < 16 && Math.Abs((int)((p >> 8) & 255) - 203) < 16 && Math.Abs((int)((p >> 16) & 255) - 141) < 16;
            Console.WriteLine(IsRing(bottomLeft) && IsRing(bottomRight) && !IsRing(seamLeft) && !IsRing(seamRight)
                ? "PASS: Different-height segments have visible bottom edges and no inner vertical ring."
                : $"PARTIAL: Segment pixel probe inconclusive (bottom={bottomLeft:X8}/{bottomRight:X8}, seam={seamLeft:X8}/{seamRight:X8}).");
            SetCursorPos(savedCursor.X, savedCursor.Y);
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
                bool active = (bool)backend.GetType().GetField("_maskApplied", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
                bool shouldBeActive = CursorCoverage.Intersects(bounds, moved.X, moved.Y, 300);
                if (active != shouldBeActive || (active && (Math.Abs(gradient.EllipseCenter.X - (moved.X - bounds.Left)) > 0.01 || Math.Abs(gradient.EllipseCenter.Y - (moved.Y - bounds.Top)) > 0.01)))
                    throw new Exception("Cursor mask state or coordinates differ across Composition windows.");
            }
            SetCursorPos(original.X, original.Y);
        }
        else Console.WriteLine("PARTIAL: Multi-window cursor positioning unavailable.");
        // Explicit cursor trajectories: far from the seam the other monitor
        // must use its plain ring, while the seam activates BOTH masks. A
        // one-pixel move must still update each intersecting mask.
        if (left.Width > 320 && right.Width > 320)
        {
            var updateAt = scene.GetType().GetMethod("UpdateCursorAt")!;
            int y = area.Top + area.Height / 2;
            int farX = left.Left + 10;
            updateAt.Invoke(scene, new object[] { farX, y });
            bool Masked(int i) => (bool)windows[i]!.GetType().GetField("_maskApplied", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[i])!;
            if (!Masked(0) || Masked(1)) throw new Exception("An unaffected monitor retained a cursor mask.");
            var rightGradient = (Windows.UI.Composition.CompositionRadialGradientBrush)windows[1]!.GetType().GetField("_gradient", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[1])!;
            var rightBefore = rightGradient.EllipseCenter;
            updateAt.Invoke(scene, new object[] { farX + 1, y });
            if (Masked(1) || rightGradient.EllipseCenter != rightBefore) throw new Exception("An unaffected monitor was updated for cursor movement.");
            var idleVisual = (Windows.UI.Composition.SpriteVisual)windows[1]!.GetType().GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[1])!;
            var plainBrush = windows[1]!.GetType().GetField("_ringBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(windows[1]);
            if (!Equals(idleVisual.Brush, plainBrush)) throw new Exception("An unaffected monitor did not use the unmasked ring.");
            updateAt.Invoke(scene, new object[] { left.Right - 1, y });
            if (!Masked(0) || !Masked(1)) throw new Exception("A seam-overlapping cutout did not activate both monitors.");
            float previous = rightGradient.EllipseCenter.X;
            updateAt.Invoke(scene, new object[] { left.Right, y });
            if (!Masked(0) || !Masked(1) || rightGradient.EllipseCenter.X != previous + 1)
                throw new Exception("One-pixel cursor movement was skipped.");
            updateAt.Invoke(scene, new object[] { farX, y });
            if (!Masked(0) || Masked(1)) throw new Exception("Leaving the seam did not restore the idle monitor.");
            Console.WriteLine("PASS: Idle monitor bypasses mask, seam activates both, and one-pixel movement is preserved.");
        }
        else Console.WriteLine("SKIP: Synthetic viewport too narrow for cursor isolation probe.");
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

    private static void TestFullscreenDetector(MainWindow window, System.Windows.Forms.Screen[] screens, bool spanned)
    {
        if (screens.Length != 1 || Environment.ProcessPath == null ||
            System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("SKIP: External fullscreen detector fixture requires a one-monitor apphost.");
            return;
        }
        Config.Current.IsSpanned = spanned;
        window.UpdateMonitorSetup();
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--fullscreen-fixture")
        { UseShellExecute = false, CreateNoWindow = true }) ?? throw new Exception("Could not start fullscreen fixture.");
        var fullscreenSet = (System.Collections.Generic.HashSet<string>)typeof(MainWindow).GetField("_fullscreenMonitors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        try
        {
            for (int i = 0; i < 30 && !fullscreenSet.Contains(screens[0].DeviceName); i++)
            {
                if (process.HasExited) throw new Exception("Fullscreen fixture exited unexpectedly.");
                PumpFor(100);
            }
            if (!fullscreenSet.Contains(screens[0].DeviceName) || !window.DevCompositionActive)
                throw new Exception("Fullscreen detector did not activate Composition fullscreen layout.");
            AssertBounds(GetCompositionHwnd(window), screens[0].Bounds);
            AssertRingBounds(window, screens[0].Bounds);
            process.Kill();
            process.WaitForExit();
            for (int i = 0; i < 30 && fullscreenSet.Contains(screens[0].DeviceName); i++) PumpFor(100);
            if (fullscreenSet.Contains(screens[0].DeviceName) || !window.DevCompositionActive)
                throw new Exception("Fullscreen detector did not restore Composition working area.");
            AssertBounds(GetCompositionHwnd(window), screens[0].Bounds);
            AssertRingBounds(window, screens[0].WorkingArea);
            Console.WriteLine($"PASS: External fullscreen window triggers detector and restores working area (spanned={spanned}).");
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            Config.Current.IsSpanned = false;
            window.UpdateMonitorSetup();
        }
    }

    private static void TestTiled(MainWindow window, System.Windows.Forms.Screen screen)
    {
        window.SetDevTiling(true);
        if (!window.DevCompositionActive) throw new Exception("Tiled renderer caused fallback: " + window.DevCompositionStatus);
        object scene = GetScene(window);
        if (!(bool)scene.GetType().GetProperty("UsingTiles")!.GetValue(scene)!)
            throw new Exception("Tiled renderer failed to activate: " + window.DevCompositionStatus);
        var area = screen.WorkingArea;
        var left = new System.Drawing.Rectangle(area.Left, area.Top, area.Width / 2, area.Height);
        var right = new System.Drawing.Rectangle(left.Right, area.Top, area.Width - left.Width, area.Height);
        scene.GetType().GetMethod("Configure")!.Invoke(scene, new object[] {
            new[] { new CompositionLayout.Piece(left, left, false, true), new CompositionLayout.Piece(right, right, true, false) },
            180f, (float)Config.Current.Thickness, true, 4000d, false, true });
        var windows = GetWindows(window);
        if (windows.Count != 2) throw new Exception("Tiled scene did not create two windows.");
        var update = scene.GetType().GetMethod("UpdateCursorAt")!;
        var allTiles = new System.Collections.Generic.List<object>();
        foreach (object backend in windows)
        {
            var tiles = (System.Collections.IList)backend.GetType().GetField("_tiles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
            foreach (object tile in tiles) allTiles.Add(tile);
        }
        if (allTiles.Count < 4) throw new Exception("Expected multiple mask tiles.");
        int cursorY = area.Top + 40;
        int x = left.Left + 30;
        update.Invoke(scene, new object[] { x, cursorY });
        int visibleCount = 0, activeCount = 0, skippedCount = 0;
        foreach (object tile in allTiles)
        {
            Type t = tile.GetType();
            var tileRect = (System.Drawing.Rectangle)t.GetField("Bounds")!.GetValue(tile)!;
            var visual = (Windows.UI.Composition.SpriteVisual)t.GetField("Visual")!.GetValue(tile)!;
            if (!visual.IsVisible) { skippedCount++; continue; }
            visibleCount++;
            bool masked = (bool)t.GetField("MaskApplied")!.GetValue(tile)!;
            bool expected = CursorCoverage.Intersects(tileRect, x, cursorY, 180);
            if (masked != expected) throw new Exception("Tiled cursor intersection mismatch.");
            if (masked)
            {
                activeCount++;
                var gradient = (Windows.UI.Composition.CompositionRadialGradientBrush)t.GetField("Gradient")!.GetValue(tile)!;
                if (Math.Abs(gradient.EllipseCenter.X - (x - tileRect.Left)) > 0.01) throw new Exception("Wrong tile-local cursor coordinates.");
            }
        }
        if (activeCount == 0 || activeCount >= visibleCount) throw new Exception("Tile updates were not localized.");
        update.Invoke(scene, new object[] { left.Right, cursorY });
        foreach (object backend in windows)
        {
            var tiles = (System.Collections.IList)backend.GetType().GetField("_tiles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
            bool any = false;
            foreach (object tile in tiles)
            {
                var t = tile.GetType();
                any |= (bool)t.GetField("MaskApplied")!.GetValue(tile)! && ((Windows.UI.Composition.SpriteVisual)t.GetField("Visual")!.GetValue(tile)!).IsVisible;
            }
            if (!any) throw new Exception("Cursor at monitor boundary did not activate tiles on both monitors.");
        }
        Console.WriteLine($"PASS: Tiled cursor updates localized: visible={visibleCount}, masked={activeCount}, empty tiles hidden={skippedCount}; seam activates both.");
        // Sample actual core pixels across two independent tiled surfaces.
        if (GetCursorPos(out Point saved))
        {
            SetCursorPos(area.Left + area.Width / 2, area.Bottom - 1);
            scene.GetType().GetMethod("UpdateCursor")!.Invoke(scene, null);
            Pump(); System.Threading.Thread.Sleep(800); DwmFlush();
            int y = area.Top + (int)(20 + Config.Current.Thickness / 2);
            IntPtr dc = GetDC(IntPtr.Zero);
            uint a = GetPixel(dc, left.Left + left.Width / 2, y);
            uint b = GetPixel(dc, right.Left + right.Width / 2, y);
            ReleaseDC(IntPtr.Zero, dc);
            static bool Ring(uint p) => p != 0xffffffff && Math.Abs((int)(p & 255) - 246) < 16 && Math.Abs((int)((p >> 8) & 255) - 203) < 16;
            if (!Ring(a) || !Ring(b)) throw new Exception($"Tiled surface failed pixel probe: {a:X8}/{b:X8}.");
            SetCursorPos(saved.X, saved.Y);
            Console.WriteLine("PASS: Tiled rendering visible on both synthetic monitor surfaces.");
        }
        else Console.WriteLine("PARTIAL: Tiled pixel probe could not move cursor.");
        window.SetDevTiling(false);
        if (!window.DevCompositionActive) throw new Exception("Tiled-to-legacy switch failed.");
    }

    private static void TestCaretMasks(MainWindow window, System.Windows.Forms.Screen screen)
    {
        var setCaret = typeof(MainWindow).GetMethod("SetCaretPosition", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var area = screen.WorkingArea;
        var caret = new System.Drawing.Point(area.Left + area.Width / 2, area.Top + 30);
        var outer = (Grid)window.FindName("CaretMaskGrid");
        window.SetCompositionExperiment(false);
        setCaret.Invoke(window, new object?[] { caret });
        if (outer.OpacityMask is not RadialGradientBrush || ((Grid)window.FindName("MainGrid")).OpacityMask is not RadialGradientBrush)
            throw new Exception("WPF did not retain both caret and mouse masks.");
        setCaret.Invoke(window, new object?[] { null });
        if (outer.OpacityMask != null) throw new Exception("WPF caret mask did not clear.");
        window.SetCompositionExperiment(true);
        foreach (bool tiled in new[] { false, true })
        {
            window.SetDevTiling(tiled);
            object scene = GetScene(window);
            var backend = GetWindows(window)[0]!;
            var type = backend.GetType();
            scene.GetType().GetMethod("UpdateCursorAt")!.Invoke(scene, new object[] { caret.X, caret.Y });
            setCaret.Invoke(window, new object?[] { caret });
            if (!window.DevCompositionActive || outer.OpacityMask != null) throw new Exception($"Caret caused Composition fallback or WPF invalidation: {window.DevCompositionStatus}, WPF mask={outer.OpacityMask != null}.");
            if (tiled)
            {
                var tiles = (System.Collections.IList)type.GetField("_tiles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
                bool both = false;
                foreach (object tile in tiles)
                {
                    var t = tile.GetType();
                    var extra = t.GetField("CaretMask")!.GetValue(tile);
                    if (extra == null || !(bool)t.GetField("MaskApplied")!.GetValue(tile)!) continue;
                    var source = (Windows.UI.Composition.SpriteVisual)extra.GetType().GetField("SourceVisual")!.GetValue(extra)!;
                    if (source.Brush == (Windows.UI.Composition.CompositionBrush)t.GetField("MaskBrush")!.GetValue(tile)!) both = true;
                }
                if (!both) throw new Exception("Tiled mouse and caret masks were not combined.");
            }
            else
            {
                var extra = type.GetField("_caretMask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
                var source = (Windows.UI.Composition.SpriteVisual)extra.GetType().GetField("SourceVisual")!.GetValue(extra)!;
                if (source.Brush != (Windows.UI.Composition.CompositionBrush)type.GetField("_maskedBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!)
                    throw new Exception("Whole-monitor mouse and caret masks were not combined.");
            }
            setCaret.Invoke(window, new object?[] { null });
            if (type.GetField("_caret", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend) != null)
                throw new Exception("Composition caret did not clear.");
        }
        window.SetDevTiling(false);
        Console.WriteLine("PASS: Simultaneous mouse/caret masks and caret clearing in WPF, tiled and whole-monitor Composition.");
    }

    private static void TestCaretTracking(MainWindow window, System.Windows.Forms.Screen screen, string fixture)
    {
        if (Environment.ProcessPath == null || System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("SKIP: Caret fixture requires an apphost."); return;
        }
        var field = typeof(MainWindow).GetField("_caretPosition", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Config.Current.TextCaretCutout = true;
        window.RefreshCaretTracking();
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, fixture)
            { UseShellExecute = false, CreateNoWindow = true }) ?? throw new Exception("Could not start caret fixture.");
        try
        {
            for (int i = 0; i < 50 && field.GetValue(window) == null; i++)
            {
                if (process.HasExited) throw new Exception("Caret fixture exited unexpectedly.");
                PumpFor(100);
            }
            if (field.GetValue(window) is not System.Drawing.Point caret || !screen.WorkingArea.Contains(caret))
                throw new Exception("Focused external text caret was not detected on the expected screen.");
            if (fixture == "--wpf-caret-fixture")
            {
                var legacy = typeof(MainWindow).Assembly.GetType("WinRinglight.CaretTracker")!.GetMethod("FindLegacyCaret", BindingFlags.Static | BindingFlags.NonPublic)!;
                if (legacy.Invoke(null, new object?[] { System.Windows.Automation.AutomationElement.FocusedElement, null, null }) is not System.Drawing.Point legacyPoint || !screen.WorkingArea.Contains(legacyPoint))
                    throw new Exception("Legacy UI Automation text selection geometry was not detected for WPF.");
                Console.WriteLine("PASS: Legacy UI Automation fallback detects external WPF caret geometry.");
            }
            keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x41, 0, 0, UIntPtr.Zero);
            keybd_event(0x41, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero);
            PumpFor(700);
            if (field.GetValue(window) is not System.Drawing.Point selected || !screen.WorkingArea.Contains(selected))
                throw new Exception($"Selected text lost its caret cutout ({fixture}).");
            var selectedField = typeof(MainWindow).GetField("_textSelection", BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (selectedField.GetValue(window) is not System.Drawing.Rectangle selection || selection.Width < 20)
                throw new Exception($"Selected text geometry not obtained ({fixture}): {selectedField.GetValue(window)}.");
            if (fixture == "--wpf-caret-fixture")
            {
                keybd_event(0x09, 0, 0, UIntPtr.Zero); keybd_event(0x09, 0, 2, UIntPtr.Zero);
                for (int i = 0; i < 25 && field.GetValue(window) != null; i++) PumpFor(100);
                if (field.GetValue(window) != null) throw new Exception("Read-only focused text field produced a caret cutout.");
                keybd_event(0x10, 0, 0, UIntPtr.Zero); keybd_event(0x09, 0, 0, UIntPtr.Zero);
                keybd_event(0x09, 0, 2, UIntPtr.Zero); keybd_event(0x10, 0, 2, UIntPtr.Zero);
                for (int i = 0; i < 25 && field.GetValue(window) == null; i++) PumpFor(100);
                if (field.GetValue(window) == null) throw new Exception("Editable field was not restored after read-only focus.");
                caret = (System.Drawing.Point)field.GetValue(window)!;
                Console.WriteLine("PASS: Read-only focused text is ignored, editable focus restores the caret.");
            }
            byte moveKey = fixture == "--wpf-caret-fixture" ? (byte)0x27 : (byte)0x25;
            for (int i = 0; i < 4; i++) { keybd_event(moveKey, 0, 0, UIntPtr.Zero); keybd_event(moveKey, 0, 2, UIntPtr.Zero); }
            for (int i = 0; i < 30 && Equals(field.GetValue(window), caret); i++) PumpFor(100);
            if (field.GetValue(window) is not System.Drawing.Point moved || moved == caret)
                throw new Exception($"Text caret movement was not tracked ({fixture}).");
            process.Kill(); process.WaitForExit();
            for (int i = 0; i < 30 && field.GetValue(window) != null; i++) PumpFor(100);
            if (field.GetValue(window) != null) throw new Exception("Caret position remained after focus loss.");
            Console.WriteLine($"PASS: External focused text caret ({fixture}) detected, moved and cleared on focus loss.");
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            Config.Current.TextCaretCutout = false;
            window.RefreshCaretTracking();
        }
    }

    private static void TestCaretSwitch(MainWindow window)
    {
        if (Environment.ProcessPath == null || System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return;
        Config.Current.TextCaretCutout = true; window.RefreshCaretTracking();
        var field = typeof(MainWindow).GetField("_caretPosition", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var first = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--caret-fixture") { UseShellExecute = false })!;
        System.Diagnostics.Process? second = null;
        try
        {
            for (int i = 0; i < 50 && field.GetValue(window) == null; i++) PumpFor(100);
            if (field.GetValue(window) is not System.Drawing.Point a) throw new Exception("First app caret missing.");
            second = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--wpf-caret-fixture") { UseShellExecute = false })!;
            for (int i = 0; i < 50; i++)
            {
                PumpFor(100);
                if (field.GetValue(window) is System.Drawing.Point p && Math.Abs(p.Y - a.Y) > 100) break;
            }
            if (field.GetValue(window) is not System.Drawing.Point b || Math.Abs(b.Y - a.Y) <= 100)
                throw new Exception($"Caret did not follow the newly focused app: first={a}, current={field.GetValue(window)}.");
            // The first app was never clicked or explicitly defocused; only the
            // second window took foreground, matching the browser-switch report.
            Console.WriteLine("PASS: Caret follows a second app without clearing the first app's focused text field.");
        }
        finally
        {
            if (!first.HasExited) first.Kill();
            if (second != null) { if (!second.HasExited) second.Kill(); second.Dispose(); }
            Config.Current.TextCaretCutout = false; window.RefreshCaretTracking();
        }
    }

    private static void TestCaretWatchdog()
    {
        var type = typeof(MainWindow).Assembly.GetType("WinRinglight.CaretTracker")!;
        Action<System.Drawing.Point?> callback = _ => { };
        object tracker = Activator.CreateInstance(type, Dispatcher.CurrentDispatcher, callback)!;
        void Set(string name, object value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tracker, value);
        int Get(string name) => (int)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tracker)!;
        try
        {
            // Simulate an old app's provider stuck in an in-flight call. This
            // exercises the replacement decision, not a real hung COM server.
            Set("_enabled", true); Set("_workerCount", 1); Set("_workerVersion", 1);
            Set("_queryVersion", 1); Set("_queryGeneration", -1); Set("_queryForeground", IntPtr.Zero);
            Set("_queryStarted", System.Diagnostics.Stopwatch.GetTimestamp() - System.Diagnostics.Stopwatch.Frequency);
            type.GetMethod("Expire", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tracker, null);
            if (Get("_workerVersion") != 2) throw new Exception("Stalled old provider did not trigger a replacement worker.");
            Console.WriteLine("PASS: Stalled old-app provider triggers a bounded replacement worker (simulated).");
        }
        finally { ((IDisposable)tracker).Dispose(); }
    }

    private static void TestChromeAddressCaret(MainWindow window)
    {
        string? chrome = new[] {
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe")
        }.FirstOrDefault(System.IO.File.Exists);
        if (chrome == null) { Console.WriteLine("SKIP: Chrome not installed; address bar caret requires real-device testing."); return; }
        string profile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winringlight-chrome-" + Guid.NewGuid().ToString("N"));
        var start = new System.Diagnostics.ProcessStartInfo(chrome) { UseShellExecute = false };
        foreach (var arg in new[] { "--user-data-dir=" + profile, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--new-window", "about:blank" }) start.ArgumentList.Add(arg);
        Config.Current.TextCaretCutout = true; window.RefreshCaretTracking();
        var field = typeof(MainWindow).GetField("_caretPosition", BindingFlags.Instance | BindingFlags.NonPublic)!;
        string? firefox = new[] {
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mozilla Firefox", "firefox.exe")
        }.FirstOrDefault(System.IO.File.Exists);
        string ffProfile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winringlight-firefox-" + Guid.NewGuid().ToString("N"));
        System.Diagnostics.Process? ff = null;
        if (firefox != null)
        {
            System.IO.Directory.CreateDirectory(ffProfile);
            var ffStart = new System.Diagnostics.ProcessStartInfo(firefox) { UseShellExecute = false };
            foreach (var arg in new[] { "--no-remote", "--new-instance", "--profile", ffProfile, "--new-window", "about:blank" }) ffStart.ArgumentList.Add(arg);
            ff = System.Diagnostics.Process.Start(ffStart);
            if (ff == null) throw new Exception("Could not launch Firefox fixture.");
            for (int i = 0; i < 120 && !ff.HasExited; i++) { ff.Refresh(); if (ff.MainWindowHandle != IntPtr.Zero) break; PumpFor(100); }
            ff.Refresh();
            if (ff.HasExited || ff.MainWindowHandle == IntPtr.Zero)
            {
                Console.WriteLine("PARTIAL: Firefox was installed but could not open an interactive fixture; Firefox-to-Chrome remains untested.");
                try { if (!ff.HasExited) ff.Kill(true); } catch { }
                ff.Dispose(); ff = null;
                // Isolated CI desktop only: the Firefox launcher may have
                // exited after spawning a detached browser process.
                foreach (var child in System.Diagnostics.Process.GetProcessesByName("firefox"))
                {
                    try { if (!child.HasExited) child.Kill(true); } catch { }
                    child.Dispose();
                }
            }
            if (ff != null)
            {
            SetForegroundWindow(ff.MainWindowHandle); PumpFor(600);
            Key(0x11, true); Key(0x4c, true); Key(0x4c, false); Key(0x11, false); PumpFor(300);
            for (int i = 0; i < 8; i++) { Key(0x41, true); Key(0x41, false); PumpFor(40); }
            for (int i = 0; i < 30 && field.GetValue(window) == null; i++) PumpFor(100);
            if (field.GetValue(window) == null) throw new Exception("Firefox address caret not detected before browser switch.");
            }
        }
        else Console.WriteLine("SKIP: Firefox not installed; Firefox-to-Chrome needs real-device testing.");
        using var process = System.Diagnostics.Process.Start(start) ?? throw new Exception("Could not launch Chrome fixture.");
        try
        {
            for (int i = 0; i < 100; i++)
            {
                process.Refresh();
                if (process.HasExited) throw new Exception("Chrome fixture exited.");
                if (process.MainWindowHandle != IntPtr.Zero) break;
                PumpFor(100);
            }
            process.Refresh();
            if (process.MainWindowHandle == IntPtr.Zero) throw new Exception("Chrome window not available.");
            SetForegroundWindow(process.MainWindowHandle); PumpFor(500);
            // Ctrl+L, type a known address-bar string, then compare Home and End.
            Key(0x11, true); Key(0x4c, true); Key(0x4c, false); Key(0x11, false); PumpFor(250);
            for (int i = 0; i < 12; i++) { Key(0x41, true); Key(0x41, false); PumpFor(30); }
            Key(0x24, true); Key(0x24, false); PumpFor(700);
            if (field.GetValue(window) is not System.Drawing.Point beginning) throw new Exception("Chrome address bar start caret was not detected.");
            for (int i = 0; i < 12; i++) { Key(0x27, true); Key(0x27, false); PumpFor(30); }
            PumpFor(800);
            for (int i = 0; i < 20 && Equals(field.GetValue(window), beginning); i++) PumpFor(100);
            if (field.GetValue(window) is not System.Drawing.Point end || end.X <= beginning.X + 25)
            {
                var trackerType = typeof(MainWindow).Assembly.GetType("WinRinglight.CaretTracker")!;
                var args = new object?[] { false };
                var modern = trackerType.GetMethod("FindAutomationCaret", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
                var legacy = trackerType.GetMethod("FindLegacyCaret", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object?[] { System.Windows.Automation.AutomationElement.FocusedElement, beginning, null });
                var focus = System.Windows.Automation.AutomationElement.FocusedElement;
                string detail = "";
                if (focus.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out object val))
                    detail += $"value={( (System.Windows.Automation.ValuePattern)val).Current.Value}; ";
                if (focus.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out object txt))
                {
                    var textPattern = (System.Windows.Automation.TextPattern)txt;
                    var sels = textPattern.GetSelection();
                    foreach (var sel in sels)
                    {
                        var clone = sel.Clone();
                        int moved = clone.MoveEndpointByUnit(System.Windows.Automation.Text.TextPatternRangeEndpoint.Start, System.Windows.Automation.Text.TextUnit.Character, -1000);
                        detail += $"range={sel.CompareEndpoints(System.Windows.Automation.Text.TextPatternRangeEndpoint.Start, sel, System.Windows.Automation.Text.TextPatternRangeEndpoint.End)}, moved={moved}, rects={string.Join("/", sel.GetBoundingRectangles().Select(r => r.ToString()))}; ";
                    }
                }
                var fg = GetForegroundWindow();
                uint tid = GetWindowThreadProcessId(fg, out _);
                var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
                bool ok = GetGUIThreadInfo(tid, ref info);
                var native = new Point { X = info.Rect.Left, Y = info.Rect.Top };
                ClientToScreen(info.Caret, ref native);
                throw new Exception($"Chrome address bar caret did not move to end: start={beginning}, end={field.GetValue(window)}, modern={modern}, inactive={args[0]}, legacy={legacy}, focus={focus.Current.Name}, foreground={fg:X}, main={process.MainWindowHandle:X}, native={ok}/{native.X},{native.Y} rect={info.Rect.Left},{info.Rect.Top},{info.Rect.Right},{info.Rect.Bottom} caret={info.Caret:X} {detail}.");
            }
            Key(0x10, true);
            for (int i = 0; i < 3; i++) { Key(0x25, true); Key(0x25, false); PumpFor(50); }
            Key(0x10, false); PumpFor(700);
            if (field.GetValue(window) is not System.Drawing.Point selected || selected.X >= end.X - 5)
                throw new Exception($"Chrome selected text did not follow the active endpoint: end={end}, selection={field.GetValue(window)}.");
            Key(0x11, true); Key(0x41, true); Key(0x41, false); Key(0x11, false); PumpFor(700);
            if (field.GetValue(window) is not System.Drawing.Point) throw new Exception("Chrome address bar selection lost caret.");
            Console.WriteLine("PASS: Chrome address bar caret tracks start/end and remains visible for selected text.");
            if (ff != null) Console.WriteLine("PASS: Chrome address caret detected after switching directly from Firefox with its text field focused.");

            // Real browser DOM: reject a focused read-only control but retain a
            // contenteditable input. No text content is read by the application.
            string html = "<html><body><textarea aria-label='Read-only fixture' readonly>Read only text</textarea><div aria-label='Editable fixture' role='textbox' contenteditable='true'>Editable text</div></body></html>";
            System.Windows.Clipboard.SetText("data:text/html," + Uri.EscapeDataString(html));
            Key(0x11, true); Key(0x4c, true); Key(0x4c, false); Key(0x11, false); PumpFor(200);
            Key(0x11, true); Key(0x56, true); Key(0x56, false); Key(0x11, false);
            Key(0x0d, true); Key(0x0d, false); PumpFor(1200);
            var root = System.Windows.Automation.AutomationElement.FromHandle(process.MainWindowHandle);
            System.Windows.Automation.AutomationElement? Find(string name) => root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, name));
            System.Windows.Automation.AutomationElement? ro = null, editable = null;
            for (int i = 0; i < 40 && (ro == null || editable == null); i++)
            { ro = Find("Read-only fixture"); editable = Find("Editable fixture"); if (ro == null || editable == null) PumpFor(100); }
            if (ro == null || editable == null) throw new Exception("Chrome editable/read-only fixtures were not exposed to UI Automation.");
            ro.SetFocus();
            for (int i = 0; i < 30 && field.GetValue(window) != null; i++) PumpFor(100);
            if (field.GetValue(window) != null) throw new Exception("Chrome read-only field left a false caret cutout.");
            editable.SetFocus();
            for (int i = 0; i < 30 && field.GetValue(window) == null; i++) PumpFor(100);
            if (field.GetValue(window) == null) throw new Exception("Chrome contenteditable field did not show a caret cutout.");
            Console.WriteLine("PASS: Chrome read-only field is ignored and contenteditable field is tracked.");
        }
        finally
        {
            try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } } catch { }
            try { if (ff != null && !ff.HasExited) { ff.Kill(true); ff.WaitForExit(); } } catch { }
            ff?.Dispose();
            try { System.IO.Directory.Delete(ffProfile, true); } catch { }
            Config.Current.TextCaretCutout = false; window.RefreshCaretTracking();
            try { System.IO.Directory.Delete(profile, true); } catch { }
        }
        static void Key(byte key, bool down) => keybd_event(key, 0, down ? 0u : 2u, UIntPtr.Zero);
    }

    private static void TestRasterQuality(MainWindow window, System.Windows.Forms.Screen screen)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        if (Math.Abs(dpi.DpiScaleX - 1) > 0.001 || Math.Abs(dpi.DpiScaleY - 1) > 0.001 || screen.WorkingArea.Width < 600 || screen.WorkingArea.Height < 400)
        {
            Console.WriteLine("SKIP: Raster comparison requires a 100% DPI desktop of at least 600x400.");
            return;
        }
        window.SetCompositionExperiment(false);
        window.SetRinglightState(false);
        PumpFor(550);
        const int width = 320, height = 200;
        var bounds = new System.Drawing.Rectangle(screen.WorkingArea.Left + 60, screen.WorkingArea.Top + 60, width, height);
        var background = new Window { WindowStyle = WindowStyle.None, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize,
            Left = bounds.Left, Top = bounds.Top, Width = width, Height = height, Topmost = true, Background = Brushes.Black };
        var color = System.Windows.Media.Color.FromRgb(246, 203, 141);
        var brush = new SolidColorBrush(color);
        var canvas = new Grid { Width = width, Height = height, Background = Brushes.Black };
        canvas.Children.Add(new GlowBorder { Width = 280, Height = 160, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 20, 0, 0), BorderThickness = new Thickness(30),
            CornerRadius = new CornerRadius(44), BorderBrush = brush, Opacity = 0.55 });
        canvas.Children.Add(new Border { Width = 280, Height = 160, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 20, 0, 0), BorderThickness = new Thickness(30),
            CornerRadius = new CornerRadius(44), BorderBrush = brush });
        canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        Type backendType = typeof(MainWindow).Assembly.GetType("WinRinglight.CompositionBackend")!;
        GetCursorPos(out Point oldCursor);
        try
        {
            background.Show(); PumpFor(150);
            SetCursorPos(screen.WorkingArea.Right - 1, screen.WorkingArea.Bottom - 1);
            // Real desktop pixels: independently remove the ring at the mouse
            // and caret, then clear only the caret while the mouse remains.
            foreach (bool tiled in new[] { false, true })
            {
                object probe = Activator.CreateInstance(backendType, new object?[] { bounds, bounds, 30f, true, 4000d,
                    false, true, 25f, null, false, false, 1f, tiled })!;
                try
                {
                    var cursor = backendType.GetMethod("UpdateCursorAt")!;
                    var caret = backendType.GetMethod("UpdateCaret")!;
                    int x1 = bounds.Left + 100, x2 = bounds.Left + 220, y = bounds.Top + 35;
                    void WaitPixels() { Pump(); System.Threading.Thread.Sleep(500); DwmFlush(); }
                    uint Pixel(int x) { IntPtr dc = GetDC(IntPtr.Zero); uint p = GetPixel(dc, x, y); ReleaseDC(IntPtr.Zero, dc); return p; }
                    cursor.Invoke(probe, new object[] { bounds.Right + 100, bounds.Bottom + 100 });
                    WaitPixels(); uint initial1 = Pixel(x1), initial2 = Pixel(x2);
                    cursor.Invoke(probe, new object[] { x1, y });
                    caret.Invoke(probe, new object?[] { new System.Drawing.Point(x2, y) });
                    WaitPixels(); uint cut1 = Pixel(x1), cut2 = Pixel(x2);
                    caret.Invoke(probe, new object?[] { null });
                    WaitPixels(); uint restored1 = Pixel(x1), restored2 = Pixel(x2);
                    var selectionUpdate = backendType.GetMethod("UpdateTextCutout")!;
                    cursor.Invoke(probe, new object[] { bounds.Right + 100, bounds.Bottom + 100 });
                    selectionUpdate.Invoke(probe, new object?[] { new System.Drawing.Point(x2, y), new System.Drawing.Rectangle(bounds.Left + 80, y - 8, 160, 16) });
                    WaitPixels(); uint selectedStart = Pixel(bounds.Left + 85), selectedEnd = Pixel(bounds.Left + 235);
                    selectionUpdate.Invoke(probe, new object?[] { new System.Drawing.Point(x2, y), null });
                    WaitPixels(); uint deselectedStart = Pixel(bounds.Left + 85), caretOnly = Pixel(x2);
                    static int RgbSum(uint p) => (int)(p & 255) + (int)((p >> 8) & 255) + (int)((p >> 16) & 255);
                    if (RgbSum(initial1) < 300 || RgbSum(initial2) < 300 || RgbSum(cut1) > 40 || RgbSum(cut2) > 40 || RgbSum(restored1) > 40 || RgbSum(restored2) < 300)
                        throw new Exception($"Caret desktop pixel probe failed (tiled={tiled}): {initial1:X}/{initial2:X} -> {cut1:X}/{cut2:X} -> {restored1:X}/{restored2:X}");
                    if (RgbSum(selectedStart) > 40 || RgbSum(selectedEnd) > 40 || RgbSum(deselectedStart) < 300 || RgbSum(caretOnly) > 40)
                        throw new Exception($"Selected text desktop pixel probe failed (tiled={tiled}): {selectedStart:X}/{selectedEnd:X} -> {deselectedStart:X}/{caretOnly:X}.");
                }
                finally { ((IDisposable)probe).Dispose(); }
            }
            Console.WriteLine("PASS: Real desktop pixels show mouse, caret and full selection cutouts and restoration in both renderers.");
            double[] errors = new double[2];
            var firstPixels = new System.Collections.Generic.List<uint>();
            int changedPixels = 0;
            for (int n = 0; n < 2; n++)
            {
                object backend = Activator.CreateInstance(backendType, new object?[] { bounds, bounds, 30f, true, 4000d,
                    false, true, 2f, null, false, false, n == 0 ? 1f : 2f, false })!;
                try
                {
                    Pump(); System.Threading.Thread.Sleep(450); DwmFlush();
                    IntPtr dc = GetDC(IntPtr.Zero);
                    uint core = GetPixel(dc, bounds.Left + 160, bounds.Top + 35);
                    if (Math.Abs((int)(core & 255) - 246) > 16 || Math.Abs((int)((core >> 8) & 255) - 203) > 16)
                    {
                        ReleaseDC(IntPtr.Zero, dc);
                        Console.WriteLine($"PARTIAL: Raster comparison desktop probe unavailable (core=0x{core:X8}).");
                        return;
                    }
                    long diff = 0; int samples = 0; int sampleIndex = 0;
                    for (int y = 12; y < 110; y += 2)
                        for (int x = 12; x < 110; x += 2)
                        {
                            int i = (y * width + x) * 4;
                            uint observed = GetPixel(dc, bounds.Left + x, bounds.Top + y);
                            if (n == 0) firstPixels.Add(observed);
                            else if (firstPixels[sampleIndex] != observed) changedPixels++;
                            sampleIndex++;
                            diff += Math.Abs((int)(observed & 255) - pixels[i + 2]);
                            diff += Math.Abs((int)((observed >> 8) & 255) - pixels[i + 1]);
                            diff += Math.Abs((int)((observed >> 16) & 255) - pixels[i]);
                            samples += 3;
                        }
                    ReleaseDC(IntPtr.Zero, dc);
                    errors[n] = (double)diff / samples;
                    if (n == 0)
                    {
                        var grad = (Windows.UI.Composition.CompositionRadialGradientBrush)backendType.GetField("_gradient", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
                        var result = (Windows.UI.Composition.SpriteVisual)backendType.GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
                        var masked = (Windows.UI.Composition.CompositionBrush)backendType.GetField("_maskedBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
                        grad.EllipseCenter = new System.Numerics.Vector2(-1000, -1000);
                        result.Brush = masked;
                        Pump(); System.Threading.Thread.Sleep(500); DwmFlush();
                        IntPtr maskDc = GetDC(IntPtr.Zero);
                        int maxDelta = 0;
                        for (int y = 12; y < 110; y += 4)
                            for (int x = 12; x < 110; x += 4)
                            {
                                uint before = firstPixels[((y - 12) / 2) * 49 + ((x - 12) / 2)];
                                uint after = GetPixel(maskDc, bounds.Left + x, bounds.Top + y);
                                for (int shift = 0; shift <= 16; shift += 8)
                                    maxDelta = Math.Max(maxDelta, Math.Abs((int)((before >> shift) & 255) - (int)((after >> shift) & 255)));
                            }
                        ReleaseDC(IntPtr.Zero, maskDc);
                        if (maxDelta > 2) throw new Exception($"Unmasked ring differs visibly from fully opaque mask (max channel delta {maxDelta}).");
                        Console.WriteLine($"PASS: Inactive direct ring matches fully opaque cursor mask (max channel delta {maxDelta}).");
                    }
                }
                finally { ((IDisposable)backend).Dispose(); }
                Pump();
            }
            Console.WriteLine(errors[1] < errors[0] && changedPixels > 0
                ? $"PASS: Desktop corner raster comparison vs WPF: native error={errors[0]:F6}, 2x error={errors[1]:F6}, changed={changedPixels}."
                : $"PARTIAL: Desktop corner raster comparison vs WPF: native error={errors[0]:F6}, 2x error={errors[1]:F6}, changed={changedPixels}; improvement not established.");
        }
        finally
        {
            SetCursorPos(oldCursor.X, oldCursor.Y);
            background.Close();
            window.SetCompositionExperiment(true);
            window.SetRinglightState(true);
        }
    }

    private static void AssertRingBounds(MainWindow window, System.Drawing.Rectangle expected)
    {
        object backend = GetFirstBackend(window);
        var ring = (System.Drawing.Rectangle)backend.GetType().GetField("_ringBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(backend)!;
        if (ring != expected) throw new Exception($"Composition ring bounds {ring} differ from expected {expected}.");
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
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [StructLayout(LayoutKind.Sequential)] private struct GuiRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo { public uint Size, Flags; public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret; public GuiRect Rect; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
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
