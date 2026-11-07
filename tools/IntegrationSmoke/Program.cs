using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinRinglight;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
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
            if (!CompositionPolicy.CanUse(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362),
                screens.Length, false, Config.Current.SelectedMonitors, false, dpi.DpiScaleX, dpi.DpiScaleY))
            {
                Console.WriteLine($"SKIP: Composition eligibility not met (screens={screens.Length}, dpi={dpi.DpiScaleX}/{dpi.DpiScaleY}).");
                return 0;
            }
            window.SetRinglightState(true);
            window.SetCompositionExperiment(true);
            if (!window.DevCompositionActive) throw new Exception("Composition failed to activate: " + window.DevCompositionStatus);
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
            Config.Current.HideFromCapture = false;
            window.ApplyCaptureAffinity();
            if (!window.DevCompositionActive) throw new Exception("Clearing capture exclusion caused fallback.");
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
            window.ApplyStyleLive(false);
            if (window.DevCompositionActive) throw new Exception("Style-change fallback did not occur.");
            Console.WriteLine("PASS: Composition reactivation and style-change fallback.");
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

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
}
