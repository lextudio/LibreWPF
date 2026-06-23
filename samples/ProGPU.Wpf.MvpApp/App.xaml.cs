using System;
using System.Windows;

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

        base.OnStartup(e);
    }
}
