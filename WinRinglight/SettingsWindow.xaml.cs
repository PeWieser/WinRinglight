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
        private MainWindow _mainWindow;
        private const string REGISTRY_APP_NAME = "WinRinglightApp";

        public SettingsWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            // Load texts from config
            TabAppearance.Header = Config.GetText("TabAppearance");
            TabSystem.Header = Config.GetText("TabSystem");
            LblThickness.Text = Config.GetText("ChangeRinglightSize");
            LblTemperature.Text = Config.GetText("ColorTemperature");
            LblTheme.Text = Config.GetText("VisualTheme");
            LblWarm.Text = Config.GetText("Warm");
            LblCold.Text = Config.GetText("Cold");
            LblAutostart.Text = Config.GetText("StartWithWindows");
            LblWebcam.Text = Config.GetText("StartWithWebcam");
            LblHotkey.Text = Config.GetText("HotkeyToggle");
            LblSupport.Text = Config.GetText("SupportProject");
            TxtHotkey.Text = Config.GetText("PressHotkey");
            LblMonitor.Text = Config.GetText("SelectMonitor");

            LoadMonitors();

            this.Title = Config.GetText("AppName") + " - " + Config.GetText("SettingsMenu");
            ComboStyle.SelectedIndex = 0;

            CheckAutostartStatus();

            // Set the webcam checkbox to match our config
            ChkWebcam.IsChecked = Config.AutoWebcamEnabled;
        }

        // --- APPEARANCE TAB LOGIC ---

        private void LoadMonitors()
        {
            ComboMonitor.Items.Clear();

            // Add the "All" option first
            ComboMonitor.Items.Add(Config.GetText("AllMonitors"));

            // Get all connected screens
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                ComboMonitor.Items.Add($"Monitor {i + 1} ({screens[i].Bounds.Width}x{screens[i].Bounds.Height})");
            }

            ComboMonitor.SelectedIndex = 0; // Default: All
        }

        private void ComboMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mainWindow == null || !this.IsLoaded) return;

            // Index 0 is "All", Index 1+ is specific monitor
            int selectedIndex = ComboMonitor.SelectedIndex;
            _mainWindow.UpdateMonitorPosition(selectedIndex);
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mainWindow == null || !this.IsLoaded) return;
            SendLiveUpdate();
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
            SendLiveUpdate();
        }

        private void SendLiveUpdate()
        {
            _mainWindow.ApplySettingsLive(1.0, SliderThickness.Value, SliderTemperature.Value);
        }

        private void ComboStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mainWindow == null || !this.IsLoaded) return;
            _mainWindow.ApplyStyleLive(ComboStyle.SelectedIndex == 0);
        }

        // --- SYSTEM TAB LOGIC ---

        private string _currentHotkeyText = "";

        private void TxtHotkey_GotFocus(object sender, RoutedEventArgs e)
        {
            // Save what was in there before
            _currentHotkeyText = TxtHotkey.Text;

            // Show the "Waiting..." text
            TxtHotkey.Text = Config.GetText("WaitingForHotkey");
            TxtHotkey.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 170, 50)); // Make it warm orange while waiting
        }

        private void TxtHotkey_LostFocus(object sender, RoutedEventArgs e)
        {
            // Restore the original text if it still says "Waiting..."
            if (TxtHotkey.Text == Config.GetText("WaitingForHotkey"))
            {
                TxtHotkey.Text = _currentHotkeyText;
            }

            // Restore the blue text color
            TxtHotkey.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 178, 255)); // #66B2FF
        }

        private void TxtHotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;

            // Ignore pure modifier keys (Wait until a letter/number is pressed)
            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);
            if (key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            // Build the string
            string shortcutText = "";
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) shortcutText += "Ctrl + ";
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) shortcutText += "Alt + ";
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) shortcutText += "Shift + ";

            shortcutText += key.ToString();

            // Update the UI
            TxtHotkey.Text = shortcutText;
            _currentHotkeyText = shortcutText; // Save the new valid shortcut

            // Important: Remove focus from the textbox to conclude the input process!
            Keyboard.ClearFocus();

            TxtHotkey.Text = shortcutText;
            _currentHotkeyText = shortcutText;

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

            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key != null)
                    {
                        if (ChkAutostart.IsChecked == true)
                        {
                            // Null check for the process path to fix CS8602
                            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                            if (exePath != null)
                            {
                                key.SetValue(REGISTRY_APP_NAME, $"\"{exePath}\"");
                            }
                        }
                        else
                        {
                            key.DeleteValue(REGISTRY_APP_NAME, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Explicitly use System.Windows.MessageBox to fix CS0104
                System.Windows.MessageBox.Show("Fehler beim Ändern des Autostarts: " + ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ChkWebcam_Changed(object sender, RoutedEventArgs e)
        {
            if (!this.IsLoaded) return;
            Config.AutoWebcamEnabled = ChkWebcam.IsChecked == true;
        }

        // Explicitly use System.Windows.Input.KeyEventArgs to fix CS0104

        private void LinkSupport_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com") { UseShellExecute = true });
            e.Handled = true;
        }
    }
}