using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinRinglight
{
    public partial class MainWindow : Window
    {
        // Native Win32 API Definitions
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(ref Win32Point pt);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Point { public Int32 X; public Int32 Y; }

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;

        private DispatcherTimer _renderTimer;

        // Nullable tray icon to fix the CS8618 warning
        private System.Windows.Forms.NotifyIcon? _trayIcon;

        public MainWindow()
        {
            InitializeComponent();

            // Set dynamic size based on virtual screen (Multi-Monitor support)
            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            this.Loaded += MainWindow_Loaded;

            // Initialize high-performance render timer
            _renderTimer = new DispatcherTimer(DispatcherPriority.Render);
            _renderTimer.Interval = TimeSpan.FromMilliseconds(10);
            _renderTimer.Tick += RenderTimer_Tick;

            SetupTrayIcon();
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();
            _trayIcon.Icon = System.Drawing.SystemIcons.Information;
            _trayIcon.Text = Config.GetText("AppName"); // <--- Config call
            _trayIcon.Visible = true;

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();

            // <--- Config call for "Settings"
            var settingsItem = new System.Windows.Forms.ToolStripMenuItem(Config.GetText("SettingsMenu"));
            settingsItem.Click += (s, e) => {
                var settingsWin = new SettingsWindow(this);
                settingsWin.Show();
            };
            contextMenu.Items.Add(settingsItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            // <--- Config call for "Exit"
            var exitItem = new System.Windows.Forms.ToolStripMenuItem(Config.GetText("ExitMenu"));
            exitItem.Click += (s, e) => { System.Windows.Application.Current.Shutdown(); };
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = contextMenu;
        }

        protected override void OnClosed(EventArgs e)
        {
            // Dispose icon safely using the null-conditional operator
            _trayIcon?.Dispose();
            base.OnClosed(e);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Apply click-through and transparency logic
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);

            _renderTimer.Start();
        }

        private void RenderTimer_Tick(object? sender, EventArgs e)
        {
            Win32Point mousePosition = new Win32Point();
            GetCursorPos(ref mousePosition);

            // Explicitly use WPF Point to avoid ambiguity with System.Drawing.Point
            System.Windows.Point relativeMouse = this.PointFromScreen(new System.Windows.Point(mousePosition.X, mousePosition.Y));

            CursorCutoutMask.Center = relativeMouse;
            CursorCutoutMask.GradientOrigin = relativeMouse;
        }

        public void ApplySettingsLive(double brightness, double thickness, double kelvin)
        {
            // 1. Set Opacity (Brightness)
            RinglightBorder.Opacity = brightness;

            // 2. Set Thickness (Size)
            RinglightBorder.BorderThickness = new Thickness(thickness);

            // 3. Simple Color Temperature Simulation
            // We map 2000K (Orange/Warm) to 6500K (White/Cold)
            byte r = 255;
            byte g = (byte)(255 - ((6500 - kelvin) / 4500.0) * 50);  // Less green when warm
            byte b = (byte)(255 - ((6500 - kelvin) / 4500.0) * 150); // Less blue when warm

            // Use System.Windows.Media explicitly to fix CS0104 ambiguity
            RinglightBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        }

        // Called dynamically when dropdown is changed
        public void ApplyStyleLive(bool isAppleStyle)
        {
            if (isAppleStyle)
            {
                // Apple Squircle look
                RinglightBorder.CornerRadius = new CornerRadius(120);
                RinglightBorder.Margin = new Thickness(20);
            }
            else
            {
                // Windows minimalist look
                RinglightBorder.CornerRadius = new CornerRadius(10);
                RinglightBorder.Margin = new Thickness(0); // Sticks exactly to the screen edges
            }
        }
    }
}