using System;
using System.Windows;

namespace CompositionPrototype;

public partial class MainWindow : Window
{
    private CompositionOverlay? _overlay;
    public MainWindow()
    {
        InitializeComponent();
        ThicknessLabel.Text = $"{ThicknessSlider.Value:0} px";
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_overlay != null)
        {
            _overlay.Dispose();
            _overlay = null;
            ToggleButton.Content = "Overlay starten";
            Status.Text = "Overlay beendet.";
            return;
        }
        try
        {
            _overlay = new CompositionOverlay((float)ThicknessSlider.Value, Rounded.IsChecked == true);
            ToggleButton.Content = "Overlay stoppen";
            ApplyCapture();
        }
        catch (Exception ex)
        {
            _overlay?.Dispose();
            _overlay = null;
            Status.Text = "Overlay konnte nicht gestartet werden: " + ex.Message;
        }
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ThicknessLabel.Text = $"{ThicknessSlider.Value:0} px";
        try { _overlay?.SetRing((float)ThicknessSlider.Value, Rounded.IsChecked == true); }
        catch (Exception ex) { Status.Text = "Ring konnte nicht aktualisiert werden: " + ex.Message; }
    }

    private void CaptureChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && _overlay != null) ApplyCapture();
    }

    private void ApplyCapture()
    {
        if (_overlay == null) return;
        bool ok = _overlay.SetCaptureExcluded(CaptureExcluded.IsChecked == true);
        Status.Text = ok
            ? "Overlay läuft. Klicks sollten durchgehen. Aufnahme-Ausschluss muss mit deiner Aufnahme-App geprüft werden."
            : "Overlay läuft, aber die Windows-Aufnahme-Einstellung konnte nicht gesetzt werden.";
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    protected override void OnClosed(EventArgs e) { _overlay?.Dispose(); base.OnClosed(e); }
}
