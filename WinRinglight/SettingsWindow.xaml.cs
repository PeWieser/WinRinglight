using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinRinglight
{
    public partial class SettingsWindow : Window
    {
        // Reference to our main overlay window
        private MainWindow _mainWindow;

        public SettingsWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            // Load texts from Config using the robust GetText method
            LblBrightness.Text = Config.GetText("Brightness");
            LblThickness.Text = Config.GetText("ChangeRinglightSize");
            LblTemperature.Text = Config.GetText("ColorTemperature");
            this.Title = Config.GetText("AppName") + " - " + Config.GetText("SettingsMenu");

            ComboStyle.SelectedIndex = 0;
        }

        // Fired whenever any of the 3 sliders is moved
        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Prevent crashes if the window is still loading
            if (_mainWindow == null || !this.IsLoaded) return;

            double brightness = SliderBrightness.Value;
            double thickness = SliderThickness.Value;
            double temperature = SliderTemperature.Value;

            // Send values to MainWindow for live preview
            _mainWindow.ApplySettingsLive(brightness, thickness, temperature);
        }

        // Fired when the dropdown (Apple/Windows style) is changed
        private void ComboStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_mainWindow == null || !this.IsLoaded) return;

            // 0 = Apple Style, 1 = Windows Style
            bool isAppleStyle = ComboStyle.SelectedIndex == 0;
            _mainWindow.ApplyStyleLive(isAppleStyle);
        }
    }
}