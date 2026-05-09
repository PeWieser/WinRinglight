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

        private Win32Point _mousePt = new Win32Point();

        public MainWindow()
        {
            bool createdNew;
            _mutex = new System.Threading.Mutex(true, "WinRinglight_SingleInstance", out createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show("Das Programm läuft bereits im Hintergrund! Bitte beende es über das Symbol unten rechts in der Taskleiste, bevor du es neu startest.", "WinRinglight läuft bereits", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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

            CompositionTarget.Rendering += OnRendering;

            _webcamTimer?.Start();

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

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!_isRinglightOn) return;

            if (GetCursorPos(ref _mousePt))
            {
                if (_mousePt.X == _lastMouseX && _mousePt.Y == _lastMouseY) return;

                if ((DateTime.Now - _lastMaskUpdate).TotalMilliseconds < 16) return;    //change at most every ~16ms (60fps) to avoid excessive CPU usage
                _lastMaskUpdate = DateTime.Now;

                _lastMouseX = _mousePt.X;
                _lastMouseY = _mousePt.Y;

                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Win32Point localPt = _mousePt;
                ScreenToClient(hwnd, ref localPt);

                System.Windows.Point relMouse = new System.Windows.Point(localPt.X, localPt.Y);

                CursorCutoutMask.Center = relMouse;
                CursorCutoutMask.GradientOrigin = relMouse;
            }
        }

        public void ApplyStyleLive(bool isAppleStyle)
        {
            foreach (var border in _activeBorders)
            {
                Rect baseRect = (Rect)border.Tag;

                if (isAppleStyle)
                {
                    double minSide = Math.Min(baseRect.Width, baseRect.Height);
                    border.CornerRadius = new CornerRadius(minSide * 0.22);
                    border.Margin = new Thickness(baseRect.X + 20, baseRect.Y + 20, 0, 0);
                    border.Width = baseRect.Width - 40;
                    border.Height = baseRect.Height - 40;
                }
                else
                {
                    border.Margin = new Thickness(baseRect.X, baseRect.Y, 0, 0);
                    border.CornerRadius = new CornerRadius(3);
                    border.Width = baseRect.Width;
                    border.Height = baseRect.Height;
                }
            }
        }

        private System.Collections.Generic.List<System.Windows.Controls.Border> _activeBorders = new System.Collections.Generic.List<System.Windows.Controls.Border>();

        public void UpdateMonitorSetup()
        {
            if (!this.IsLoaded) return;

            _activeBorders.Clear();
            MainGrid.Children.Clear();

            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            if (Config.Current.IsSpanned)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                {
                    var wa = screen.WorkingArea;
                    if (wa.Left < minX) minX = wa.Left;
                    if (wa.Top < minY) minY = wa.Top;
                    if (wa.Right > maxX) maxX = wa.Right;
                    if (wa.Bottom > maxY) maxY = wa.Bottom;
                }

                double spanW = maxX - minX;
                double spanH = maxY - minY;

                double relX = minX - SystemParameters.VirtualScreenLeft;
                double relY = minY - SystemParameters.VirtualScreenTop;

                AddRinglight(relX, relY, spanW, spanH);
            }
            else
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                foreach (int index in Config.Current.SelectedMonitors)
                {
                    if (index < screens.Length)
                    {
                        var s = screens[index].WorkingArea;
                        double relX = s.X - SystemParameters.VirtualScreenLeft;
                        double relY = s.Y - SystemParameters.VirtualScreenTop;
                        AddRinglight(relX, relY, s.Width, s.Height);
                    }
                }
            }

            ApplySettingsLive(1.0, Config.Current.Thickness, Config.Current.Temperature);
            ApplyStyleLive(Config.Current.VisualStyleIndex == 0);
        }

        public double GetSmartTemperature()
        {
            double h = DateTime.Now.TimeOfDay.TotalHours;

            if (h >= 6 && h < 12) return MapTemp(h, 6, 12, 2500, 6500); // Sonnenaufgang bis Mittag
            if (h >= 12 && h < 18) return MapTemp(h, 12, 18, 6500, 3000); // Mittag bis Abend
            if (h >= 18 && h < 22) return MapTemp(h, 18, 22, 3000, 2000); // Abend bis Nacht

            return 2000;
        }

        private double MapTemp(double val, double inMin, double inMax, double outMin, double outMax)
        {
            return (val - inMin) * (outMax - outMin) / (inMax - inMin) + outMin;
        }

        private void AddRinglight(double x, double y, double w, double h)
        {
            System.Windows.Controls.Border glowBorder = new System.Windows.Controls.Border
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(x, y, 0, 0),
                Width = w,
                Height = h,
                Opacity = 0.55,

                CacheMode = new BitmapCache { EnableClearType = false, SnapsToDevicePixels = false, RenderAtScale = 0.25 },

                Effect = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = 15,
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
                }
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

            double baseH = SystemParameters.PrimaryScreenHeight;
            CursorCutoutMask.RadiusX = baseH * Config.CursorCutoutRadiusPercent;
            CursorCutoutMask.RadiusY = baseH * Config.CursorCutoutRadiusPercent;
            StopMiddle.Offset = Math.Clamp(1.0 - Config.CursorBlurRadiusPercent, 0.01, 0.99);
        }
    }
}