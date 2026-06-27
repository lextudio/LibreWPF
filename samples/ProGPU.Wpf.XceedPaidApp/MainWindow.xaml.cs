using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using Xceed.Wpf.DataGrid;
using Xceed.Wpf.DataGrid.Settings;
using Xceed.Wpf.DataGrid.ThemePack;
using Xceed.Wpf.DataGrid.Views;

namespace ProGPU.Wpf.XceedPaidApp;

public partial class MainWindow : Window
{
    private SettingsRepository? _savedSettings;
    private int _priorityBandQueryCount;

    public MainWindow()
        : this(XceedPaidLicenseBootstrap.ConfigureFromEnvironment())
    {
    }

    internal MainWindow(XceedPaidLicenseStatus licenseStatus)
    {
        LicenseStatus = licenseStatus;
        ViewModel = new XceedPaidViewModel(licenseStatus);
        DataContext = ViewModel;
        InitializeComponent();
        PaidColumnChooser.Columns = PaidDataGrid.Columns;
        Loaded += OnLoaded;
    }

    internal XceedPaidLicenseStatus LicenseStatus { get; }

    internal XceedPaidViewModel ViewModel { get; }

    internal int PriorityBandQueryCount => _priorityBandQueryCount;

    internal DataGridCollectionViewSource PaidRowsViewSource => (DataGridCollectionViewSource)FindResource("PaidRowsView");

    internal DataGridVirtualizingQueryableCollectionViewSource VirtualPaidRowsViewSource => (DataGridVirtualizingQueryableCollectionViewSource)FindResource("VirtualPaidRowsView");

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Status = "Paid Xceed Toolkit/DataGrid loaded";
        ViewModel.Activity.Add("Loaded paid Toolkit Plus, AvalonDock Windows10 theme, and Xceed DataGrid document");
        ViewModel.Activity.Add("Paid DataGrid view applies active-row filtering, category grouping, updated-date sorting, stats, details, search, merged headers, and Office2007 theme");
        ViewModel.Activity.Add("Paid DataGrid export, settings persistence, column chooser, Tableflow, and Cardflow 3D commands are available");
        ViewModel.Activity.Add("Paid DataGrid virtualizing queryable source uses bounded pages and realized-item cache");
        UpdateColumnChooserStatus("Paid DataGrid column chooser initialized");
    }

    private void PaidRowsView_Filter(object sender, FilterEventArgs e)
    {
        if (e.Item is PaidGridItem item)
        {
            e.Accepted = item.Active;
        }
    }

    private void PriorityBand_QueryValue(object sender, DataGridItemPropertyQueryValueEventArgs e)
    {
        if (e.Item is PaidGridItem item)
        {
            _priorityBandQueryCount++;
            e.Value = GetPriorityBand(item);
        }
    }

    private void AddRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.ActionsEnabled)
        {
            ViewModel.Status = "Actions disabled";
            ViewModel.LastAction = "MaterialSwitch disabled Add row";
            return;
        }

        var item = ViewModel.AddRow();
        PaidDataGrid.SelectedItem = item;
        PaidDataGrid.BringItemIntoView(item);
        ViewModel.Status = $"Added {item.Title}";
    }

    private void SelectMiddleButton_Click(object sender, RoutedEventArgs e)
    {
        SelectRow(ViewModel.Rows.Count / 2, "Selected middle row");
    }

    private void SelectLastButton_Click(object sender, RoutedEventArgs e)
    {
        SelectRow(ViewModel.Rows.Count - 1, "Selected last row");
    }

    private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        ExportGrid("CSV", ".csv", stream => PaidDataGrid.ExportToCsv(stream));
    }

    private void ExportExcelButton_Click(object sender, RoutedEventArgs e)
    {
        ExportGrid("Excel XMLSS", ".xml", stream => PaidDataGrid.ExportToExcel(stream));
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _savedSettings = new SettingsRepository();
        PaidDataGrid.SaveUserSettings(_savedSettings, UserSettings.All);
        ViewModel.LastAction = "Saved paid DataGrid user settings in memory";
        ViewModel.Status = ViewModel.LastAction;
        ViewModel.Activity.Add(ViewModel.LastAction);
    }

    private void LoadSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_savedSettings is null)
        {
            ViewModel.LastAction = "No saved paid DataGrid user settings";
            ViewModel.Status = ViewModel.LastAction;
            ViewModel.Activity.Add(ViewModel.LastAction);
            return;
        }

        PaidDataGrid.LoadUserSettings(_savedSettings, UserSettings.All);
        ViewModel.LastAction = "Reloaded paid DataGrid user settings";
        ViewModel.Status = ViewModel.LastAction;
        ViewModel.Activity.Add(ViewModel.LastAction);
    }

    private void ToggleStatusColumnButton_Click(object sender, RoutedEventArgs e)
    {
        var statusColumn = FindPaidColumn(PaidDataGrid, "Status");
        statusColumn.Visible = !statusColumn.Visible;
        UpdateColumnChooserStatus(statusColumn.Visible
            ? "Showed paid DataGrid Status column"
            : "Hid paid DataGrid Status column");
    }

    private void TableViewButton_Click(object sender, RoutedEventArgs e)
    {
        PaidDataGrid.View = PaidTableView;
        ViewModel.LastAction = "Activated paid DataGrid TableView";
        ViewModel.Status = ViewModel.LastAction;
        ViewModel.Activity.Add(ViewModel.LastAction);
    }

    private void TableflowViewButton_Click(object sender, RoutedEventArgs e)
    {
        PaidDataGrid.View = new TableflowView
        {
            Theme = new Office2007BlueTheme(),
            AllowColumnChooser = true,
            ColumnChooserSortOrder = ColumnChooserSortOrder.TitleAscending
        };
        ViewModel.LastAction = "Activated paid DataGrid TableflowView";
        ViewModel.Status = ViewModel.LastAction;
        ViewModel.Activity.Add(ViewModel.LastAction);
    }

    private void CardflowViewButton_Click(object sender, RoutedEventArgs e)
    {
        PaidDataGrid.View = new CardflowView3D
        {
            Theme = new ElementalBlackTheme(),
            SideCardsCount = 3,
            ShowReflections = false,
            IsCardFlippingEnabled = false,
            CardHeightToViewportRatio = 0.55
        };
        ViewModel.LastAction = "Activated paid DataGrid CardflowView3D";
        ViewModel.Status = ViewModel.LastAction;
        ViewModel.Activity.Add(ViewModel.LastAction);
    }

    internal static ColumnBase FindPaidColumn(DataGridControl grid, string fieldName)
    {
        return grid.Columns
            .Cast<ColumnBase>()
            .FirstOrDefault(column => string.Equals(column.FieldName, fieldName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Expected paid DataGrid column '{fieldName}'.");
    }

    internal static string GetPriorityBand(PaidGridItem item)
    {
        return item.Score switch
        {
            >= 80 => "Critical",
            >= 60 => "High",
            >= 35 => "Medium",
            _ => "Low"
        };
    }

    private void SelectRow(int index, string action)
    {
        if (index < 0 || index >= ViewModel.Rows.Count)
        {
            return;
        }

        var item = ViewModel.Rows[index];
        ViewModel.SelectedRow = item;
        PaidDataGrid.BringItemIntoView(item);
        ViewModel.LastAction = $"{action}: {item.Title}";
        ViewModel.Status = ViewModel.LastAction;
        ViewModel.Activity.Add(ViewModel.LastAction);
    }

    private void UpdateColumnChooserStatus(string action)
    {
        ViewModel.LastAction = $"{action}; visible columns: {PaidDataGrid.VisibleColumns.Count}";
        ViewModel.Status = ViewModel.LastAction;
        ViewModel.Activity.Add(ViewModel.LastAction);
    }

    private void ExportGrid(string label, string extension, Action<Stream> export)
    {
        var directory = ResolveExportDirectory();
        Directory.CreateDirectory(directory);
        var fileName = $"progpu-wpf-xceed-paid-{DateTime.UtcNow:yyyyMMdd-HHmmss}{extension}";
        var path = Path.Combine(directory, fileName);

        using (var stream = File.Create(path))
        {
            export(stream);
        }

        ViewModel.LastExportPath = path;
        ViewModel.LastAction = $"Exported paid DataGrid {label}: {path}";
        ViewModel.Status = ViewModel.LastAction;
        ViewModel.Activity.Add(ViewModel.LastAction);
    }

    private static string ResolveExportDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("PROGPU_WPF_XCEED_PAID_EXPORT_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(Path.GetTempPath(), "progpu-wpf-xceed-paid");
    }
}

internal sealed class XceedPaidViewModel : INotifyPropertyChanged
{
    private PaidGridItem _selectedRow = null!;
    private string _status = "Paid Xceed ready";
    private string _lastAction = "Idle";
    private string _lastExportPath = "No export yet";
    private string _filterText = "ProGPU";
    private string _searchText = "ProGPU";
    private bool _actionsEnabled = true;
    private int _batchSize = 50;
    private double _scoreBias = 35.0;
    private double _progress = 35.0;
    private bool _isRefreshing;

    internal XceedPaidViewModel(XceedPaidLicenseStatus licenseStatus)
    {
        LicenseStatusText = licenseStatus.DescribePublic();
        PackageStatus = "Toolkit Plus 5.2, DataGrid 7.3, AvalonDock Windows10 theme, virtualization, unbound columns, export/settings APIs, column chooser, Views3D/theme-pack assemblies";
        Rows = CreateRows(100_000);
        _selectedRow = Rows[0];
        Activity =
        [
            "Created 100,000 paid DataGrid rows",
            "Configured explicit Xceed DataGrid columns",
            "Configured computed paid DataGrid unbound priority column",
            "Configured Toolkit Plus Material controls",
            "Configured paid DataGrid merged headers, search, export, settings, column chooser, and view commands",
            "Configured paid DataGrid virtualizing queryable source"
        ];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PaidGridItem> Rows { get; }

    public IQueryable<PaidGridItem> VirtualRows => Rows.AsQueryable();

    public ObservableCollection<string> Activity { get; }

    public string LicenseStatusText { get; }

    public string PackageStatus { get; }

    public int RowCount => Rows.Count;

    public int ActiveRowCount => (Rows.Count / 3 * 2) + (Rows.Count % 3 == 0 ? 0 : Math.Min(Rows.Count % 3, 2));

    public PaidGridItem SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (!ReferenceEquals(_selectedRow, value) && value is not null)
            {
                _selectedRow = value;
                OnPropertyChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string LastAction
    {
        get => _lastAction;
        set => SetProperty(ref _lastAction, value);
    }

    public string LastExportPath
    {
        get => _lastExportPath;
        set => SetProperty(ref _lastExportPath, value);
    }

    public string FilterText
    {
        get => _filterText;
        set => SetProperty(ref _filterText, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public bool ActionsEnabled
    {
        get => _actionsEnabled;
        set => SetProperty(ref _actionsEnabled, value);
    }

    public int BatchSize
    {
        get => _batchSize;
        set => SetProperty(ref _batchSize, value);
    }

    public double ScoreBias
    {
        get => _scoreBias;
        set
        {
            if (SetProperty(ref _scoreBias, value))
            {
                Progress = value;
            }
        }
    }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => SetProperty(ref _isRefreshing, value);
    }

    public PaidGridItem AddRow()
    {
        int nextId = Rows.Count + 1;
        var item = CreateRow(nextId);
        Rows.Add(item);
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(ActiveRowCount));
        SelectedRow = item;
        LastAction = $"Added {item.Title}";
        Activity.Add(LastAction);
        return item;
    }

    private static ObservableCollection<PaidGridItem> CreateRows(int count)
    {
        var rows = new ObservableCollection<PaidGridItem>();
        for (int i = 1; i <= count; i++)
        {
            rows.Add(CreateRow(i));
        }

        return rows;
    }

    private static PaidGridItem CreateRow(int id)
    {
        string[] owners = ["ProGPU", "WPF", "Xceed", "SDK", "Toolkit"];
        string[] categories = ["Rendering", "Framework", "Toolkit", "DataGrid", "Docking"];
        string[] statuses = ["Ready", "Queued", "Running", "Reviewed", "Pinned"];
        return new PaidGridItem(
            id,
            $"Paid row {id:D6}",
            owners[id % owners.Length],
            categories[id % categories.Length],
            statuses[id % statuses.Length],
            (id * 17) % 100,
            new DateTime(2026, 1, 1).AddDays(-(id % 365)),
            id % 3 != 0);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class PaidGridItem
{
    public PaidGridItem(
        int id,
        string title,
        string owner,
        string category,
        string status,
        int score,
        DateTime updated,
        bool active)
    {
        Id = id;
        Title = title;
        Owner = owner;
        Category = category;
        Status = status;
        Score = score;
        Updated = updated;
        Active = active;
    }

    private IReadOnlyList<PaidGridDetail>? _details;

    public int Id { get; }

    public string Title { get; }

    public string Owner { get; }

    public string Category { get; }

    public string Status { get; }

    public int Score { get; }

    public DateTime Updated { get; }

    public bool Active { get; }

    public IReadOnlyList<PaidGridDetail> Details => _details ??=
    [
        new PaidGridDetail(1, $"Inspect {Title}", Owner, Score),
        new PaidGridDetail(2, $"Render {Title}", Category, (Score + 11) % 100)
    ];
}

public sealed class PaidGridDetail
{
    public PaidGridDetail(
        int step,
        string note,
        string owner,
        int score)
    {
        Step = step;
        Note = note;
        Owner = owner;
        Score = score;
    }

    public int Step { get; }

    public string Note { get; }

    public string Owner { get; }

    public int Score { get; }
}
