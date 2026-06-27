using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DataGridLicenser = Xceed.Wpf.DataGrid.Licenser;
using ToolkitLicenser = Xceed.Wpf.Toolkit.Licenser;

namespace ProGPU.Wpf.XceedPaidApp;

public partial class App : Application
{
    internal static int StartupEventCount { get; private set; }

    internal static int ExitEventCount { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var licenseStatus = XceedPaidLicenseBootstrap.ConfigureFromEnvironment();

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_XCEED_PAID_VALIDATE") == "1")
        {
            XceedPaidSelfTest.ValidatePackageSurface(licenseStatus);
            if (!licenseStatus.IsConfigured &&
                Environment.GetEnvironmentVariable("PROGPU_WPF_XCEED_PAID_REQUIRE_LICENSE") != "1")
            {
                Console.WriteLine($"ProGPU WPF paid Xceed package surface validation succeeded; runtime license validation skipped: {licenseStatus.DescribePublic()}.");
                Shutdown();
                return;
            }

            var window = new MainWindow(licenseStatus);
            XceedPaidSelfTest.Validate(window);
            Shutdown();
            Console.WriteLine("ProGPU WPF paid Xceed validation succeeded.");
            return;
        }

        base.OnStartup(e);

        if (!licenseStatus.IsConfigured)
        {
            MainWindow = CreateMissingLicenseWindow(licenseStatus);
            MainWindow.Show();
            return;
        }

        var mainWindow = new MainWindow(licenseStatus);
        MainWindow = mainWindow;
        mainWindow.Show();

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_XCEED_PAID_RUN_VALIDATE") == "1")
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(ValidateRunningApplication));
        }
    }

    private void OnAppStartup(object sender, StartupEventArgs e)
    {
        StartupEventCount++;
        Properties["XceedPaidStartupArgumentCount"] = e.Args.Length;
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
                ?? throw new InvalidOperationException("Expected paid Xceed MainWindow.");
            XceedPaidSelfTest.Validate(window, expectLoaded: true);
            Console.WriteLine("ProGPU WPF paid Xceed Application.Run validation succeeded.");
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Current.Shutdown(1);
        }
    }

    private static Window CreateMissingLicenseWindow(XceedPaidLicenseStatus licenseStatus)
    {
        return new Window
        {
            Title = "ProGPU WPF Paid Xceed",
            Width = 760,
            Height = 360,
            Background = Brushes.White,
            Content = new Border
            {
                Padding = new Thickness(24),
                Child = new TextBlock
                {
                    Text = "Set XCEED_TOOLKIT_LICENSE_KEY and XCEED_DATAGRID_LICENSE_KEY to run the paid Toolkit/DataGrid MVP sample.\n\n" +
                           licenseStatus.DescribePublic(),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 16
                }
            }
        };
    }
}

internal sealed record XceedPaidLicenseStatus(
    bool ToolkitConfigured,
    bool DataGridConfigured,
    string? Failure)
{
    internal bool IsConfigured => ToolkitConfigured && DataGridConfigured && Failure is null;

    internal string DescribePublic()
    {
        if (Failure is not null)
        {
            return Failure;
        }

        return $"Toolkit license env: {(ToolkitConfigured ? "present" : "missing")}; DataGrid license env: {(DataGridConfigured ? "present" : "missing")}.";
    }
}

internal static class XceedPaidLicenseBootstrap
{
    internal const string ToolkitLicenseEnvironmentVariable = "XCEED_TOOLKIT_LICENSE_KEY";
    internal const string DataGridLicenseEnvironmentVariable = "XCEED_DATAGRID_LICENSE_KEY";

    internal static XceedPaidLicenseStatus ConfigureFromEnvironment()
    {
        var toolkitKey = Environment.GetEnvironmentVariable(ToolkitLicenseEnvironmentVariable);
        var dataGridKey = Environment.GetEnvironmentVariable(DataGridLicenseEnvironmentVariable);
        var toolkitConfigured = !string.IsNullOrWhiteSpace(toolkitKey);
        var dataGridConfigured = !string.IsNullOrWhiteSpace(dataGridKey);

        if (!toolkitConfigured || !dataGridConfigured)
        {
            return new XceedPaidLicenseStatus(
                toolkitConfigured,
                dataGridConfigured,
                $"Missing paid Xceed license environment variable(s): {DescribeMissing(toolkitConfigured, dataGridConfigured)}.");
        }

        try
        {
            ToolkitLicenser.LicenseKey = toolkitKey;
            DataGridLicenser.LicenseKey = dataGridKey;
            return new XceedPaidLicenseStatus(true, true, null);
        }
        catch (Exception ex)
        {
            return new XceedPaidLicenseStatus(
                toolkitConfigured,
                dataGridConfigured,
                $"Failed to configure Xceed licenses from environment variables: {ex.GetBaseException().Message}");
        }
    }

    private static string DescribeMissing(bool toolkitConfigured, bool dataGridConfigured)
    {
        if (!toolkitConfigured && !dataGridConfigured)
        {
            return $"{ToolkitLicenseEnvironmentVariable}, {DataGridLicenseEnvironmentVariable}";
        }

        return toolkitConfigured
            ? DataGridLicenseEnvironmentVariable
            : ToolkitLicenseEnvironmentVariable;
    }
}

internal static class XceedPaidSelfTest
{
    internal static void ValidatePackageSurface(XceedPaidLicenseStatus licenseStatus)
    {
        AssertType<Xceed.Wpf.Toolkit.MaterialButton>("Toolkit Plus MaterialButton");
        AssertType<Xceed.Wpf.Toolkit.MaterialTextField>("Toolkit Plus MaterialTextField");
        AssertType<Xceed.Wpf.Toolkit.MaterialSlider>("Toolkit Plus MaterialSlider");
        AssertType<Xceed.Wpf.Toolkit.MaterialSwitch>("Toolkit Plus MaterialSwitch");
        AssertType<Xceed.Wpf.DataGrid.DataGridControl>("Xceed DataGridControl");
        AssertType<Xceed.Wpf.DataGrid.DataGridCollectionViewSource>("Xceed DataGridCollectionViewSource");
        AssertType<Xceed.Wpf.DataGrid.Views.TableView>("Xceed DataGrid TableView");
        AssertType<Xceed.Wpf.DataGrid.Column>("Xceed DataGrid Column");
        AssertType<Xceed.Wpf.DataGrid.DataGridGroupDescription>("Xceed DataGrid group description");
        AssertType<Xceed.Wpf.DataGrid.DataGridItemProperty>("Xceed DataGrid item property");
        AssertType<Xceed.Wpf.DataGrid.DetailConfiguration>("Xceed DataGrid detail configuration");
        AssertType<Xceed.Wpf.DataGrid.FilterRow>("Xceed DataGrid filter row");
        AssertType<Xceed.Wpf.DataGrid.StatRow>("Xceed DataGrid stat row");
        AssertType<Xceed.Wpf.DataGrid.StatCell>("Xceed DataGrid stat cell");
        AssertType<Xceed.Wpf.DataGrid.Stats.CountFunction>("Xceed DataGrid count stat function");
        AssertType<Xceed.Wpf.DataGrid.Stats.AverageFunction>("Xceed DataGrid average stat function");
        AssertType<Xceed.Wpf.DataGrid.ThemePack.Office2007BlueTheme>("Xceed DataGrid ThemePack Office2007BlueTheme");
        AssertType<Xceed.Wpf.DataGrid.Views.CardflowView3D>("Xceed DataGrid Views3D CardflowView3D");
        AssertType<Xceed.Wpf.DataGrid.Views.ElementalBlackTheme>("Xceed DataGrid Views3D ElementalBlackTheme");
        AssertType<Xceed.Wpf.DataGrid.Workbooks.WorkbooksExporter>("Xceed DataGrid Workbooks exporter");
        AssertAssembly("Xceed.Wpf.DataGrid.Views3D");
        AssertAssembly("Xceed.Wpf.DataGrid.ThemePack.1");
        AssertAssembly("Xceed.Wpf.DataGrid.Workbooks");
        AssertAssembly("Xceed.Wpf.AvalonDock.Themes.Windows10");

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_XCEED_PAID_REQUIRE_LICENSE") == "1" &&
            !licenseStatus.IsConfigured)
        {
            throw new InvalidOperationException(licenseStatus.DescribePublic());
        }
    }

    internal static void Validate(MainWindow window, bool expectLoaded = false)
    {
        ValidatePackageSurface(window.LicenseStatus);
        if (!window.LicenseStatus.IsConfigured)
        {
            throw new InvalidOperationException(window.LicenseStatus.DescribePublic());
        }

        var viewModel = window.ViewModel;
        var rowsView = window.PaidRowsViewSource;
        AssertEqual(100_000, viewModel.RowCount, "paid DataGrid row count");
        AssertEqual(66_667, viewModel.ActiveRowCount, "paid DataGrid active filtered row count");
        AssertEqual(viewModel.Rows[0], viewModel.SelectedRow, "paid DataGrid initial selected row");
        AssertEqual(true, viewModel.Rows[0].Details.Count >= 2, "paid DataGrid lazy detail row data");
        AssertEqual(Xceed.Wpf.DataGrid.AutoFilterMode.And, rowsView.AutoFilterMode, "paid DataGrid auto-filter mode");
        AssertEqual(Xceed.Wpf.DataGrid.FilterCriteriaMode.And, rowsView.FilterCriteriaMode, "paid DataGrid filter criteria mode");
        AssertEqual(false, rowsView.AutoCreateItemProperties, "paid DataGrid explicit item properties");
        AssertEqual(false, rowsView.DefaultCalculateDistinctValues, "paid DataGrid distinct value calculation");
        AssertEqual(true, rowsView.AutoCreateDetailDescriptions, "paid DataGrid auto detail descriptions");
        AssertEqual(8, rowsView.ItemProperties.Count, "paid DataGrid item-property metadata");
        AssertEqual(1, rowsView.GroupDescriptions.Count, "paid DataGrid group description count");
        AssertEqual(1, rowsView.SortDescriptions.Count, "paid DataGrid sort description count");
        AssertEqual(2, rowsView.StatFunctions.Count, "paid DataGrid stat function count");
        AssertEqual(false, window.PaidDataGrid.AutoCreateColumns, "paid DataGrid auto columns");
        AssertEqual(true, window.PaidDataGrid.ReadOnly, "paid DataGrid read-only state");
        AssertEqual(true, window.PaidDataGrid.AutoCreateDetailConfigurations, "paid DataGrid auto detail configurations");
        AssertEqual(1, window.PaidDataGrid.DetailConfigurations.Count, "paid DataGrid explicit detail configuration");
        AssertEqual(Xceed.Wpf.DataGrid.ItemScrollingBehavior.Immediate, window.PaidDataGrid.ItemScrollingBehavior, "paid DataGrid scroll behavior");
        AssertEqual(Xceed.Wpf.DataGrid.NavigationBehavior.RowOnly, window.PaidDataGrid.NavigationBehavior, "paid DataGrid navigation behavior");
        AssertEqual(8, window.PaidDataGrid.Columns.Count, "paid DataGrid explicit columns");
        AssertEqual(viewModel.SelectedRow, window.PaidDataGrid.SelectedItem, "paid DataGrid selected item");
        AssertEqual(true, window.PaidDataGrid.View is Xceed.Wpf.DataGrid.Views.TableView, "paid DataGrid table view");
        AssertEqual(true, window.PaidTableView.Theme is Xceed.Wpf.DataGrid.ThemePack.Office2007BlueTheme, "paid DataGrid ThemePack table view theme");
        AssertEqual(3, window.PaidTableView.FixedHeaders.Count, "paid DataGrid fixed header row count");
        AssertEqual(1, window.PaidTableView.FixedFooters.Count, "paid DataGrid fixed footer row count");
        AssertEqual(true, window.MaterialActionsSwitch.IsChecked, "paid Toolkit MaterialSwitch binding");
        AssertEqual("ProGPU", viewModel.FilterText, "paid Toolkit MaterialTextField binding");

        if (expectLoaded)
        {
            window.PaidDataGridDocument.IsSelected = true;
            window.PaidDataGridDocument.IsActive = true;
            window.PaidDataGrid.UpdateLayout();
            window.PaidDataGrid.BringItemIntoView(viewModel.Rows[viewModel.Rows.Count - 1]);
            window.PaidDataGrid.UpdateLayout();

            if (window.PaidDataGrid.ActualWidth <= 0 ||
                window.PaidDataGrid.ActualHeight <= 0)
            {
                throw new InvalidOperationException("Expected paid Xceed DataGrid to participate in loaded layout.");
            }
        }
    }

    private static void AssertType<T>(string description)
    {
        if (typeof(T).FullName is null)
        {
            throw new InvalidOperationException($"Expected {description} type to load.");
        }
    }

    private static void AssertAssembly(string assemblyName)
    {
        Assembly.Load(new AssemblyName(assemblyName));
    }

    private static void AssertEqual<T>(T expected, T actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to be {expected}, got {actual}.");
        }
    }
}
