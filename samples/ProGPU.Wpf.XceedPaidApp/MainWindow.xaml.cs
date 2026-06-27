using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Xceed.Wpf.DataGrid;

namespace ProGPU.Wpf.XceedPaidApp;

public partial class MainWindow : Window
{
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
        Loaded += OnLoaded;
    }

    internal XceedPaidLicenseStatus LicenseStatus { get; }

    internal XceedPaidViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Status = "Paid Xceed Toolkit/DataGrid loaded";
        ViewModel.Activity.Add("Loaded paid Toolkit Plus, AvalonDock Windows10 theme, and Xceed DataGrid document");
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
}

internal sealed class XceedPaidViewModel : INotifyPropertyChanged
{
    private PaidGridItem _selectedRow = null!;
    private string _status = "Paid Xceed ready";
    private string _lastAction = "Idle";
    private string _filterText = "ProGPU";
    private bool _actionsEnabled = true;
    private int _batchSize = 50;
    private double _scoreBias = 35.0;
    private double _progress = 35.0;
    private bool _isRefreshing;

    internal XceedPaidViewModel(XceedPaidLicenseStatus licenseStatus)
    {
        LicenseStatusText = licenseStatus.DescribePublic();
        PackageStatus = "Toolkit Plus 5.2, DataGrid 7.3, AvalonDock Windows10 theme, Views3D/theme-pack assemblies";
        Rows = CreateRows(100_000);
        _selectedRow = Rows[0];
        Activity =
        [
            "Created 100,000 paid DataGrid rows",
            "Configured explicit Xceed DataGrid columns",
            "Configured Toolkit Plus Material controls"
        ];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PaidGridItem> Rows { get; }

    public ObservableCollection<string> Activity { get; }

    public string LicenseStatusText { get; }

    public string PackageStatus { get; }

    public int RowCount => Rows.Count;

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

    public string FilterText
    {
        get => _filterText;
        set => SetProperty(ref _filterText, value);
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

    public int Id { get; }

    public string Title { get; }

    public string Owner { get; }

    public string Category { get; }

    public string Status { get; }

    public int Score { get; }

    public DateTime Updated { get; }

    public bool Active { get; }
}
