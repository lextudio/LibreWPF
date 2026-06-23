using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        InitializeComponent();

        if (FindResource("ItemsViewSource") is CollectionViewSource itemsViewSource)
        {
            itemsViewSource.Source = viewModel.Items;
        }
    }

    private void OnOverviewNavigationClick(object sender, RoutedEventArgs e)
    {
        NavigationFrame.Navigate(new Uri("OverviewPage.xaml", UriKind.Relative));
    }

    private void OnDetailsNavigationClick(object sender, RoutedEventArgs e)
    {
        NavigationFrame.Navigate(new Uri("DetailsPage.xaml", UriKind.Relative));
    }

    private void OnItemsViewSourceFilter(object sender, FilterEventArgs e)
    {
        e.Accepted = DataContext is not MainViewModel { ShowActiveOnly: true }
            || e.Item is MvpItem { IsActive: true };
    }

    private void OnActiveOnlyFilterChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && DataContext is MainViewModel viewModel)
        {
            viewModel.ShowActiveOnly = checkBox.IsChecked == true;
        }

        RefreshItemsView();
    }

    private void RefreshItemsView()
    {
        if (FindResource("ItemsViewSource") is CollectionViewSource itemsViewSource)
        {
            itemsViewSource.View?.Refresh();
        }
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
    private bool _showActiveOnly;
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

    public bool ShowActiveOnly
    {
        get => _showActiveOnly;
        set => SetField(ref _showActiveOnly, value);
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
        ShowActiveOnly = false;
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

public sealed class MvpActiveTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool active && active ? "Active" : "Inactive";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class MvpItemSummaryConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string name = values.Length > 0 && values[0] is string itemName ? itemName : "None";
        string category = values.Length > 1 && values[1] is string itemCategory ? itemCategory : "Uncategorized";
        double progress = values.Length > 2 && values[2] is double itemProgress ? itemProgress : 0.0;

        return string.Create(
            culture,
            $"{name} / {category} / {progress:0}%");
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
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
        AssertEqual(true, themeResources.Contains("SelectedItemTemplate"), "app theme selected item template key");
        AssertEqual(true, themeResources.Contains("MvpTemplateButtonStyle"), "app theme template Button style key");
        var panelBrush = Require<SolidColorBrush>(window.FindResource("MvpPanelBrush"), "MVP panel brush");
        var buttonStyle = Require<Style>(application.TryFindResource(typeof(Button)), "app Button style");
        var templateButtonStyle = Require<Style>(
            application.TryFindResource("MvpTemplateButtonStyle"),
            "template Button style");
        var activeTextConverter = Require<MvpActiveTextConverter>(
            window.FindResource("MvpActiveTextConverter"),
            "active text converter");
        var itemSummaryConverter = Require<MvpItemSummaryConverter>(
            window.FindResource("MvpItemSummaryConverter"),
            "item summary converter");
        var selectedItemTemplate = Require<DataTemplate>(
            application.TryFindResource("SelectedItemTemplate"),
            "selected item DataTemplate");
        AssertEqual(Color.FromRgb(0xF4, 0xF7, 0xFB), panelBrush.Color, "MVP panel brush color");
        AssertEqual(typeof(Button), buttonStyle.TargetType, "app Button implicit style target type");
        AssertEqual(typeof(Button), templateButtonStyle.TargetType, "template Button style target type");
        AssertEqual(typeof(MvpItem), selectedItemTemplate.DataType, "selected item template data type");
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
        var selectedItemSummaryText = Require<TextBlock>(
            window.FindName("SelectedItemSummaryText"),
            "selected item summary TextBlock");
        var activeOnlyCheckBox = Require<CheckBox>(
            window.FindName("ActiveOnlyCheckBox"),
            "active-only CheckBox");
        var groupedItemsList = Require<ListBox>(
            window.FindName("GroupedItemsList"),
            "grouped items ListBox");
        var selectedItemContent = Require<ContentControl>(
            window.FindName("SelectedItemContent"),
            "selected item ContentControl");
        var templateButton = Require<Button>(window.FindName("TemplateButton"), "template Button");
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
        ValidateCollectionView(window, viewModel, groupedItemsList, activeOnlyCheckBox, activeTextConverter);
        ValidateSelectedSummaryBinding(selectedItemSummaryText, itemSummaryConverter);
        AssertEqual(viewModel.SelectedItem, selectedItemContent.Content, "selected item content");
        AssertEqual(
            selectedItemTemplate,
            selectedItemContent.ContentTemplate,
            "selected item content template");
        ValidateSelectedItemTemplate(selectedItemTemplate);
        ValidateTemplateButton(window, templateButton, templateButtonStyle);
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
        AssertEqual("Alpha / Framework / 35%", selectedItemSummaryText.Text, "initial selected summary text");
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
        AssertEqual("Validated / Input / 72%", selectedItemSummaryText.Text, "updated selected summary text");
    }

    private static void ValidateCollectionView(
        Window window,
        MainViewModel viewModel,
        ListBox groupedItemsList,
        CheckBox activeOnlyCheckBox,
        MvpActiveTextConverter activeTextConverter)
    {
        var itemsViewSource = Require<CollectionViewSource>(
            window.FindResource("ItemsViewSource"),
            "items CollectionViewSource");
        AssertEqual(viewModel.Items, itemsViewSource.Source, "CollectionViewSource source");
        AssertEqual(2, itemsViewSource.SortDescriptions.Count, "CollectionViewSource sort count");
        AssertEqual("Category", itemsViewSource.SortDescriptions[0].PropertyName, "first sort property");
        AssertEqual(ListSortDirection.Ascending, itemsViewSource.SortDescriptions[0].Direction, "first sort direction");
        AssertEqual("Name", itemsViewSource.SortDescriptions[1].PropertyName, "second sort property");
        AssertEqual(1, itemsViewSource.GroupDescriptions.Count, "CollectionViewSource group count");
        var groupDescription = Require<PropertyGroupDescription>(
            itemsViewSource.GroupDescriptions[0],
            "items PropertyGroupDescription");
        AssertEqual("Category", groupDescription.PropertyName, "items group property");
        AssertEqual(false, activeOnlyCheckBox.IsChecked == true, "active-only initial check state");
        AssertEqual(false, viewModel.ShowActiveOnly, "active-only initial view model state");
        AssertEqual(itemsViewSource.View, groupedItemsList.ItemsSource, "grouped ListBox ItemsSource view");
        ValidateGroupedItemTemplate(groupedItemsList.ItemTemplate, activeTextConverter);

        var initialItems = CopyItems(itemsViewSource.View);
        AssertEqual(2, initialItems.Count, "initial collection view item count");
        AssertEqual("Alpha", initialItems[0].Name, "initial collection view first item");
        AssertEqual("Beta", initialItems[1].Name, "initial collection view second item");
        AssertEqual(2, itemsViewSource.View.Groups?.Count ?? -1, "initial collection view group count");
        var firstGroup = Require<CollectionViewGroup>(
            itemsViewSource.View.Groups?[0],
            "first collection view group");
        AssertEqual("Framework", firstGroup.Name, "first collection view group name");

        activeOnlyCheckBox.IsChecked = true;
        DrainDispatcher(window);
        var filteredItems = CopyItems(itemsViewSource.View);
        AssertEqual(true, viewModel.ShowActiveOnly, "active-only checked view model state");
        AssertEqual(1, filteredItems.Count, "filtered collection view item count");
        AssertEqual("Alpha", filteredItems[0].Name, "filtered collection view first item");

        activeOnlyCheckBox.IsChecked = false;
        DrainDispatcher(window);
        var restoredItems = CopyItems(itemsViewSource.View);
        AssertEqual(false, viewModel.ShowActiveOnly, "active-only restored view model state");
        AssertEqual(2, restoredItems.Count, "restored collection view item count");
    }

    private static void ValidateGroupedItemTemplate(DataTemplate template, MvpActiveTextConverter converter)
    {
        var root = Require<FrameworkElement>(
            template.LoadContent(),
            "grouped item template root");
        var activeText = Require<TextBlock>(
            root.FindName("GroupedItemActiveText"),
            "grouped item active TextBlock");
        var binding = Require<Binding>(
            BindingOperations.GetBinding(activeText, TextBlock.TextProperty),
            "grouped item active binding");

        AssertEqual("IsActive", binding.Path.Path, "grouped item active binding path");
        AssertEqual(converter, binding.Converter, "grouped item active converter");
        AssertEqual("Active", converter.Convert(true, typeof(string), null!, CultureInfo.InvariantCulture), "active converter true text");
        AssertEqual("Inactive", converter.Convert(false, typeof(string), null!, CultureInfo.InvariantCulture), "active converter false text");
    }

    private static void ValidateSelectedSummaryBinding(TextBlock textBlock, MvpItemSummaryConverter converter)
    {
        var binding = Require<MultiBinding>(
            BindingOperations.GetMultiBinding(textBlock, TextBlock.TextProperty),
            "selected summary MultiBinding");

        AssertEqual(converter, binding.Converter, "selected summary converter");
        AssertEqual(3, binding.Bindings.Count, "selected summary binding count");
        AssertEqual("SelectedItem.Name", GetBindingPath(binding.Bindings[0]), "selected summary name path");
        AssertEqual("SelectedItem.Category", GetBindingPath(binding.Bindings[1]), "selected summary category path");
        AssertEqual("Progress", GetBindingPath(binding.Bindings[2]), "selected summary progress path");
    }

    private static List<MvpItem> CopyItems(IEnumerable source)
    {
        var items = new List<MvpItem>();
        foreach (object? item in source)
        {
            items.Add(Require<MvpItem>(item, "collection view item"));
        }

        return items;
    }

    private static string GetColumnBindingPath(DataGridColumn column)
    {
        return column is DataGridBoundColumn { Binding: Binding binding }
            ? binding.Path.Path
            : throw new InvalidOperationException($"Expected {column.Header} column to have a Binding.");
    }

    private static string GetBindingPath(BindingBase binding)
    {
        return binding is Binding { Path: { } path }
            ? path.Path
            : throw new InvalidOperationException("Expected a standard Binding with a path.");
    }

    private static void ValidateSelectedItemTemplate(DataTemplate template)
    {
        var root = Require<FrameworkElement>(
            template.LoadContent(),
            "selected item template root");
        var nameText = Require<TextBlock>(
            root.FindName("TemplateNameText"),
            "selected item template name TextBlock");
        var categoryText = Require<TextBlock>(
            root.FindName("TemplateCategoryText"),
            "selected item template category TextBlock");
        var activeText = Require<TextBlock>(
            root.FindName("TemplateActiveText"),
            "selected item template active TextBlock");

        AssertEqual("Name", GetTextBindingPath(nameText), "selected item template name binding path");
        AssertEqual("Category", GetTextBindingPath(categoryText), "selected item template category binding path");
        AssertEqual("IsActive", GetTextBindingPath(activeText), "selected item template active binding path");
    }

    private static void ValidateTemplateButton(Window window, Button button, Style style)
    {
        AssertEqual(style, button.Style, "template Button style");
        AssertEqual("Templated action", button.Content, "template Button content");

        button.ApplyTemplate();
        DrainDispatcher(window);
        var template = Require<ControlTemplate>(button.Template, "template Button ControlTemplate");
        var border = Require<Border>(
            template.FindName("TemplateBorder", button),
            "template Button border part");
        var contentPresenter = Require<ContentPresenter>(
            template.FindName("TemplateContentPresenter", button),
            "template Button content presenter part");

        AssertEqual(typeof(Button), template.TargetType, "template Button target type");
        AssertEqual(button.Background, border.Background, "template Button background TemplateBinding");
        AssertEqual("Templated action", contentPresenter.Content, "template Button content TemplateBinding");
        AssertEqual(1.0, border.Opacity, "template Button enabled opacity");

        button.IsEnabled = false;
        DrainDispatcher(window);
        AssertEqual(0.45, border.Opacity, "template Button disabled trigger opacity");
        button.IsEnabled = true;
        DrainDispatcher(window);
        AssertEqual(1.0, border.Opacity, "template Button restored opacity");
    }

    private static string GetTextBindingPath(TextBlock textBlock)
    {
        return BindingOperations.GetBinding(textBlock, TextBlock.TextProperty)?.Path.Path
            ?? throw new InvalidOperationException($"Expected {textBlock.Name} text to have a Binding.");
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
