using System.Windows;
using System.Windows.Media;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public partial class App : Application
{
    public int StartupEventCount { get; private set; }

    public int StartupArgsLength { get; private set; } = -1;

    public int ExitEventCount { get; private set; }

    public int LastExitCode { get; private set; } = -1;

    private void OnAppStartup(object sender, StartupEventArgs e)
    {
        StartupEventCount++;
        StartupArgsLength = e.Args.Length;
        Resources["StartupInjectedBrush"] = new SolidColorBrush(Color.FromRgb(0x7A, 0x4E, 0xB2));
        Resources["StartupInjectedText"] = "startup resource value";
    }

    private void OnAppExit(object sender, ExitEventArgs e)
    {
        ExitEventCount++;
        LastExitCode = e.ApplicationExitCode;
    }
}
