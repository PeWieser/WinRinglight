using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;

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

        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Point { public Int32 X; public Int32 Y; }

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;

        private const int HOTKEY_ID = 9000;

        private DispatcherTimer _renderTimer;
        private DispatcherTimer _webcamTimer;
        private System.Windows.Forms.NotifyIcon? _trayIcon;

        private bool _isRinglightOn = true;
        private bool _lastWebcamState = false;

        public MainWindow()
        {
            InitializeComponent();

            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            this.Loaded += MainWindow_Loaded;

            _renderTimer = new DispatcherTimer(DispatcherPriority.Render);
            _renderTimer.Interval = TimeSpan.FromMilliseconds(10);
            _renderTimer.Tick += RenderTimer_Tick;

            _webcamTimer = new DispatcherTimer();
            _webcamTimer.Interval = TimeSpan.FromSeconds(2);
            _webcamTimer.Tick += WebcamTimer_Tick;

            SetupTrayIcon();
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();

            // EXPLICIT: System.Windows.Application
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

            var toggleItem = new System.Windows.Forms.ToolStripMenuItem("Toggle Light (On/Off)");
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
            // EXPLICIT: System.Windows.Application
            exitItem.Click += (s, e) => { System.Windows.Application.Current.Shutdown(); };
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = contextMenu;
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
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);

            HwndSource? source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(HwndHook);

            _renderTimer.Start();
            _webcamTimer.Start();
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

            uint vk = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
            RegisterHotKey(hwnd, HOTKEY_ID, fsModifiers, vk);
        }

        public void ToggleRinglight()
        {
            SetRinglightState(!_isRinglightOn);
        }

        public void SetRinglightState(bool turnOn)
        {
            if (_isRinglightOn == turnOn) return;

            _isRinglightOn = turnOn;

            DoubleAnimation fadeAnimation = new DoubleAnimation
            {
                To = _isRinglightOn ? 1.0 : 0.0,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new QuadraticEase() { EasingMode = EasingMode.EaseInOut }
            };

            MainGrid.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
        }

        private void WebcamTimer_Tick(object? sender, EventArgs e)
        {
            if (!Config.AutoWebcamEnabled) return;

            bool isCameraOn = WebcamHelper.IsWebcamInUse();

            if (isCameraOn != _lastWebcamState)
            {
                _lastWebcamState = isCameraOn;
                SetRinglightState(isCameraOn);
            }
        }

        private void RenderTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isRinglightOn) return;

            Win32Point mousePosition = new Win32Point();
            GetCursorPos(ref mousePosition);

            // EXPLICIT: System.Windows.Point
            System.Windows.Point relativeMouse = this.PointFromScreen(new System.Windows.Point(mousePosition.X, mousePosition.Y));

            CursorCutoutMask.Center = relativeMouse;
            CursorCutoutMask.GradientOrigin = relativeMouse;
        }

        public void ApplySettingsLive(double brightness, double thickness, double kelvin)
        {
            RinglightBorder.Opacity = brightness;
            RinglightBorder.BorderThickness = new Thickness(thickness);

            double t = (kelvin - 2000) / 4500.0;
            byte r = (byte)(255 - (20 * t));
            byte g = (byte)(170 + (75 * t));
            byte b = (byte)(50 + (205 * t));

            // EXPLICIT: System.Windows.Media.Color
            RinglightBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        }

        public void ApplyStyleLive(bool isAppleStyle)
        {
            if (isAppleStyle)
            {
                RinglightBorder.CornerRadius = new CornerRadius(120);
                RinglightBorder.Margin = new Thickness(20);
            }
            else
            {
                RinglightBorder.CornerRadius = new CornerRadius(10);
                RinglightBorder.Margin = new Thickness(0);
            }
        }

        // Moves the ringlight to a specific screen or spans it across all
        public void UpdateMonitorPosition(int monitorIndex)
        {
            if (monitorIndex == 0)
            {
                // Option: All Monitors
                this.Left = SystemParameters.VirtualScreenLeft;
                this.Top = SystemParameters.VirtualScreenTop;
                this.Width = SystemParameters.VirtualScreenWidth;
                this.Height = SystemParameters.VirtualScreenHeight;
            }
            else
            {
                // Option: Specific Monitor (Index is 1-based because 0 is "All")
                var screen = System.Windows.Forms.Screen.AllScreens[monitorIndex - 1];
                var workingArea = screen.Bounds;

                // Move window to the specific monitor's coordinates
                this.Left = workingArea.Left;
                this.Top = workingArea.Top;
                this.Width = workingArea.Width;
                this.Height = workingArea.Height;
            }

            // Refresh visuals
            this.UpdateLayout();
        }
    }
}