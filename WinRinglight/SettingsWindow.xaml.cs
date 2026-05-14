using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace WinRinglight
{
    public partial class SettingsWindow : Window
    {
        public string AppVersion => Config.Current.Version;

        private MainWindow _mainWindow;
        private const string REGISTRY_APP_NAME = "!00_WinRinglightApp";
        private const string TASK_NAME = "WinRinglight_Autostart";

        private System.Windows.Threading.DispatcherTimer _updateTimer;
        private DateTime _lastRender = DateTime.MinValue;

        public SettingsWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            this.DataContext = this;
            _mainWindow = mainWindow;

            _updateTimer = new System.Windows.Threading.DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromMilliseconds(40);
            _updateTimer.Tick += (s, e) =>
            {
                _updateTimer.Stop();

                Config.Save();
            };

            // Load texts from config
            TabAppearance.Header = Config.GetText("TabAppearance");
            TabSystem.Header = Config.GetText("TabSystem");
            LblThickness.Text = Config.GetText("ChangeRinglightSize");
            LblTemperature.Text = Config.GetText("ColorTemperature");
            LblTheme.Text = Config.GetText("VisualTheme");
            LblWarm.Text = Config.GetText("Warm");
            LblCold.Text = Config.GetText("Cold");
            LblAutoTemp.Text = Config.GetText("AutoTemp");
            LblAutostart.Text = Config.GetText("StartWithWindows");
            LblWebcam.Text = Config.GetText("StartWithWebcam");
            LblHideCapture.Text = Config.GetText("HideCapture");
            ChkHideCapture.IsChecked = Config.Current.HideFromCapture;
            LblHotkey.Text = Config.GetText("HotkeyToggle");
            LblSupport.Text = Config.GetText("SupportProject");
            TxtHotkey.Text = Config.GetText("PressHotkey");

            SliderThickness.Value = Config.Current.Thickness;
            SliderTemperature.Value = Config.Current.Temperature;
            ComboStyle.SelectedIndex = Config.Current.VisualStyleIndex;
            ChkWebcam.IsChecked = Config.Current.AutoWebcam;
            TxtHotkey.Text = Config.Current.HotkeyText;

            SliderTemperature.Minimum = Config.MinTemperatureKelvin;
            SliderTemperature.Maximum = Config.MaxTemperatureKelvin;

            double screenH = SystemParameters.PrimaryScreenHeight;
            SliderThickness.Minimum = screenH * Config.MinRinglightWidthPercent;
            SliderThickness.Maximum = screenH * Config.MaxRinglightWidthPercent;

            SliderTemperature.IsEnabled = !Config.Current.AutoTemperature;
            SliderThickness.Value = Config.Current.Thickness;
            SliderTemperature.Value = Config.Current.Temperature;

            LoadMonitors();
            

            this.Title = Config.GetText("AppName") + " - " + Config.GetText("SettingsMenu");

            CheckAutostartStatus();
            ChkWebcam.IsChecked = Config.Current.AutoWebcam;
            ChkAutoTemp.IsChecked = Config.Current.AutoTemperature;
        }

        // --- APPEARANCE TAB LOGIC ---

        // Helper class for UI Binding
        public class MonitorItem
        {
            public int Index { get; set; }
            public int Number => Index + 1;
            public string Info { get; set; } = "";
            public bool IsSelected { get; set; }
        }

        private void LoadMonitors()
        {
            var monitorList = new System.Collections.Generic.List<MonitorItem>();
            var screens = System.Windows.Forms.Screen.AllScreens;

            for (int i = 0; i < screens.Length; i++)
            {
                monitorList.Add(new MonitorItem
                {
                    Index = i,
                    Info = $"Monitor {i + 1}: {screens[i].Bounds.Width}x{screens[i].Bounds.Height}",
                    IsSelected = Config.Current.SelectedMonitors.Contains(i)
                });
            }
            MonitorItemsControl.ItemsSource = monitorList;
            ChkSpanAll.IsChecked = Config.Current.IsSpanned;
        }

        private void MonitorBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;

            var checkbox = sender as System.Windows.Controls.CheckBox;
            var item = checkbox?.DataContext as MonitorItem;
            if (item == null) return;

            if (checkbox.IsChecked == true)
            {
                if (!Config.Current.SelectedMonitors.Contains(item.Index))
                    Config.Current.SelectedMonitors.Add(item.Index);
            }
            else
            {
                Config.Current.SelectedMonitors.Remove(item.Index);
            }

            _mainWindow.UpdateMonitorSetup();
            Config.Save();
        }

        private void ChkSpanAll_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            Config.Current.IsSpanned = ChkSpanAll.IsChecked == true;
            _mainWindow.UpdateMonitorSetup();
            Config.Save();
        }

        private void ChkAutoTemp_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;

            Config.Current.AutoTemperature = ChkAutoTemp.IsChecked == true;

            SliderTemperature.IsEnabled = !Config.Current.AutoTemperature;

            _mainWindow.ApplySettingsLive(1.0, Config.Current.Thickness, Config.Current.Temperature);
            Config.Save();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mainWindow == null || !this.IsLoaded) return;
            Config.Current.Thickness = SliderThickness.Value;
            Config.Current.Temperature = SliderTemperature.Value;

            if ((DateTime.Now - _lastRender).TotalMilliseconds > 16)
            {
                SendLiveUpdate();
                _lastRender = DateTime.Now;
            }
            _updateTimer.Stop();
            _updateTimer.Start();
        }

        private bool _isSnapping = false;

        private void SliderTemperature_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mainWindow == null || !this.IsLoaded || _isSnapping) return;

            double currentValue = SliderTemperature.Value;
            if (currentValue > 3850 && currentValue < 4150 && currentValue != 4000)
            {
                _isSnapping = true;
                SliderTemperature.Value = 4000;
                _isSnapping = false;
            }
            Config.Current.Temperature = SliderTemperature.Value;

            if ((DateTime.Now - _lastRender).TotalMilliseconds > 16)
            {
                SendLiveUpdate();
                _lastRender = DateTime.Now;
            }
            _updateTimer.Stop();
            _updateTimer.Start();
        }

        private void SendLiveUpdate()
        {
            _mainWindow.ApplySettingsLive(1.0, SliderThickness.Value, SliderTemperature.Value);
        }

        private void ComboStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mainWindow == null || !this.IsLoaded) return;
            Config.Current.VisualStyleIndex = ComboStyle.SelectedIndex;
            _mainWindow.ApplyStyleLive(ComboStyle.SelectedIndex == 0);
            Config.Save();
        }

        // --- SYSTEM TAB LOGIC ---

        private string _currentHotkeyText = "";

        private void TxtHotkey_GotFocus(object sender, RoutedEventArgs e)
        {
            _currentHotkeyText = TxtHotkey.Text;

            TxtHotkey.Text = Config.GetText("WaitingForHotkey");
            TxtHotkey.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 170, 50)); // Make it warm orange while waiting
        }

        private void TxtHotkey_LostFocus(object sender, RoutedEventArgs e)
        {

            if (TxtHotkey.Text == Config.GetText("WaitingForHotkey"))
            {
                TxtHotkey.Text = _currentHotkeyText;
            }

            TxtHotkey.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 178, 255)); // #66B2FF
        }

        private void TxtHotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;

            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);

            if (key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            bool hasCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool hasAlt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

            if (!hasCtrl && !hasAlt)
            {
                return;
            }

            string shortcutText = "";
            if (hasCtrl) shortcutText += "Ctrl + ";
            if (hasAlt) shortcutText += "Alt + ";

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) shortcutText += "Shift + ";

            shortcutText += key.ToString();

            TxtHotkey.Text = shortcutText;
            _currentHotkeyText = shortcutText;

            Config.Current.HotkeyText = shortcutText;
            Config.Save();

            Keyboard.ClearFocus();

            _mainWindow.UpdateHotkey(Keyboard.Modifiers, key);
        }

        private void CheckAutostartStatus()
        {
            // Nullable RegistryKey to fix CS8600
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false))
            {
                if (key != null)
                {
                    object? val = key.GetValue(REGISTRY_APP_NAME);
                    ChkAutostart.IsChecked = val != null;
                }
            }
        }

        private void ChkAutostart_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;

            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            bool enable = ChkAutostart.IsChecked == true;

            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                            key.SetValue(REGISTRY_APP_NAME, $"\"{exePath}\"");
                        else
                            key.DeleteValue(REGISTRY_APP_NAME, false);
                    }
                }

                if (enable)
                {
                    string args = $"/create /tn \"{TASK_NAME}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f";
                    //RunSchtasks(args);
                }
                else
                {
                    string args = $"/delete /tn \"{TASK_NAME}\" /f";
                    //RunSchtasks(args);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Fehler beim Autostart. Bitte starte das Programm als Administrator.\n\nDetails: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            Config.Save();
        }

        /*private void RunSchtasks(string arguments)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = Process.Start(processInfo))
            {
                process?.WaitForExit();
            }
        }*/

        private void ChkWebcam_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            Config.Current.AutoWebcam = ChkWebcam.IsChecked == true;
            Config.Save();
        }

        private void ChkHideCapture_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            Config.Current.HideFromCapture = ChkHideCapture.IsChecked == true;
            Config.Save();
            _mainWindow.ApplyCaptureAffinity(); // Ruft das Update im Hauptfenster auf
        }

        private void LinkGithub_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void LinkSupport_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(Config.Current.SupportUrl) { UseShellExecute = true });
        }

        protected override void OnClosed(EventArgs e)
        {
            Config.Save();

            base.OnClosed(e);
        }
    }
}