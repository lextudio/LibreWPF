using System;
using System.Windows;
using System.Windows.Threading;

namespace ProGPU.Wpf.MvpApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (Environment.GetEnvironmentVariable("PROGPU_WPF_MVP_VALIDATE") == "1")
        {
            var window = new MainWindow();
            MvpSelfTest.Validate(window);
            Shutdown();
            Console.WriteLine("ProGPU WPF MVP validation succeeded.");
            return;
        }

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_MVP_RUN_VALIDATE") == "1")
        {
            base.OnStartup(e);
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(ValidateRunningApplication));
            return;
        }

        base.OnStartup(e);
    }

    private static void ValidateRunningApplication()
    {
        try
        {
            var window = FindMainWindow()
                ?? throw new InvalidOperationException("Expected MVP StartupUri MainWindow.");
            MvpSelfTest.Validate(window, expectLoadedStoryboardApplied: true);
            Console.WriteLine("ProGPU WPF MVP Application.Run validation succeeded.");
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Current.Shutdown(1);
        }
    }

    private static MainWindow? FindMainWindow()
    {
        var application = Current;
        if (application?.MainWindow is MainWindow mainWindow)
        {
            return mainWindow;
        }

        if (application == null)
        {
            return null;
        }

        foreach (Window window in application.Windows)
        {
            if (window is MainWindow candidate)
            {
                return candidate;
            }
        }

        return null;
    }
}
