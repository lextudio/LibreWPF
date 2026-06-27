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
        AssertType<Xceed.Wpf.DataGrid.DataGridVirtualizingCollectionView>("Xceed DataGrid virtualizing collection view");
        AssertType<Xceed.Wpf.DataGrid.DataGridVirtualizingQueryableCollectionViewSource>("Xceed DataGrid virtualizing queryable collection view source");
        AssertType<Xceed.Wpf.DataGrid.DataGridVirtualizingPanel>("Xceed DataGrid virtualizing panel");
        AssertType<Xceed.Wpf.DataGrid.Views.TableView>("Xceed DataGrid TableView");
        AssertType<Xceed.Wpf.DataGrid.Column>("Xceed DataGrid Column");
        AssertType<Xceed.Wpf.DataGrid.DataGridGroupDescription>("Xceed DataGrid group description");
        AssertType<Xceed.Wpf.DataGrid.DataGridItemProperty>("Xceed DataGrid item property");
        AssertType<Xceed.Wpf.DataGrid.DataGridUnboundItemProperty>("Xceed DataGrid unbound item property");
        AssertType<Xceed.Wpf.DataGrid.UnboundColumn>("Xceed DataGrid unbound column");
        AssertType<Xceed.Wpf.DataGrid.DetailConfiguration>("Xceed DataGrid detail configuration");
        AssertType<Xceed.Wpf.DataGrid.FilterRow>("Xceed DataGrid filter row");
        AssertType<Xceed.Wpf.DataGrid.MergedHeader>("Xceed DataGrid merged header");
        AssertType<Xceed.Wpf.DataGrid.MergedColumn>("Xceed DataGrid merged column");
        AssertType<Xceed.Wpf.DataGrid.MergedColumnManagerRow>("Xceed DataGrid merged-column manager row");
        AssertType<Xceed.Wpf.DataGrid.ColumnChooserControl>("Xceed DataGrid column chooser control");
        AssertType<Xceed.Wpf.DataGrid.ColumnChooserContextMenu>("Xceed DataGrid column chooser context menu");
        AssertType<Xceed.Wpf.DataGrid.SearchControl>("Xceed DataGrid search control");
        AssertType<Xceed.Wpf.DataGrid.StatRow>("Xceed DataGrid stat row");
        AssertType<Xceed.Wpf.DataGrid.StatCell>("Xceed DataGrid stat cell");
        AssertType<Xceed.Wpf.DataGrid.Stats.CountFunction>("Xceed DataGrid count stat function");
        AssertType<Xceed.Wpf.DataGrid.Stats.AverageFunction>("Xceed DataGrid average stat function");
        AssertType<Xceed.Wpf.DataGrid.Export.CsvExporter>("Xceed DataGrid CSV exporter");
        AssertType<Xceed.Wpf.DataGrid.Export.ExcelExporter>("Xceed DataGrid Excel exporter");
        AssertType<Xceed.Wpf.DataGrid.Export.HtmlClipboardExporter>("Xceed DataGrid HTML clipboard exporter");
        AssertType<Xceed.Wpf.DataGrid.Settings.SettingsRepository>("Xceed DataGrid settings repository");
        AssertType<Xceed.Wpf.DataGrid.ThemePack.Office2007BlueTheme>("Xceed DataGrid ThemePack Office2007BlueTheme");
        AssertType<Xceed.Wpf.DataGrid.Views.TableflowView>("Xceed DataGrid TableflowView");
        AssertType<Xceed.Wpf.DataGrid.Views.TreeGridflowView>("Xceed DataGrid TreeGridflowView");
        AssertType<Xceed.Wpf.DataGrid.Views.CardflowView3D>("Xceed DataGrid Views3D CardflowView3D");
        AssertType<Xceed.Wpf.DataGrid.Views.ElementalBlackTheme>("Xceed DataGrid Views3D ElementalBlackTheme");
        AssertType<Xceed.Wpf.DataGrid.Workbooks.WorkbooksExporter>("Xceed DataGrid Workbooks exporter");
        AssertPublicMethod<Xceed.Wpf.DataGrid.DataGridControl>("ExportToCsv", typeof(System.IO.Stream));
        AssertPublicMethod<Xceed.Wpf.DataGrid.DataGridControl>("ExportToExcel", typeof(System.IO.Stream));
        AssertPublicMethod<Xceed.Wpf.DataGrid.DataGridControl>("ExportToXps", typeof(System.IO.Stream), typeof(Size));
        AssertPublicMethod<Xceed.Wpf.DataGrid.DataGridControl>(
            "SaveUserSettings",
            typeof(Xceed.Wpf.DataGrid.Settings.SettingsRepository),
            typeof(Xceed.Wpf.DataGrid.Settings.UserSettings));
        AssertPublicMethod<Xceed.Wpf.DataGrid.DataGridControl>(
            "LoadUserSettings",
            typeof(Xceed.Wpf.DataGrid.Settings.SettingsRepository),
            typeof(Xceed.Wpf.DataGrid.Settings.UserSettings));
        AssertPublicProperty<Xceed.Wpf.DataGrid.ColumnChooserControl>("Columns");
        AssertPublicProperty<Xceed.Wpf.DataGrid.ColumnChooserControl>("VisibleColumnsSectionTitle");
        AssertPublicProperty<Xceed.Wpf.DataGrid.ColumnChooserControl>("HiddenColumnsSectionTitle");
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
        var virtualRowsView = window.VirtualPaidRowsViewSource;
        AssertEqual(100_000, viewModel.RowCount, "paid DataGrid row count");
        AssertEqual(66_667, viewModel.ActiveRowCount, "paid DataGrid active filtered row count");
        AssertEqual(viewModel.Rows[0], viewModel.SelectedRow, "paid DataGrid initial selected row");
        AssertEqual(100_000, viewModel.VirtualRows.Count(), "paid DataGrid virtual queryable row count");
        AssertEqual(true, viewModel.Rows[0].Details.Count >= 2, "paid DataGrid lazy detail row data");
        AssertEqual(Xceed.Wpf.DataGrid.AutoFilterMode.And, rowsView.AutoFilterMode, "paid DataGrid auto-filter mode");
        AssertEqual(Xceed.Wpf.DataGrid.FilterCriteriaMode.And, rowsView.FilterCriteriaMode, "paid DataGrid filter criteria mode");
        AssertEqual(false, rowsView.AutoCreateItemProperties, "paid DataGrid explicit item properties");
        AssertEqual(false, rowsView.DefaultCalculateDistinctValues, "paid DataGrid distinct value calculation");
        AssertEqual(true, rowsView.AutoCreateDetailDescriptions, "paid DataGrid auto detail descriptions");
        AssertEqual(9, rowsView.ItemProperties.Count, "paid DataGrid item-property metadata");
        var priorityProperty = rowsView.ItemProperties
            .Cast<Xceed.Wpf.DataGrid.DataGridItemPropertyBase>()
            .Single(itemProperty => itemProperty.Name == "PriorityBand");
        AssertEqual(true, priorityProperty is Xceed.Wpf.DataGrid.DataGridUnboundItemProperty, "paid DataGrid priority unbound property");
        AssertEqual("Low", priorityProperty.GetValue(viewModel.Rows[0]), "paid DataGrid low priority unbound value");
        AssertEqual("Critical", MainWindow.GetPriorityBand(viewModel.Rows[4]), "paid DataGrid critical priority helper value");
        AssertEqual(true, window.PriorityBandQueryCount > 0, "paid DataGrid unbound priority query event");
        AssertEqual(1, rowsView.GroupDescriptions.Count, "paid DataGrid group description count");
        AssertEqual(1, rowsView.SortDescriptions.Count, "paid DataGrid sort description count");
        AssertEqual(2, rowsView.StatFunctions.Count, "paid DataGrid stat function count");
        AssertEqual(256, virtualRowsView.PageSize, "paid DataGrid virtual page size");
        AssertEqual(1024, virtualRowsView.MaxRealizedItemCount, "paid DataGrid virtual realized-item cache");
        AssertEqual(Xceed.Wpf.DataGrid.CommitMode.EditCommitted, virtualRowsView.CommitMode, "paid DataGrid virtual commit mode");
        AssertEqual("ProGPU", window.PaidDataGrid.SearchText, "paid DataGrid search text binding");
        AssertEqual(false, window.PaidDataGrid.AutoCreateColumns, "paid DataGrid auto columns");
        AssertEqual(true, window.PaidDataGrid.ReadOnly, "paid DataGrid read-only state");
        AssertEqual(true, window.PaidDataGrid.AutoCreateDetailConfigurations, "paid DataGrid auto detail configurations");
        AssertEqual(1, window.PaidDataGrid.DetailConfigurations.Count, "paid DataGrid explicit detail configuration");
        AssertEqual(1, window.PaidDataGrid.MergedHeaders.Count, "paid DataGrid merged header count");
        AssertEqual(true, window.PaidDataGrid.ClipboardExporters.Count >= 3, "paid DataGrid clipboard exporters");
        AssertEqual(Xceed.Wpf.DataGrid.ItemScrollingBehavior.Immediate, window.PaidDataGrid.ItemScrollingBehavior, "paid DataGrid scroll behavior");
        AssertEqual(Xceed.Wpf.DataGrid.NavigationBehavior.RowOnly, window.PaidDataGrid.NavigationBehavior, "paid DataGrid navigation behavior");
        AssertEqual(9, window.PaidDataGrid.Columns.Count, "paid DataGrid explicit columns");
        AssertEqual(9, window.PaidDataGrid.VisibleColumns.Count, "paid DataGrid visible column count");
        AssertEqual(true, ReferenceEquals(window.PaidDataGrid.Columns, window.PaidColumnChooser.Columns), "paid DataGrid column chooser column source");
        AssertEqual(true, window.PaidTableView.AllowColumnChooser, "paid DataGrid table view column chooser");
        AssertEqual(Xceed.Wpf.DataGrid.Views.ColumnChooserSortOrder.TitleAscending, window.PaidTableView.ColumnChooserSortOrder, "paid DataGrid column chooser sort order");
        var idColumn = MainWindow.FindPaidColumn(window.PaidDataGrid, "Id");
        var priorityColumn = MainWindow.FindPaidColumn(window.PaidDataGrid, "PriorityBand");
        var statusColumn = MainWindow.FindPaidColumn(window.PaidDataGrid, "Status");
        AssertEqual(true, priorityColumn is Xceed.Wpf.DataGrid.UnboundColumn, "paid DataGrid priority unbound column");
        AssertEqual(false, idColumn.ShowInColumnChooser, "paid DataGrid fixed Id column chooser visibility");
        AssertEqual(true, statusColumn.ShowInColumnChooser, "paid DataGrid Status column chooser visibility");
        statusColumn.Visible = false;
        AssertEqual(8, window.PaidDataGrid.VisibleColumns.Count, "paid DataGrid hidden Status visible column count");
        statusColumn.Visible = true;
        AssertEqual(9, window.PaidDataGrid.VisibleColumns.Count, "paid DataGrid restored Status visible column count");
        AssertEqual(false, window.VirtualPaidDataGrid.AutoCreateColumns, "paid virtual DataGrid auto columns");
        AssertEqual(true, window.VirtualPaidDataGrid.ReadOnly, "paid virtual DataGrid read-only state");
        AssertEqual(Xceed.Wpf.DataGrid.ItemScrollingBehavior.Deferred, window.VirtualPaidDataGrid.ItemScrollingBehavior, "paid virtual DataGrid scroll behavior");
        AssertEqual(7, window.VirtualPaidDataGrid.Columns.Count, "paid virtual DataGrid explicit columns");
        AssertEqual(viewModel.SelectedRow, window.PaidDataGrid.SelectedItem, "paid DataGrid selected item");
        AssertEqual(true, window.PaidDataGrid.View is Xceed.Wpf.DataGrid.Views.TableView, "paid DataGrid table view");
        AssertEqual(true, window.PaidTableView.Theme is Xceed.Wpf.DataGrid.ThemePack.Office2007BlueTheme, "paid DataGrid ThemePack table view theme");
        AssertEqual(4, window.PaidTableView.FixedHeaders.Count, "paid DataGrid fixed header row count");
        AssertEqual(1, window.PaidTableView.FixedFooters.Count, "paid DataGrid fixed footer row count");
        AssertEqual(true, window.PaidSearchControl is Xceed.Wpf.DataGrid.SearchControl, "paid DataGrid search control");
        AssertEqual(true, window.MaterialActionsSwitch.IsChecked, "paid Toolkit MaterialSwitch binding");
        AssertEqual("ProGPU", viewModel.FilterText, "paid Toolkit MaterialTextField binding");
        AssertEqual("ProGPU", viewModel.SearchText, "paid DataGrid search view-model binding");

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

    private static void AssertPublicMethod<T>(string methodName, params Type[] parameterTypes)
    {
        var method = typeof(T).GetMethod(methodName, parameterTypes);
        if (method is null)
        {
            throw new InvalidOperationException($"Expected public {typeof(T).FullName}.{methodName} method.");
        }
    }

    private static void AssertPublicProperty<T>(string propertyName)
    {
        var property = typeof(T).GetProperty(propertyName);
        if (property is null)
        {
            throw new InvalidOperationException($"Expected public {typeof(T).FullName}.{propertyName} property.");
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to be {expected}, got {actual}.");
        }
    }
}
