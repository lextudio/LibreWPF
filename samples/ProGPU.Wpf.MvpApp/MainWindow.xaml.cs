using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProGPU.Wpf.MvpApp;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand RefreshStatusCommand =
        new("Refresh status", nameof(RefreshStatusCommand), typeof(MainWindow));

    public MainWindow()
    {
        DataContext = new MainViewModel();
        InitializeComponent();
    }

    private void OnOverviewNavigationClick(object sender, RoutedEventArgs e)
    {
        NavigationFrame.Navigate(new Uri("OverviewPage.xaml", UriKind.Relative));
    }

    private void OnDetailsNavigationClick(object sender, RoutedEventArgs e)
    {
        NavigationFrame.Navigate(new Uri("DetailsPage.xaml", UriKind.Relative));
    }

    private void OnRefreshStatusCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = DataContext is MainViewModel { ActionsEnabled: true };
        e.Handled = true;
    }

    private void OnRefreshStatusCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.RefreshCommandStatus();
        }

        e.Handled = true;
    }
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _newItemName = "Gamma";
    private MvpItem? _selectedItem;
    private string _selectedCategory = "Framework";
    private bool _actionsEnabled = true;
    private double _progress = 35.0;
    private int _refreshCount;

    public MainViewModel()
    {
        Items =
        [
            new MvpItem("Alpha", "Framework", true),
            new MvpItem("Beta", "Rendering", false)
        ];
        Categories = ["Framework", "Rendering", "Input"];
        Nodes =
        [
            new MvpNode(
                "Application",
                "WPF",
                new MvpNode("Startup", "Lifecycle"),
                new MvpNode("Resources", "XAML")),
            new MvpNode(
                "Platform",
                "ProGPU",
                new MvpNode("Window", "Silk.NET"),
                new MvpNode("Rendering", "WebGPU"))
        ];
        _selectedItem = Items[0];
        AddItemCommand = new RelayCommand(AddItem, () => ActionsEnabled);
        ResetCommand = new RelayCommand(Reset);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MvpItem> Items { get; }

    public ObservableCollection<string> Categories { get; }

    public ObservableCollection<MvpNode> Nodes { get; }

    public ICommand AddItemCommand { get; }

    public ICommand ResetCommand { get; }

    public string NewItemName
    {
        get => _newItemName;
        set => SetField(ref _newItemName, value);
    }

    public MvpItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set => SetField(ref _selectedCategory, value);
    }

    public bool ActionsEnabled
    {
        get => _actionsEnabled;
        set
        {
            if (SetField(ref _actionsEnabled, value) && AddItemCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (SetField(ref _progress, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => SelectedItem == null
        ? $"Progress {Progress:0}%"
        : $"{SelectedItem.Name} selected, progress {Progress:0}%";

    public int RefreshCount
    {
        get => _refreshCount;
        private set
        {
            if (SetField(ref _refreshCount, value))
            {
                OnPropertyChanged(nameof(CommandStatusText));
            }
        }
    }

    public string CommandStatusText => RefreshCount == 0
        ? "Commands idle"
        : $"Refresh command {RefreshCount}";

    public void RefreshCommandStatus()
    {
        RefreshCount++;
    }

    private void AddItem()
    {
        string name = string.IsNullOrWhiteSpace(NewItemName)
            ? $"Item {Items.Count + 1}"
            : NewItemName.Trim();
        var item = new MvpItem(name, SelectedCategory, true);
        Items.Add(item);
        SelectedItem = item;
        NewItemName = string.Empty;
    }

    private void Reset()
    {
        Items.Clear();
        Items.Add(new MvpItem("Alpha", "Framework", true));
        Items.Add(new MvpItem("Beta", "Rendering", false));
        SelectedItem = Items[0];
        SelectedCategory = Categories[0];
        NewItemName = "Gamma";
        Progress = 35.0;
        ActionsEnabled = true;
        RefreshCount = 0;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record MvpItem(string Name, string Category, bool IsActive);

public sealed class MvpNode
{
    public MvpNode(string name, string kind, params MvpNode[] children)
    {
        Name = name;
        Kind = kind;
        Children = new ObservableCollection<MvpNode>(children);
    }

    public string Name { get; }

    public string Kind { get; }

    public ObservableCollection<MvpNode> Children { get; }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public void Execute(object? parameter)
    {
        _execute();
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal static class MvpSelfTest
{
    public static void Validate(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var viewModel = window.DataContext as MainViewModel
            ?? throw new InvalidOperationException("Expected MVP DataContext.");
        var application = Application.Current
            ?? throw new InvalidOperationException("Expected current Application.");
        var themeResources = Require<ResourceDictionary>(
            application.Resources.MergedDictionaries.Count > 0
                ? application.Resources.MergedDictionaries[0]
                : null,
            "app merged theme ResourceDictionary");
        AssertEqual(true, themeResources.Contains("MvpPanelBrush"), "app theme panel brush key");
        AssertEqual(true, themeResources.Contains(typeof(Button)), "app theme implicit Button style key");
        var panelBrush = Require<SolidColorBrush>(window.FindResource("MvpPanelBrush"), "MVP panel brush");
        var buttonStyle = Require<Style>(application.TryFindResource(typeof(Button)), "app Button style");
        AssertEqual(Color.FromRgb(0xF4, 0xF7, 0xFB), panelBrush.Color, "MVP panel brush color");
        AssertEqual(typeof(Button), buttonStyle.TargetType, "app Button implicit style target type");
        var mainMenu = Require<Menu>(window.FindName("MainMenu"), "main Menu");
        var fileMenuItem = Require<MenuItem>(window.FindName("FileMenuItem"), "file MenuItem");
        var viewMenuItem = Require<MenuItem>(window.FindName("ViewMenuItem"), "view MenuItem");
        var addMenuItem = Require<MenuItem>(window.FindName("AddMenuItem"), "add MenuItem");
        var resetMenuItem = Require<MenuItem>(window.FindName("ResetMenuItem"), "reset MenuItem");
        var refreshMenuItem = Require<MenuItem>(window.FindName("RefreshMenuItem"), "refresh MenuItem");
        var actionsEnabledMenuItem = Require<MenuItem>(
            window.FindName("ActionsEnabledMenuItem"),
            "actions enabled MenuItem");
        var commandStatusText = Require<TextBlock>(
            window.FindName("CommandStatusText"),
            "command status TextBlock");
        Require<TextBox>(window.FindName("NameTextBox"), "name TextBox");
        Require<ListBox>(window.FindName("ItemsList"), "items ListBox");
        var itemsDataGrid = Require<DataGrid>(window.FindName("ItemsDataGrid"), "items DataGrid");
        var summaryPanel = Require<SummaryPanel>(window.FindName("SummaryPanel"), "summary Panel");
        var summaryNameText = Require<TextBlock>(
            summaryPanel.FindName("SummaryNameText"),
            "summary name text");
        var summaryCategoryText = Require<TextBlock>(
            summaryPanel.FindName("SummaryCategoryText"),
            "summary category text");
        var summaryProgressText = Require<TextBlock>(
            summaryPanel.FindName("SummaryProgressText"),
            "summary progress text");
        var nodesTreeView = Require<TreeView>(window.FindName("NodesTreeView"), "nodes TreeView");
        var navigationFrame = Require<Frame>(window.FindName("NavigationFrame"), "navigation Frame");
        var detailsNavigationButton = Require<Button>(
            window.FindName("DetailsNavigationButton"),
            "details navigation Button");
        Require<CheckBox>(window.FindName("EnabledCheckBox"), "enabled CheckBox");
        Require<Slider>(window.FindName("ProgressSlider"), "progress Slider");
        Require<ComboBox>(window.FindName("CategoryCombo"), "category ComboBox");
        AssertEqual(2, mainMenu.Items.Count, "main menu item count");
        AssertEqual(3, fileMenuItem.Items.Count, "file menu item count");
        AssertEqual(3, viewMenuItem.Items.Count, "view menu item count");
        AssertEqual(viewModel.AddItemCommand, addMenuItem.Command, "add menu command binding");
        AssertEqual("Ctrl+N", addMenuItem.InputGestureText, "add menu input gesture text");
        AssertEqual(viewModel.ResetCommand, resetMenuItem.Command, "reset menu command binding");
        AssertEqual(MainWindow.RefreshStatusCommand, refreshMenuItem.Command, "refresh menu routed command");
        AssertEqual("Ctrl+R", refreshMenuItem.InputGestureText, "refresh menu input gesture text");
        AssertEqual(1, window.CommandBindings.Count, "window command binding count");
        AssertEqual(MainWindow.RefreshStatusCommand, window.CommandBindings[0].Command, "window routed command binding");
        AssertEqual(1, window.InputBindings.Count, "window input binding count");
        var refreshKeyBinding = Require<KeyBinding>(window.InputBindings[0], "refresh KeyBinding");
        AssertEqual(Key.R, refreshKeyBinding.Key, "refresh key binding key");
        AssertEqual(ModifierKeys.Control, refreshKeyBinding.Modifiers, "refresh key binding modifiers");
        AssertEqual(MainWindow.RefreshStatusCommand, refreshKeyBinding.Command, "refresh key binding command");
        AssertEqual(true, actionsEnabledMenuItem.IsCheckable, "actions menu checkable state");
        AssertEqual(true, actionsEnabledMenuItem.IsChecked, "actions menu initial checked state");
        AssertEqual(viewModel.Items, itemsDataGrid.ItemsSource, "DataGrid items source");
        AssertEqual(3, itemsDataGrid.Columns.Count, "DataGrid column count");
        AssertEqual("Name", GetColumnBindingPath(itemsDataGrid.Columns[0]), "DataGrid name column binding");
        AssertEqual("Category", GetColumnBindingPath(itemsDataGrid.Columns[1]), "DataGrid category column binding");
        AssertEqual("IsActive", GetColumnBindingPath(itemsDataGrid.Columns[2]), "DataGrid active column binding");
        AssertEqual(viewModel.Nodes, nodesTreeView.ItemsSource, "TreeView items source");
        AssertEqual(2, viewModel.Nodes.Count, "TreeView root node count");
        AssertEqual("Startup", viewModel.Nodes[0].Children[0].Name, "TreeView first child node");
        var nodeTemplate = Require<HierarchicalDataTemplate>(
            nodesTreeView.ItemTemplate,
            "node hierarchical data template");
        AssertEqual("Children", GetTemplateItemsSourcePath(nodeTemplate), "TreeView hierarchical template ItemsSource path");
        DrainDispatcher(window);
        AssertEqual("Commands idle", commandStatusText.Text, "initial command status text");
        AssertEqual("Name: Alpha", summaryNameText.Text, "summary initial name text");
        AssertEqual("Category: Framework", summaryCategoryText.Text, "summary initial category text");
        AssertEqual("Progress: 35%", summaryProgressText.Text, "summary initial progress text");
        ValidateNavigation(window, navigationFrame, detailsNavigationButton);

        AssertEqual(true, MainWindow.RefreshStatusCommand.CanExecute(null, window), "refresh command initial CanExecute state");
        MainWindow.RefreshStatusCommand.Execute(null, window);
        DrainDispatcher(window);
        AssertEqual(1, viewModel.RefreshCount, "refresh command execution count");
        AssertEqual("Refresh command 1", commandStatusText.Text, "refreshed command status text");

        int initialCount = viewModel.Items.Count;
        viewModel.NewItemName = "Validated";
        viewModel.SelectedCategory = "Input";
        addMenuItem.Command.Execute(addMenuItem.CommandParameter);

        AssertEqual(initialCount + 1, viewModel.Items.Count, "added item count");
        AssertEqual("Validated", viewModel.SelectedItem?.Name, "selected item name");
        AssertEqual("Input", viewModel.SelectedItem?.Category, "selected item category");
        AssertEqual(true, viewModel.SelectedItem?.IsActive ?? false, "selected item active state");
        actionsEnabledMenuItem.IsChecked = false;
        AssertEqual(false, viewModel.ActionsEnabled, "actions menu unchecked view model state");
        AssertEqual(false, MainWindow.RefreshStatusCommand.CanExecute(null, window), "refresh command disabled CanExecute state");
        actionsEnabledMenuItem.IsChecked = true;
        AssertEqual(true, viewModel.ActionsEnabled, "actions menu checked view model state");
        AssertEqual(true, MainWindow.RefreshStatusCommand.CanExecute(null, window), "refresh command reenabled CanExecute state");

        viewModel.Progress = 72.0;
        DrainDispatcher(window);
        AssertEqual("Validated selected, progress 72%", viewModel.StatusText, "status text");
        AssertEqual("Name: Validated", summaryNameText.Text, "summary updated name text");
        AssertEqual("Category: Input", summaryCategoryText.Text, "summary updated category text");
        AssertEqual("Progress: 72%", summaryProgressText.Text, "summary updated progress text");
    }

    private static string GetColumnBindingPath(DataGridColumn column)
    {
        return column is DataGridBoundColumn { Binding: Binding binding }
            ? binding.Path.Path
            : throw new InvalidOperationException($"Expected {column.Header} column to have a Binding.");
    }

    private static string GetTemplateItemsSourcePath(HierarchicalDataTemplate template)
    {
        return template.ItemsSource is Binding binding
            ? binding.Path.Path
            : throw new InvalidOperationException("Expected hierarchical data template to bind ItemsSource.");
    }

    private static void ValidateNavigation(Window window, Frame frame, Button detailsButton)
    {
        DrainDispatcher(window);
        var overviewPage = Require<OverviewPage>(frame.Content, "initial overview page");
        var overviewTitle = Require<TextBlock>(
            overviewPage.FindName("OverviewTitle"),
            "overview page title");
        AssertEqual("SDK overview page", overviewTitle.Text, "overview page title text");

        detailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, detailsButton));
        DrainDispatcher(window);
        var detailsPage = Require<DetailsPage>(frame.Content, "navigated details page");
        var detailsTitle = Require<TextBlock>(
            detailsPage.FindName("DetailsTitle"),
            "details page title");
        var detailsList = Require<ListBox>(
            detailsPage.FindName("DetailsList"),
            "details page list");
        AssertEqual("SDK details page", detailsTitle.Text, "details page title text");
        AssertEqual(3, detailsList.Items.Count, "details page list item count");
        AssertEqual(new Uri("DetailsPage.xaml", UriKind.Relative), frame.Source, "navigation frame source");
        AssertEqual(true, frame.CanGoBack, "navigation frame back stack state");
    }

    private static void DrainDispatcher(DispatcherObject dispatcherObject)
    {
        dispatcherObject.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ApplicationIdle);
    }

    private static T Require<T>(object? value, string description)
    {
        return value is T typed
            ? typed
            : throw new InvalidOperationException($"Expected {description} to be {typeof(T).Name}.");
    }

    private static void AssertEqual<T>(T expected, T actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expected}', but found '{actual}'.");
        }
    }
}
