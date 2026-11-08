using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace WinRinglight
{
    public partial class MainWindow : Window
    {
        // --- Native Win32 API Definitions ---
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(ref Win32Point pt);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref Win32Point lpPoint);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]

        private static extern bool SetProcessWorkingSetSize(IntPtr process, UIntPtr minimumWorkingSetSize, UIntPtr maximumWorkingSetSize);

        [DllImport("user32.dll")]
        public static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
        public const uint WDA_NONE = 0x00000000;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // Win10 2004+ Feature

        public enum QUERY_USER_NOTIFICATION_STATE
        {
            QUNS_NOT_PRESENT = 1,
            QUNS_BUSY = 2,
            QUNS_RUNNING_D3D_FULL_SCREEN = 3,
            QUNS_PRESENTATION_MODE = 4,
            QUNS_ACCEPTS_NOTIFICATIONS = 5,
            QUNS_QUIET_TIME = 6,
            QUNS_APP = 7
        }

        [DllImport("shell32.dll")]
        static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        private const uint GW_HWNDNEXT = 2;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
        private const int DWMWA_CLOAKED = 14;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private DispatcherTimer? _fullscreenTimer;
        private readonly System.Collections.Generic.HashSet<string> _fullscreenMonitors = new();
        public event EventHandler? DisplaysChanged;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Point { public Int32 X; public Int32 Y; }

        private static System.Threading.Mutex _mutex = null!;

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;

        private const int HOTKEY_ID = 9000;

        private DispatcherTimer? _webcamTimer;
        private DispatcherTimer? _cursorTimer;
        private bool _cursorRenderingSubscribed;
        private DispatcherTimer? _temperatureTimer;
        private readonly System.Diagnostics.Stopwatch _temperatureClock = new();
        private double _displayedTemperature;
        private double _temperatureFrom;
        private double _temperatureTarget;
        private System.Windows.Forms.NotifyIcon? _trayIcon;

        private bool _isRinglightOn = false;
        private bool _lastWebcamState = false;

        private bool _hasCursorPosition;
        private int _lastMouseX = -1;
        private int _lastMouseY = -1;

        private Win32Point _mousePt = new Win32Point();

        public MainWindow()
        {
            bool createdNew;
            _mutex = new System.Threading.Mutex(true, "WinRinglight_SingleInstance", out createdNew);
            if (!createdNew)
            {
                //System.Windows.MessageBox.Show("Das Programm läuft bereits im Hintergrund! Bitte beende es über das Symbol unten rechts in der Taskleiste, bevor du es neu startest.", "WinRinglight läuft bereits", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                System.Windows.Application.Current.Shutdown();
                return;
            }

            Config.Load();
            InitializeComponent();

            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline),
                new FrameworkPropertyMetadata { DefaultValue = 60 }
            );

            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            this.Loaded += MainWindow_Loaded;

            _webcamTimer = new DispatcherTimer();
            _webcamTimer.Interval = TimeSpan.FromSeconds(2);
            _webcamTimer.Tick += WebcamTimer_Tick;

            _fullscreenTimer = new DispatcherTimer();
            _fullscreenTimer.Interval = TimeSpan.FromMilliseconds(50);     //check for fullscreen changes
            _fullscreenTimer.Tick += FullscreenTimer_Tick;

            _cursorTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                // Target roughly 60 cursor updates per second.
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _cursorTimer.Tick += UpdateCursorCutout;

            _temperatureTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _temperatureTimer.Tick += TemperatureTimer_Tick;

            SetupTrayIcon();
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();

            var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/WinRinglight;component/icon.ico"))?.Stream;
            if (iconStream != null)
            {
                _trayIcon.Icon = new System.Drawing.Icon(iconStream);
            }
            else
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Information;
            }

            _trayIcon.Text = Config.GetText("AppName");
            _trayIcon.Visible = true;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();

            var toggleItem = new System.Windows.Forms.ToolStripMenuItem("Toggle ringlight");
            toggleItem.Click += (s, e) => { ToggleRinglight(); };
            contextMenu.Items.Add(toggleItem);

            var settingsItem = new System.Windows.Forms.ToolStripMenuItem(Config.GetText("SettingsMenu"));
            settingsItem.Click += (s, e) => {
                var settingsWin = new SettingsWindow(this);
                settingsWin.Show();
            };
            contextMenu.Items.Add(settingsItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem(Config.GetText("ExitMenu"));

            exitItem.Click += (s, e) => { System.Windows.Application.Current.Shutdown(); };
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = contextMenu;

            MainGrid.Opacity = 0.0;
        }

        protected override void OnClosed(EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
            StopCursorTracking();
            _caretTracker?.Dispose();
            StopComposition();
            _temperatureTimer?.Stop();
            _webcamTimer?.Stop();
            _fullscreenTimer?.Stop();
            _trayIcon?.Dispose();
            base.OnClosed(e);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshCaretTracking();
            MainGrid.IsHitTestVisible = false;
            this.IsHitTestVisible = false;
            //RenderOptions.SetEdgeMode(MainGrid, EdgeMode.Aliased);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);

            HwndSource? source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(HwndHook);

            UpdateMonitorSetup();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;

            RegisterHotkeyFromString(Config.Current.HotkeyText);

            _webcamTimer?.Start();

            ApplyCaptureAffinity();
            _fullscreenTimer?.Start();

            FlushMemory();
        }

        private void DisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) { _compositionBackend?.InvalidateLayout(); UpdateMonitorSetup(); DisplaysChanged?.Invoke(this, EventArgs.Empty); } }));
        }

        public void FlushMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, (UIntPtr)0xFFFFFFFF, (UIntPtr)0xFFFFFFFF);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            const int WM_DPICHANGED = 0x02E0;
            if (msg == WM_DPICHANGED)
                Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) { _compositionBackend?.InvalidateLayout(); UpdateMonitorSetup(); } }));
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleRinglight();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void UpdateHotkey(System.Windows.Input.ModifierKeys modifiers, System.Windows.Input.Key key)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);

            uint fsModifiers = 0;
            if ((modifiers & System.Windows.Input.ModifierKeys.Alt) != 0) fsModifiers |= 0x0001;
            if ((modifiers & System.Windows.Input.ModifierKeys.Control) != 0) fsModifiers |= 0x0002;
            if ((modifiers & System.Windows.Input.ModifierKeys.Shift) != 0) fsModifiers |= 0x0004;

            fsModifiers |= 0x4000;

            uint vk = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);

            bool success = RegisterHotKey(hwnd, HOTKEY_ID, fsModifiers, vk);

        }

        public void RegisterHotkeyFromString(string hotkeyString)
        {
            if (string.IsNullOrWhiteSpace(hotkeyString)) return;

            System.Windows.Input.ModifierKeys modifiers = System.Windows.Input.ModifierKeys.None;
            if (hotkeyString.Contains("Ctrl")) modifiers |= System.Windows.Input.ModifierKeys.Control;
            if (hotkeyString.Contains("Alt")) modifiers |= System.Windows.Input.ModifierKeys.Alt;
            if (hotkeyString.Contains("Shift")) modifiers |= System.Windows.Input.ModifierKeys.Shift;

            string keyString = hotkeyString.Substring(hotkeyString.LastIndexOf('+') + 1).Trim();

            if (Enum.TryParse(keyString, out System.Windows.Input.Key key))
            {
                UpdateHotkey(modifiers, key);
            }
        }

        public void ToggleRinglight()
        {
            SetRinglightState(!_isRinglightOn);
        }

        public void SetRinglightState(bool turnOn)
        {
            if (_isRinglightOn == turnOn) return;

            _isRinglightOn = turnOn;

            if (turnOn)
            {
                // Force an update even if the cursor returned to its previous position
                // while the light was off, before the fade-in begins.
                _hasCursorPosition = false;
                UpdateCursorCutout(null, EventArgs.Empty);
                RefreshCursorTracking();
            }
            else
            {
                StopCursorTracking();
                // No need to keep animating a color while the light fades out.
                if (_temperatureTimer?.IsEnabled == true)
                {
                    _temperatureTimer.Stop();
                    ApplySettingsLive(1.0, Config.Current.Thickness, Config.Current.Temperature);
                }
            }

            DoubleAnimation fadeAnimation = new DoubleAnimation
            {
                To = _isRinglightOn ? 1.0 : 0.0,
                Duration = TimeSpan.FromSeconds(0.4),
                EasingFunction = new QuadraticEase() { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(fadeAnimation, 60);

            MainGrid.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
            if (_compositionBackend != null)
            {
                try { _compositionBackend.SetVisible(turnOn); }
                catch (Exception ex) { FailComposition(ex); }
            }
        }

        private void WebcamTimer_Tick(object? sender, EventArgs e)
        {
            if (Config.Current.AutoWebcam)
            {
                bool isCameraOn = WebcamHelper.IsWebcamInUse();
                if (isCameraOn != _lastWebcamState)
                {
                    _lastWebcamState = isCameraOn;
                    SetRinglightState(isCameraOn);
                }
            }

            // Smart temperature is independent of webcam automation. Avoid
            // interrupting a checkbox transition already in progress.
            if (Config.Current.AutoTemperature && _isRinglightOn && _temperatureTimer?.IsEnabled != true)
            {
                ApplySettingsLive(1.0, Config.Current.Thickness, Config.Current.Temperature);
            }
        }

        public void TransitionTemperatureMode()
        {
            double target = Config.Current.AutoTemperature ? GetSmartTemperature() : Config.Current.Temperature;
            if (!_isRinglightOn || _activeBorders.Count == 0)
            {
                _temperatureTimer?.Stop();
                ApplySettingsLive(1.0, Config.Current.Thickness, Config.Current.Temperature);
                return;
            }

            // Start from the currently displayed color, including when the user
            // toggles again halfway through a previous transition.
            _temperatureFrom = _displayedTemperature;
            _temperatureTarget = target;
            _temperatureClock.Restart();
            _temperatureTimer?.Start();
        }

        private void TemperatureTimer_Tick(object? sender, EventArgs e)
        {
            double elapsed = _temperatureClock.Elapsed.TotalMilliseconds;
            double temperature = TemperatureTransition.At(_temperatureFrom, _temperatureTarget, elapsed);
            ApplyRingAppearance(Config.Current.Thickness, temperature);
            if (elapsed >= TemperatureTransition.DurationMilliseconds)
            {
                _temperatureTimer?.Stop();
                _temperatureClock.Stop();
            }
        }

        public void ApplyCaptureAffinity()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            uint affinity = Config.Current.HideFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
            SetWindowDisplayAffinity(hwnd, affinity);
            if (_compositionBackend != null && !_compositionBackend.SetCaptureExcluded(Config.Current.HideFromCapture))
                FailComposition(new InvalidOperationException("Capture affinity could not be applied"));
        }

        private void FullscreenTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isRinglightOn) return;

            var screens = System.Windows.Forms.Screen.AllScreens;
            var entries = new System.Collections.Generic.List<FullscreenPolicy.Entry>();
            var resolved = new System.Collections.Generic.HashSet<int>();
            IntPtr currentHwnd = GetTopWindow(IntPtr.Zero);
            uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            while (currentHwnd != IntPtr.Zero)
            {
                var name = new System.Text.StringBuilder(256);
                GetClassName(currentHwnd, name, name.Capacity);
                string cName = name.ToString();
                bool taskbar = cName == "Shell_TrayWnd" || cName == "Shell_SecondaryTrayWnd";
                GetWindowThreadProcessId(currentHwnd, out uint pid);
                if (taskbar)
                {
                    var monitor = System.Windows.Forms.Screen.FromHandle(currentHwnd);
                    int index = Array.FindIndex(screens, x => x.DeviceName == monitor.DeviceName);
                    if (index >= 0 && resolved.Add(index)) entries.Add(new FullscreenPolicy.Entry(index, true));
                }
                else if (pid != myPid && IsWindowVisible(currentHwnd) && cName != "Progman" && cName != "WorkerW" && cName != "EdgeUiInputTopWnd")
                {
                    DwmGetWindowAttribute(currentHwnd, DWMWA_CLOAKED, out int cloaked, 4);
                    int style = GetWindowLong(currentHwnd, GWL_EXSTYLE);
                    if (cloaked == 0 && (style & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT)) == 0 && GetWindowRect(currentHwnd, out RECT rect))
                    {
                        for (int i = 0; i < screens.Length; i++)
                        {
                            var b = screens[i].Bounds;
                            if (rect.Left <= b.Left + 5 && rect.Top <= b.Top + 5 && rect.Right >= b.Right - 5 && rect.Bottom >= b.Bottom - 5 && resolved.Add(i))
                                entries.Add(new FullscreenPolicy.Entry(i, false));
                        }
                    }
                }
                if (resolved.Count == screens.Length) break;
                currentHwnd = GetWindow(currentHwnd, GW_HWNDNEXT);
            }
            IntPtr foreground = GetForegroundWindow();
            var fgName = new System.Text.StringBuilder(256);
            GetClassName(foreground, fgName, fgName.Capacity);
            string fgClass = fgName.ToString();
            int? shellMonitor = null;
            if (fgClass == "Windows.UI.Core.CoreWindow" || fgClass == "StartMenuExperienceHost" ||
                fgClass == "SearchHost" || fgClass == "Shell_TrayWnd" || fgClass == "Shell_SecondaryTrayWnd")
            {
                var screen = System.Windows.Forms.Screen.FromHandle(foreground);
                shellMonitor = Array.FindIndex(screens, x => x.DeviceName == screen.DeviceName);
            }
            var indices = FullscreenPolicy.Detect(screens.Length, entries, shellMonitor);
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (int index in indices) names.Add(screens[index].DeviceName);
            if (!_fullscreenMonitors.SetEquals(names))
            {
                _fullscreenMonitors.Clear();
                _fullscreenMonitors.UnionWith(names);
                UpdateMonitorSetup();
            }
        }

        private void StopCursorTracking()
        {
            _cursorTimer?.Stop();
            if (_cursorRenderingSubscribed)
            {
                CompositionTarget.Rendering -= UpdateCursorCutout;
                _cursorRenderingSubscribed = false;
            }
        }

        public void RefreshCursorTracking()
        {
            _caretTracker?.SetRate(Config.Current.CursorMaxFps, Config.Current.CursorUnlimitedFps);
            StopCursorTracking();
            if (!_isRinglightOn || _cursorTimer == null) return;
            if (Config.Current.CursorUnlimitedFps)
            {
                // Uncapped by the app, but still limited by WPF's render cadence.
                CompositionTarget.Rendering += UpdateCursorCutout;
                _cursorRenderingSubscribed = true;
            }
            else
            {
                int fps = Math.Clamp(Config.Current.CursorMaxFps, 1, 240);
                _cursorTimer.Interval = TimeSpan.FromSeconds(1.0 / fps);
                _cursorTimer.Start();
            }
        }

        private void UpdateCursorCutout(object? sender, EventArgs e)
        {
            if (!_isRinglightOn) return;

            if (GetCursorPos(ref _mousePt))
            {
                if (_hasCursorPosition && _mousePt.X == _lastMouseX && _mousePt.Y == _lastMouseY) return;

                _hasCursorPosition = true;
                _lastMouseX = _mousePt.X;
                _lastMouseY = _mousePt.Y;

                if (_compositionBackend != null)
                {
                    // Do not invalidate the full WPF mask while Composition is active.
                    try { _compositionBackend.UpdateCursorAt(_mousePt.X, _mousePt.Y); }
                    catch (Exception ex) { FailComposition(ex); }
                    return;
                }

                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Win32Point localPt = _mousePt;
                ScreenToClient(hwnd, ref localPt);
                System.Windows.Point relMouse = new System.Windows.Point(localPt.X, localPt.Y);
                CursorCutoutMask.Center = relMouse;
                CursorCutoutMask.GradientOrigin = relMouse;
            }
        }

        private void AnimateBorderToRect(Border border, Rect targetRect, bool isAppleStyle)
        {
            double targetX = targetRect.X;
            double targetY = targetRect.Y;
            double targetW = targetRect.Width;
            double targetH = targetRect.Height;

            if (isAppleStyle)
            {
                targetX += 20;
                targetY += 20;
                targetW -= 40;
                targetH -= 40;
            }

            var duration = TimeSpan.FromSeconds(0.4); 
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

            ThicknessAnimation marginAnim = new ThicknessAnimation { To = new Thickness(targetX, targetY, 0, 0), Duration = duration, EasingFunction = ease };
            DoubleAnimation widthAnim = new DoubleAnimation { To = targetW, Duration = duration, EasingFunction = ease };
            DoubleAnimation heightAnim = new DoubleAnimation { To = targetH, Duration = duration, EasingFunction = ease };

            Timeline.SetDesiredFrameRate(marginAnim, 60);
            Timeline.SetDesiredFrameRate(widthAnim, 60);
            Timeline.SetDesiredFrameRate(heightAnim, 60);

            border.BeginAnimation(FrameworkElement.MarginProperty, marginAnim);
            border.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
            border.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
        }

        public void ApplyStyleLive(bool isAppleStyle)
        {
            if (_compositionBackend != null)
            {
                try { _compositionBackend.SetRing((float)Config.Current.Thickness, isAppleStyle, _displayedTemperature, true); }
                catch (Exception ex) { FailComposition(ex); }
            }
            foreach (var border in _activeBorders)
            {
                Rect baseRect = (Rect)border.Tag;

                AnimateBorderToRect(border, baseRect, isAppleStyle);

                if (isAppleStyle)
                {
                    double minSide = Math.Min(baseRect.Width, baseRect.Height);
                    border.CornerRadius = new CornerRadius(minSide * 0.22);
                }
                else
                {
                    border.CornerRadius = new CornerRadius(3);
                }
            }
            UpdateOutlineAppearance(Config.Current.Thickness, _displayedTemperature);
        }

        private OutlineElement? _wpfOutline;
        private string? _outlineKey;
        private DisplayOutlineLayers.Layers? _outlineLayers;
        private double _outlineThickness;

        private void UpdateOutlineAppearance(double thickness, double kelvin)
        {
            bool enabled = Config.Current.IsSpanned && Config.Current.SpannedDisplayOutline;
            foreach (var border in _activeBorders) border.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            if (!enabled)
            {
                if (_wpfOutline != null) _wpfOutline.Visibility = Visibility.Collapsed;
                _compositionBackend?.SetOutline(null);
                _outlineKey = null; _outlineLayers = null;
                return;
            }
            var screens = System.Windows.Forms.Screen.AllScreens;
            var displays = new CompositionLayout.Display[screens.Length];
            for (int i = 0; i < screens.Length; i++) displays[i] = new(screens[i].Bounds, screens[i].WorkingArea);
            var regions = DisplayOutline.WorkingRegions(displays, _fullscreenMonitors.Count > 0);
            float scale = _compositionBackend?.DpiScale ?? (float)VisualTreeHelper.GetDpi(this).DpiScaleX;
            double pixels = Math.Max(0.01, thickness * scale);
            bool rounded = Config.Current.VisualStyleIndex == 0;
            double radius = rounded && screens.Length > 0
                ? Math.Min(screens[0].Bounds.Width, screens[0].Bounds.Height) * 0.22 : 3 * scale;
            string key = $"{pixels:R}/{scale:R}/{rounded}/{radius:R}/" + string.Join(";", regions);
            bool changed = _outlineKey != key || _outlineLayers == null;
            var previous = _outlineLayers;
            bool animate = changed && previous != null && _outlineThickness == pixels;
            if (changed)
            {
                _outlineLayers = DisplayOutlineLayers.Create(regions, radius, rounded ? 20 * scale : 0, pixels);
                _outlineKey = key;
                _outlineThickness = pixels;
            }
            _compositionBackend?.SetOutlineTransition(_outlineLayers, animate);
            if (_wpfOutline == null) _wpfOutline = new OutlineElement { IsHitTestVisible = false };
            if (!MainGrid.Children.Contains(_wpfOutline)) MainGrid.Children.Add(_wpfOutline);
            _wpfOutline.Visibility = Visibility.Visible;
            if (changed)
            {
                _wpfOutline.BeginAnimation(OutlineElement.TransitionProperty, null);
                _wpfOutline.PreviousLayers = animate ? previous : null;
                _wpfOutline.Layers = _outlineLayers;
                if (animate)
                {
                    var transition = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
                        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
                    Timeline.SetDesiredFrameRate(transition, 60);
                    _wpfOutline.BeginAnimation(OutlineElement.TransitionProperty, transition);
                }
                else _wpfOutline.Transition = 1;
            }
            else _wpfOutline.Layers = _outlineLayers;
            _wpfOutline.Thickness = pixels;
            _wpfOutline.DpiScale = scale;
            _wpfOutline.DisableGlow = DevDisableGlow;
            double t = (kelvin - 2000) / 4500.0;
            _wpfOutline.Color = System.Windows.Media.Color.FromRgb((byte)(255 - 20 * t), (byte)(170 + 75 * t), (byte)(50 + 205 * t));
            var origin = PointFromScreen(new System.Windows.Point(0, 0));
            var unitX = PointFromScreen(new System.Windows.Point(1, 0));
            var unitY = PointFromScreen(new System.Windows.Point(0, 1));
            _wpfOutline.DeviceToLocal = new Matrix(unitX.X - origin.X, unitX.Y - origin.Y,
                unitY.X - origin.X, unitY.Y - origin.Y, origin.X, origin.Y);
            _wpfOutline.InvalidateVisual();
        }

        private System.Collections.Generic.List<System.Windows.Controls.Border> _activeBorders = new System.Collections.Generic.List<System.Windows.Controls.Border>();

        // Session-only experiments. They intentionally never modify saved settings.
        public bool DevCacheElements { get; private set; }
        public bool DevCacheGroup { get; private set; }
        public bool DevDisableGlow { get; private set; }
        public bool DevDirectRaster { get; private set; }
        public bool DevTiling { get; private set; } = true;
        private CompositionScene? _compositionBackend;
        private CaretTracker? _caretTracker;
        private System.Drawing.Point? _caretPosition;
        private System.Drawing.Rectangle? _textSelection;
        private RadialGradientBrush? _caretWpfMask;

        public void RefreshCaretTracking()
        {
            _caretTracker ??= new CaretTracker(Dispatcher, SetTextCutout);
            _caretTracker.SetRate(Config.Current.CursorMaxFps, Config.Current.CursorUnlimitedFps);
            _caretTracker.SetEnabled(Config.Current.TextCaretCutout);
        }

        private void SetCaretPosition(System.Drawing.Point? position) => SetTextCutout(position, null);

        private void SetTextCutout(System.Drawing.Point? position, System.Drawing.Rectangle? selection)
        {
            _caretPosition = position;
            _textSelection = selection;
            if (_compositionBackend != null)
            {
                try { _compositionBackend.UpdateTextCutout(position, selection); }
                catch (Exception ex) { FailComposition(ex); }
            }
            UpdateWpfCaretMask();
        }

        private void UpdateWpfCaretMask()
        {
            if (_compositionBackend != null || _caretPosition == null)
            {
                CaretMaskGrid.OpacityMask = null;
                return;
            }
            _caretWpfMask ??= new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                SpreadMethod = GradientSpreadMethod.Pad,
                GradientStops = new GradientStopCollection {
                    new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 0.2),
                    new GradientStop(Colors.White, 1) }
            };
            var p = _caretPosition.Value;
            var local = CaretMaskGrid.PointFromScreen(new System.Windows.Point(p.X, p.Y));
            var selected = _textSelection.GetValueOrDefault();
            var tl = _textSelection.HasValue ? CaretMaskGrid.PointFromScreen(new System.Windows.Point(selected.Left, selected.Top)) : local;
            var br = _textSelection.HasValue ? CaretMaskGrid.PointFromScreen(new System.Windows.Point(selected.Right, selected.Bottom)) : local;
            var ellipse = TextCutoutEllipse.Create(local.X, local.Y, CursorCutoutMask.RadiusX, tl.X, tl.Y, br.X, br.Y, _textSelection.HasValue);
            _caretWpfMask.Center = new System.Windows.Point(ellipse.X, ellipse.Y);
            _caretWpfMask.GradientOrigin = _caretWpfMask.Center;
            _caretWpfMask.RadiusX = ellipse.RadiusX;
            _caretWpfMask.RadiusY = ellipse.RadiusY;
            _caretWpfMask.GradientStops[1].Offset = ellipse.InnerStop;
            CaretMaskGrid.OpacityMask = _caretWpfMask;
        }
        public bool DevCompositionActive => _compositionBackend != null;
        public string DevCompositionStatus { get; private set; } = "WPF";
        public event EventHandler? DevCompositionChanged;

        public void SetCompositionExperiment(bool enabled)
        {
            if (!enabled) { StopComposition(); return; }
            StopComposition();
            try
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
                {
                    StopComposition("WPF fallback: Composition requires Windows 10 version 1903 or later.");
                    return;
                }
                _compositionBackend = new CompositionScene();
                _compositionBackend.RasterFailed += message =>
                {
                    DevCompositionStatus = "Composition active; Direct2D fallback: " + message;
                    DevCompositionChanged?.Invoke(this, EventArgs.Empty);
                };
                _compositionBackend.SetDirectRaster(DevDirectRaster);
                _compositionBackend.SetTiling(DevTiling);
                ConfigureCompositionScene();
                _compositionBackend.UpdateTextCutout(_caretPosition, _textSelection);
                UpdateWpfCaretMask();
                MainGrid.Visibility = Visibility.Hidden;
                DevCompositionChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) { FailComposition(ex); }
        }

        public void SetDevTiling(bool enabled)
        {
            DevTiling = enabled;
            if (_compositionBackend == null) return;
            try { _compositionBackend.SetTiling(enabled); ConfigureCompositionScene(); }
            catch (Exception ex) { FailComposition(ex); }
        }

        public void SetDevDirectRaster(bool enabled)
        {
            DevDirectRaster = enabled;
            if (_compositionBackend == null) return;
            try
            {
                _compositionBackend.SetDirectRaster(enabled);
                ConfigureCompositionScene();
            }
            catch (Exception ex) { FailComposition(ex); }
        }

        private void ConfigureCompositionScene()
        {
            if (_compositionBackend == null) return;
            var screens = System.Windows.Forms.Screen.AllScreens;
            var displays = new CompositionLayout.Display[screens.Length];
            for (int i = 0; i < screens.Length; i++)
                displays[i] = new CompositionLayout.Display(screens[i].Bounds, screens[i].WorkingArea);
            var fullscreen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < screens.Length; i++) if (_fullscreenMonitors.Contains(screens[i].DeviceName)) fullscreen.Add(i);
            var layout = CompositionLayout.Build(displays, Config.Current.IsSpanned, Config.Current.SelectedMonitors, fullscreen);
            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            float radius = (float)((primary?.Bounds.Height ?? 0) * Config.CursorCutoutRadiusPercent);
            _compositionBackend.ConfigureOutline(layout, radius, (float)Config.Current.Thickness,
                Config.Current.VisualStyleIndex == 0, _displayedTemperature, Config.Current.HideFromCapture, _isRinglightOn,
                Config.Current.IsSpanned && Config.Current.SpannedDisplayOutline);
            UpdateOutlineAppearance(Config.Current.Thickness, _displayedTemperature);
            string status = _compositionBackend.UsingTiles ? "Composition active (tiled experiment)" :
                _compositionBackend.TileFallbackReason != null ? "Composition active; tiled renderer failed: " + _compositionBackend.TileFallbackReason : "Composition active (whole-monitor masks)";
            if (_compositionBackend.RasterFallbackReason != null)
                status += "; Direct2D fallback: " + _compositionBackend.RasterFallbackReason;
            else if (DevDirectRaster) status += _compositionBackend.UsingSoftwareRaster ? "; Direct2D 2x (WARP software)" : "; Direct2D 2x";
            if (DevCompositionStatus != status) { DevCompositionStatus = status; DevCompositionChanged?.Invoke(this, EventArgs.Empty); }
        }

        private void FailComposition(Exception ex)
        {
            System.Diagnostics.Trace.WriteLine("Composition fallback: " + ex);
            StopComposition("WPF fallback: " + ex.Message);
        }

        private void StopComposition(string status = "WPF")
        {
            var backend = _compositionBackend;
            _compositionBackend = null;
            try { backend?.Dispose(); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Composition cleanup: " + ex); }
            if (MainGrid != null) MainGrid.Visibility = Visibility.Visible;
            UpdateWpfCaretMask();
            if (backend != null)
            {
                // Re-synchronise WPF after leaving Composition: its mask and
                // appearance were intentionally not updated while hidden.
                ApplyRingAppearance(Config.Current.Thickness, _displayedTemperature);
                _hasCursorPosition = false;
                if (_isRinglightOn) UpdateCursorCutout(null, EventArgs.Empty);
            }
            DevCompositionStatus = status;
            DevCompositionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetDevExperiments(bool cacheElements, bool cacheGroup, bool disableGlow)
        {
            DevCacheElements = cacheElements;
            DevCacheGroup = cacheGroup;
            DevDisableGlow = disableGlow;
            ApplyDevExperiments();
        }

        private void ApplyDevExperiments()
        {
            MainGrid.CacheMode = DevCacheGroup ? new BitmapCache() : null;
            if (_wpfOutline != null) { _wpfOutline.DisableGlow = DevDisableGlow; _wpfOutline.InvalidateVisual(); }
            foreach (var border in _activeBorders)
            {
                border.CacheMode = DevCacheElements ? new BitmapCache() : null;
                if (border is GlowBorder)
                    border.Opacity = DevDisableGlow ? 0.0 : 0.55;
            }
        }


        public void UpdateMonitorSetup()
        {
            if (!this.IsLoaded) return;
            // WICHTIG: Die Liste wird ganz oben deklariert, damit sie überall gültig ist!
            System.Collections.Generic.List<Rect> targetRects = new System.Collections.Generic.List<Rect>();

            if (Config.Current.IsSpanned)
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                var displays = new CompositionLayout.Display[screens.Length];
                for (int i = 0; i < screens.Length; i++)
                    displays[i] = new CompositionLayout.Display(screens[i].Bounds, screens[i].WorkingArea);
                var span = CompositionLayout.SpannedBounds(displays, _fullscreenMonitors.Count > 0);
                if (span.Width > 0 && span.Height > 0)
                    targetRects.Add(new Rect(span.Left - SystemParameters.VirtualScreenLeft,
                        span.Top - SystemParameters.VirtualScreenTop, span.Width, span.Height));
            }
            else
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                foreach (int index in Config.Current.SelectedMonitors)
                {
                    if (index >= 0 && index < screens.Length)
                    {
                        var s = screens[index];
                        var rect = _fullscreenMonitors.Contains(s.DeviceName) ? s.Bounds : s.WorkingArea;

                        double relX = rect.X - SystemParameters.VirtualScreenLeft;
                        double relY = rect.Y - SystemParameters.VirtualScreenTop;
                        targetRects.Add(new Rect(relX, relY, rect.Width, rect.Height));
                    }
                }
            }

            // A display hotplug can change the desktop bounds without changing
            // the number of selected ringlights.
            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            if (_activeBorders.Count != targetRects.Count * 2)
            {
                _activeBorders.Clear();
                MainGrid.Children.Clear();

                this.Left = SystemParameters.VirtualScreenLeft;
                this.Top = SystemParameters.VirtualScreenTop;
                this.Width = SystemParameters.VirtualScreenWidth;
                this.Height = SystemParameters.VirtualScreenHeight;

                foreach (var r in targetRects)
                {
                    AddRinglight(r.X, r.Y, r.Width, r.Height);
                }

                ApplySettingsLive(1.0, Config.Current.Thickness, Config.Current.Temperature);
                ApplyStyleLive(Config.Current.VisualStyleIndex == 0);
                ApplyDevExperiments();
            }
            else
            {
                bool isApple = Config.Current.VisualStyleIndex == 0;

                for (int i = 0; i < targetRects.Count; i++)
                {
                    var rect = targetRects[i];
                    var glowBorder = _activeBorders[i * 2];
                    var coreBorder = _activeBorders[i * 2 + 1];

                    glowBorder.Tag = rect;
                    coreBorder.Tag = rect;

                    AnimateBorderToRect(glowBorder, rect, isApple);
                    AnimateBorderToRect(coreBorder, rect, isApple);
                }
            }
            if (_compositionBackend != null)
            {
                try { ConfigureCompositionScene(); }
                catch (Exception ex) { FailComposition(ex); }
            }
            else UpdateOutlineAppearance(Config.Current.Thickness, _displayedTemperature);
        }

        public double GetSmartTemperature()
        {
            double h = DateTime.Now.TimeOfDay.TotalHours;

            if (h >= 6 && h < 12) return MapTemp(h, 6, 12, 2500, 6500); 
            if (h >= 12 && h < 18) return MapTemp(h, 12, 18, 6500, 3000); 
            if (h >= 18 && h < 22) return MapTemp(h, 18, 22, 3000, 2000);

            return 2000;
        }

        private double MapTemp(double val, double inMin, double inMax, double outMin, double outMax)
        {
            return (val - inMin) * (outMax - outMin) / (inMax - inMin) + outMin;
        }

        private void AddRinglight(double x, double y, double w, double h)
        {
            System.Windows.Controls.Border glowBorder = new GlowBorder
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(x, y, 0, 0),
                Width = w,
                Height = h,
                Opacity = 0.55
            };
            glowBorder.Tag = new Rect(x, y, w, h);
            _activeBorders.Add(glowBorder);
            MainGrid.Children.Add(glowBorder);

            System.Windows.Controls.Border coreBorder = new System.Windows.Controls.Border
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(x, y, 0, 0),
                Width = w,
                Height = h
            };

            RenderOptions.SetEdgeMode(coreBorder, EdgeMode.Unspecified);

            coreBorder.Tag = new Rect(x, y, w, h);
            _activeBorders.Add(coreBorder);
            MainGrid.Children.Add(coreBorder);
        }

        public void ApplySettingsLive(double brightness, double thickness, double kelvin)
        {
            if (Config.Current.AutoTemperature)
            {
                kelvin = GetSmartTemperature();
            }

            if (_temperatureTimer?.IsEnabled == true)
            {
                // Thickness or monitor changes should not interrupt a transition;
                // a new manual slider value should take effect immediately.
                if (!Config.Current.AutoTemperature && Math.Abs(kelvin - _temperatureTarget) > 0.01)
                    _temperatureTimer.Stop();
                else
                    kelvin = _displayedTemperature;
            }
            ApplyRingAppearance(thickness, kelvin);

            double baseH = SystemParameters.PrimaryScreenHeight;
            CursorCutoutMask.RadiusX = baseH * Config.CursorCutoutRadiusPercent;
            CursorCutoutMask.RadiusY = baseH * Config.CursorCutoutRadiusPercent;
            StopMiddle.Offset = Math.Clamp(1.0 - Config.CursorBlurRadiusPercent, 0.01, 0.99);
            UpdateWpfCaretMask();
        }

        private void ApplyRingAppearance(double thickness, double kelvin)
        {
            _displayedTemperature = kelvin;
            if (_compositionBackend != null)
            {
                try
                {
                    _compositionBackend.SetRing((float)thickness, Config.Current.VisualStyleIndex == 0, kelvin);
                    UpdateOutlineAppearance(thickness, kelvin);
                    return;
                }
                catch (Exception ex) { FailComposition(ex); }
            }
            UpdateOutlineAppearance(thickness, kelvin);
            foreach (var border in _activeBorders)
            {
                border.BorderThickness = new Thickness(thickness);

                if (Config.Current.VisualStyleIndex != 0)
                {
                    //border.CornerRadius = new CornerRadius(Math.Max(1, thickness));
                    border.CornerRadius = new CornerRadius(3);
                }

                double t = (kelvin - 2000) / 4500.0;
                byte r = (byte)(255 - (20 * t));
                byte g = (byte)(170 + (75 * t));
                byte b_col = (byte)(50 + (205 * t));

                System.Windows.Media.Color lightColor = System.Windows.Media.Color.FromRgb(r, g, b_col);
                border.BorderBrush = new SolidColorBrush(lightColor);

                if (border.Effect is System.Windows.Media.Effects.BlurEffect blur)
                {
                    blur.Radius = Math.Min(40, thickness * 0.4);
                }
            }

        }
    }
}