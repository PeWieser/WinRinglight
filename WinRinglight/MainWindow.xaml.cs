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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
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

        // Ersetzt CompositionTarget.Rendering. Das Event feuerte JEDEN Frame,
        // auch im Stillstand, und hielt den Render-Takt dauerhaft wach.
        // Der Timer laeuft nur, wenn das Ringlicht an ist.
        private DispatcherTimer? _cursorTimer;
        private bool _cursorTimerRunning;
        private bool _isFullscreenActive = false;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Point { public Int32 X; public Int32 Y; }

        private static System.Threading.Mutex _mutex = null!;

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;

        private const int HOTKEY_ID = 9000;

        private DispatcherTimer? _webcamTimer;
        private System.Windows.Forms.NotifyIcon? _trayIcon;

        private bool _isRinglightOn = false;
        private bool _lastWebcamState = false;

        private int _lastMouseX = -1;
        private int _lastMouseY = -1;
        private DateTime _lastMaskUpdate = DateTime.MinValue;

        // Drosselt, wie oft das Loch um die Maus tatsaechlich neu gezeichnet wird.
        private readonly CursorThrottle _throttle = new CursorThrottle();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private long _lastWalkMs;
        private bool _fullscreenTimerRunning;

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

            _cursorTimer = new DispatcherTimer(DispatcherPriority.Render);
            _cursorTimer.Interval = TimeSpan.FromMilliseconds(33);         // ~30 Hz reicht fuer ein Loch
            _cursorTimer.Tick += (_, _) => UpdateCursorCutout();

            SetupTrayIcon();
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();

            var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico"))?.Stream;
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

            _trayIcon?.Dispose();
            base.OnClosed(e);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            MainGrid.IsHitTestVisible = false;
            this.IsHitTestVisible = false;
            //RenderOptions.SetEdgeMode(MainGrid, EdgeMode.Aliased);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);

            HwndSource? source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(HwndHook);

            UpdateMonitorSetup();

            RegisterHotkeyFromString(Config.Current.HotkeyText);

            _webcamTimer?.Start();

            ApplyCaptureAffinity();
            UpdateFullscreenTimerState();

            FlushMemory();
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

            // Beim Einschalten muss das Loch sofort an der richtigen Stelle sitzen,
            // nicht erst beim naechsten Mauszucken.
            _throttle.Reset();

            UpdateCursorTimerState();

            // Beim Einschalten sofort zeichnen - sonst sitzt das Loch fuer die
            // ersten 33 ms noch an der alten Position (sichtbarer Sprung).
            if (_isRinglightOn)
            {
                UpdateCursorCutout();
            }

            // Den Vollbild-Timer nur laufen lassen, wenn er etwas bewirken kann:
            // aus + keine Webcam-Automatik = niemand kann das Ringlicht einschalten,
            // ausser dem Hotkey (der kommt als Windows-Nachricht, nicht per Timer).
            UpdateFullscreenTimerState();

            DoubleAnimation fadeAnimation = new DoubleAnimation
            {
                To = _isRinglightOn ? 1.0 : 0.0,
                Duration = TimeSpan.FromSeconds(0.4),
                EasingFunction = new QuadraticEase() { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(fadeAnimation, 60);

            MainGrid.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
        }

        private void WebcamTimer_Tick(object? sender, EventArgs e)
        {
            if (!Config.Current.AutoWebcam) return;

            bool isCameraOn = WebcamHelper.IsWebcamInUse();

            if (isCameraOn != _lastWebcamState)
            {
                _lastWebcamState = isCameraOn;
                SetRinglightState(isCameraOn);
            }
            if (Config.Current.AutoTemperature && _isRinglightOn)
            {
                ApplySettingsLive(1.0, Config.Current.Thickness, Config.Current.Temperature);
            }
        }

        public void ApplyCaptureAffinity()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            uint affinity = Config.Current.HideFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
            SetWindowDisplayAffinity(hwnd, affinity);
        }

        /// <summary>
        /// Startet oder stoppt den Vollbild-Timer je nach Zustand.
        /// Ohne Ringlicht und ohne Webcam-Automatik hat der Timer keine Aufgabe.
        /// </summary>
        /// <summary>
        /// Startet oder stoppt den Cursor-Timer. Aus = kein Timer = kein
        /// Aufwachen des UI-Threads.
        /// </summary>
        private void UpdateCursorTimerState()
        {
            if (_cursorTimer == null) return;

            if (_isRinglightOn && !_cursorTimerRunning)
            {
                _cursorTimer.Start();
                _cursorTimerRunning = true;
            }
            else if (!_isRinglightOn && _cursorTimerRunning)
            {
                _cursorTimer.Stop();
                _cursorTimerRunning = false;
            }
        }

        private void UpdateFullscreenTimerState()
        {
            if (_fullscreenTimer == null) return;

            bool shouldRun = IdlePolicy.ShouldRunTimer(_isRinglightOn, Config.Current.AutoWebcam);
            if (shouldRun && !_fullscreenTimerRunning)
            {
                _fullscreenTimer.Interval = TimeSpan.FromMilliseconds(IdlePolicy.IntervalFor(_isRinglightOn));
                _fullscreenTimer.Start();
                _fullscreenTimerRunning = true;
            }
            else if (!shouldRun && _fullscreenTimerRunning)
            {
                _fullscreenTimer.Stop();
                _fullscreenTimerRunning = false;
            }
            else if (shouldRun)
            {
                _fullscreenTimer.Interval = TimeSpan.FromMilliseconds(IdlePolicy.IntervalFor(_isRinglightOn));
            }
        }

        private void FullscreenTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isRinglightOn) return;

            // Der teure Teil ist der Z-Order-Walk (DwmGetWindowAttribute pro
            // Fenster, also prozessuebergreifend). Bei 50-ms-Ticks lief er
            // 20x/s. Er wird jetzt hoechstens 1x/s gemacht; dazwischen ist der
            // Tick ein reiner Vergleich und kostet nichts.
            bool doWalk = IdlePolicy.ShouldWalk(_clock.ElapsedMilliseconds, _lastWalkMs);
            if (doWalk) _lastWalkMs = _clock.ElapsedMilliseconds;

            // Zustand vom letzten Walk uebernehmen; nur ein echter Walk
            // darf ihn ueberschreiben (sonst wuerde jeder uebersprungene
            // Tick das Ringlicht faelschlich auf "kein Vollbild" setzen).
            bool isFullscreenNow = _isFullscreenActive;

            if (doWalk)
            {
            isFullscreenNow = false;
            IntPtr currentHwnd = GetTopWindow(IntPtr.Zero);

            // Eigene Prozess-ID
            uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

            // --- STUFE 1: DIE Z-ORDER SCHLEIFE ---
            while (currentHwnd != IntPtr.Zero)
            {
                System.Text.StringBuilder className = new System.Text.StringBuilder(256);
                GetClassName(currentHwnd, className, className.Capacity);
                string cName = className.ToString();

                // Wenn wir die Taskleiste (Haupt oder Nebenmonitor) VOR einem Vollbild-Spiel finden:
                // -> Die Taskleiste ist sichtbar! Abbruch.
                if (cName == "Shell_TrayWnd" || cName == "Shell_SecondaryTrayWnd")
                {
                    isFullscreenNow = false;
                    break;
                }

                // Uns selbst ignorieren
                GetWindowThreadProcessId(currentHwnd, out uint windowPid);
                if (windowPid == myPid)
                {
                    currentHwnd = GetWindow(currentHwnd, GW_HWNDNEXT);
                    continue;
                }

                if (IsWindowVisible(currentHwnd))
                {
                    DwmGetWindowAttribute(currentHwnd, DWMWA_CLOAKED, out int cloaked, 4);

                    if (cloaked == 0)
                    {
                        int exStyle = GetWindowLong(currentHwnd, GWL_EXSTYLE);
                        bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
                        bool isTransparent = (exStyle & WS_EX_TRANSPARENT) != 0;

                        if (!isToolWindow && !isTransparent)
                        {
                            // Desktop-Elemente ignorieren
                            if (cName != "Progman" && cName != "WorkerW" && cName != "EdgeUiInputTopWnd")
                            {
                                GetWindowRect(currentHwnd, out RECT appRect);
                                var screen = System.Windows.Forms.Screen.FromHandle(currentHwnd);

                                int w = appRect.Right - appRect.Left;
                                int h = appRect.Bottom - appRect.Top;

                                // Ist es ein ECHTES Vollbild-Fenster (Monitor Bounds)?
                                if (w >= screen.Bounds.Width - 5 && h >= screen.Bounds.Height - 5)
                                {
                                    // Wir haben ein Vollbild gefunden, das die Taskleiste verdeckt!
                                    isFullscreenNow = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                currentHwnd = GetWindow(currentHwnd, GW_HWNDNEXT);
            }

            }   // Ende: nur wenn doWalk

            // --- STUFE 2: DER STARTMENÜ-OVERRIDE (Deine Beobachtung!) ---
            // Wir prüfen das Fenster, in dem der Cursor gerade blinkt.
            IntPtr fgHwnd = GetForegroundWindow();
            System.Text.StringBuilder fgClass = new System.Text.StringBuilder(256);
            GetClassName(fgHwnd, fgClass, fgClass.Capacity);
            string fgName = fgClass.ToString();

            // Wenn der Nutzer Win oder Win+S gedrückt hat, ist eines dieser Overlays aktiv:
            if (fgName == "Windows.UI.Core.CoreWindow" || // Windows 10 Startmenü / Suche
                fgName == "StartMenuExperienceHost" ||    // Windows 11 Startmenü
                fgName == "SearchHost" ||                 // Windows 11 Suche
                fgName == "Shell_TrayWnd")                // Taskleiste wurde angeklickt
            {
                // Egal was das Spiel im Hintergrund macht: Taskleiste freimachen!
                isFullscreenNow = false;
            }

            // Wenn sich der Status geändert hat -> Animieren!
            if (isFullscreenNow != _isFullscreenActive)
            {
                _isFullscreenActive = isFullscreenNow;
                UpdateMonitorSetup();
            }
        }


        /// <summary>
        /// Holt die Cursorposition und aktualisiert das Loch - aber nur, wenn
        /// es sich lohnt. Wird vom Cursor-Timer aufgerufen, nicht mehr pro Frame.
        /// </summary>
        private void UpdateCursorCutout()
        {
            if (!_isRinglightOn) return;

            if (!GetCursorPos(ref _mousePt)) return;

            // Billige Vorpruefung: gar keine Bewegung -> gar keine Arbeit.
            if (_mousePt.X == _lastMouseX && _mousePt.Y == _lastMouseY) return;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Win32Point localPt = _mousePt;
            ScreenToClient(hwnd, ref localPt);

            // GetCursorPos/ScreenToClient liefern PHYSISICHE Pixel.
            // WPF-Brushes rechnen in DIP, deshalb umrechnen.
            System.Windows.Point cursorPx = new System.Windows.Point(localPt.X, localPt.Y);
            System.Windows.Point relMouse = ScreenLayout.ToWindowDip(
                cursorPx, _layout.WindowPixels, _layout.Scale);

            // Erst hier entscheiden: Zeitabstand + tote Zone.
            _throttle.MinIntervalMs = Config.CursorUpdateIntervalMs;
            _throttle.DeadZoneDip = Config.CursorDeadZoneDip;

            if (!_throttle.ShouldUpdate(relMouse.X, relMouse.Y, _clock.ElapsedMilliseconds))
            {
                return;
            }

            _lastMouseX = _mousePt.X;
            _lastMouseY = _mousePt.Y;

            CursorCutoutMask.Center = relMouse;
            CursorCutoutMask.GradientOrigin = relMouse;
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

        /// <summary>
        /// Animiert den Glow genauso wie den Kern, damit beide synchron bleiben.
        /// Die vier Verlaufsflaechen selbst bleiben dabei unveraendert - nur die
        /// Position und Groesse des Canvas wandern.
        /// </summary>
        private void AnimateGlowToRect(Canvas glow, Rect targetRect, bool isAppleStyle)
        {
            double targetX = targetRect.X;
            double targetY = targetRect.Y;
            double targetW = targetRect.Width;
            double targetH = targetRect.Height;

            if (isAppleStyle)
            {
                // Gleicher Innenabstand wie beim Kern (MainWindow: targetX/Y += 20).
                targetX += 20;
                targetY += 20;
                targetW -= 40;
                targetH -= 40;
            }

            var duration = TimeSpan.FromSeconds(0.4);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

            var marginAnim = new ThicknessAnimation
            {
                To = new Thickness(targetX, targetY, 0, 0),
                Duration = duration,
                EasingFunction = ease,
            };
            var widthAnim = new DoubleAnimation { To = targetW, Duration = duration, EasingFunction = ease };
            var heightAnim = new DoubleAnimation { To = targetH, Duration = duration, EasingFunction = ease };

            Timeline.SetDesiredFrameRate(marginAnim, 60);
            Timeline.SetDesiredFrameRate(widthAnim, 60);
            Timeline.SetDesiredFrameRate(heightAnim, 60);

            glow.BeginAnimation(FrameworkElement.MarginProperty, marginAnim);
            glow.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
            glow.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
        }

        public void ApplyStyleLive(bool isAppleStyle)
        {
            foreach (var glow in _glowPanels)
            {
                AnimateGlowToRect(glow, (Rect)glow.Tag, isAppleStyle);
            }

            foreach (var border in _coreBorders)
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
        }

        // Der Kern ist eine Border (dicker Rahmen). Der Glow besteht aus vier
        // Verlaufsflaechen und ist deshalb ein Canvas - ein BorderBrush kann nur
        // eine Richtung. Beide Listen laufen parallel.
        private readonly System.Collections.Generic.List<System.Windows.Controls.Border> _coreBorders
            = new System.Collections.Generic.List<System.Windows.Controls.Border>();

        private readonly System.Collections.Generic.List<System.Windows.Controls.Canvas> _glowPanels
            = new System.Collections.Generic.List<System.Windows.Controls.Canvas>();

        // Fuer welchen Zustand wurde der Glow zuletzt gebaut?
        private readonly System.Collections.Generic.List<GlowState?> _glowStates
            = new System.Collections.Generic.List<GlowState?>();

        /// <summary>
        /// Alle Monitore als reine Daten (physische Pixel), damit die Layout-Rechnung
        /// ohne UI und ohne WinForms-Abhaengigkeit getestet werden kann.
        /// </summary>
        private static System.Collections.Generic.List<MonitorInfo> GetMonitorInfos()
        {
            var list = new System.Collections.Generic.List<MonitorInfo>();
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                System.Drawing.Rectangle b = screen.Bounds;
                System.Drawing.Rectangle w = screen.WorkingArea;
                list.Add(new MonitorInfo(
                    new Rect(b.X, b.Y, b.Width, b.Height),
                    new Rect(w.X, w.Y, w.Width, w.Height)));
            }
            return list;
        }

        /// <summary>DPI-Faktor des aktuellen Fensters (1.0 = 100 %).</summary>
        private double GetDpiScale()
        {
            try
            {
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    return source.CompositionTarget.TransformToDevice.M11;
                }
            }
            catch { }
            return 1.0;
        }

        private WindowLayout _layout = new WindowLayout(new Rect(0, 0, 0, 0),
            System.Array.Empty<Rect>(), 1.0, true);

        public void UpdateMonitorSetup()
        {
            if (!this.IsLoaded) return;

            var monitors = GetMonitorInfos();

            WindowLayout layout;
            if (Config.ShrinkWindowToUsedArea)
            {
                layout = ScreenLayout.Compute(
                    monitors,
                    Config.Current.SelectedMonitors,
                    Config.Current.IsSpanned,
                    _isFullscreenActive,
                    GetDpiScale());
            }
            else
            {
                // Alter Pfad: Fenster = gesamter virtueller Desktop.
                layout = ScreenLayout.Compute(
                    monitors,
                    Config.Current.SelectedMonitors,
                    Config.Current.IsSpanned,
                    _isFullscreenActive,
                    GetDpiScale());

                // Absichtlich auf den vollen Desktop aufblasen.
                layout = new WindowLayout(
                    new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                             SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight),
                    layout.Rings, layout.Scale, layout.IsFallback);
            }

            if (layout.IsFallback)
            {
                // Keine gueltige Monitorauswahl: nichts anfassen.
                return;
            }

            _layout = layout;

            // Fenster exakt auf die genutzte Flaeche setzen (physische Pixel,
            // deshalb ueber die HwndSource, nicht ueber Left/Top/Width/Height in DIP).
            ApplyWindowRect(layout.WindowPixels);

            if (_coreBorders.Count != layout.Rings.Count)
            {
                _coreBorders.Clear();
                _glowPanels.Clear();
                _glowStates.Clear();
                MainGrid.Children.Clear();

                foreach (var r in layout.Rings)
                {
                    AddRinglight(r.X, r.Y, r.Width, r.Height);
                }

                ApplySettingsLive(1.0, Config.Current.Thickness, Config.Current.Temperature);
                ApplyStyleLive(Config.Current.VisualStyleIndex == 0);
            }
            else
            {
                bool isApple = Config.Current.VisualStyleIndex == 0;

                for (int i = 0; i < layout.Rings.Count; i++)
                {
                    var rect = layout.Rings[i];

                    var glow = _glowPanels[i];
                    var core = _coreBorders[i];

                    glow.Tag = rect;
                    core.Tag = rect;

                    // WICHTIG fuer die Fluessigkeit: Glow und Kern muessen
                    // gemeinsam wandern. Wuerde der Kern animieren und der Glow
                    // springen, sieht man einen Versatz.
                    AnimateBorderToRect(core, rect, isApple);
                    AnimateGlowToRect(glow, rect, isApple);
                }
            }
        }

        /// <summary>
        /// Setzt das Fensterrechteck in physischen Pixeln. WPF rechnet Left/Top/Width/Height
        /// in DIP, deshalb bei Skalierung != 100 % unbrauchbar fuer exakte Monitorgrenzen.
        /// </summary>
        private void ApplyWindowRect(Rect pixels)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                SetWindowPos(hwnd, IntPtr.Zero,
                    (int)System.Math.Round(pixels.X),
                    (int)System.Math.Round(pixels.Y),
                    (int)System.Math.Round(pixels.Width),
                    (int)System.Math.Round(pixels.Height),
                    SWP_NOZORDER | SWP_NOACTIVATE);
            }
            catch { }
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
            // Der weiche Rand ist ein Farbverlauf, kein BlurEffect.
            // Ein BlurEffect wirkt auf die Bounds (= ganzer Monitor) und kostete
            // im Messlauf 16x so viel wie alles andere zusammen.
            //
            // Ein BorderBrush hat nur eine Richtung. Fuer die vier Kanten
            // braucht es vier Flaechen, deshalb ein Canvas mit vier Rechtecken.
            var glow = new Canvas
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Width = w,
                Height = h,
                Margin = new Thickness(x, y, 0, 0),
                Opacity = 0.55,
                IsHitTestVisible = false,
            };
            glow.Tag = new Rect(x, y, w, h);
            _glowPanels.Add(glow);
            MainGrid.Children.Add(glow);

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
            _coreBorders.Add(coreBorder);
            MainGrid.Children.Add(coreBorder);
        }

        /// <summary>
        /// Baut die vier Verlaufsflaechen fuer den weichen Rand neu auf.
        /// Wird nur bei Aenderungen von Groesse, Dicke oder Farbe aufgerufen -
        /// NICHT bei Mausbewegungen.
        /// </summary>
        private void RebuildGlow(Canvas glow, Rect rect, double thickness, System.Windows.Media.Color color)
        {
            glow.Children.Clear();

            // Der Glow liegt AUSSERHALB des Kerns und laeuft nach aussen aus.
            // Vorher kam er vom BlurEffect, der ueber die Kernkante hinaus
            // unscharf wurde - deshalb beginnt der Verlauf an der Kernkante.
            double band = RingGlow.WidthFor(thickness);
            double edge = thickness;          // innere Kante = Kernkante
            double outer = thickness + band;  // aussen, wo der Saum auf null geht

            if (outer <= 0 || rect.Width <= 0 || rect.Height <= 0) return;

            // Laenge des Verlaufs in relativen Einheiten ist immer 1; die
            // Stuetzstellen verteilen sich auf die Bandbreite.
            // Oben: von innen (deckend) nach aussen (transparent)
            AddGlowStrip(glow, new Rect(edge, 0, System.Math.Max(0, rect.Width - 2 * edge), outer - edge), color,
                new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));

            // Unten
            AddGlowStrip(glow, new Rect(edge, rect.Height - outer, System.Math.Max(0, rect.Width - 2 * edge), outer - edge), color,
                new System.Windows.Point(0, 1), new System.Windows.Point(0, 0));

            // Links
            AddGlowStrip(glow, new Rect(0, edge, outer - edge, System.Math.Max(0, rect.Height - 2 * edge)), color,
                new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));

            // Rechts
            AddGlowStrip(glow, new Rect(rect.Width - outer, edge, outer - edge, System.Math.Max(0, rect.Height - 2 * edge)), color,
                new System.Windows.Point(1, 0), new System.Windows.Point(0, 0));
        }

        private static void AddGlowStrip(Canvas parent, Rect area, System.Windows.Media.Color color,
            System.Windows.Point from, System.Windows.Point to)
        {
            if (area.Width <= 0.5 || area.Height <= 0.5) return;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = area.Width,
                Height = area.Height,
                Fill = RingGlow.Create(color, 1.0, from, to),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(rect, area.X);
            Canvas.SetTop(rect, area.Y);
            parent.Children.Add(rect);
        }

        /// <summary>
        /// Kelvin -> Farbe. Ausgelagert, damit Einstellungen und Live-Update
        /// dieselbe Rechnung benutzen.
        /// </summary>
        internal static System.Windows.Media.Color KelvinToColor(double kelvin)
        {
            double t = (kelvin - 2000) / 4500.0;
            byte r = (byte)System.Math.Max(0, System.Math.Min(255, 255 - (20 * t)));
            byte g = (byte)System.Math.Max(0, System.Math.Min(255, 170 + (75 * t)));
            byte b = (byte)System.Math.Max(0, System.Math.Min(255, 50 + (205 * t)));
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }

        public void ApplySettingsLive(double brightness, double thickness, double kelvin)
        {
            if (Config.Current.AutoTemperature)
            {
                kelvin = GetSmartTemperature();
            }

            System.Windows.Media.Color lightColor = KelvinToColor(kelvin);
            var frozen = new SolidColorBrush(lightColor);
            frozen.Freeze();

            foreach (var border in _coreBorders)
            {
                border.BorderThickness = new Thickness(thickness);

                if (Config.Current.VisualStyleIndex != 0)
                {
                    border.CornerRadius = new CornerRadius(3);
                }

                border.BorderBrush = frozen;
            }

            // Der Glow braucht bei jeder Aenderung von Farbe oder Dicke
            // neue Verlaeufe - aber nur dann. Nicht pro Frame und nicht pro
            // Mausbewegung, sonst entsteht genau das Ruckeln, das vermieden
            // werden soll.
            for (int i = 0; i < _glowPanels.Count && i < _coreBorders.Count; i++)
            {
                var rect = (Rect)_coreBorders[i].Tag;
                var state = new GlowState(rect.Width, rect.Height, thickness, kelvin);
                var last = _glowStates.Count > i ? _glowStates[i] : null;

                if (GlowRebuildPolicy.NeedsRebuild(last, state))
                {
                    RebuildGlow(_glowPanels[i], rect, thickness, lightColor);
                    while (_glowStates.Count <= i) _glowStates.Add(null);
                    _glowStates[i] = state;
                }
            }

            double baseH = SystemParameters.PrimaryScreenHeight;
            CursorCutoutMask.RadiusX = baseH * Config.CursorCutoutRadiusPercent;
            CursorCutoutMask.RadiusY = baseH * Config.CursorCutoutRadiusPercent;
            StopMiddle.Offset = Math.Clamp(1.0 - Config.CursorBlurRadiusPercent, 0.01, 0.99);
        }
    }
}