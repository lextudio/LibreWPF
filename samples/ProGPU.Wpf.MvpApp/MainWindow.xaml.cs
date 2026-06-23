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
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ProGPU.Wpf.MvpApp;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand RefreshStatusCommand =
        new("Refresh status", nameof(RefreshStatusCommand), typeof(MainWindow));

    internal int EditorPasswordChangedCount { get; private set; }

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

    private void OnBindingGroupCommitClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.BindingGroupStatus = BindingGroupPanel.BindingGroup?.CommitEdit() == true
                ? "Group committed"
                : "Group has validation errors";
        }
    }

    private void OnEditorPasswordChanged(object sender, RoutedEventArgs e)
    {
        EditorPasswordChangedCount++;
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
    private string _validationText = "valid: ready";
    private string _bindingGroupFirstName = "group: Ada";
    private string _bindingGroupLastName = "group: Lovelace";
    private string _bindingGroupStatus = "Group ready";
    private string? _nullDisplayText;

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

    public string ValidationText
    {
        get => _validationText;
        set => SetField(ref _validationText, value);
    }

    public string BindingGroupFirstName
    {
        get => _bindingGroupFirstName;
        set => SetField(ref _bindingGroupFirstName, value);
    }

    public string BindingGroupLastName
    {
        get => _bindingGroupLastName;
        set => SetField(ref _bindingGroupLastName, value);
    }

    public string BindingGroupStatus
    {
        get => _bindingGroupStatus;
        set => SetField(ref _bindingGroupStatus, value);
    }

    public string? NullDisplayText
    {
        get => _nullDisplayText;
        set => SetField(ref _nullDisplayText, value);
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
        ValidationText = "valid: ready";
        BindingGroupFirstName = "group: Ada";
        BindingGroupLastName = "group: Lovelace";
        BindingGroupStatus = "Group ready";
        NullDisplayText = null;
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

public sealed class MvpItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ActiveTemplate { get; set; }

    public DataTemplate? InactiveTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item is MvpItem { IsActive: true }
            ? ActiveTemplate
            : InactiveTemplate;
    }
}

public sealed class MvpNonEmptyValidationRule : ValidationRule
{
    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        return value is string text && text.StartsWith("valid:", StringComparison.Ordinal)
            ? ValidationResult.ValidResult
            : new ValidationResult(false, "Value must start with valid:");
    }
}

public sealed class MvpBindingGroupValidationRule : ValidationRule
{
    public string FirstProperty { get; set; } = string.Empty;

    public string SecondProperty { get; set; } = string.Empty;

    public string RequiredPrefix { get; set; } = string.Empty;

    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        if (value is not BindingGroup bindingGroup)
        {
            return new ValidationResult(false, "Expected a BindingGroup value.");
        }

        foreach (object item in bindingGroup.Items)
        {
            if (!HasRequiredPrefix(bindingGroup, item, FirstProperty) ||
                !HasRequiredPrefix(bindingGroup, item, SecondProperty))
            {
                return new ValidationResult(false, $"Grouped values must start with '{RequiredPrefix}'.");
            }
        }

        return ValidationResult.ValidResult;
    }

    private bool HasRequiredPrefix(BindingGroup bindingGroup, object item, string propertyName)
    {
        object value = bindingGroup.GetValue(item, propertyName);
        string text = value?.ToString() ?? string.Empty;
        return text.StartsWith(RequiredPrefix, StringComparison.Ordinal);
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
        var activeItemTemplate = Require<DataTemplate>(
            window.FindResource("MvpActiveItemTemplate"),
            "active selector item DataTemplate");
        var inactiveItemTemplate = Require<DataTemplate>(
            window.FindResource("MvpInactiveItemTemplate"),
            "inactive selector item DataTemplate");
        var itemTemplateSelector = Require<MvpItemTemplateSelector>(
            window.FindResource("MvpItemTemplateSelector"),
            "item template selector");
        var selectorItemContainerStyle = Require<Style>(
            window.FindResource("MvpSelectorItemContainerStyle"),
            "selector item container style");
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
        var itemsList = Require<ListBox>(window.FindName("ItemsList"), "items ListBox");
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
        var priorityBindingText = Require<TextBlock>(
            window.FindName("PriorityBindingText"),
            "priority binding TextBlock");
        var fallbackBindingText = Require<TextBlock>(
            window.FindName("FallbackBindingText"),
            "fallback binding TextBlock");
        var targetNullBindingText = Require<TextBlock>(
            window.FindName("TargetNullBindingText"),
            "target-null binding TextBlock");
        var relativeSelfBindingText = Require<TextBlock>(
            window.FindName("RelativeSelfBindingText"),
            "relative self binding TextBlock");
        var relativeAncestorBorder = Require<Border>(
            window.FindName("RelativeAncestorBorder"),
            "relative ancestor Border");
        var relativeAncestorBindingText = Require<TextBlock>(
            window.FindName("RelativeAncestorBindingText"),
            "relative ancestor binding TextBlock");
        var selectedItemContent = Require<ContentControl>(
            window.FindName("SelectedItemContent"),
            "selected item ContentControl");
        var selectorItemsList = Require<ListBox>(
            window.FindName("SelectorItemsList"),
            "selector items ListBox");
        var templateButton = Require<Button>(window.FindName("TemplateButton"), "template Button");
        var validationTextBox = Require<TextBox>(
            window.FindName("ValidationTextBox"),
            "validation TextBox");
        var validationEchoText = Require<TextBlock>(
            window.FindName("ValidationEchoText"),
            "validation echo TextBlock");
        var bindingGroupPanel = Require<StackPanel>(
            window.FindName("BindingGroupPanel"),
            "BindingGroup panel");
        var bindingGroupFirstBox = Require<TextBox>(
            window.FindName("BindingGroupFirstBox"),
            "BindingGroup first TextBox");
        var bindingGroupLastBox = Require<TextBox>(
            window.FindName("BindingGroupLastBox"),
            "BindingGroup last TextBox");
        var bindingGroupCommitButton = Require<Button>(
            window.FindName("BindingGroupCommitButton"),
            "BindingGroup commit Button");
        var bindingGroupStatusText = Require<TextBlock>(
            window.FindName("BindingGroupStatusText"),
            "BindingGroup status TextBlock");
        var bindingGroupFirstEchoText = Require<TextBlock>(
            window.FindName("BindingGroupFirstEchoText"),
            "BindingGroup first echo TextBlock");
        var bindingGroupLastEchoText = Require<TextBlock>(
            window.FindName("BindingGroupLastEchoText"),
            "BindingGroup last echo TextBlock");
        var loadedStoryboardText = Require<TextBlock>(
            window.FindName("LoadedStoryboardText"),
            "loaded storyboard TextBlock");
        var clickStoryboardButton = Require<Button>(
            window.FindName("ClickStoryboardButton"),
            "click storyboard Button");
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
        var editorPasswordBox = Require<PasswordBox>(
            window.FindName("EditorPasswordBox"),
            "editor PasswordBox");
        var editorRichTextBox = Require<RichTextBox>(
            window.FindName("EditorRichTextBox"),
            "editor RichTextBox");
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
        ValidateTemplateSelector(
            viewModel,
            selectorItemsList,
            activeItemTemplate,
            inactiveItemTemplate,
            itemTemplateSelector,
            selectorItemContainerStyle);
        ValidateTemplateButton(window, templateButton, templateButtonStyle);
        ValidateValidation(window, viewModel, validationTextBox, validationEchoText);
        ValidateBindingGroup(
            window,
            viewModel,
            bindingGroupPanel,
            bindingGroupFirstBox,
            bindingGroupLastBox,
            bindingGroupCommitButton,
            bindingGroupStatusText,
            bindingGroupFirstEchoText,
            bindingGroupLastEchoText);
        ValidateStoryboards(window, loadedStoryboardText, clickStoryboardButton);
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
        ValidateBindingFallbacks(
            window,
            viewModel,
            priorityBindingText,
            fallbackBindingText,
            targetNullBindingText,
            relativeSelfBindingText,
            relativeAncestorBorder,
            relativeAncestorBindingText);
        ValidateItemsContextMenu(window, viewModel, itemsList);
        ValidateNavigation(window, navigationFrame, detailsNavigationButton);
        ValidateEditor(window, editorPasswordBox, editorRichTextBox);

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
        AssertEqual("Validated", priorityBindingText.Text, "updated priority binding selected item text");
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

    private static void ValidateItemsContextMenu(Window window, MainViewModel viewModel, ListBox itemsList)
    {
        var contextMenu = Require<ContextMenu>(itemsList.ContextMenu, "items ContextMenu");
        AssertEqual("ItemsContextMenu", contextMenu.Name, "items ContextMenu name");
        AssertEqual(4, contextMenu.Items.Count, "items ContextMenu item count");

        var addItem = Require<MenuItem>(contextMenu.Items[0], "context add MenuItem");
        var refreshItem = Require<MenuItem>(contextMenu.Items[1], "context refresh MenuItem");
        Require<Separator>(contextMenu.Items[2], "context menu separator");
        var actionsItem = Require<MenuItem>(contextMenu.Items[3], "context actions MenuItem");

        AssertEqual("ContextAddMenuItem", addItem.Name, "context add MenuItem name");
        AssertEqual("_Add item", addItem.Header, "context add MenuItem header");
        AssertEqual("ContextRefreshMenuItem", refreshItem.Name, "context refresh MenuItem name");
        AssertEqual("_Refresh status", refreshItem.Header, "context refresh MenuItem header");
        AssertEqual(MainWindow.RefreshStatusCommand, refreshItem.Command, "context refresh routed command");
        AssertEqual("ContextActionsEnabledMenuItem", actionsItem.Name, "context actions MenuItem name");
        AssertEqual(true, actionsItem.IsCheckable, "context actions checkable state");

        var contextDataContextBinding = Require<Binding>(
            BindingOperations.GetBinding(contextMenu, FrameworkElement.DataContextProperty),
            "context menu DataContext binding");
        var contextDataContextSource = Require<RelativeSource>(
            contextDataContextBinding.RelativeSource,
            "context menu DataContext RelativeSource");
        AssertEqual("PlacementTarget.DataContext", contextDataContextBinding.Path.Path, "context menu DataContext path");
        AssertEqual(RelativeSourceMode.Self, contextDataContextSource.Mode, "context menu DataContext source");

        var addCommandBinding = Require<Binding>(
            BindingOperations.GetBinding(addItem, MenuItem.CommandProperty),
            "context add command binding");
        AssertEqual("AddItemCommand", addCommandBinding.Path.Path, "context add command path");

        var refreshTargetBinding = Require<Binding>(
            BindingOperations.GetBinding(refreshItem, MenuItem.CommandTargetProperty),
            "context refresh command target binding");
        var refreshTargetSource = Require<RelativeSource>(
            refreshTargetBinding.RelativeSource,
            "context refresh command target RelativeSource");
        AssertEqual("PlacementTarget", refreshTargetBinding.Path.Path, "context refresh command target path");
        AssertEqual(
            RelativeSourceMode.FindAncestor,
            refreshTargetSource.Mode,
            "context refresh command target source");
        AssertEqual(
            typeof(ContextMenu),
            refreshTargetSource.AncestorType,
            "context refresh command target ancestor");

        var actionsCheckedBinding = Require<Binding>(
            BindingOperations.GetBinding(actionsItem, MenuItem.IsCheckedProperty),
            "context actions checked binding");
        AssertEqual("ActionsEnabled", actionsCheckedBinding.Path.Path, "context actions checked path");

        contextMenu.PlacementTarget = itemsList;
        UpdateBinding(contextMenu, FrameworkElement.DataContextProperty);
        UpdateBinding(addItem, MenuItem.CommandProperty);
        UpdateBinding(refreshItem, MenuItem.CommandTargetProperty);
        UpdateBinding(actionsItem, MenuItem.IsCheckedProperty);
        DrainDispatcher(window);

        AssertEqual(viewModel, contextMenu.DataContext, "context menu inherited DataContext");
        AssertEqual(viewModel.AddItemCommand, addItem.Command, "context add command resolved command");
        AssertEqual(itemsList, refreshItem.CommandTarget, "context refresh command target");
        AssertEqual(true, actionsItem.IsChecked, "context actions initial checked state");
        AssertEqual(
            true,
            MainWindow.RefreshStatusCommand.CanExecute(null, refreshItem.CommandTarget),
            "context refresh command target CanExecute state");

        int initialCount = viewModel.Items.Count;
        viewModel.NewItemName = "Context added";
        viewModel.SelectedCategory = "Input";
        addItem.Command.Execute(addItem.CommandParameter);
        DrainDispatcher(window);
        AssertEqual(initialCount + 1, viewModel.Items.Count, "context add command item count");
        AssertEqual("Context added", viewModel.SelectedItem?.Name, "context add selected item name");
        AssertEqual("Input", viewModel.SelectedItem?.Category, "context add selected item category");

        actionsItem.IsChecked = false;
        DrainDispatcher(window);
        AssertEqual(false, viewModel.ActionsEnabled, "context actions unchecked view model state");
        actionsItem.IsChecked = true;
        DrainDispatcher(window);
        AssertEqual(true, viewModel.ActionsEnabled, "context actions checked view model state");
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

    private static void ValidateBindingFallbacks(
        Window window,
        MainViewModel viewModel,
        TextBlock priorityText,
        TextBlock fallbackText,
        TextBlock targetNullText,
        TextBlock relativeSelfText,
        Border relativeAncestorBorder,
        TextBlock relativeAncestorText)
    {
        var priorityBinding = Require<PriorityBinding>(
            BindingOperations.GetPriorityBinding(priorityText, TextBlock.TextProperty),
            "MVP PriorityBinding");
        AssertEqual("Priority fallback", priorityBinding.FallbackValue, "PriorityBinding fallback value");
        AssertEqual(2, priorityBinding.Bindings.Count, "PriorityBinding child binding count");
        AssertEqual("MissingPriorityText", GetBindingPath(priorityBinding.Bindings[0]), "PriorityBinding missing child path");
        AssertEqual("SelectedItem.Name", GetBindingPath(priorityBinding.Bindings[1]), "PriorityBinding selected child path");
        Require<PriorityBindingExpression>(
            BindingOperations.GetPriorityBindingExpression(priorityText, TextBlock.TextProperty),
            "MVP PriorityBinding expression");

        var fallbackBinding = Require<Binding>(
            BindingOperations.GetBinding(fallbackText, TextBlock.TextProperty),
            "fallback TextBlock binding");
        AssertEqual("MissingFallbackText", fallbackBinding.Path.Path, "fallback binding path");
        AssertEqual("Fallback binding text", fallbackBinding.FallbackValue, "fallback binding value");

        var targetNullBinding = Require<Binding>(
            BindingOperations.GetBinding(targetNullText, TextBlock.TextProperty),
            "target-null TextBlock binding");
        AssertEqual("NullDisplayText", targetNullBinding.Path.Path, "target-null binding path");
        AssertEqual("Target null text", targetNullBinding.TargetNullValue, "target-null binding value");

        var selfBinding = Require<Binding>(
            BindingOperations.GetBinding(relativeSelfText, TextBlock.TextProperty),
            "relative self binding");
        var selfSource = Require<RelativeSource>(
            selfBinding.RelativeSource,
            "relative self binding source");
        AssertEqual(RelativeSourceMode.Self, selfSource.Mode, "relative self binding mode");
        AssertEqual("Tag", selfBinding.Path.Path, "relative self binding path");

        var ancestorBinding = Require<Binding>(
            BindingOperations.GetBinding(relativeAncestorText, TextBlock.TextProperty),
            "relative ancestor binding");
        var ancestorSource = Require<RelativeSource>(
            ancestorBinding.RelativeSource,
            "relative ancestor binding source");
        AssertEqual(RelativeSourceMode.FindAncestor, ancestorSource.Mode, "relative ancestor binding mode");
        AssertEqual(typeof(Border), ancestorSource.AncestorType, "relative ancestor binding type");
        AssertEqual("Tag", ancestorBinding.Path.Path, "relative ancestor binding path");

        DrainDispatcher(window);
        AssertEqual("Alpha", priorityText.Text, "initial priority binding text");
        AssertEqual("Fallback binding text", fallbackText.Text, "fallback binding text");
        AssertEqual("Target null text", targetNullText.Text, "target-null binding text");
        AssertEqual("Self binding text", relativeSelfText.Text, "relative self binding text");
        AssertEqual("Ancestor binding text", relativeAncestorText.Text, "relative ancestor binding text");

        viewModel.NullDisplayText = "Non-null binding text";
        DrainDispatcher(window);
        AssertEqual("Non-null binding text", targetNullText.Text, "non-null target binding text");
        viewModel.NullDisplayText = null;
        DrainDispatcher(window);
        AssertEqual("Target null text", targetNullText.Text, "restored target-null binding text");

        relativeAncestorBorder.Tag = "Updated ancestor binding text";
        DrainDispatcher(window);
        AssertEqual("Updated ancestor binding text", relativeAncestorText.Text, "updated ancestor binding text");
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

    private static void ValidateTemplateSelector(
        MainViewModel viewModel,
        ListBox selectorItemsList,
        DataTemplate activeTemplate,
        DataTemplate inactiveTemplate,
        MvpItemTemplateSelector selector,
        Style containerStyle)
    {
        AssertEqual(viewModel.Items, selectorItemsList.ItemsSource, "selector ListBox items source");
        AssertEqual(selector, selectorItemsList.ItemTemplateSelector, "selector ListBox template selector");
        AssertEqual(containerStyle, selectorItemsList.ItemContainerStyle, "selector ListBox item container style");
        AssertEqual(typeof(ListBoxItem), containerStyle.TargetType, "selector item container style target type");
        AssertEqual(activeTemplate, selector.ActiveTemplate, "active selector template");
        AssertEqual(inactiveTemplate, selector.InactiveTemplate, "inactive selector template");
        AssertEqual(activeTemplate, selector.SelectTemplate(viewModel.Items[0], selectorItemsList), "active selector result");
        AssertEqual(inactiveTemplate, selector.SelectTemplate(viewModel.Items[1], selectorItemsList), "inactive selector result");
        Require<WrapPanel>(
            selectorItemsList.ItemsPanel.LoadContent(),
            "selector ListBox ItemsPanel root");

        ValidateSelectorTemplate(
            activeTemplate,
            "SelectorActiveNameText",
            "active selector template binding path");
        ValidateSelectorTemplate(
            inactiveTemplate,
            "SelectorInactiveNameText",
            "inactive selector template binding path");
        ValidateSelectorItemContainerStyle(containerStyle);
    }

    private static void ValidateSelectorTemplate(
        DataTemplate template,
        string name,
        string description)
    {
        var root = Require<FrameworkElement>(
            template.LoadContent(),
            description);
        var textBlock = Require<TextBlock>(
            root.FindName(name),
            description);

        AssertEqual("Name", GetTextBindingPath(textBlock), description);
    }

    private static void ValidateSelectorItemContainerStyle(Style style)
    {
        AssertEqual(2, style.Setters.Count, "selector item container setter count");
        var trigger = Require<DataTrigger>(
            style.Triggers[0],
            "selector item container DataTrigger");
        var triggerSetter = Require<Setter>(
            trigger.Setters[0],
            "selector item container trigger setter");

        AssertEqual("IsActive", GetBindingPath(trigger.Binding), "selector item container trigger binding");
        AssertEqual("True", trigger.Value?.ToString(), "selector item container trigger value");
        AssertEqual(FrameworkElement.TagProperty, triggerSetter.Property, "selector item container trigger property");
        AssertEqual("ActiveContainer", triggerSetter.Value, "selector item container trigger value");
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

    private static void ValidateValidation(
        Window window,
        MainViewModel viewModel,
        TextBox textBox,
        TextBlock echoText)
    {
        var binding = Require<Binding>(
            BindingOperations.GetBinding(textBox, TextBox.TextProperty),
            "validation TextBox binding");

        AssertEqual("ValidationText", binding.Path.Path, "validation binding path");
        AssertEqual(BindingMode.TwoWay, binding.Mode, "validation binding mode");
        AssertEqual(UpdateSourceTrigger.Explicit, binding.UpdateSourceTrigger, "validation update trigger");
        AssertEqual(true, binding.NotifyOnValidationError, "validation notification flag");
        AssertEqual(1, binding.ValidationRules.Count, "validation rule count");
        Require<MvpNonEmptyValidationRule>(
            binding.ValidationRules[0],
            "MVP non-empty validation rule");

        var errorTemplate = Require<ControlTemplate>(
            Validation.GetErrorTemplate(textBox),
            "validation error template");
        AssertEqual(
            window.FindResource("MvpValidationErrorTemplate"),
            errorTemplate,
            "validation error template resource");

        DrainDispatcher(window);
        AssertEqual("valid: ready", textBox.Text, "initial validation TextBox text");
        AssertEqual("Current: valid: ready", echoText.Text, "initial validation echo text");

        var bindingExpression = Require<BindingExpression>(
            BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty),
            "validation TextBox binding expression");
        textBox.Text = "invalid";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual("valid: ready", viewModel.ValidationText, "invalid validation leaves source unchanged");
        AssertEqual(true, Validation.GetHasError(textBox), "invalid validation error flag");
        AssertEqual(1, Validation.GetErrors(textBox).Count, "invalid validation error count");
        AssertEqual(
            "Value must start with valid:",
            Validation.GetErrors(textBox)[0].ErrorContent,
            "invalid validation error content");

        textBox.Text = "valid: updated";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual(false, Validation.GetHasError(textBox), "valid validation clears error flag");
        AssertEqual("valid: updated", viewModel.ValidationText, "valid validation updates source");
        AssertEqual("Current: valid: updated", echoText.Text, "updated validation echo text");
    }

    private static void ValidateBindingGroup(
        Window window,
        MainViewModel viewModel,
        StackPanel panel,
        TextBox firstBox,
        TextBox lastBox,
        Button commitButton,
        TextBlock statusText,
        TextBlock firstEchoText,
        TextBlock lastEchoText)
    {
        var bindingGroup = Require<BindingGroup>(panel.BindingGroup, "MVP BindingGroup");
        AssertEqual("MvpBindingGroup", bindingGroup.Name, "BindingGroup name");
        AssertEqual(1, bindingGroup.Items.Count, "BindingGroup item count");
        AssertEqual(viewModel, bindingGroup.Items[0], "BindingGroup source item");
        AssertEqual(1, bindingGroup.ValidationRules.Count, "BindingGroup validation rule count");
        var rule = Require<MvpBindingGroupValidationRule>(
            bindingGroup.ValidationRules[0],
            "MVP BindingGroup validation rule");

        AssertEqual("BindingGroupFirstName", rule.FirstProperty, "BindingGroup first property");
        AssertEqual("BindingGroupLastName", rule.SecondProperty, "BindingGroup last property");
        AssertEqual("group:", rule.RequiredPrefix, "BindingGroup required prefix");
        AssertEqual("BindingGroupFirstName", GetTextBoxBindingPath(firstBox), "BindingGroup first binding path");
        AssertEqual("BindingGroupLastName", GetTextBoxBindingPath(lastBox), "BindingGroup last binding path");

        DrainDispatcher(window);
        AssertEqual("group: Ada", firstBox.Text, "BindingGroup first initial text");
        AssertEqual("group: Lovelace", lastBox.Text, "BindingGroup last initial text");
        AssertEqual("group: Ada", viewModel.BindingGroupFirstName, "BindingGroup first initial source");
        AssertEqual("group: Lovelace", viewModel.BindingGroupLastName, "BindingGroup last initial source");
        AssertEqual("Group ready", statusText.Text, "BindingGroup initial status text");
        AssertEqual("First: group: Ada", firstEchoText.Text, "BindingGroup first initial echo");
        AssertEqual("Last: group: Lovelace", lastEchoText.Text, "BindingGroup last initial echo");
        AssertEqual(false, Validation.GetHasError(panel), "BindingGroup initial error state");
        AssertEqual(true, bindingGroup.ValidateWithoutUpdate(), "BindingGroup initial validation");

        firstBox.Text = "Ada";
        commitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, commitButton));
        DrainDispatcher(window);
        AssertEqual("group: Ada", viewModel.BindingGroupFirstName, "BindingGroup rejected first source");
        AssertEqual("group: Lovelace", viewModel.BindingGroupLastName, "BindingGroup rejected last source");
        AssertEqual(true, Validation.GetHasError(panel), "BindingGroup rejected error state");
        AssertEqual(1, Validation.GetErrors(panel).Count, "BindingGroup rejected error count");
        AssertEqual(
            "Grouped values must start with 'group:'.",
            Validation.GetErrors(panel)[0].ErrorContent,
            "BindingGroup rejected error content");
        AssertEqual("Group has validation errors", statusText.Text, "BindingGroup rejected status");

        firstBox.Text = "group: Grace";
        lastBox.Text = "group: Hopper";
        commitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, commitButton));
        DrainDispatcher(window);
        AssertEqual("group: Grace", viewModel.BindingGroupFirstName, "BindingGroup accepted first source");
        AssertEqual("group: Hopper", viewModel.BindingGroupLastName, "BindingGroup accepted last source");
        AssertEqual(false, Validation.GetHasError(panel), "BindingGroup accepted error state");
        AssertEqual("Group committed", statusText.Text, "BindingGroup accepted status");
        AssertEqual("First: group: Grace", firstEchoText.Text, "BindingGroup first accepted echo");
        AssertEqual("Last: group: Hopper", lastEchoText.Text, "BindingGroup last accepted echo");
    }

    private static void ValidateStoryboards(
        Window window,
        TextBlock loadedText,
        Button clickButton)
    {
        var loadedTrigger = Require<EventTrigger>(
            loadedText.Triggers[0],
            "loaded storyboard EventTrigger");
        AssertEqual(FrameworkElement.LoadedEvent, loadedTrigger.RoutedEvent, "loaded storyboard routed event");
        ValidateStoryboardAction(
            loadedTrigger,
            "LoadedStoryboardText",
            0.42,
            "loaded storyboard");

        var clickTrigger = Require<EventTrigger>(
            clickButton.Triggers[0],
            "click storyboard EventTrigger");
        AssertEqual(Button.ClickEvent, clickTrigger.RoutedEvent, "click storyboard routed event");
        ValidateStoryboardAction(
            clickTrigger,
            "ClickStoryboardButton",
            0.58,
            "click storyboard");

        AssertEqual(1.0, loadedText.Opacity, "loaded storyboard initial opacity");
        AssertEqual(1.0, clickButton.Opacity, "click storyboard initial opacity");
    }

    private static void ValidateStoryboardAction(
        EventTrigger trigger,
        string targetName,
        double targetOpacity,
        string description)
    {
        AssertEqual(1, trigger.Actions.Count, $"{description} action count");
        var beginStoryboard = Require<BeginStoryboard>(
            trigger.Actions[0],
            $"{description} BeginStoryboard");
        var storyboard = Require<Storyboard>(
            beginStoryboard.Storyboard,
            $"{description} Storyboard");
        AssertEqual(1, storyboard.Children.Count, $"{description} animation count");
        var animation = Require<DoubleAnimation>(
            storyboard.Children[0],
            $"{description} DoubleAnimation");

        AssertEqual(targetName, Storyboard.GetTargetName(animation), $"{description} target name");
        AssertEqual("Opacity", Storyboard.GetTargetProperty(animation).Path, $"{description} target property");
        AssertEqual(TimeSpan.Zero, animation.Duration.TimeSpan, $"{description} duration");
        AssertEqual(targetOpacity, animation.To ?? double.NaN, $"{description} target value");
    }

    private static string GetTextBindingPath(TextBlock textBlock)
    {
        return BindingOperations.GetBinding(textBlock, TextBlock.TextProperty)?.Path.Path
            ?? throw new InvalidOperationException($"Expected {textBlock.Name} text to have a Binding.");
    }

    private static string GetTextBoxBindingPath(TextBox textBox)
    {
        return BindingOperations.GetBinding(textBox, TextBox.TextProperty)?.Path.Path
            ?? throw new InvalidOperationException($"Expected {textBox.Name} text to have a Binding.");
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

    private static void ValidateEditor(MainWindow window, PasswordBox passwordBox, RichTextBox richTextBox)
    {
        AssertEqual(16, passwordBox.MaxLength, "editor PasswordBox max length");
        AssertEqual('*', passwordBox.PasswordChar, "editor PasswordBox password char");
        AssertEqual(0, window.EditorPasswordChangedCount, "editor PasswordBox initial changed count");

        passwordBox.Password = "mvp-secret";
        DrainDispatcher(window);
        AssertEqual("mvp-secret", passwordBox.Password, "editor PasswordBox password");
        AssertEqual(10, passwordBox.SecurePassword.Length, "editor PasswordBox secure password length");
        AssertEqual(1, window.EditorPasswordChangedCount, "editor PasswordBox changed count");

        passwordBox.Clear();
        DrainDispatcher(window);
        AssertEqual(string.Empty, passwordBox.Password, "editor PasswordBox cleared password");
        AssertEqual(2, window.EditorPasswordChangedCount, "editor PasswordBox clear changed count");

        var document = Require<FlowDocument>(richTextBox.Document, "editor FlowDocument");
        AssertEqual(new Thickness(6), document.PagePadding, "editor FlowDocument page padding");
        var paragraph = Require<Paragraph>(document.Blocks.FirstBlock, "editor document paragraph");
        var plainRun = FindDirectRun(paragraph, "Editable plain text", "editor plain Run");
        var bold = FindDirectBold(paragraph, "editor Bold inline");
        var boldRun = Require<Run>(bold.Inlines.FirstInline, "editor bold Run");

        AssertEqual("Editable plain text", plainRun.Text, "editor plain Run text");
        AssertEqual("bold text", boldRun.Text, "editor bold Run text");
        var documentText = new TextRange(document.ContentStart, document.ContentEnd).Text;
        AssertContains("Editable plain text", documentText, "editor FlowDocument TextRange plain text");
        AssertContains("bold text", documentText, "editor FlowDocument TextRange bold text");

        richTextBox.Selection.Select(plainRun.ContentStart, plainRun.ContentEnd);
        AssertEqual(true, EditingCommands.ToggleBold.CanExecute(null, richTextBox), "editor RichTextBox ToggleBold CanExecute");
        EditingCommands.ToggleBold.Execute(null, richTextBox);
        AssertEqual(
            FontWeights.Bold,
            richTextBox.Selection.GetPropertyValue(TextElement.FontWeightProperty),
            "editor RichTextBox ToggleBold applied weight");
        EditingCommands.ToggleBold.Execute(null, richTextBox);
        AssertEqual(
            FontWeights.Normal,
            richTextBox.Selection.GetPropertyValue(TextElement.FontWeightProperty),
            "editor RichTextBox ToggleBold restored weight");
    }

    private static void DrainDispatcher(DispatcherObject dispatcherObject)
    {
        dispatcherObject.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ApplicationIdle);
    }

    private static void UpdateBinding(DependencyObject target, DependencyProperty property)
    {
        BindingOperations.GetBindingExpression(target, property)?.UpdateTarget();
    }

    private static Run FindDirectRun(Paragraph paragraph, string text, string description)
    {
        foreach (Inline inline in paragraph.Inlines)
        {
            if (inline is Run run && run.Text == text)
            {
                return run;
            }
        }

        throw new InvalidOperationException($"Expected {description}.");
    }

    private static Bold FindDirectBold(Paragraph paragraph, string description)
    {
        foreach (Inline inline in paragraph.Inlines)
        {
            if (inline is Bold bold)
            {
                return bold;
            }
        }

        throw new InvalidOperationException($"Expected {description}.");
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

    private static void AssertContains(string expectedText, string actualText, string description)
    {
        if (!actualText.Contains(expectedText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected {description} to contain '{expectedText}', but found '{actualText}'.");
        }
    }
}
