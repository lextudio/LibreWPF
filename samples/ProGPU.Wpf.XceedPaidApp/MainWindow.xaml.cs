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
        ViewModel.Activity.Add("Paid editable DataGrid uses current-cell edit triggers and IDataErrorInfo validation");
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

    private void ValidateEditButton_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedEditableRow;
        item.Score = 125;
        var error = item[nameof(PaidEditableGridItem.Score)];
        item.Score = 75;
        ViewModel.LastAction = $"Validated paid editable DataGrid score rule: {error}";
        ViewModel.Status = ViewModel.LastAction;
        ViewModel.Activity.Add(ViewModel.LastAction);
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

    internal void ExercisePaidDataGridRuntimeCommands()
    {
        var initialRowCount = ViewModel.RowCount;
        var initialActiveRowCount = ViewModel.ActiveRowCount;

        AddRowButton_Click(this, new RoutedEventArgs());
        AssertRuntimeCondition(ViewModel.RowCount == initialRowCount + 1, "Add row command should append one paid DataGrid row.");
        AssertRuntimeCondition(ViewModel.ActiveRowCount == initialActiveRowCount + 1, "Add row command should refresh active-row metadata.");
        AssertRuntimeCondition(ReferenceEquals(ViewModel.SelectedRow, PaidDataGrid.SelectedItem), "Add row command should synchronize paid DataGrid selection.");
        AssertRuntimeCondition(ViewModel.LastAction.Contains("Added", StringComparison.Ordinal), "Add row command should report activity.");

        SelectLastButton_Click(this, new RoutedEventArgs());
        AssertRuntimeCondition(ReferenceEquals(ViewModel.Rows[^1], ViewModel.SelectedRow), "Select last command should update the selected view-model row.");
        AssertRuntimeCondition(ReferenceEquals(ViewModel.Rows[^1], PaidDataGrid.SelectedItem), "Select last command should update the paid DataGrid selected item.");

        SaveSettingsButton_Click(this, new RoutedEventArgs());
        AssertRuntimeCondition(_savedSettings is not null, "Save settings command should create an in-memory paid DataGrid settings repository.");
        LoadSettingsButton_Click(this, new RoutedEventArgs());
        AssertRuntimeCondition(ViewModel.LastAction.Contains("Reloaded", StringComparison.Ordinal), "Load settings command should restore the saved paid DataGrid settings.");

        var statusColumn = FindPaidColumn(PaidDataGrid, "Status");
        ToggleStatusColumnButton_Click(this, new RoutedEventArgs());
        AssertRuntimeCondition(!statusColumn.Visible, "Toggle Status command should hide the paid DataGrid Status column.");
        AssertRuntimeCondition(PaidDataGrid.VisibleColumns.Count == 8, "Toggle Status command should update paid DataGrid visible-column count when hidden.");
        ToggleStatusColumnButton_Click(this, new RoutedEventArgs());
        AssertRuntimeCondition(statusColumn.Visible, "Toggle Status command should restore the paid DataGrid Status column.");
        AssertRuntimeCondition(PaidDataGrid.VisibleColumns.Count == 9, "Toggle Status command should update paid DataGrid visible-column count when restored.");

        TableflowViewButton_Click(this, new RoutedEventArgs());
        AssertRuntimeCondition(PaidDataGrid.View is TableflowView, "Tableflow command should activate the paid DataGrid TableflowView.");
        CardflowViewButton_Click(this, new RoutedEventArgs());
        AssertRuntimeCondition(PaidDataGrid.View is CardflowView3D, "Cardflow command should activate the paid DataGrid CardflowView3D.");
        TableViewButton_Click(this, new RoutedEventArgs());
        AssertRuntimeCondition(ReferenceEquals(PaidDataGrid.View, PaidTableView), "TableView command should restore the XAML-defined paid DataGrid TableView.");

        ValidateEditButton_Click(this, new RoutedEventArgs());
        AssertRuntimeCondition(
            ViewModel.LastAction.Contains("Score must stay between 0 and 100.", StringComparison.Ordinal),
            "Validate edit command should execute the paid editable DataGrid score validation path.");
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
        PaidDataGrid.SelectedItem = item;
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

    private static void AssertRuntimeCondition(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
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
    private PaidEditableGridItem _selectedEditableRow = null!;

    internal XceedPaidViewModel(XceedPaidLicenseStatus licenseStatus)
    {
        LicenseStatusText = licenseStatus.DescribePublic();
        PackageStatus = "Toolkit Plus 5.2, DataGrid 7.3, AvalonDock Windows10 theme, virtualization, editing/validation, unbound columns, export/settings APIs, column chooser, Views3D/theme-pack assemblies";
        Rows = CreateRows(100_000);
        EditableRows = CreateEditableRows(128);
        _selectedRow = Rows[0];
        _selectedEditableRow = EditableRows[0];
        Activity =
        [
            "Created 100,000 paid DataGrid rows",
            "Created paid editable DataGrid validation rows",
            "Configured explicit Xceed DataGrid columns",
            "Configured computed paid DataGrid unbound priority column",
            "Configured Toolkit Plus Material controls",
            "Configured paid DataGrid merged headers, search, export, settings, editing, validation, column chooser, and view commands",
            "Configured paid DataGrid virtualizing queryable source"
        ];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PaidGridItem> Rows { get; }

    public IQueryable<PaidGridItem> VirtualRows => Rows.AsQueryable();

    public ObservableCollection<PaidEditableGridItem> EditableRows { get; }

    public ObservableCollection<string> Activity { get; }

    public string[] StatusOptions { get; } = ["Ready", "Queued", "Running", "Reviewed", "Pinned"];

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

    public PaidEditableGridItem SelectedEditableRow
    {
        get => _selectedEditableRow;
        set
        {
            if (!ReferenceEquals(_selectedEditableRow, value) && value is not null)
            {
                _selectedEditableRow = value;
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

    private static ObservableCollection<PaidEditableGridItem> CreateEditableRows(int count)
    {
        var rows = new ObservableCollection<PaidEditableGridItem>();
        for (int i = 1; i <= count; i++)
        {
            var row = CreateRow(i);
            rows.Add(new PaidEditableGridItem(
                row.Id,
                row.Title,
                row.Status,
                row.Score,
                row.Active));
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

public sealed class PaidEditableGridItem : INotifyPropertyChanged, IDataErrorInfo
{
    private string _title;
    private string _status;
    private int _score;
    private bool _active;

    public PaidEditableGridItem(
        int id,
        string title,
        string status,
        int score,
        bool active)
    {
        Id = id;
        _title = title;
        _status = status;
        _score = score;
        _active = active;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public int Score
    {
        get => _score;
        set => SetProperty(ref _score, value);
    }

    public bool Active
    {
        get => _active;
        set => SetProperty(ref _active, value);
    }

    public string Error
    {
        get
        {
            string titleError = this[nameof(Title)];
            if (!string.IsNullOrEmpty(titleError))
            {
                return titleError;
            }

            string statusError = this[nameof(Status)];
            if (!string.IsNullOrEmpty(statusError))
            {
                return statusError;
            }

            return this[nameof(Score)];
        }
    }

    public string this[string columnName]
    {
        get
        {
            return columnName switch
            {
                nameof(Title) when string.IsNullOrWhiteSpace(Title) => "Title is required.",
                nameof(Status) when !IsKnownStatus(Status) => "Status must be one of the paid sample states.",
                nameof(Score) when Score < 0 || Score > 100 => "Score must stay between 0 and 100.",
                _ => string.Empty
            };
        }
    }

    private static bool IsKnownStatus(string status)
    {
        return status is "Ready" or "Queued" or "Running" or "Reviewed" or "Pinned";
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
