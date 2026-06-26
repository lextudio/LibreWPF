using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace ProGPU.Wpf.SciChartMvpApp;

public partial class App : Application
{
    internal static int StartupEventCount { get; private set; }

    internal static int ExitEventCount { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
#if PROGPU_WPF_REAL_SCICHART
        RealSciChartMvp.ConfigureRuntimeLicenseFromEnvironment();
#endif

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_SCICHART_VALIDATE") == "1")
        {
            SciChartMvpSelfTest.Validate(SciChartMvpRenderer.Render());
#if PROGPU_WPF_REAL_SCICHART
            var realSciChartResult = RealSciChartMvp.Create();
            RealSciChartMvp.Validate(realSciChartResult);
            if (realSciChartResult.CreatedRealControls)
            {
                Console.WriteLine("ProGPU WPF real SciChart package MVP validation succeeded.");
            }
            else
            {
                Console.WriteLine($"ProGPU WPF real SciChart package MVP restored and validated data APIs; native runtime unavailable. Native dependencies: {realSciChartResult.NativeDependencySummary}. Native compatibility: {realSciChartResult.NativeCompatibilitySummary}. Native exports: {realSciChartResult.NativeExportSummary}. Native resolver: {realSciChartResult.NativeResolverSummary}.");
            }
#endif
            Shutdown();
            Console.WriteLine("ProGPU WPF SciChart MVP validation succeeded.");
            return;
        }

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_SCICHART_RUN_VALIDATE") == "1")
        {
            base.OnStartup(e);
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(ValidateRunningApplication));
            return;
        }

        base.OnStartup(e);
    }

    private void OnAppStartup(object sender, StartupEventArgs e)
    {
        StartupEventCount++;
        Properties["SciChartMvpStartupArgumentCount"] = e.Args.Length;
    }

    private void OnAppExit(object sender, ExitEventArgs e)
    {
        ExitEventCount++;
    }

    private static void ValidateRunningApplication()
    {
        try
        {
            var window = Current.MainWindow as MainWindow
                ?? Current.Windows.OfType<MainWindow>().FirstOrDefault()
                ?? throw new InvalidOperationException("Expected SciChart MVP StartupUri MainWindow.");

            window.ValidateRenderedChart();
            Console.WriteLine("ProGPU WPF SciChart MVP Application.Run validation succeeded.");
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Current.Shutdown(1);
        }
    }
}
