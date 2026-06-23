using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Navigation;
using System.Windows.Threading;
using WpfCalendar = System.Windows.Controls.Calendar;

namespace ProGPU.Wpf.MvpApp;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand RefreshStatusCommand =
        new("Refresh status", nameof(RefreshStatusCommand), typeof(MainWindow));

    internal int EditorPasswordChangedCount { get; private set; }

    internal int DataObjectRoundTripCount { get; private set; }

    internal string? LastDataObjectText { get; private set; }

    internal string? LastDataObjectCustomText { get; private set; }

    internal int SelectorSelectionChangedCount { get; private set; }

    internal int MultiSelectorSelectionChangedCount { get; private set; }

    internal int SelectorExpanderExpandedCount { get; private set; }

    internal int SelectorExpanderCollapsedCount { get; private set; }

    internal int InputToggleCheckedCount { get; private set; }

    internal int InputToggleUncheckedCount { get; private set; }

    internal int CategoryRadioCheckedCount { get; private set; }

    internal string? LastCategoryRadioName { get; private set; }

    internal int InputRepeatButtonClickCount { get; private set; }

    internal int InputDateSelectionChangedCount { get; private set; }

    internal string? LastDateSelectionSenderName { get; private set; }

    internal int MvpRoutedEventSourceCount { get; private set; }

    internal int MvpRoutedEventScopeCount { get; private set; }

    internal int MvpRoutedEventHandledTooCount { get; private set; }

    internal string? LastMvpRoutedEventSenderName { get; private set; }

    internal string? LastMvpRoutedEventOriginalSourceName { get; private set; }

    internal string? LastMvpRoutedEventPayload { get; private set; }

    internal string? LastMvpRoutedEventName { get; private set; }

    internal int MvpStyleEventSetterClickCount { get; private set; }

    internal string? LastMvpStyleEventSetterSenderName { get; private set; }

    internal string? LastMvpStyleEventSetterRoutedEventName { get; private set; }

    internal int DocumentLinkRequestNavigateCount { get; private set; }

    internal string? LastDocumentLinkRequestNavigateText { get; private set; }

    internal string? LastDocumentLinkRequestNavigateUri { get; private set; }

    internal string? LastDocumentLinkRequestNavigateRoutedEventName { get; private set; }

    internal int MvpTabSelectionChangedCount { get; private set; }

    internal string? LastMvpTabHeader { get; private set; }

    internal int ExplicitExplorerTreeExpandedCount { get; private set; }

    internal int ExplicitExplorerTreeCollapsedCount { get; private set; }

    internal int ExplicitExplorerTreeSelectedCount { get; private set; }

    internal int ExplicitExplorerTreeUnselectedCount { get; private set; }

    internal string? LastExplicitExplorerTreeSenderName { get; private set; }

    internal string? LastExplicitExplorerTreeRoutedEventName { get; private set; }

    internal string? LastExplicitExplorerTreeHeader { get; private set; }

    public MainWindow()
    {
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        InitializeComponent();

        MvpRoutedEventScope.AddHandler(
            MvpRoutedEventButton.MvpActivatedEvent,
            new MvpRoutedEventHandler(OnMvpRoutedEventScopeHandledToo),
            handledEventsToo: true);

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

    private void OnAboutMenuItemClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutWindow();
        if (IsVisible)
        {
            dialog.Owner = this;
        }

        dialog.ShowDialog();
    }

    private void OnDocumentLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        DocumentLinkRequestNavigateCount++;
        LastDocumentLinkRequestNavigateText = sender is Hyperlink link
            ? new TextRange(link.ContentStart, link.ContentEnd).Text.Trim()
            : sender.GetType().Name;
        LastDocumentLinkRequestNavigateUri = e.Uri?.ToString();
        LastDocumentLinkRequestNavigateRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
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

    private void OnDataObjectRoundTripClick(object sender, RoutedEventArgs e)
    {
        var payload = DataObjectPayloadTextBox.Text;
        var dataObject = new DataObject();
        dataObject.SetText(payload);
        dataObject.SetData("ProGPU.Wpf.MvpApp.CustomText", $"custom:{payload}");

        LastDataObjectText = dataObject.GetData(DataFormats.UnicodeText)?.ToString();
        LastDataObjectCustomText = dataObject.GetData("ProGPU.Wpf.MvpApp.CustomText")?.ToString();
        DataObjectRoundTripCount++;
        DataObjectStatusText.Text = $"{LastDataObjectText} | {LastDataObjectCustomText}";
        e.Handled = true;
    }

    private void OnSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectorSelectionChangedCount++;
    }

    private void OnMvpTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource))
        {
            return;
        }

        MvpTabSelectionChangedCount++;
        LastMvpTabHeader = sender is TabControl { SelectedItem: TabItem { Header: object header } }
            ? header.ToString()
            : null;
    }

    private void OnMultiSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MultiSelectorSelectionChangedCount++;
    }

    private void OnSelectorExpanderExpanded(object sender, RoutedEventArgs e)
    {
        SelectorExpanderExpandedCount++;
    }

    private void OnSelectorExpanderCollapsed(object sender, RoutedEventArgs e)
    {
        SelectorExpanderCollapsedCount++;
    }

    private void OnInputToggleChecked(object sender, RoutedEventArgs e)
    {
        InputToggleCheckedCount++;
    }

    private void OnInputToggleUnchecked(object sender, RoutedEventArgs e)
    {
        InputToggleUncheckedCount++;
    }

    private void OnCategoryRadioChecked(object sender, RoutedEventArgs e)
    {
        CategoryRadioCheckedCount++;
        LastCategoryRadioName = (sender as FrameworkElement)?.Name;

        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { Tag: string category })
        {
            viewModel.SelectedCategory = category;
        }
    }

    private void OnInputRepeatButtonClick(object sender, RoutedEventArgs e)
    {
        InputRepeatButtonClickCount++;
    }

    private void OnInputDateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        InputDateSelectionChangedCount++;
        LastDateSelectionSenderName = (sender as FrameworkElement)?.Name;
    }

    private void OnExplicitExplorerTreeExpanded(object sender, RoutedEventArgs e)
    {
        ExplicitExplorerTreeExpandedCount++;
        RecordExplicitExplorerTreeEvent(sender, e);
    }

    private void OnExplicitExplorerTreeCollapsed(object sender, RoutedEventArgs e)
    {
        ExplicitExplorerTreeCollapsedCount++;
        RecordExplicitExplorerTreeEvent(sender, e);
    }

    private void OnExplicitExplorerTreeSelected(object sender, RoutedEventArgs e)
    {
        ExplicitExplorerTreeSelectedCount++;
        RecordExplicitExplorerTreeEvent(sender, e);
    }

    private void OnExplicitExplorerTreeUnselected(object sender, RoutedEventArgs e)
    {
        ExplicitExplorerTreeUnselectedCount++;
        RecordExplicitExplorerTreeEvent(sender, e);
    }

    private void RecordExplicitExplorerTreeEvent(object sender, RoutedEventArgs e)
    {
        LastExplicitExplorerTreeSenderName = GetElementName(sender);
        LastExplicitExplorerTreeRoutedEventName = e.RoutedEvent?.Name;
        LastExplicitExplorerTreeHeader = sender is TreeViewItem { Header: object header }
            ? header.ToString()
            : null;
        ExplicitExplorerTreeStatusText.Text =
            $"{LastExplicitExplorerTreeRoutedEventName}: {LastExplicitExplorerTreeHeader}";
    }

    private void OnMvpRoutedEventSource(object sender, MvpRoutedEventArgs e)
    {
        MvpRoutedEventSourceCount++;
        LastMvpRoutedEventName = e.RoutedEvent?.Name;
        LastMvpRoutedEventPayload = e.Payload;
        LastMvpRoutedEventSenderName = GetElementName(sender);
        LastMvpRoutedEventOriginalSourceName = GetElementName(e.OriginalSource);
    }

    private void OnMvpRoutedEventScope(object sender, MvpRoutedEventArgs e)
    {
        MvpRoutedEventScopeCount++;
        LastMvpRoutedEventName = e.RoutedEvent?.Name;
        LastMvpRoutedEventPayload = e.Payload;
        LastMvpRoutedEventSenderName = GetElementName(sender);
        LastMvpRoutedEventOriginalSourceName = GetElementName(e.OriginalSource);
        MvpRoutedEventStatusText.Text = $"Handled {e.Payload}";
        e.Handled = true;
    }

    private void OnMvpRoutedEventScopeHandledToo(object sender, MvpRoutedEventArgs e)
    {
        MvpRoutedEventHandledTooCount++;
        LastMvpRoutedEventName = e.RoutedEvent?.Name;
        LastMvpRoutedEventPayload = e.Payload;
        LastMvpRoutedEventSenderName = GetElementName(sender);
        LastMvpRoutedEventOriginalSourceName = GetElementName(e.OriginalSource);
    }

    private void OnMvpStyleEventSetterClick(object sender, RoutedEventArgs e)
    {
        MvpStyleEventSetterClickCount++;
        LastMvpStyleEventSetterSenderName = GetElementName(sender);
        LastMvpStyleEventSetterRoutedEventName = e.RoutedEvent?.Name;
        EventSetterStatusText.Text = "EventSetter clicked";
        e.Handled = true;
    }

    private static string? GetElementName(object? value)
    {
        return value is FrameworkElement element ? element.Name : null;
    }
}

public sealed class MainViewModel : INotifyPropertyChanged, IDataErrorInfo, INotifyDataErrorInfo
{
    private string _newItemName = "Gamma";
    private MvpItem? _selectedItem;
    private string _selectedCategory = "Framework";
    private bool _actionsEnabled = true;
    private bool _showActiveOnly;
    private double _progress = 35.0;
    private int _refreshCount;
    private string _validationText = "valid: ready";
    private string _dataErrorText = "data: ready";
    private string _notifyDataErrorText = "notify: ready";
    private string _bindingGroupFirstName = "group: Ada";
    private string _bindingGroupLastName = "group: Lovelace";
    private string _bindingGroupStatus = "Group ready";
    private DateTime? _selectedDate = new(2026, 6, 23);
    private int _selectedTabIndex;
    private string? _nullDisplayText;

    public MainViewModel()
    {
        Items =
        [
            new MvpItem("Alpha", "Framework", true),
            new MvpItem("Beta", "Rendering", false)
        ];
        Categories = ["Framework", "Rendering", "Input"];
        FormattedItems = ["Alpha", "Beta"];
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
        RequeryCommand = new MvpRequeryCommand();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public ObservableCollection<MvpItem> Items { get; }

    public ObservableCollection<string> Categories { get; }

    public ObservableCollection<string> FormattedItems { get; }

    public ObservableCollection<MvpNode> Nodes { get; }

    public ICommand AddItemCommand { get; }

    public ICommand ResetCommand { get; }

    public MvpRequeryCommand RequeryCommand { get; }

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

    public string DataErrorText
    {
        get => _dataErrorText;
        set => SetField(ref _dataErrorText, value);
    }

    public string NotifyDataErrorText
    {
        get => _notifyDataErrorText;
        set
        {
            if (SetField(ref _notifyDataErrorText, value))
            {
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(NotifyDataErrorText)));
            }
        }
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

    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set => SetField(ref _selectedDate, value);
    }

    public string? NullDisplayText
    {
        get => _nullDisplayText;
        set => SetField(ref _nullDisplayText, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetField(ref _selectedTabIndex, value);
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

    public string Error => string.Empty;

    public string this[string columnName] => columnName == nameof(DataErrorText) && !DataErrorText.StartsWith("data:", StringComparison.Ordinal)
        ? "Data value must start with data:"
        : string.Empty;

    public bool HasErrors
    {
        get
        {
            foreach (object _ in GetErrors(null))
            {
                return true;
            }

            return false;
        }
    }

    public IEnumerable GetErrors(string? propertyName)
    {
        if ((propertyName is null || propertyName == nameof(NotifyDataErrorText)) &&
            !NotifyDataErrorText.StartsWith("notify:", StringComparison.Ordinal))
        {
            yield return "Notify value must start with notify:";
        }
    }

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
        FormattedItems.Clear();
        FormattedItems.Add("Alpha");
        FormattedItems.Add("Beta");
        SelectedItem = Items[0];
        SelectedCategory = Categories[0];
        NewItemName = "Gamma";
        Progress = 35.0;
        ActionsEnabled = true;
        ShowActiveOnly = false;
        RefreshCount = 0;
        ValidationText = "valid: ready";
        DataErrorText = "data: ready";
        NotifyDataErrorText = "notify: ready";
        BindingGroupFirstName = "group: Ada";
        BindingGroupLastName = "group: Lovelace";
        BindingGroupStatus = "Group ready";
        SelectedDate = new DateTime(2026, 6, 23);
        SelectedTabIndex = 0;
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

public sealed class MvpRequeryCommand : ICommand
{
    public int CanExecuteProbeCount { get; private set; }

    public int ExecuteCount { get; private set; }

    public bool CanExecuteValue { get; set; }

    public object? LastParameter { get; private set; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter)
    {
        CanExecuteProbeCount++;
        return CanExecuteValue;
    }

    public void Execute(object? parameter)
    {
        ExecuteCount++;
        LastParameter = parameter;
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

public sealed class MvpItemContainerStyleSelector : StyleSelector
{
    public Style? ActiveStyle { get; set; }

    public Style? InactiveStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
    {
        return item is MvpItem { IsActive: true }
            ? ActiveStyle
            : InactiveStyle;
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

public static class MvpResourceFactory
{
    public static string CreateSummary(string prefix, int value)
    {
        return $"{prefix}:{value}";
    }
}

public sealed class MvpTextExtension : MarkupExtension
{
    public string Prefix { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return string.IsNullOrEmpty(Prefix)
            ? Value
            : $"{Prefix} {Value}";
    }
}

internal static class MvpSelfTest
{
    public static void Validate(MainWindow window, bool expectLoadedStoryboardApplied = false)
    {
        ArgumentNullException.ThrowIfNull(window);

        var viewModel = window.DataContext as MainViewModel
            ?? throw new InvalidOperationException("Expected MVP DataContext.");
        var application = Application.Current
            ?? throw new InvalidOperationException("Expected current Application.");
        AssertEqual(ShutdownMode.OnMainWindowClose, application.ShutdownMode, "Application ShutdownMode");
        ValidateApplicationRunState(application, window, expectLoadedStoryboardApplied);
        ValidateRuntimeNameScope(window);
        var themeResources = Require<ResourceDictionary>(
            application.Resources.MergedDictionaries.Count > 0
                ? application.Resources.MergedDictionaries[0]
                : null,
            "app merged theme ResourceDictionary");
        AssertEqual(true, themeResources.Contains("MvpPanelBrush"), "app theme panel brush key");
        AssertEqual(true, themeResources.Contains(typeof(Button)), "app theme implicit Button style key");
        AssertEqual(true, themeResources.Contains("SelectedItemTemplate"), "app theme selected item template key");
        AssertEqual(true, themeResources.Contains("MvpBasedOnButtonStyle"), "app theme BasedOn Button style key");
        AssertEqual(true, themeResources.Contains("MvpTemplateButtonStyle"), "app theme template Button style key");
        AssertEqual(true, themeResources.Contains("MvpTriggerTextBlockStyle"), "app theme trigger TextBlock style key");
        AssertEqual(true, themeResources.Contains("MvpMultiTriggerTextBlockStyle"), "app theme MultiTrigger TextBlock style key");
        AssertEqual(true, themeResources.Contains("MvpMultiDataTriggerTextBlockStyle"), "app theme MultiDataTrigger TextBlock style key");
        var panelBrush = Require<SolidColorBrush>(window.FindResource("MvpPanelBrush"), "MVP panel brush");
        var buttonStyle = Require<Style>(application.TryFindResource(typeof(Button)), "app Button style");
        var implicitItemTemplate = Require<DataTemplate>(
            application.TryFindResource(new DataTemplateKey(typeof(MvpItem))),
            "implicit item DataTemplate");
        var basedOnButtonStyle = Require<Style>(
            application.TryFindResource("MvpBasedOnButtonStyle"),
            "BasedOn Button style");
        var templateButtonStyle = Require<Style>(
            application.TryFindResource("MvpTemplateButtonStyle"),
            "template Button style");
        var triggerTextBlockStyle = Require<Style>(
            application.TryFindResource("MvpTriggerTextBlockStyle"),
            "trigger TextBlock style");
        var multiTriggerTextBlockStyle = Require<Style>(
            application.TryFindResource("MvpMultiTriggerTextBlockStyle"),
            "MultiTrigger TextBlock style");
        var multiDataTriggerTextBlockStyle = Require<Style>(
            application.TryFindResource("MvpMultiDataTriggerTextBlockStyle"),
            "MultiDataTrigger TextBlock style");
        var eventSetterButtonStyle = Require<Style>(
            window.FindResource("MvpEventSetterButtonStyle"),
            "EventSetter Button style");
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
        var activeItemContainerStyle = Require<Style>(
            window.FindResource("MvpActiveItemContainerStyle"),
            "active item container style");
        var inactiveItemContainerStyle = Require<Style>(
            window.FindResource("MvpInactiveItemContainerStyle"),
            "inactive item container style");
        var itemContainerStyleSelector = Require<MvpItemContainerStyleSelector>(
            window.FindResource("MvpItemContainerStyleSelector"),
            "item container style selector");
        var selectedItemTemplate = Require<DataTemplate>(
            application.TryFindResource("SelectedItemTemplate"),
            "selected item DataTemplate");
        AssertEqual(Color.FromRgb(0xF4, 0xF7, 0xFB), panelBrush.Color, "MVP panel brush color");
        AssertEqual(typeof(Button), buttonStyle.TargetType, "app Button implicit style target type");
        AssertEqual(typeof(Button), basedOnButtonStyle.TargetType, "BasedOn Button style target type");
        AssertEqual(buttonStyle, basedOnButtonStyle.BasedOn, "BasedOn Button style base style");
        AssertEqual(typeof(Button), templateButtonStyle.TargetType, "template Button style target type");
        AssertEqual(typeof(TextBlock), triggerTextBlockStyle.TargetType, "trigger TextBlock style target type");
        AssertEqual(typeof(TextBlock), multiTriggerTextBlockStyle.TargetType, "MultiTrigger TextBlock style target type");
        AssertEqual(typeof(TextBlock), multiDataTriggerTextBlockStyle.TargetType, "MultiDataTrigger TextBlock style target type");
        AssertEqual(typeof(Button), eventSetterButtonStyle.TargetType, "EventSetter Button style target type");
        AssertEqual(typeof(MvpItem), implicitItemTemplate.DataType, "implicit item template data type");
        AssertEqual(typeof(MvpItem), selectedItemTemplate.DataType, "selected item template data type");
        var mainMenu = Require<Menu>(window.FindName("MainMenu"), "main Menu");
        var fileMenuItem = Require<MenuItem>(window.FindName("FileMenuItem"), "file MenuItem");
        var viewMenuItem = Require<MenuItem>(window.FindName("ViewMenuItem"), "view MenuItem");
        var addMenuItem = Require<MenuItem>(window.FindName("AddMenuItem"), "add MenuItem");
        var resetMenuItem = Require<MenuItem>(window.FindName("ResetMenuItem"), "reset MenuItem");
        var aboutMenuItem = Require<MenuItem>(window.FindName("AboutMenuItem"), "about MenuItem");
        var refreshMenuItem = Require<MenuItem>(window.FindName("RefreshMenuItem"), "refresh MenuItem");
        var actionsEnabledMenuItem = Require<MenuItem>(
            window.FindName("ActionsEnabledMenuItem"),
            "actions enabled MenuItem");
        var commandStatusText = Require<TextBlock>(
            window.FindName("CommandStatusText"),
            "command status TextBlock");
        var requeryCommandButton = Require<Button>(
            window.FindName("RequeryCommandButton"),
            "requery command Button");
        var mvpTabControl = Require<TabControl>(
            window.FindName("MvpTabControl"),
            "MVP TabControl");
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
        var formattedItemsList = Require<ListBox>(
            window.FindName("FormattedItemsList"),
            "formatted items ListBox");
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
        var selectorGroupBox = Require<GroupBox>(
            window.FindName("SelectorGroupBox"),
            "selector GroupBox");
        var selectedValueComboBox = Require<ComboBox>(
            window.FindName("SelectedValueComboBox"),
            "selected value ComboBox");
        var multiSelectItemsList = Require<ListBox>(
            window.FindName("MultiSelectItemsList"),
            "multi-select ListBox");
        var selectorExpander = Require<Expander>(
            window.FindName("SelectorExpander"),
            "selector Expander");
        var selectorScrollViewer = Require<ScrollViewer>(
            window.FindName("SelectorScrollViewer"),
            "selector ScrollViewer");
        var selectorScrollText = Require<TextBlock>(
            window.FindName("SelectorScrollText"),
            "selector scroll TextBlock");
        var mvpToolBarTray = Require<ToolBarTray>(
            window.FindName("MvpToolBarTray"),
            "MVP ToolBarTray");
        var mvpToolBar = Require<ToolBar>(
            window.FindName("MvpToolBar"),
            "MVP ToolBar");
        var toolBarRefreshButton = Require<Button>(
            window.FindName("ToolBarRefreshButton"),
            "toolbar refresh Button");
        var toolBarSeparator = Require<Separator>(
            window.FindName("ToolBarSeparator"),
            "toolbar Separator");
        var toolBarToggleButton = Require<ToggleButton>(
            window.FindName("ToolBarToggleButton"),
            "toolbar ToggleButton");
        var popupOwnerButton = Require<Button>(
            window.FindName("PopupOwnerButton"),
            "popup owner Button");
        var inputPopup = Require<Popup>(
            window.FindName("InputPopup"),
            "input Popup");
        var inputToggleButton = Require<ToggleButton>(
            window.FindName("InputToggleButton"),
            "input ToggleButton");
        var frameworkRadioButton = Require<RadioButton>(
            window.FindName("FrameworkRadioButton"),
            "framework RadioButton");
        var renderingRadioButton = Require<RadioButton>(
            window.FindName("RenderingRadioButton"),
            "rendering RadioButton");
        var inputRepeatButton = Require<RepeatButton>(
            window.FindName("InputRepeatButton"),
            "input RepeatButton");
        var inputCalendar = Require<WpfCalendar>(
            window.FindName("InputCalendar"),
            "input Calendar");
        var inputDatePicker = Require<DatePicker>(
            window.FindName("InputDatePicker"),
            "input DatePicker");
        var keyboardNavigationPanel = Require<StackPanel>(
            window.FindName("KeyboardNavigationPanel"),
            "keyboard navigation StackPanel");
        var keyboardNavigationAccessLabel = Require<Label>(
            window.FindName("KeyboardNavigationAccessLabel"),
            "keyboard navigation access Label");
        var keyboardNavigationFirstBox = Require<TextBox>(
            window.FindName("KeyboardNavigationFirstBox"),
            "first keyboard navigation TextBox");
        var keyboardNavigationSecondButton = Require<Button>(
            window.FindName("KeyboardNavigationSecondButton"),
            "second keyboard navigation Button");
        var keyboardNavigationThirdBox = Require<TextBox>(
            window.FindName("KeyboardNavigationThirdBox"),
            "third keyboard navigation TextBox");
        var mvpDockPanel = Require<DockPanel>(
            window.FindName("MvpDockPanel"),
            "MVP DockPanel");
        var dockTopBand = Require<Border>(
            window.FindName("DockTopBand"),
            "dock top Border");
        var dockLeftBand = Require<Border>(
            window.FindName("DockLeftBand"),
            "dock left Border");
        var dockRightBand = Require<Border>(
            window.FindName("DockRightBand"),
            "dock right Border");
        var dockFillText = Require<TextBlock>(
            window.FindName("DockFillText"),
            "dock fill TextBlock");
        var mvpWrapPanel = Require<WrapPanel>(
            window.FindName("MvpWrapPanel"),
            "MVP WrapPanel");
        var mvpUniformGrid = Require<UniformGrid>(
            window.FindName("MvpUniformGrid"),
            "MVP UniformGrid");
        var mvpGridSplitterGrid = Require<Grid>(
            window.FindName("MvpGridSplitterGrid"),
            "MVP GridSplitter grid");
        var splitterLeftColumn = Require<ColumnDefinition>(
            window.FindName("SplitterLeftColumn"),
            "splitter left ColumnDefinition");
        var splitterRightColumn = Require<ColumnDefinition>(
            window.FindName("SplitterRightColumn"),
            "splitter right ColumnDefinition");
        var splitterLeftPane = Require<Border>(
            window.FindName("SplitterLeftPane"),
            "splitter left Border");
        var mvpGridSplitter = Require<GridSplitter>(
            window.FindName("MvpGridSplitter"),
            "MVP GridSplitter");
        var splitterRightPane = Require<Border>(
            window.FindName("SplitterRightPane"),
            "splitter right Border");
        var mvpViewbox = Require<Viewbox>(
            window.FindName("MvpViewbox"),
            "MVP Viewbox");
        var viewboxText = Require<TextBlock>(
            window.FindName("ViewboxText"),
            "viewbox TextBlock");
        var componentResourceText = Require<TextBlock>(
            window.FindName("ComponentResourceText"),
            "component resource TextBlock");
        var localizedResourceText = Require<TextBlock>(
            window.FindName("LocalizedResourceText"),
            "localized resource TextBlock");
        var resourceAccessText = Require<AccessText>(
            window.FindName("ResourceAccessText"),
            "resource AccessText");
        var objectProviderText = Require<TextBlock>(
            window.FindName("ObjectProviderText"),
            "object data provider TextBlock");
        var xmlProviderText = Require<TextBlock>(
            window.FindName("XmlProviderText"),
            "XML data provider TextBlock");
        var resourceArrayItemsControl = Require<ItemsControl>(
            window.FindName("ResourceArrayItemsControl"),
            "resource array ItemsControl");
        var nullIntrinsicText = Require<TextBlock>(
            window.FindName("NullIntrinsicText"),
            "null intrinsic TextBlock");
        var markupExtensionText = Require<TextBlock>(
            window.FindName("MarkupExtensionText"),
            "MarkupExtension TextBlock");
        var packResourceText = Require<TextBlock>(
            window.FindName("PackResourceText"),
            "pack resource TextBlock");
        var componentPackResourceText = Require<TextBlock>(
            window.FindName("ComponentPackResourceText"),
            "component pack resource TextBlock");
        var startupResourceText = Require<TextBlock>(
            window.FindName("StartupResourceText"),
            "startup resource TextBlock");
        var systemParameterText = Require<TextBlock>(
            window.FindName("SystemParameterText"),
            "SystemParameters TextBlock");
        var systemFontText = Require<TextBlock>(
            window.FindName("SystemFontText"),
            "SystemFonts TextBlock");
        var systemColorBorder = Require<Border>(
            window.FindName("SystemColorBorder"),
            "SystemColors Border");
        var systemColorText = Require<TextBlock>(
            window.FindName("SystemColorText"),
            "SystemColors TextBlock");
        var mvpThemedControl = Require<MvpThemedControl>(
            window.FindName("MvpThemedControl"),
            "MVP themed control");
        var drawingImageControl = Require<Image>(
            window.FindName("MvpDrawingImageControl"),
            "MVP DrawingImage Image");
        var drawingImageBrushBorder = Require<Border>(
            window.FindName("MvpDrawingImageBrushBorder"),
            "MVP DrawingImageBrush Border");
        var resourceDynamicBorder = Require<Border>(
            window.FindName("ResourceDynamicBorder"),
            "resource DynamicResource Border");
        var selectedItemContent = Require<ContentControl>(
            window.FindName("SelectedItemContent"),
            "selected item ContentControl");
        var implicitTemplateContent = Require<ContentControl>(
            window.FindName("ImplicitTemplateContent"),
            "implicit template ContentControl");
        var selectorItemsList = Require<ListBox>(
            window.FindName("SelectorItemsList"),
            "selector items ListBox");
        var styleSelectorItemsList = Require<ListBox>(
            window.FindName("StyleSelectorItemsList"),
            "style selector items ListBox");
        var templateButton = Require<Button>(window.FindName("TemplateButton"), "template Button");
        var basedOnStyleButton = Require<Button>(
            window.FindName("BasedOnStyleButton"),
            "BasedOn style Button");
        var styleTriggerText = Require<TextBlock>(
            window.FindName("StyleTriggerText"),
            "style trigger TextBlock");
        var multiTriggerText = Require<TextBlock>(
            window.FindName("MultiTriggerText"),
            "MultiTrigger TextBlock");
        var multiDataTriggerText = Require<TextBlock>(
            window.FindName("MultiDataTriggerText"),
            "MultiDataTrigger TextBlock");
        var eventSetterStyleButton = Require<Button>(
            window.FindName("EventSetterStyleButton"),
            "EventSetter style Button");
        var eventSetterStatusText = Require<TextBlock>(
            window.FindName("EventSetterStatusText"),
            "EventSetter status TextBlock");
        var validationTextBox = Require<TextBox>(
            window.FindName("ValidationTextBox"),
            "validation TextBox");
        var validationEchoText = Require<TextBlock>(
            window.FindName("ValidationEchoText"),
            "validation echo TextBlock");
        var dataErrorTextBox = Require<TextBox>(
            window.FindName("DataErrorTextBox"),
            "IDataErrorInfo TextBox");
        var dataErrorEchoText = Require<TextBlock>(
            window.FindName("DataErrorEchoText"),
            "IDataErrorInfo echo TextBlock");
        var notifyDataErrorTextBox = Require<TextBox>(
            window.FindName("NotifyDataErrorTextBox"),
            "INotifyDataErrorInfo TextBox");
        var notifyDataErrorEchoText = Require<TextBlock>(
            window.FindName("NotifyDataErrorEchoText"),
            "INotifyDataErrorInfo echo TextBlock");
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
        var dropShadowEffectBorder = Require<Border>(
            window.FindName("MvpDropShadowEffectBorder"),
            "MVP DropShadowEffect Border");
        var blurEffectBorder = Require<Border>(
            window.FindName("MvpBlurEffectBorder"),
            "MVP BlurEffect Border");
        var summaryPanel = Require<SummaryPanel>(window.FindName("SummaryPanel"), "summary Panel");
        var dependencyPropertyManagerText = Require<MvpHeaderTextBlock>(
            window.FindName("DependencyPropertyManagerText"),
            "dependency property manager TextBlock");
        var mvpRoutedEventScope = Require<StackPanel>(
            window.FindName("MvpRoutedEventScope"),
            "MVP routed-event scope StackPanel");
        var mvpRoutedEventButton = Require<MvpRoutedEventButton>(
            window.FindName("MvpRoutedEventButton"),
            "MVP routed-event Button");
        var mvpRoutedEventStatusText = Require<TextBlock>(
            window.FindName("MvpRoutedEventStatusText"),
            "MVP routed-event status TextBlock");
        var summaryHeaderText = Require<TextBlock>(
            summaryPanel.FindName("SummaryHeaderText"),
            "summary header text");
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
        var explicitExplorerTreeView = Require<TreeView>(
            window.FindName("ExplicitExplorerTreeView"),
            "explicit explorer TreeView");
        var explicitExplorerAlpha = Require<TreeViewItem>(
            window.FindName("ExplicitExplorerAlpha"),
            "explicit explorer alpha TreeViewItem");
        var explicitExplorerAlphaChild = Require<TreeViewItem>(
            window.FindName("ExplicitExplorerAlphaChild"),
            "explicit explorer alpha child TreeViewItem");
        var explicitExplorerBeta = Require<TreeViewItem>(
            window.FindName("ExplicitExplorerBeta"),
            "explicit explorer beta TreeViewItem");
        var explicitExplorerTreeStatusText = Require<TextBlock>(
            window.FindName("ExplicitExplorerTreeStatusText"),
            "explicit explorer tree status TextBlock");
        var explorerListView = Require<ListView>(
            window.FindName("ExplorerListView"),
            "explorer ListView");
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
        var dataObjectPayloadTextBox = Require<TextBox>(
            window.FindName("DataObjectPayloadTextBox"),
            "DataObject payload TextBox");
        var dataObjectRoundTripButton = Require<Button>(
            window.FindName("DataObjectRoundTripButton"),
            "DataObject round-trip Button");
        var dataObjectStatusText = Require<TextBlock>(
            window.FindName("DataObjectStatusText"),
            "DataObject status TextBlock");
        var documentViewer = Require<FlowDocumentScrollViewer>(
            window.FindName("DocumentViewer"),
            "document FlowDocumentScrollViewer");
        var documentPageViewer = Require<FlowDocumentPageViewer>(
            window.FindName("DocumentPageViewer"),
            "document FlowDocumentPageViewer");
        var documentReader = Require<FlowDocumentReader>(
            window.FindName("DocumentReader"),
            "document FlowDocumentReader");
        Require<CheckBox>(window.FindName("EnabledCheckBox"), "enabled CheckBox");
        Require<Slider>(window.FindName("ProgressSlider"), "progress Slider");
        Require<ComboBox>(window.FindName("CategoryCombo"), "category ComboBox");
        AssertEqual(2, mainMenu.Items.Count, "main menu item count");
        AssertEqual(5, fileMenuItem.Items.Count, "file menu item count");
        AssertEqual(3, viewMenuItem.Items.Count, "view menu item count");
        AssertEqual(viewModel.AddItemCommand, addMenuItem.Command, "add menu command binding");
        AssertEqual("Ctrl+N", addMenuItem.InputGestureText, "add menu input gesture text");
        AssertEqual(viewModel.ResetCommand, resetMenuItem.Command, "reset menu command binding");
        AssertEqual("_About", aboutMenuItem.Header, "about menu item header");
        AssertEqual(MainWindow.RefreshStatusCommand, refreshMenuItem.Command, "refresh menu routed command");
        AssertEqual("Ctrl+R", refreshMenuItem.InputGestureText, "refresh menu input gesture text");
        AssertEqual(1, window.CommandBindings.Count, "window command binding count");
        AssertEqual(MainWindow.RefreshStatusCommand, window.CommandBindings[0].Command, "window routed command binding");
        AssertEqual(1, window.InputBindings.Count, "window input binding count");
        var refreshKeyBinding = Require<KeyBinding>(window.InputBindings[0], "refresh KeyBinding");
        AssertEqual(Key.R, refreshKeyBinding.Key, "refresh key binding key");
        AssertEqual(ModifierKeys.Control, refreshKeyBinding.Modifiers, "refresh key binding modifiers");
        AssertEqual(MainWindow.RefreshStatusCommand, refreshKeyBinding.Command, "refresh key binding command");
        ValidateMvpTabControl(window, viewModel, mvpTabControl);
        AssertEqual(true, actionsEnabledMenuItem.IsCheckable, "actions menu checkable state");
        AssertEqual(true, actionsEnabledMenuItem.IsChecked, "actions menu initial checked state");
        AssertEqual(viewModel.Items, itemsDataGrid.ItemsSource, "DataGrid items source");
        AssertEqual(3, itemsDataGrid.Columns.Count, "DataGrid column count");
        AssertEqual("Name", GetColumnBindingPath(itemsDataGrid.Columns[0]), "DataGrid name column binding");
        AssertEqual("Category", GetColumnBindingPath(itemsDataGrid.Columns[1]), "DataGrid category column binding");
        AssertEqual("IsActive", GetColumnBindingPath(itemsDataGrid.Columns[2]), "DataGrid active column binding");
        ValidateCollectionView(window, viewModel, groupedItemsList, activeOnlyCheckBox, activeTextConverter);
        ValidateFormattedItemsList(window, viewModel, formattedItemsList);
        ValidateSelectedSummaryBinding(selectedItemSummaryText, itemSummaryConverter);
        AssertEqual(viewModel.SelectedItem, selectedItemContent.Content, "selected item content");
        AssertEqual(
            selectedItemTemplate,
            selectedItemContent.ContentTemplate,
            "selected item content template");
        ValidateSelectedItemTemplate(selectedItemTemplate);
        ValidateImplicitItemTemplate(viewModel, implicitTemplateContent, implicitItemTemplate);
        ValidateTemplateSelector(
            viewModel,
            selectorItemsList,
            activeItemTemplate,
            inactiveItemTemplate,
            itemTemplateSelector,
            selectorItemContainerStyle);
        ValidateItemContainerStyleSelector(
            viewModel,
            styleSelectorItemsList,
            activeItemContainerStyle,
            inactiveItemContainerStyle,
            itemContainerStyleSelector);
        ValidateBasedOnButton(basedOnStyleButton, basedOnButtonStyle);
        ValidateStyleTriggersAndEventSetter(
            window,
            viewModel,
            styleTriggerText,
            triggerTextBlockStyle,
            multiTriggerText,
            multiTriggerTextBlockStyle,
            multiDataTriggerText,
            multiDataTriggerTextBlockStyle,
            eventSetterStyleButton,
            eventSetterButtonStyle,
            eventSetterStatusText);
        ValidateTemplateButton(window, templateButton, templateButtonStyle);
        ValidateValidation(window, viewModel, validationTextBox, validationEchoText);
        ValidateDataErrorValidation(window, viewModel, dataErrorTextBox, dataErrorEchoText);
        ValidateNotifyDataErrorValidation(
            window,
            viewModel,
            notifyDataErrorTextBox,
            notifyDataErrorEchoText);
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
        ValidateStoryboards(window, loadedStoryboardText, clickStoryboardButton, expectLoadedStoryboardApplied);
        ValidateNativeEffects(dropShadowEffectBorder, blurEffectBorder);
        AssertEqual(viewModel.Nodes, nodesTreeView.ItemsSource, "TreeView items source");
        AssertEqual(2, viewModel.Nodes.Count, "TreeView root node count");
        AssertEqual("Startup", viewModel.Nodes[0].Children[0].Name, "TreeView first child node");
        var nodeTemplate = Require<HierarchicalDataTemplate>(
            nodesTreeView.ItemTemplate,
            "node hierarchical data template");
        AssertEqual("Children", GetTemplateItemsSourcePath(nodeTemplate), "TreeView hierarchical template ItemsSource path");
        ValidateExplicitExplorerTree(
            window,
            explicitExplorerTreeView,
            explicitExplorerAlpha,
            explicitExplorerAlphaChild,
            explicitExplorerBeta,
            explicitExplorerTreeStatusText);
        var explorerGridView = Require<GridView>(explorerListView.View, "explorer GridView");
        AssertEqual(viewModel.Items, explorerListView.ItemsSource, "explorer ListView ItemsSource");
        AssertEqual(viewModel.SelectedItem, explorerListView.SelectedItem, "explorer ListView selected item");
        AssertEqual(false, explorerGridView.AllowsColumnReorder, "explorer GridView column reorder state");
        AssertEqual(3, explorerGridView.Columns.Count, "explorer GridView column count");
        AssertEqual("Name", explorerGridView.Columns[0].Header, "explorer GridView name header");
        AssertEqual("Name", GetGridViewColumnBindingPath(explorerGridView.Columns[0]), "explorer GridView name binding");
        AssertEqual("Category", explorerGridView.Columns[1].Header, "explorer GridView category header");
        AssertEqual("Category", GetGridViewColumnBindingPath(explorerGridView.Columns[1]), "explorer GridView category binding");
        AssertEqual("Active", explorerGridView.Columns[2].Header, "explorer GridView active header");
        AssertEqual("IsActive", GetGridViewColumnBindingPath(explorerGridView.Columns[2]), "explorer GridView active binding");
        DrainDispatcher(window);
        AssertEqual("Commands idle", commandStatusText.Text, "initial command status text");
        AssertEqual("Overview tools", MvpStateProperties.GetSectionName(dependencyPropertyManagerText), "inherited attached section value");
        AssertEqual(
            BaseValueSource.Inherited,
            DependencyPropertyHelper.GetValueSource(
                dependencyPropertyManagerText,
                MvpStateProperties.SectionNameProperty).BaseValueSource,
            "inherited attached section value source");
        AssertEqual("Overview tools", dependencyPropertyManagerText.Text, "inherited attached section text");
        AssertEqual(100d, MvpStateProperties.GetImportance(dependencyPropertyManagerText), "coerced attached importance value");
        AssertGreaterThan(0, MvpStateProperties.ImportanceChangedCount, "attached importance changed callback count");
        AssertEqual("StatusText", GetBindingPath(dependencyPropertyManagerText, MvpHeaderTextBlock.HeaderTextProperty), "AddOwner header binding path");
        AssertEqual("Alpha selected, progress 35%", dependencyPropertyManagerText.HeaderText, "AddOwner initial header property");
        AssertEqual(FontWeights.SemiBold, dependencyPropertyManagerText.FontWeight, "metadata override FontWeight value");
        AssertEqual(Brushes.DarkSlateBlue, dependencyPropertyManagerText.Foreground, "metadata override Foreground value");
        AssertEqual(new MvpTypedOffset(12.5, 24.25), dependencyPropertyManagerText.TypedOffset, "TypeConverter dependency property value");
        AssertEqual(
            BaseValueSource.Local,
            DependencyPropertyHelper.GetValueSource(
                dependencyPropertyManagerText,
                MvpHeaderTextBlock.TypedOffsetProperty).BaseValueSource,
            "TypeConverter dependency property value source");
        ValidateMvpRoutedEvent(window, mvpRoutedEventScope, mvpRoutedEventButton, mvpRoutedEventStatusText);
        AssertEqual("StatusText", GetBindingPath(summaryPanel, SummaryPanel.HeaderTextProperty), "summary header binding path");
        AssertEqual("Alpha selected, progress 35%", summaryPanel.HeaderText, "summary initial header property");
        AssertEqual("Alpha selected, progress 35%", summaryHeaderText.Text, "summary initial header text");
        AssertEqual("Name: Alpha", summaryNameText.Text, "summary initial name text");
        AssertEqual("Category: Framework", summaryCategoryText.Text, "summary initial category text");
        AssertEqual("Progress: 35%", summaryProgressText.Text, "summary initial progress text");
        summaryPanel.SetCurrentValue(SummaryPanel.HeaderTextProperty, "Manual dependency property header");
        DrainDispatcher(window);
        AssertEqual("Manual dependency property header", summaryHeaderText.Text, "summary SetCurrentValue header text");
        UpdateBinding(summaryPanel, SummaryPanel.HeaderTextProperty);
        DrainDispatcher(window);
        AssertEqual("Alpha selected, progress 35%", summaryHeaderText.Text, "summary rebound header text");
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
        ValidateSelectorControls(
            window,
            viewModel,
            selectorGroupBox,
            selectedValueComboBox,
            multiSelectItemsList,
            selectorExpander,
            selectorScrollViewer,
            selectorScrollText);
        ValidateInputControls(
            window,
            viewModel,
            mvpToolBarTray,
            mvpToolBar,
            toolBarRefreshButton,
            toolBarSeparator,
            toolBarToggleButton,
            popupOwnerButton,
            inputPopup,
            inputToggleButton,
            frameworkRadioButton,
            renderingRadioButton,
            inputRepeatButton,
            inputCalendar,
            inputDatePicker,
            keyboardNavigationPanel,
            keyboardNavigationAccessLabel,
            keyboardNavigationFirstBox,
            keyboardNavigationSecondButton,
            keyboardNavigationThirdBox);
        ValidateLayoutControls(
            mvpDockPanel,
            dockTopBand,
            dockLeftBand,
            dockRightBand,
            dockFillText,
            mvpWrapPanel,
            mvpUniformGrid,
            mvpGridSplitterGrid,
            splitterLeftColumn,
            splitterRightColumn,
            splitterLeftPane,
            mvpGridSplitter,
            splitterRightPane,
            mvpViewbox,
            viewboxText);
        ValidateResourceControls(
            window,
            componentResourceText,
            localizedResourceText,
            resourceAccessText,
            objectProviderText,
            xmlProviderText,
            resourceArrayItemsControl,
            nullIntrinsicText,
            markupExtensionText,
            packResourceText,
            componentPackResourceText,
            startupResourceText,
            systemParameterText,
            systemFontText,
            systemColorBorder,
            systemColorText,
            mvpThemedControl,
            drawingImageControl,
            drawingImageBrushBorder,
            resourceDynamicBorder,
            expectLoadedStoryboardApplied);
        ValidateItemsContextMenu(window, viewModel, itemsList);
        ValidateApplicationLoadComponent();
        ValidateLooseXamlReaderWriter();
        ValidateDispatcherOperations(window);
        ValidateNavigation(window, navigationFrame, detailsNavigationButton);
        ValidateSecondaryWindow(window, aboutMenuItem);
        ValidateEditor(
            window,
            editorPasswordBox,
            editorRichTextBox,
            dataObjectPayloadTextBox,
            dataObjectRoundTripButton,
            dataObjectStatusText);
        ValidateDocument(window, documentViewer, documentPageViewer, documentReader);

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
        DrainDispatcher(window);
        AssertEqual(viewModel.SelectedItem, implicitTemplateContent.Content, "implicit item content updated selected item");
        AssertEqual(viewModel.SelectedItem, explorerListView.SelectedItem, "explorer ListView updated selected item");
        actionsEnabledMenuItem.IsChecked = false;
        AssertEqual(false, viewModel.ActionsEnabled, "actions menu unchecked view model state");
        AssertEqual(false, MainWindow.RefreshStatusCommand.CanExecute(null, window), "refresh command disabled CanExecute state");
        actionsEnabledMenuItem.IsChecked = true;
        AssertEqual(true, viewModel.ActionsEnabled, "actions menu checked view model state");
        AssertEqual(true, MainWindow.RefreshStatusCommand.CanExecute(null, window), "refresh command reenabled CanExecute state");
        ValidateRequeryCommand(window, viewModel, requeryCommandButton);

        viewModel.Progress = 72.0;
        DrainDispatcher(window);
        AssertEqual("Validated selected, progress 72%", viewModel.StatusText, "status text");
        AssertEqual("Validated selected, progress 72%", dependencyPropertyManagerText.HeaderText, "AddOwner updated header property");
        AssertEqual("Validated selected, progress 72%", summaryPanel.HeaderText, "summary updated header property");
        AssertEqual("Validated selected, progress 72%", summaryHeaderText.Text, "summary updated header text");
        AssertEqual("Validated", priorityBindingText.Text, "updated priority binding selected item text");
        AssertEqual("Name: Validated", summaryNameText.Text, "summary updated name text");
        AssertEqual("Category: Input", summaryCategoryText.Text, "summary updated category text");
        AssertEqual("Progress: 72%", summaryProgressText.Text, "summary updated progress text");
        AssertEqual("Validated / Input / 72%", selectedItemSummaryText.Text, "updated selected summary text");
    }

    private static void ValidateApplicationRunState(
        Application application,
        MainWindow window,
        bool expectStartupUriWindow)
    {
        if (!expectStartupUriWindow)
        {
            return;
        }

        AssertEqual(window, application.MainWindow, "Application MainWindow");
        int openWindowCount = 0;
        bool containsMainWindow = false;
        foreach (Window candidate in application.Windows)
        {
            openWindowCount++;
            containsMainWindow |= ReferenceEquals(candidate, window);
        }

        AssertEqual(1, openWindowCount, "Application Windows count after StartupUri activation");
        AssertEqual(true, containsMainWindow, "Application Windows contains StartupUri MainWindow");
        AssertEqual(true, window.IsVisible, "StartupUri MainWindow visible");
    }

    private static void ValidateRuntimeNameScope(Window window)
    {
        const string runtimeName = "MvpRuntimeRegisteredName";
        var registeredButton = new Button { Content = "Runtime registered name" };
        var replacementText = new TextBlock { Text = "Runtime replacement name" };

        window.RegisterName(runtimeName, registeredButton);
        try
        {
            AssertEqual(registeredButton, window.FindName(runtimeName), "runtime namescope registered object");
            window.UnregisterName(runtimeName);
            AssertEqual<object?>(null, window.FindName(runtimeName), "runtime namescope unregistered object");
            window.RegisterName(runtimeName, replacementText);
            AssertEqual(replacementText, window.FindName(runtimeName), "runtime namescope replacement object");
        }
        finally
        {
            if (ReferenceEquals(replacementText, window.FindName(runtimeName)) ||
                ReferenceEquals(registeredButton, window.FindName(runtimeName)))
            {
                window.UnregisterName(runtimeName);
            }
        }
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
        ValidateGroupedItemsGroupStyle(groupedItemsList.GroupStyle);
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

    private static void ValidateGroupedItemsGroupStyle(Collection<GroupStyle> groupStyles)
    {
        AssertEqual(1, groupStyles.Count, "grouped ListBox GroupStyle count");
        var groupStyle = Require<GroupStyle>(groupStyles[0], "grouped ListBox GroupStyle");
        var headerTemplate = Require<DataTemplate>(
            groupStyle.HeaderTemplate,
            "grouped ListBox GroupStyle HeaderTemplate");
        var root = Require<Border>(
            headerTemplate.LoadContent(),
            "grouped ListBox GroupStyle HeaderTemplate root");
        var headerText = Require<TextBlock>(
            root.Child,
            "grouped ListBox GroupStyle HeaderTemplate TextBlock");

        AssertEqual(new Thickness(0, 8, 0, 4), root.Margin, "grouped ListBox GroupStyle header margin");
        AssertEqual(new Thickness(6, 3, 6, 3), root.Padding, "grouped ListBox GroupStyle header padding");
        AssertEqual(FontWeights.SemiBold, headerText.FontWeight, "grouped ListBox GroupStyle header weight");
        AssertEqual("Name", GetTextBindingPath(headerText), "grouped ListBox GroupStyle header binding path");
    }

    private static void ValidateFormattedItemsList(
        Window window,
        MainViewModel viewModel,
        ListBox listBox)
    {
        AssertEqual(viewModel.FormattedItems, listBox.ItemsSource, "formatted ListBox ItemsSource");
        AssertEqual(3, listBox.AlternationCount, "formatted ListBox AlternationCount");
        AssertEqual("formatted {0}", listBox.ItemStringFormat, "formatted ListBox ItemStringFormat");
        AssertEqual(2, listBox.Items.Count, "formatted ListBox initial item count");
        AssertEqual("Alpha", listBox.Items[0], "formatted ListBox first item");

        viewModel.FormattedItems.Add("Gamma");
        DrainDispatcher(window);
        AssertEqual(3, listBox.Items.Count, "formatted ListBox collection-change item count");
        AssertEqual("Gamma", listBox.Items[2], "formatted ListBox collection-change item");

        viewModel.FormattedItems.Remove("Gamma");
        DrainDispatcher(window);
        AssertEqual(2, listBox.Items.Count, "formatted ListBox restored item count");
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

    private static void ValidateMvpTabControl(
        MainWindow window,
        MainViewModel viewModel,
        TabControl tabControl)
    {
        AssertEqual(viewModel.SelectedTabIndex, tabControl.SelectedIndex, "MVP TabControl selected index");
        AssertEqual(15, tabControl.Items.Count, "MVP TabControl item count");

        var controlsTab = Require<TabItem>(tabControl.Items[0], "MVP controls TabItem");
        var documentTab = Require<TabItem>(tabControl.Items[14], "MVP document TabItem");
        AssertEqual("Controls", controlsTab.Header, "MVP first tab header");
        AssertEqual("Document", documentTab.Header, "MVP last tab header");

        int initialSelectionEvents = window.MvpTabSelectionChangedCount;
        tabControl.SelectedIndex = 1;
        DrainDispatcher(window);
        AssertEqual(1, viewModel.SelectedTabIndex, "MVP TabControl selected index source update");
        AssertEqual("Views", window.LastMvpTabHeader, "MVP TabControl selected header after control update");
        AssertGreaterThan(
            initialSelectionEvents,
            window.MvpTabSelectionChangedCount,
            "MVP TabControl control selection event count");

        int afterControlUpdateEvents = window.MvpTabSelectionChangedCount;
        viewModel.SelectedTabIndex = 2;
        DrainDispatcher(window);
        AssertEqual(2, tabControl.SelectedIndex, "MVP TabControl selected index target update");
        AssertEqual("Bindings", window.LastMvpTabHeader, "MVP TabControl selected header after source update");
        AssertGreaterThan(
            afterControlUpdateEvents,
            window.MvpTabSelectionChangedCount,
            "MVP TabControl source selection event count");

        int afterSourceUpdateEvents = window.MvpTabSelectionChangedCount;
        viewModel.SelectedTabIndex = 0;
        DrainDispatcher(window);
        AssertEqual(0, tabControl.SelectedIndex, "MVP TabControl restored selected index");
        AssertEqual("Controls", window.LastMvpTabHeader, "MVP TabControl restored selected header");
        AssertGreaterThan(
            afterSourceUpdateEvents,
            window.MvpTabSelectionChangedCount,
            "MVP TabControl restored selection event count");
    }

    private static void ValidateExplicitExplorerTree(
        MainWindow window,
        TreeView treeView,
        TreeViewItem alphaItem,
        TreeViewItem alphaChildItem,
        TreeViewItem betaItem,
        TextBlock statusText)
    {
        AssertEqual(2, treeView.Items.Count, "explicit explorer TreeView item count");
        AssertEqual(alphaItem, treeView.Items[0], "explicit explorer alpha item owner");
        AssertEqual(betaItem, treeView.Items[1], "explicit explorer beta item owner");
        AssertEqual("Alpha branch", alphaItem.Header, "explicit explorer alpha header");
        AssertEqual("Alpha child", alphaChildItem.Header, "explicit explorer alpha child header");
        AssertEqual("Beta branch", betaItem.Header, "explicit explorer beta header");
        AssertEqual(1, alphaItem.Items.Count, "explicit explorer alpha child count");
        AssertEqual(alphaChildItem, alphaItem.Items[0], "explicit explorer alpha child owner");
        AssertEqual("Tree idle", statusText.Text, "explicit explorer initial status");

        int initialExpandedEvents = window.ExplicitExplorerTreeExpandedCount;
        alphaItem.IsExpanded = true;
        DrainDispatcher(window);
        AssertEqual(true, alphaItem.IsExpanded, "explicit explorer alpha expanded state");
        AssertEqual("ExplicitExplorerAlpha", window.LastExplicitExplorerTreeSenderName, "explicit explorer expanded sender");
        AssertEqual("Expanded", window.LastExplicitExplorerTreeRoutedEventName, "explicit explorer expanded event name");
        AssertEqual("Expanded: Alpha branch", statusText.Text, "explicit explorer expanded status");
        AssertGreaterThan(
            initialExpandedEvents,
            window.ExplicitExplorerTreeExpandedCount,
            "explicit explorer expanded event count");

        int initialCollapsedEvents = window.ExplicitExplorerTreeCollapsedCount;
        alphaItem.IsExpanded = false;
        DrainDispatcher(window);
        AssertEqual(false, alphaItem.IsExpanded, "explicit explorer alpha collapsed state");
        AssertEqual("ExplicitExplorerAlpha", window.LastExplicitExplorerTreeSenderName, "explicit explorer collapsed sender");
        AssertEqual("Collapsed", window.LastExplicitExplorerTreeRoutedEventName, "explicit explorer collapsed event name");
        AssertEqual("Collapsed: Alpha branch", statusText.Text, "explicit explorer collapsed status");
        AssertGreaterThan(
            initialCollapsedEvents,
            window.ExplicitExplorerTreeCollapsedCount,
            "explicit explorer collapsed event count");

        int initialSelectedEvents = window.ExplicitExplorerTreeSelectedCount;
        alphaItem.IsSelected = true;
        DrainDispatcher(window);
        AssertEqual(true, alphaItem.IsSelected, "explicit explorer alpha selected state");
        AssertEqual(alphaItem, treeView.SelectedItem, "explicit explorer selected alpha item");
        AssertEqual("ExplicitExplorerAlpha", window.LastExplicitExplorerTreeSenderName, "explicit explorer alpha selected sender");
        AssertEqual("Selected", window.LastExplicitExplorerTreeRoutedEventName, "explicit explorer selected event name");
        AssertEqual("Selected: Alpha branch", statusText.Text, "explicit explorer selected status");
        AssertGreaterThan(
            initialSelectedEvents,
            window.ExplicitExplorerTreeSelectedCount,
            "explicit explorer selected event count");

        int selectedAfterAlpha = window.ExplicitExplorerTreeSelectedCount;
        int initialUnselectedEvents = window.ExplicitExplorerTreeUnselectedCount;
        betaItem.IsSelected = true;
        DrainDispatcher(window);
        AssertEqual(false, alphaItem.IsSelected, "explicit explorer alpha unselected state");
        AssertEqual(true, betaItem.IsSelected, "explicit explorer beta selected state");
        AssertEqual(betaItem, treeView.SelectedItem, "explicit explorer selected beta item");
        AssertEqual("ExplicitExplorerBeta", window.LastExplicitExplorerTreeSenderName, "explicit explorer beta selected sender");
        AssertEqual("Selected", window.LastExplicitExplorerTreeRoutedEventName, "explicit explorer beta selected event name");
        AssertEqual("Selected: Beta branch", statusText.Text, "explicit explorer beta selected status");
        AssertGreaterThan(
            selectedAfterAlpha,
            window.ExplicitExplorerTreeSelectedCount,
            "explicit explorer beta selected event count");
        AssertGreaterThan(
            initialUnselectedEvents,
            window.ExplicitExplorerTreeUnselectedCount,
            "explicit explorer alpha unselected event count");
    }

    private static void ValidateRequeryCommand(Window window, MainViewModel viewModel, Button button)
    {
        var command = viewModel.RequeryCommand;
        AssertEqual(command, button.Command, "requery command Button command binding");
        AssertEqual("mvp requery command payload", button.CommandParameter, "requery command Button parameter");

        var canExecuteChangedCount = 0;
        EventHandler handler = (_, _) => canExecuteChangedCount++;
        command.CanExecuteChanged += handler;
        try
        {
            command.CanExecuteValue = false;
            var disabledProbeBaseline = command.CanExecuteProbeCount;
            CommandManager.InvalidateRequerySuggested();
            DrainDispatcher(window);
            AssertGreaterThan(0, canExecuteChangedCount, "requery command CanExecuteChanged count");
            AssertEqual(false, command.CanExecute(button.CommandParameter), "requery command disabled CanExecute state");
            AssertGreaterThan(disabledProbeBaseline, command.CanExecuteProbeCount, "requery command disabled probe count");

            var firstRequeryCount = canExecuteChangedCount;
            var enabledProbeBaseline = command.CanExecuteProbeCount;
            command.CanExecuteValue = true;
            CommandManager.InvalidateRequerySuggested();
            DrainDispatcher(window);
            AssertGreaterThan(firstRequeryCount, canExecuteChangedCount, "requery command second CanExecuteChanged count");
            AssertEqual(true, command.CanExecute(button.CommandParameter), "requery command enabled CanExecute state");
            AssertGreaterThan(enabledProbeBaseline, command.CanExecuteProbeCount, "requery command enabled probe count");

            button.Command.Execute(button.CommandParameter);
            AssertEqual(1, command.ExecuteCount, "requery command execution count");
            AssertEqual("mvp requery command payload", command.LastParameter, "requery command execution parameter");
        }
        finally
        {
            command.CanExecuteChanged -= handler;
        }
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

    private static void ValidateSelectorControls(
        MainWindow window,
        MainViewModel viewModel,
        GroupBox groupBox,
        ComboBox selectedValueComboBox,
        ListBox multiSelectItemsList,
        Expander expander,
        ScrollViewer scrollViewer,
        TextBlock scrollText)
    {
        AssertEqual("Selector container", groupBox.Header, "selector GroupBox header");
        Require<Grid>(groupBox.Content, "selector GroupBox content");

        AssertEqual(viewModel.Items, selectedValueComboBox.ItemsSource, "selected-value ComboBox ItemsSource");
        AssertEqual("Name", selectedValueComboBox.DisplayMemberPath, "selected-value ComboBox display path");
        AssertEqual("Category", selectedValueComboBox.SelectedValuePath, "selected-value ComboBox value path");
        var selectedValueBinding = Require<Binding>(
            BindingOperations.GetBinding(selectedValueComboBox, Selector.SelectedValueProperty),
            "selected-value ComboBox binding");
        AssertEqual("SelectedCategory", selectedValueBinding.Path.Path, "selected-value ComboBox binding path");
        AssertEqual(BindingMode.TwoWay, selectedValueBinding.Mode, "selected-value ComboBox binding mode");

        DrainDispatcher(window);
        AssertEqual("Framework", selectedValueComboBox.SelectedValue, "selected-value ComboBox initial value");
        int initialSelectorEvents = window.SelectorSelectionChangedCount;
        selectedValueComboBox.SelectedItem = viewModel.Items[1];
        DrainDispatcher(window);
        AssertEqual(viewModel.Items[1], selectedValueComboBox.SelectedItem, "selected-value ComboBox selected item");
        AssertEqual("Rendering", selectedValueComboBox.SelectedValue, "selected-value ComboBox selected value");
        AssertEqual("Rendering", viewModel.SelectedCategory, "selected-value ComboBox updated source");
        AssertGreaterThan(
            initialSelectorEvents,
            window.SelectorSelectionChangedCount,
            "selected-value ComboBox SelectionChanged count");

        viewModel.SelectedCategory = "Framework";
        UpdateBinding(selectedValueComboBox, Selector.SelectedValueProperty);
        DrainDispatcher(window);
        AssertEqual("Framework", selectedValueComboBox.SelectedValue, "selected-value ComboBox restored value");
        AssertEqual(viewModel.Items[0], selectedValueComboBox.SelectedItem, "selected-value ComboBox restored item");

        AssertEqual(viewModel.Items, multiSelectItemsList.ItemsSource, "multi-select ListBox ItemsSource");
        AssertEqual("Name", multiSelectItemsList.DisplayMemberPath, "multi-select ListBox display path");
        AssertEqual(SelectionMode.Multiple, multiSelectItemsList.SelectionMode, "multi-select ListBox mode");
        AssertEqual(true, ScrollViewer.GetCanContentScroll(multiSelectItemsList), "multi-select ListBox logical scrolling");
        AssertEqual(true, VirtualizingPanel.GetIsVirtualizing(multiSelectItemsList), "multi-select ListBox virtualization enabled");
        AssertEqual(
            VirtualizationMode.Recycling,
            VirtualizingPanel.GetVirtualizationMode(multiSelectItemsList),
            "multi-select ListBox virtualization mode");
        var virtualizingPanel = Require<VirtualizingStackPanel>(
            multiSelectItemsList.ItemsPanel.LoadContent(),
            "multi-select ListBox virtualizing items panel");
        AssertEqual(Orientation.Vertical, virtualizingPanel.Orientation, "multi-select ListBox virtualizing panel orientation");
        int initialMultiEvents = window.MultiSelectorSelectionChangedCount;
        multiSelectItemsList.SelectedItems.Add(viewModel.Items[0]);
        multiSelectItemsList.SelectedItems.Add(viewModel.Items[1]);
        DrainDispatcher(window);
        AssertEqual(2, multiSelectItemsList.SelectedItems.Count, "multi-select ListBox selected count");
        AssertEqual(true, multiSelectItemsList.SelectedItems.Contains(viewModel.Items[0]), "multi-select ListBox first item");
        AssertEqual(true, multiSelectItemsList.SelectedItems.Contains(viewModel.Items[1]), "multi-select ListBox second item");
        AssertGreaterThan(
            initialMultiEvents,
            window.MultiSelectorSelectionChangedCount,
            "multi-select ListBox SelectionChanged add count");

        int afterAddMultiEvents = window.MultiSelectorSelectionChangedCount;
        multiSelectItemsList.SelectedItems.Remove(viewModel.Items[0]);
        DrainDispatcher(window);
        AssertEqual(1, multiSelectItemsList.SelectedItems.Count, "multi-select ListBox selected removal count");
        AssertEqual(true, multiSelectItemsList.SelectedItems.Contains(viewModel.Items[1]), "multi-select ListBox retained item");
        AssertGreaterThan(
            afterAddMultiEvents,
            window.MultiSelectorSelectionChangedCount,
            "multi-select ListBox SelectionChanged remove count");

        AssertEqual("Scrollable details", expander.Header, "selector Expander header");
        AssertEqual(false, expander.IsExpanded, "selector Expander initial state");
        AssertEqual(ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility, "selector ScrollViewer vertical visibility");
        AssertEqual(ScrollBarVisibility.Disabled, scrollViewer.HorizontalScrollBarVisibility, "selector ScrollViewer horizontal visibility");
        AssertContains("SelectedValuePath", scrollText.Text, "selector ScrollViewer text");

        int initialExpandedEvents = window.SelectorExpanderExpandedCount;
        expander.IsExpanded = true;
        DrainDispatcher(window);
        AssertEqual(true, expander.IsExpanded, "selector Expander expanded state");
        AssertGreaterThan(
            initialExpandedEvents,
            window.SelectorExpanderExpandedCount,
            "selector Expander expanded count");

        int initialCollapsedEvents = window.SelectorExpanderCollapsedCount;
        expander.IsExpanded = false;
        DrainDispatcher(window);
        AssertEqual(false, expander.IsExpanded, "selector Expander restored state");
        AssertGreaterThan(
            initialCollapsedEvents,
            window.SelectorExpanderCollapsedCount,
            "selector Expander collapsed count");
    }

    private static void ValidateResourceControls(
        Window window,
        TextBlock componentResourceText,
        TextBlock localizedResourceText,
        AccessText accessText,
        TextBlock objectProviderText,
        TextBlock xmlProviderText,
        ItemsControl arrayItemsControl,
        TextBlock nullIntrinsicText,
        TextBlock markupExtensionText,
        TextBlock packResourceText,
        TextBlock componentPackResourceText,
        TextBlock startupResourceText,
        TextBlock systemParameterText,
        TextBlock systemFontText,
        Border systemColorBorder,
        TextBlock systemColorText,
        MvpThemedControl themedControl,
        Image drawingImageControl,
        Border drawingImageBrushBorder,
        Border dynamicResourceBorder,
        bool expectStartupResources)
    {
        var componentKey = new ComponentResourceKey(typeof(MainWindow), "MvpComponentAccentBrush");
        var appBrush = Require<SolidColorBrush>(
            Application.Current?.TryFindResource(componentKey),
            "ComponentResourceKey application brush");
        var windowBrush = Require<SolidColorBrush>(
            window.FindResource(componentKey),
            "ComponentResourceKey window brush");
        var textBrush = Require<SolidColorBrush>(
            componentResourceText.Foreground,
            "ComponentResourceKey TextBlock foreground");

        AssertEqual(Color.FromRgb(0x23, 0x6B, 0x46), appBrush.Color, "ComponentResourceKey application brush color");
        AssertEqual(appBrush.Color, windowBrush.Color, "ComponentResourceKey window brush color");
        AssertEqual(appBrush.Color, textBrush.Color, "ComponentResourceKey TextBlock foreground color");
        AssertEqual("Component resource brush", componentResourceText.Text, "ComponentResourceKey TextBlock text");

        AssertEqual("MvpLocalizedResourceText", localizedResourceText.Uid, "localized TextBlock Uid");
        AssertEqual("Localized resource metadata", localizedResourceText.Text, "localized TextBlock text");
        AssertEqual("$Text (Readable Modifiable Text)", Localization.GetAttributes(localizedResourceText), "localized TextBlock attributes");
        AssertEqual("$Text (MVP localization comment)", Localization.GetComments(localizedResourceText), "localized TextBlock comments");

        AssertEqual("_Resource access key", accessText.Text, "AccessText text");
        var objectProvider = Require<ObjectDataProvider>(
            window.FindResource("MvpObjectDataProvider"),
            "MVP ObjectDataProvider resource");
        AssertEqual(false, objectProvider.IsAsynchronous, "ObjectDataProvider synchronous flag");
        AssertEqual("CreateSummary", objectProvider.MethodName, "ObjectDataProvider method name");
        AssertEqual(typeof(MvpResourceFactory), objectProvider.ObjectType, "ObjectDataProvider object type");
        AssertEqual(2, objectProvider.MethodParameters.Count, "ObjectDataProvider method parameter count");
        AssertEqual("mvp-provider", Require<string>(objectProvider.MethodParameters[0], "ObjectDataProvider first parameter"), "ObjectDataProvider first parameter");
        AssertEqual(9, Require<int>(objectProvider.MethodParameters[1], "ObjectDataProvider second parameter"), "ObjectDataProvider second parameter");
        DrainDispatcher(window);
        AssertEqual("mvp-provider:9", objectProvider.Data, "ObjectDataProvider data");
        AssertEqual("mvp-provider:9", objectProviderText.Text, "ObjectDataProvider bound text");
        var objectProviderBinding = Require<Binding>(
            BindingOperations.GetBinding(objectProviderText, TextBlock.TextProperty),
            "ObjectDataProvider TextBlock binding");
        AssertEqual(objectProvider, objectProviderBinding.Source, "ObjectDataProvider binding source");

        var xmlProvider = Require<XmlDataProvider>(
            window.FindResource("MvpXmlDataProvider"),
            "MVP XmlDataProvider resource");
        AssertEqual(false, xmlProvider.IsAsynchronous, "XmlDataProvider synchronous flag");
        AssertEqual("/mvp/item", xmlProvider.XPath, "XmlDataProvider XPath");
        DrainDispatcher(window);
        AssertEqual("mvp-xml", xmlProviderText.Text, "XmlDataProvider bound text");
        var xmlProviderBinding = Require<Binding>(
            BindingOperations.GetBinding(xmlProviderText, TextBlock.TextProperty),
            "XmlDataProvider TextBlock binding");
        AssertEqual(xmlProvider, xmlProviderBinding.Source, "XmlDataProvider binding source");
        AssertEqual("@name", xmlProviderBinding.XPath, "XmlDataProvider binding XPath");

        var arrayItems = Require<string[]>(window.FindResource("MvpStringArray"), "MVP x:Array resource");
        AssertEqual(2, arrayItems.Length, "x:Array resource length");
        AssertEqual("Array alpha", arrayItems[0], "x:Array first item");
        AssertEqual("Array beta", arrayItems[1], "x:Array second item");
        AssertEqual(arrayItems, arrayItemsControl.ItemsSource, "x:Array ItemsControl source");
        AssertEqual(2, arrayItemsControl.Items.Count, "x:Array ItemsControl count");
        AssertEqual(null, nullIntrinsicText.Tag, "x:Null TextBlock tag");
        AssertEqual("Null intrinsic target", nullIntrinsicText.Text, "x:Null TextBlock text");
        AssertEqual("Markup Extension", markupExtensionText.Text, "MarkupExtension TextBlock text");

        AssertEqual("Pack resource loaded from Assets/MvpResource.txt", packResourceText.Text, "pack resource TextBlock text");
        var applicationResources = Application.Current?.Resources
            ?? throw new InvalidOperationException("Expected application resources.");
        var componentPackText = Require<string>(
            applicationResources["MvpComponentPackText"],
            "component pack text resource");
        AssertEqual("Component pack dictionary ready", componentPackText, "component pack text resource");
        AssertEqual(componentPackText, componentPackResourceText.Text, "component pack TextBlock text");
        var componentPackBrush = Require<SolidColorBrush>(
            applicationResources["MvpComponentPackBrush"],
            "component pack brush resource");
        AssertEqual(Color.FromRgb(0x6B, 0x4E, 0x23), componentPackBrush.Color, "component pack brush color");
        var componentPackForeground = Require<SolidColorBrush>(
            componentPackResourceText.Foreground,
            "component pack TextBlock foreground");
        AssertEqual(componentPackBrush.Color, componentPackForeground.Color, "component pack TextBlock foreground color");
        AssertEqual(FontWeights.SemiBold, componentPackResourceText.FontWeight, "component pack TextBlock FontWeight");

        if (expectStartupResources)
        {
            AssertEqual(1, App.StartupEventCount, "Application Startup event count");
            AssertEqual(0, App.StartupArgumentCount, "Application Startup argument count");
            AssertEqual(0, App.ExitEventCount, "Application Exit event count before shutdown");
            AssertEqual(-1, App.LastExitCode, "Application Exit code before shutdown");
            AssertEqual("Startup property ready", Application.Current.Properties["MvpStartupProperty"], "startup application property");
            AssertEqual(0, Application.Current.Properties["MvpStartupArgumentCount"], "startup argument count property");
            AssertEqual("Startup resource ready", applicationResources["MvpStartupText"], "startup application text resource");
            AssertEqual("Startup resource ready", startupResourceText.Text, "startup DynamicResource text");
            var startupBrush = Require<SolidColorBrush>(
                applicationResources["MvpStartupBrush"],
                "startup application brush resource");
            var startupForeground = Require<SolidColorBrush>(
                startupResourceText.Foreground,
                "startup DynamicResource foreground");
            AssertEqual(Color.FromRgb(0x45, 0x5A, 0x64), startupBrush.Color, "startup application brush color");
            AssertEqual(startupBrush.Color, startupForeground.Color, "startup DynamicResource foreground color");
        }

        AssertGreaterThan(
            0,
            (int)Math.Round(SystemParameters.PrimaryScreenWidth),
            "SystemParameters primary screen width");
        AssertContains(
            SystemParameters.PrimaryScreenWidth.ToString(CultureInfo.CurrentCulture),
            systemParameterText.Text,
            "SystemParameters TextBlock text");
        var primaryScreenWidthResource = Require<double>(
            window.TryFindResource(SystemParameters.PrimaryScreenWidthKey),
            "SystemParameters primary screen width resource");
        AssertEqual(
            SystemParameters.PrimaryScreenWidth,
            primaryScreenWidthResource,
            "SystemParameters primary screen width resource value");

        AssertEqual("System font sample", systemFontText.Text, "SystemFonts TextBlock text");
        AssertEqual(
            SystemFonts.MessageFontFamily.Source,
            systemFontText.FontFamily.Source,
            "SystemFonts message font family");
        AssertEqual(SystemFonts.MessageFontSize, systemFontText.FontSize, "SystemFonts message font size");

        var systemWindowBrush = Require<SolidColorBrush>(
            window.FindResource(SystemColors.WindowBrushKey),
            "SystemColors WindowBrush resource");
        var systemWindowTextBrush = Require<SolidColorBrush>(
            window.FindResource(SystemColors.WindowTextBrushKey),
            "SystemColors WindowTextBrush resource");
        var systemBorderBrush = Require<SolidColorBrush>(
            window.FindResource(SystemColors.ControlDarkBrushKey),
            "SystemColors ControlDarkBrush resource");
        var systemColorBackground = Require<SolidColorBrush>(
            systemColorBorder.Background,
            "SystemColors Border background");
        var systemColorForeground = Require<SolidColorBrush>(
            systemColorText.Foreground,
            "SystemColors TextBlock foreground");
        var systemColorBorderBrush = Require<SolidColorBrush>(
            systemColorBorder.BorderBrush,
            "SystemColors Border border brush");
        AssertEqual(systemWindowBrush.Color, systemColorBackground.Color, "SystemColors Border background color");
        AssertEqual(systemWindowTextBrush.Color, systemColorForeground.Color, "SystemColors TextBlock foreground color");
        AssertEqual(systemBorderBrush.Color, systemColorBorderBrush.Color, "SystemColors Border brush color");
        AssertEqual("System color sample", systemColorText.Text, "SystemColors TextBlock text");

        AssertEqual("Generic theme default style", themedControl.Text, "MVP themed control text");
        themedControl.ApplyTemplate();
        var themedTemplate = Require<ControlTemplate>(
            themedControl.Template,
            "MVP themed control default template");
        var themedText = Require<TextBlock>(
            themedTemplate.FindName("ThemeText", themedControl),
            "MVP themed control template text");
        var themedRoot = Require<Border>(
            themedTemplate.FindName("ThemeRoot", themedControl),
            "MVP themed control template root");
        AssertEqual("Generic theme default style", themedText.Text, "MVP themed control template binding");
        var themedForeground = Require<SolidColorBrush>(
            themedText.Foreground,
            "MVP themed control template foreground");
        AssertEqual(Color.FromRgb(0x31, 0x2E, 0x81), themedForeground.Color, "MVP themed control foreground color");
        var themedBackground = Require<SolidColorBrush>(
            themedRoot.Background,
            "MVP themed control template background");
        AssertEqual(Color.FromRgb(0xEE, 0xF2, 0xFF), themedBackground.Color, "MVP themed control background color");
        var themedBorderBrush = Require<SolidColorBrush>(
            themedRoot.BorderBrush,
            "MVP themed control template border brush");
        AssertEqual(Color.FromRgb(0x4F, 0x46, 0xE5), themedBorderBrush.Color, "MVP themed control component resource color");
        AssertEqual(new Thickness(1), themedRoot.BorderThickness, "MVP themed control border thickness");
        AssertEqual(new Thickness(8, 5, 8, 5), themedRoot.Padding, "MVP themed control padding");

        var drawingImage = Require<DrawingImage>(
            window.FindResource("MvpDrawingImage"),
            "MVP DrawingImage resource");
        var drawingGroup = Require<DrawingGroup>(
            drawingImage.Drawing,
            "MVP DrawingImage DrawingGroup");
        AssertEqual(2, drawingGroup.Children.Count, "MVP DrawingImage child count");
        var backgroundDrawing = Require<GeometryDrawing>(
            drawingGroup.Children[0],
            "MVP DrawingImage background drawing");
        var backgroundBrush = Require<SolidColorBrush>(
            backgroundDrawing.Brush,
            "MVP DrawingImage background brush");
        AssertEqual(Color.FromRgb(0x2F, 0x80, 0xED), backgroundBrush.Color, "MVP DrawingImage background color");
        Require<RectangleGeometry>(
            backgroundDrawing.Geometry,
            "MVP DrawingImage background geometry");
        var glyphDrawing = Require<GeometryDrawing>(
            drawingGroup.Children[1],
            "MVP DrawingImage glyph drawing");
        Require<PathGeometry>(
            glyphDrawing.Geometry,
            "MVP DrawingImage glyph geometry");
        AssertEqual(drawingImage, drawingImageControl.Source, "MVP Image source");
        AssertEqual(Stretch.Uniform, drawingImageControl.Stretch, "MVP Image stretch");
        var drawingImageBrush = Require<ImageBrush>(
            window.FindResource("MvpDrawingImageBrush"),
            "MVP DrawingImageBrush resource");
        AssertEqual(drawingImage, drawingImageBrush.ImageSource, "MVP DrawingImageBrush source");
        AssertEqual(Stretch.Uniform, drawingImageBrush.Stretch, "MVP DrawingImageBrush stretch");
        AssertEqual(drawingImageBrush, drawingImageBrushBorder.Background, "MVP DrawingImageBrush Border background");
        ValidateFreezableResources(window);

        var initialDynamicBrush = Require<SolidColorBrush>(
            dynamicResourceBorder.Background,
            "dynamic resource Border initial background");
        AssertEqual(Color.FromRgb(0xF4, 0xF7, 0xFB), initialDynamicBrush.Color, "dynamic resource Border initial background color");

        applicationResources["MvpPanelBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xF2, 0xCC));
        DrainDispatcher(window);
        var updatedDynamicBrush = Require<SolidColorBrush>(
            dynamicResourceBorder.Background,
            "dynamic resource Border updated background");
        AssertEqual(Color.FromRgb(0xFF, 0xF2, 0xCC), updatedDynamicBrush.Color, "dynamic resource Border updated background color");

        applicationResources.Remove("MvpPanelBrush");
        DrainDispatcher(window);
        var restoredDynamicBrush = Require<SolidColorBrush>(
            dynamicResourceBorder.Background,
            "dynamic resource Border restored background");
        AssertEqual(Color.FromRgb(0xF4, 0xF7, 0xFB), restoredDynamicBrush.Color, "dynamic resource Border restored background color");

        var resourceUri = new Uri("pack://application:,,,/Assets/MvpResource.txt", UriKind.Absolute);
        var resourceInfo = Application.GetResourceStream(resourceUri)
            ?? throw new InvalidOperationException("Expected MVP pack resource stream.");
        using var reader = new StreamReader(resourceInfo.Stream);
        AssertEqual(
            "MVP pack resource loaded through Application.GetResourceStream.",
            reader.ReadToEnd().Trim(),
            "pack resource stream text");
    }

    private static void ValidateFreezableResources(Window window)
    {
        var firstSharedFalseBrush = Require<SolidColorBrush>(
            window.FindResource("MvpSharedFalseBrush"),
            "x:Shared=false first brush");
        var secondSharedFalseBrush = Require<SolidColorBrush>(
            window.FindResource("MvpSharedFalseBrush"),
            "x:Shared=false second brush");
        AssertEqual(false, ReferenceEquals(firstSharedFalseBrush, secondSharedFalseBrush), "x:Shared=false brush instance identity");
        AssertEqual(Color.FromRgb(0x8B, 0x5C, 0xF6), firstSharedFalseBrush.Color, "x:Shared=false first brush color");
        AssertEqual(Color.FromRgb(0x8B, 0x5C, 0xF6), secondSharedFalseBrush.Color, "x:Shared=false second brush color");

        var freezableBrush = Require<SolidColorBrush>(
            window.FindResource("MvpFreezableBrush"),
            "MVP Freezable brush");
        AssertEqual(true, freezableBrush.CanFreeze, "Freezable brush CanFreeze");
        freezableBrush.Freeze();
        AssertEqual(true, freezableBrush.IsFrozen, "Freezable brush frozen state");
        var mutableClone = Require<SolidColorBrush>(
            freezableBrush.Clone(),
            "Freezable brush clone");
        AssertEqual(false, mutableClone.IsFrozen, "Freezable brush clone mutable state");
        mutableClone.Opacity = 0.5;
        AssertEqual(0.5, mutableClone.Opacity, "Freezable brush clone opacity");
        var currentValueClone = Require<SolidColorBrush>(
            mutableClone.CloneCurrentValue(),
            "Freezable brush current-value clone");
        AssertEqual(0.5, currentValueClone.Opacity, "Freezable brush current-value clone opacity");
    }

    private static void ValidateLayoutControls(
        DockPanel dockPanel,
        Border dockTop,
        Border dockLeft,
        Border dockRight,
        TextBlock dockFillText,
        WrapPanel wrapPanel,
        UniformGrid uniformGrid,
        Grid splitterGrid,
        ColumnDefinition splitterLeftColumn,
        ColumnDefinition splitterRightColumn,
        Border splitterLeftPane,
        GridSplitter gridSplitter,
        Border splitterRightPane,
        Viewbox viewbox,
        TextBlock viewboxText)
    {
        AssertEqual(true, dockPanel.LastChildFill, "DockPanel LastChildFill");
        AssertEqual(4, dockPanel.Children.Count, "DockPanel child count");
        AssertEqual(Dock.Top, DockPanel.GetDock(dockTop), "DockPanel top attached Dock");
        AssertEqual(Dock.Left, DockPanel.GetDock(dockLeft), "DockPanel left attached Dock");
        AssertEqual(Dock.Right, DockPanel.GetDock(dockRight), "DockPanel right attached Dock");
        AssertEqual("Fill content", dockFillText.Text, "DockPanel fill text");

        AssertEqual(Orientation.Horizontal, wrapPanel.Orientation, "WrapPanel orientation");
        AssertEqual(90.0, wrapPanel.ItemWidth, "WrapPanel item width");
        AssertEqual(28.0, wrapPanel.ItemHeight, "WrapPanel item height");
        AssertEqual(3, wrapPanel.Children.Count, "WrapPanel child count");
        var thirdWrapButton = Require<Button>(wrapPanel.Children[2], "third WrapPanel Button");
        AssertEqual("Three", thirdWrapButton.Content, "third WrapPanel button content");

        AssertEqual(2, uniformGrid.Rows, "UniformGrid rows");
        AssertEqual(3, uniformGrid.Columns, "UniformGrid columns");
        AssertEqual(1, uniformGrid.FirstColumn, "UniformGrid first column");
        AssertEqual(3, uniformGrid.Children.Count, "UniformGrid child count");
        var secondUniformText = Require<TextBlock>(uniformGrid.Children[1], "second UniformGrid TextBlock");
        AssertEqual("Beta", secondUniformText.Text, "UniformGrid second child text");

        AssertEqual(3, splitterGrid.ColumnDefinitions.Count, "GridSplitter grid column count");
        AssertEqual(splitterLeftColumn, splitterGrid.ColumnDefinitions[0], "GridSplitter left column reference");
        AssertEqual(splitterRightColumn, splitterGrid.ColumnDefinitions[2], "GridSplitter right column reference");
        AssertEqual(120.0, splitterLeftColumn.Width.Value, "GridSplitter left column width");
        AssertEqual(true, splitterRightColumn.Width.IsStar, "GridSplitter right column star width");
        AssertEqual(0, Grid.GetColumn(splitterLeftPane), "GridSplitter left pane column");
        AssertEqual(1, Grid.GetColumn(gridSplitter), "GridSplitter column");
        AssertEqual(2, Grid.GetColumn(splitterRightPane), "GridSplitter right pane column");
        AssertEqual(6.0, gridSplitter.Width, "GridSplitter width");
        AssertEqual(GridResizeBehavior.PreviousAndNext, gridSplitter.ResizeBehavior, "GridSplitter resize behavior");
        AssertEqual(false, gridSplitter.ShowsPreview, "GridSplitter preview state");
        AssertEqual(12.0, gridSplitter.KeyboardIncrement, "GridSplitter keyboard increment");
        AssertEqual(HorizontalAlignment.Stretch, gridSplitter.HorizontalAlignment, "GridSplitter horizontal alignment");
        AssertEqual(VerticalAlignment.Stretch, gridSplitter.VerticalAlignment, "GridSplitter vertical alignment");

        splitterLeftColumn.Width = new GridLength(150.0);
        AssertEqual(150.0, splitterLeftColumn.Width.Value, "GridSplitter left column updated width");
        splitterLeftColumn.Width = new GridLength(120.0);

        AssertEqual(Stretch.Uniform, viewbox.Stretch, "Viewbox stretch");
        AssertEqual(54.0, viewbox.MaxHeight, "Viewbox max height");
        AssertEqual(viewboxText, viewbox.Child, "Viewbox child reference");
        AssertEqual("Scaled layout content", viewboxText.Text, "Viewbox text");
    }

    private static void ValidateInputControls(
        MainWindow window,
        MainViewModel viewModel,
        ToolBarTray toolBarTray,
        ToolBar toolBar,
        Button refreshButton,
        Separator toolBarSeparator,
        ToggleButton toolBarToggle,
        Button popupOwnerButton,
        Popup inputPopup,
        ToggleButton inputToggle,
        RadioButton frameworkRadio,
        RadioButton renderingRadio,
        RepeatButton repeatButton,
        WpfCalendar calendar,
        DatePicker datePicker,
        StackPanel keyboardNavigationPanel,
        Label keyboardNavigationAccessLabel,
        TextBox keyboardNavigationFirstBox,
        Button keyboardNavigationSecondButton,
        TextBox keyboardNavigationThirdBox)
    {
        AssertEqual(1, toolBarTray.ToolBars.Count, "MVP ToolBarTray toolbar count");
        AssertEqual(toolBar, toolBarTray.ToolBars[0], "MVP ToolBarTray toolbar reference");
        AssertEqual("MVP tools", toolBar.Header, "MVP ToolBar header");
        AssertEqual(3, toolBar.Items.Count, "MVP ToolBar item count");
        AssertEqual(refreshButton, toolBar.Items[0], "MVP ToolBar refresh item");
        AssertEqual(toolBarSeparator, toolBar.Items[1], "MVP ToolBar separator item");
        AssertEqual(toolBarToggle, toolBar.Items[2], "MVP ToolBar toggle item");
        AssertEqual(MainWindow.RefreshStatusCommand, refreshButton.Command, "MVP ToolBar refresh command");

        var toolTip = Require<ToolTip>(refreshButton.ToolTip, "toolbar refresh ToolTip");
        var toolTipText = Require<TextBlock>(toolTip.Content, "toolbar refresh ToolTip text");
        AssertEqual(PlacementMode.Bottom, toolTip.Placement, "toolbar refresh ToolTip placement");
        AssertEqual("Refresh status command", toolTipText.Text, "toolbar refresh ToolTip text");

        AssertEqual(popupOwnerButton, inputPopup.PlacementTarget, "input Popup placement target");
        AssertEqual(PlacementMode.Bottom, inputPopup.Placement, "input Popup placement");
        AssertEqual(false, inputPopup.StaysOpen, "input Popup StaysOpen");
        AssertEqual(true, inputPopup.AllowsTransparency, "input Popup AllowsTransparency");
        AssertEqual(false, inputPopup.IsOpen, "input Popup initial open state");
        var popupBorder = Require<Border>(inputPopup.Child, "input Popup Border");
        var popupText = Require<TextBlock>(popupBorder.Child, "input Popup TextBlock");
        AssertEqual("Popup content", popupText.Text, "input Popup text");

        ValidateToggleBinding(window, viewModel, toolBarToggle, "toolbar ToggleButton");
        ValidateToggleBinding(window, viewModel, inputToggle, "input ToggleButton");

        AssertEqual("MvpCategory", frameworkRadio.GroupName, "framework RadioButton group");
        AssertEqual("MvpCategory", renderingRadio.GroupName, "rendering RadioButton group");
        AssertEqual("Framework", frameworkRadio.Tag, "framework RadioButton tag");
        AssertEqual("Rendering", renderingRadio.Tag, "rendering RadioButton tag");
        AssertEqual(true, frameworkRadio.IsChecked == true, "framework RadioButton initial state");
        AssertEqual(false, renderingRadio.IsChecked == true, "rendering RadioButton initial state");

        int initialRadioEvents = window.CategoryRadioCheckedCount;
        renderingRadio.IsChecked = true;
        DrainDispatcher(window);
        AssertEqual(false, frameworkRadio.IsChecked == true, "framework RadioButton unchecked state");
        AssertEqual(true, renderingRadio.IsChecked == true, "rendering RadioButton checked state");
        AssertEqual("RenderingRadioButton", window.LastCategoryRadioName, "last checked RadioButton name");
        AssertEqual("Rendering", viewModel.SelectedCategory, "RadioButton updated selected category");
        AssertGreaterThan(initialRadioEvents, window.CategoryRadioCheckedCount, "RadioButton checked event count");

        frameworkRadio.IsChecked = true;
        DrainDispatcher(window);
        AssertEqual(true, frameworkRadio.IsChecked == true, "framework RadioButton restored state");
        AssertEqual(false, renderingRadio.IsChecked == true, "rendering RadioButton restored state");
        AssertEqual("FrameworkRadioButton", window.LastCategoryRadioName, "last restored RadioButton name");
        AssertEqual("Framework", viewModel.SelectedCategory, "RadioButton restored selected category");

        AssertEqual(180, repeatButton.Delay, "RepeatButton delay");
        AssertEqual(70, repeatButton.Interval, "RepeatButton interval");
        AssertEqual("Repeat action", repeatButton.Content, "RepeatButton content");
        int initialRepeatClicks = window.InputRepeatButtonClickCount;
        repeatButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, repeatButton));
        DrainDispatcher(window);
        AssertEqual(initialRepeatClicks + 1, window.InputRepeatButtonClickCount, "RepeatButton click count");

        var expectedInitialDate = new DateTime(2026, 6, 23);
        AssertEqual(CalendarSelectionMode.SingleDate, calendar.SelectionMode, "Calendar selection mode");
        AssertEqual(expectedInitialDate, calendar.SelectedDate, "Calendar initial selected date");
        AssertEqual(expectedInitialDate, datePicker.SelectedDate, "DatePicker initial selected date");
        AssertEqual(expectedInitialDate, viewModel.SelectedDate, "view model initial selected date");
        AssertEqual("SelectedDate", GetSelectedDateBindingPath(calendar), "Calendar SelectedDate binding path");
        AssertEqual("SelectedDate", GetSelectedDateBindingPath(datePicker), "DatePicker SelectedDate binding path");

        int initialDateEvents = window.InputDateSelectionChangedCount;
        datePicker.SelectedDate = new DateTime(2026, 6, 24);
        UpdateSource(datePicker, DatePicker.SelectedDateProperty);
        UpdateBinding(calendar, WpfCalendar.SelectedDateProperty);
        DrainDispatcher(window);
        AssertEqual(new DateTime(2026, 6, 24), viewModel.SelectedDate, "DatePicker updated view model date");
        AssertEqual(new DateTime(2026, 6, 24), calendar.SelectedDate, "DatePicker updated Calendar date");
        AssertEqual("InputDatePicker", window.LastDateSelectionSenderName, "DatePicker selection sender");
        AssertGreaterThan(initialDateEvents, window.InputDateSelectionChangedCount, "DatePicker selection event count");

        int afterDatePickerEvents = window.InputDateSelectionChangedCount;
        calendar.SelectedDate = new DateTime(2026, 6, 25);
        UpdateSource(calendar, WpfCalendar.SelectedDateProperty);
        UpdateBinding(datePicker, DatePicker.SelectedDateProperty);
        DrainDispatcher(window);
        AssertEqual(new DateTime(2026, 6, 25), viewModel.SelectedDate, "Calendar updated view model date");
        AssertEqual(new DateTime(2026, 6, 25), datePicker.SelectedDate, "Calendar updated DatePicker date");
        AssertEqual(1, calendar.SelectedDates.Count, "Calendar selected dates count");
        AssertEqual(new DateTime(2026, 6, 25), calendar.SelectedDates[0], "Calendar selected date collection item");
        AssertEqual("InputCalendar", window.LastDateSelectionSenderName, "Calendar selection sender");
        AssertGreaterThan(afterDatePickerEvents, window.InputDateSelectionChangedCount, "Calendar selection event count");

        ValidateKeyboardNavigation(
            window,
            keyboardNavigationPanel,
            keyboardNavigationAccessLabel,
            keyboardNavigationFirstBox,
            keyboardNavigationSecondButton,
            keyboardNavigationThirdBox);
    }

    private static void ValidateKeyboardNavigation(
        Window window,
        StackPanel panel,
        Label accessLabel,
        TextBox firstBox,
        Button secondButton,
        TextBox thirdBox)
    {
        AssertEqual(true, FocusManager.GetIsFocusScope(panel), "keyboard navigation focus-scope flag");
        AssertEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(panel), "keyboard navigation tab mode");
        AssertEqual(
            KeyboardNavigationMode.Cycle,
            KeyboardNavigation.GetControlTabNavigation(panel),
            "keyboard navigation control-tab mode");
        AssertEqual(
            KeyboardNavigationMode.Contained,
            KeyboardNavigation.GetDirectionalNavigation(panel),
            "keyboard navigation directional mode");
        AssertEqual(0, firstBox.TabIndex, "first keyboard navigation TabIndex");
        AssertEqual(1, secondButton.TabIndex, "second keyboard navigation TabIndex");
        AssertEqual(2, thirdBox.TabIndex, "third keyboard navigation TabIndex");
        AssertEqual("_First focus target", accessLabel.Content, "keyboard navigation access Label content");
        AssertEqual(firstBox, accessLabel.Target, "keyboard navigation access Label target");
        AssertEqual("First focus target", firstBox.Text, "first keyboard navigation text");
        AssertEqual("Second focus target", secondButton.Content, "second keyboard navigation content");
        AssertEqual("Third focus target", thirdBox.Text, "third keyboard navigation text");
        AssertEqual(firstBox, FocusManager.GetFocusedElement(panel), "initial keyboard navigation logical focus");

        FocusManager.SetFocusedElement(panel, secondButton);
        DrainDispatcher(window);
        AssertEqual(secondButton, FocusManager.GetFocusedElement(panel), "updated keyboard navigation logical focus");

        FocusManager.SetFocusedElement(panel, thirdBox);
        DrainDispatcher(window);
        AssertEqual(thirdBox, FocusManager.GetFocusedElement(panel), "third keyboard navigation logical focus");

        FocusManager.SetFocusedElement(panel, firstBox);
        DrainDispatcher(window);
        AssertEqual(firstBox, FocusManager.GetFocusedElement(panel), "restored keyboard navigation logical focus");
    }

    private static void ValidateToggleBinding(
        MainWindow window,
        MainViewModel viewModel,
        ToggleButton toggleButton,
        string description)
    {
        var binding = Require<Binding>(
            BindingOperations.GetBinding(toggleButton, ToggleButton.IsCheckedProperty),
            $"{description} IsChecked binding");
        AssertEqual("ActionsEnabled", binding.Path.Path, $"{description} IsChecked path");
        AssertEqual(BindingMode.TwoWay, binding.Mode, $"{description} IsChecked mode");
        AssertEqual(true, toggleButton.IsChecked == true, $"{description} initial checked state");

        int initialUncheckedEvents = window.InputToggleUncheckedCount;
        toggleButton.IsChecked = false;
        DrainDispatcher(window);
        AssertEqual(false, viewModel.ActionsEnabled, $"{description} unchecked view model state");
        AssertEqual(false, toggleButton.IsChecked == true, $"{description} unchecked state");
        AssertGreaterThan(initialUncheckedEvents, window.InputToggleUncheckedCount, $"{description} unchecked event count");

        int initialCheckedEvents = window.InputToggleCheckedCount;
        toggleButton.IsChecked = true;
        DrainDispatcher(window);
        AssertEqual(true, viewModel.ActionsEnabled, $"{description} restored view model state");
        AssertEqual(true, toggleButton.IsChecked == true, $"{description} restored checked state");
        AssertGreaterThan(initialCheckedEvents, window.InputToggleCheckedCount, $"{description} checked event count");
    }

    private static string GetColumnBindingPath(DataGridColumn column)
    {
        return column is DataGridBoundColumn { Binding: Binding binding }
            ? binding.Path.Path
            : throw new InvalidOperationException($"Expected {column.Header} column to have a Binding.");
    }

    private static string GetGridViewColumnBindingPath(GridViewColumn column)
    {
        return column.DisplayMemberBinding is Binding { Path: { } path }
            ? path.Path
            : throw new InvalidOperationException($"Expected {column.Header} column to have a display member Binding.");
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

    private static void ValidateImplicitItemTemplate(
        MainViewModel viewModel,
        ContentControl contentControl,
        DataTemplate template)
    {
        AssertEqual(viewModel.SelectedItem, contentControl.Content, "implicit item content");
        AssertEqual<DataTemplate?>(null, contentControl.ContentTemplate, "implicit item explicit ContentTemplate");

        var templateKey = Require<DataTemplateKey>(template.DataTemplateKey, "implicit item DataTemplate key");
        AssertEqual(typeof(MvpItem), templateKey.DataType, "implicit item DataTemplate key type");

        var root = Require<FrameworkElement>(
            template.LoadContent(),
            "implicit item template root");
        var nameText = Require<TextBlock>(
            root.FindName("ImplicitTemplateNameText"),
            "implicit item template name TextBlock");
        var categoryText = Require<TextBlock>(
            root.FindName("ImplicitTemplateCategoryText"),
            "implicit item template category TextBlock");

        AssertEqual("Name", GetTextBindingPath(nameText), "implicit item template name binding path");
        AssertEqual("Category", GetTextBindingPath(categoryText), "implicit item template category binding path");
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

    private static void ValidateItemContainerStyleSelector(
        MainViewModel viewModel,
        ListBox styleSelectorItemsList,
        Style activeStyle,
        Style inactiveStyle,
        MvpItemContainerStyleSelector selector)
    {
        AssertEqual(viewModel.Items, styleSelectorItemsList.ItemsSource, "style selector ListBox items source");
        AssertEqual("Name", styleSelectorItemsList.DisplayMemberPath, "style selector ListBox DisplayMemberPath");
        AssertEqual(
            selector,
            styleSelectorItemsList.ItemContainerStyleSelector,
            "style selector ListBox ItemContainerStyleSelector");
        AssertEqual(activeStyle, selector.ActiveStyle, "active item container selector style");
        AssertEqual(inactiveStyle, selector.InactiveStyle, "inactive item container selector style");
        AssertEqual(activeStyle, selector.SelectStyle(viewModel.Items[0], styleSelectorItemsList), "active item container selector result");
        AssertEqual(inactiveStyle, selector.SelectStyle(viewModel.Items[1], styleSelectorItemsList), "inactive item container selector result");
        AssertEqual(inactiveStyle, selector.SelectStyle(new object(), styleSelectorItemsList), "fallback item container selector result");

        ValidateSelectedItemContainerStyle(activeStyle, "active", "ActiveStyleContainer");
        ValidateSelectedItemContainerStyle(inactiveStyle, "inactive", "InactiveStyleContainer");
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

    private static void ValidateSelectedItemContainerStyle(
        Style style,
        string description,
        string expectedTag)
    {
        AssertEqual(typeof(ListBoxItem), style.TargetType, $"{description} item container style target type");
        AssertEqual(3, style.Setters.Count, $"{description} item container setter count");

        var tagSetter = Require<Setter>(
            style.Setters[0],
            $"{description} item container Tag setter");
        var marginSetter = Require<Setter>(
            style.Setters[1],
            $"{description} item container Margin setter");
        var alignmentSetter = Require<Setter>(
            style.Setters[2],
            $"{description} item container HorizontalContentAlignment setter");

        AssertEqual(FrameworkElement.TagProperty, tagSetter.Property, $"{description} item container Tag property");
        AssertEqual(expectedTag, tagSetter.Value, $"{description} item container Tag value");
        AssertEqual(FrameworkElement.MarginProperty, marginSetter.Property, $"{description} item container Margin property");
        AssertEqual(new Thickness(0, 0, 0, 4), marginSetter.Value, $"{description} item container Margin value");
        AssertEqual(Control.HorizontalContentAlignmentProperty, alignmentSetter.Property, $"{description} item container alignment property");
        AssertEqual(HorizontalAlignment.Stretch, alignmentSetter.Value, $"{description} item container alignment value");
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

    private static void ValidateBasedOnButton(Button button, Style style)
    {
        AssertEqual(style, button.Style, "BasedOn Button style");
        AssertEqual("BasedOn style", button.Content, "BasedOn Button content");
        AssertEqual(3, style.Setters.Count, "BasedOn Button derived setter count");
        AssertEqual("BasedOnStyle", button.Tag, "BasedOn Button derived Tag setter");
        AssertEqual(104.0, button.MinWidth, "BasedOn Button inherited MinWidth setter");
        AssertEqual(new Thickness(10, 5, 10, 5), button.Padding, "BasedOn Button inherited Padding setter");

        var background = Require<SolidColorBrush>(button.Background, "BasedOn Button background");
        var foreground = Require<SolidColorBrush>(button.Foreground, "BasedOn Button foreground");
        AssertEqual(Color.FromRgb(0x24, 0x6B, 0xFE), background.Color, "BasedOn Button derived background color");
        AssertEqual(Colors.White, foreground.Color, "BasedOn Button derived foreground color");
    }

    private static void ValidateStyleTriggersAndEventSetter(
        MainWindow window,
        MainViewModel viewModel,
        TextBlock triggerText,
        Style triggerStyle,
        TextBlock multiTriggerText,
        Style multiTriggerStyle,
        TextBlock multiDataTriggerText,
        Style multiDataTriggerStyle,
        Button eventSetterButton,
        Style eventSetterStyle,
        TextBlock eventSetterStatus)
    {
        AssertEqual(triggerStyle, triggerText.Style, "style trigger TextBlock style");
        AssertEqual(2, triggerStyle.Setters.Count, "style trigger setter count");
        AssertEqual(2, triggerStyle.Triggers.Count, "style trigger count");
        var baseTextStyle = Require<Style>(
            Application.Current?.TryFindResource(typeof(TextBlock)),
            "implicit TextBlock style");
        AssertEqual(baseTextStyle, triggerStyle.BasedOn, "style trigger BasedOn TextBlock style");

        var propertyTrigger = Require<Trigger>(
            triggerStyle.Triggers[0],
            "property style Trigger");
        AssertEqual(FrameworkElement.TagProperty, propertyTrigger.Property, "property style Trigger property");
        AssertEqual("Active", propertyTrigger.Value, "property style Trigger value");

        var dataTrigger = Require<DataTrigger>(
            triggerStyle.Triggers[1],
            "data style Trigger");
        AssertEqual("ActionsEnabled", GetBindingPath(dataTrigger.Binding), "data style Trigger binding path");
        AssertEqual("False", dataTrigger.Value?.ToString(), "data style Trigger value");

        AssertEqual(multiTriggerStyle, multiTriggerText.Style, "MultiTrigger TextBlock style");
        AssertEqual(2, multiTriggerStyle.Setters.Count, "MultiTrigger style setter count");
        AssertEqual(1, multiTriggerStyle.Triggers.Count, "MultiTrigger style trigger count");
        var multiTrigger = Require<MultiTrigger>(
            multiTriggerStyle.Triggers[0],
            "MultiTrigger style trigger");
        AssertEqual(2, multiTrigger.Conditions.Count, "MultiTrigger condition count");
        AssertEqual(FrameworkElement.TagProperty, multiTrigger.Conditions[0].Property, "MultiTrigger first property");
        AssertEqual("Ready", multiTrigger.Conditions[0].Value, "MultiTrigger first value");
        AssertEqual(UIElement.IsEnabledProperty, multiTrigger.Conditions[1].Property, "MultiTrigger second property");
        AssertEqual("True", multiTrigger.Conditions[1].Value?.ToString(), "MultiTrigger second value");

        AssertEqual(multiDataTriggerStyle, multiDataTriggerText.Style, "MultiDataTrigger TextBlock style");
        AssertEqual(2, multiDataTriggerStyle.Setters.Count, "MultiDataTrigger style setter count");
        AssertEqual(1, multiDataTriggerStyle.Triggers.Count, "MultiDataTrigger style trigger count");
        var multiDataTrigger = Require<MultiDataTrigger>(
            multiDataTriggerStyle.Triggers[0],
            "MultiDataTrigger style trigger");
        AssertEqual(2, multiDataTrigger.Conditions.Count, "MultiDataTrigger condition count");
        AssertEqual("ActionsEnabled", GetBindingPath(multiDataTrigger.Conditions[0].Binding), "MultiDataTrigger first binding");
        AssertEqual("False", multiDataTrigger.Conditions[0].Value?.ToString(), "MultiDataTrigger first value");
        AssertEqual("SelectedCategory", GetBindingPath(multiDataTrigger.Conditions[1].Binding), "MultiDataTrigger second binding");
        AssertEqual("Input", multiDataTrigger.Conditions[1].Value, "MultiDataTrigger second value");

        DrainDispatcher(window);
        AssertEqual("style trigger inactive", triggerText.Text, "style trigger initial text");
        AssertEqual(
            Color.FromRgb(0x5B, 0x64, 0x72),
            Require<SolidColorBrush>(triggerText.Foreground, "style trigger initial foreground").Color,
            "style trigger initial foreground");
        AssertEqual("multi trigger inactive", multiTriggerText.Text, "MultiTrigger initial text");
        AssertEqual("multi data trigger inactive", multiDataTriggerText.Text, "MultiDataTrigger initial text");

        triggerText.Tag = "Active";
        DrainDispatcher(window);
        AssertEqual("property trigger active", triggerText.Text, "property style Trigger active text");
        AssertEqual(
            Color.FromRgb(0x24, 0x6B, 0xFE),
            Require<SolidColorBrush>(triggerText.Foreground, "property style Trigger foreground").Color,
            "property style Trigger foreground");

        multiTriggerText.Tag = "Ready";
        DrainDispatcher(window);
        AssertEqual("multi trigger active", multiTriggerText.Text, "MultiTrigger active text");
        AssertEqual(
            Color.FromRgb(0x23, 0x6B, 0x46),
            Require<SolidColorBrush>(multiTriggerText.Foreground, "MultiTrigger active foreground").Color,
            "MultiTrigger active foreground");

        multiTriggerText.IsEnabled = false;
        DrainDispatcher(window);
        AssertEqual("multi trigger inactive", multiTriggerText.Text, "MultiTrigger disabled condition text");
        multiTriggerText.IsEnabled = true;
        multiTriggerText.Tag = null;
        DrainDispatcher(window);
        AssertEqual("multi trigger inactive", multiTriggerText.Text, "MultiTrigger restored text");

        viewModel.ActionsEnabled = false;
        DrainDispatcher(window);
        AssertEqual("data trigger disabled", triggerText.Text, "data style Trigger disabled text");
        AssertEqual(
            Color.FromRgb(0xB4, 0x23, 0x18),
            Require<SolidColorBrush>(triggerText.Foreground, "data style Trigger foreground").Color,
            "data style Trigger foreground");

        viewModel.ActionsEnabled = true;
        DrainDispatcher(window);
        AssertEqual("property trigger active", triggerText.Text, "restored property style Trigger text");

        viewModel.SelectedCategory = "Input";
        viewModel.ActionsEnabled = false;
        DrainDispatcher(window);
        AssertEqual("multi data trigger active", multiDataTriggerText.Text, "MultiDataTrigger active text");
        AssertEqual(
            Color.FromRgb(0xB4, 0x23, 0x18),
            Require<SolidColorBrush>(multiDataTriggerText.Foreground, "MultiDataTrigger active foreground").Color,
            "MultiDataTrigger active foreground");

        viewModel.ActionsEnabled = true;
        viewModel.SelectedCategory = "Framework";
        DrainDispatcher(window);
        AssertEqual("multi data trigger inactive", multiDataTriggerText.Text, "MultiDataTrigger restored text");
        triggerText.Tag = null;
        DrainDispatcher(window);
        AssertEqual("style trigger inactive", triggerText.Text, "restored style trigger inactive text");

        AssertEqual(eventSetterStyle, eventSetterButton.Style, "EventSetter Button style");
        AssertEqual("EventSetter action", eventSetterButton.Content, "EventSetter Button content");
        AssertEqual("EventSetterStyle", eventSetterButton.Tag, "EventSetter Button setter Tag");
        AssertEqual(2, eventSetterStyle.Setters.Count, "EventSetter style setter count");
        var eventSetter = Require<EventSetter>(
            eventSetterStyle.Setters[1],
            "Button Click EventSetter");
        AssertEqual(ButtonBase.ClickEvent, eventSetter.Event, "EventSetter routed event");
        AssertEqual("EventSetter idle", eventSetterStatus.Text, "EventSetter initial status");
        AssertEqual(0, window.MvpStyleEventSetterClickCount, "EventSetter initial click count");

        var clickArgs = new RoutedEventArgs(ButtonBase.ClickEvent, eventSetterButton);
        eventSetterButton.RaiseEvent(clickArgs);
        DrainDispatcher(window);
        AssertEqual(true, clickArgs.Handled, "EventSetter handled flag");
        AssertEqual(1, window.MvpStyleEventSetterClickCount, "EventSetter click count");
        AssertEqual("EventSetterStyleButton", window.LastMvpStyleEventSetterSenderName, "EventSetter sender name");
        AssertEqual("Click", window.LastMvpStyleEventSetterRoutedEventName, "EventSetter routed event name");
        AssertEqual("EventSetter clicked", eventSetterStatus.Text, "EventSetter updated status");
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
        AssertEqual(1.0, contentPresenter.Opacity, "template Button Normal visual state opacity");

        var visualStateGroups = VisualStateManager.GetVisualStateGroups(border);
        AssertEqual(1, visualStateGroups.Count, "template Button VisualStateGroup count");
        var commonStates = Require<VisualStateGroup>(
            visualStateGroups[0],
            "template Button CommonStates group");
        AssertEqual("CommonStates", commonStates.Name, "template Button VisualStateGroup name");
        AssertEqual(2, commonStates.States.Count, "template Button VisualState count");
        var normalState = Require<VisualState>(
            commonStates.States[0],
            "template Button Normal VisualState");
        var pressedState = Require<VisualState>(
            commonStates.States[1],
            "template Button Pressed VisualState");
        AssertEqual("Normal", normalState.Name, "template Button Normal VisualState name");
        AssertEqual("Pressed", pressedState.Name, "template Button Pressed VisualState name");
        AssertEqual(1, pressedState.Storyboard?.Children.Count ?? 0, "template Button Pressed storyboard child count");
        var pressedAnimation = Require<DoubleAnimation>(
            pressedState.Storyboard?.Children[0],
            "template Button Pressed DoubleAnimation");
        AssertEqual(
            "TemplateContentPresenter",
            Storyboard.GetTargetName(pressedAnimation),
            "template Button Pressed animation target");
        AssertEqual(
            "Opacity",
            Storyboard.GetTargetProperty(pressedAnimation).Path,
            "template Button Pressed animation property");
        AssertEqual(0.72, pressedAnimation.To, "template Button Pressed animation target opacity");
        AssertEqual(TimeSpan.Zero, pressedAnimation.Duration.TimeSpan, "template Button Pressed animation duration");

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

    private static void ValidateDataErrorValidation(
        Window window,
        MainViewModel viewModel,
        TextBox textBox,
        TextBlock echoText)
    {
        var binding = Require<Binding>(
            BindingOperations.GetBinding(textBox, TextBox.TextProperty),
            "IDataErrorInfo TextBox binding");

        AssertEqual("DataErrorText", binding.Path.Path, "IDataErrorInfo binding path");
        AssertEqual(BindingMode.TwoWay, binding.Mode, "IDataErrorInfo binding mode");
        AssertEqual(UpdateSourceTrigger.Explicit, binding.UpdateSourceTrigger, "IDataErrorInfo update trigger");
        AssertEqual(true, binding.NotifyOnValidationError, "IDataErrorInfo notification flag");
        AssertEqual(true, binding.ValidatesOnDataErrors, "IDataErrorInfo validation flag");

        DrainDispatcher(window);
        AssertEqual("data: ready", textBox.Text, "initial IDataErrorInfo TextBox text");
        AssertEqual("Data: data: ready", echoText.Text, "initial IDataErrorInfo echo text");

        var bindingExpression = Require<BindingExpression>(
            BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty),
            "IDataErrorInfo TextBox binding expression");
        textBox.Text = "broken";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual("broken", viewModel.DataErrorText, "invalid IDataErrorInfo source update");
        AssertEqual(true, Validation.GetHasError(textBox), "invalid IDataErrorInfo error flag");
        AssertEqual(
            "Data value must start with data:",
            GetSingleValidationErrorContent(textBox, "invalid IDataErrorInfo error"),
            "invalid IDataErrorInfo error content");
        AssertEqual("Data: broken", echoText.Text, "invalid IDataErrorInfo echo text");

        textBox.Text = "data: updated";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual(false, Validation.GetHasError(textBox), "valid IDataErrorInfo clears error flag");
        AssertEqual("data: updated", viewModel.DataErrorText, "valid IDataErrorInfo updates source");
        AssertEqual("Data: data: updated", echoText.Text, "updated IDataErrorInfo echo text");
    }

    private static void ValidateNotifyDataErrorValidation(
        Window window,
        MainViewModel viewModel,
        TextBox textBox,
        TextBlock echoText)
    {
        var binding = Require<Binding>(
            BindingOperations.GetBinding(textBox, TextBox.TextProperty),
            "INotifyDataErrorInfo TextBox binding");

        AssertEqual("NotifyDataErrorText", binding.Path.Path, "INotifyDataErrorInfo binding path");
        AssertEqual(BindingMode.TwoWay, binding.Mode, "INotifyDataErrorInfo binding mode");
        AssertEqual(UpdateSourceTrigger.Explicit, binding.UpdateSourceTrigger, "INotifyDataErrorInfo update trigger");
        AssertEqual(true, binding.NotifyOnValidationError, "INotifyDataErrorInfo notification flag");
        AssertEqual(true, binding.ValidatesOnNotifyDataErrors, "INotifyDataErrorInfo validation flag");

        DrainDispatcher(window);
        AssertEqual("notify: ready", textBox.Text, "initial INotifyDataErrorInfo TextBox text");
        AssertEqual("Notify: notify: ready", echoText.Text, "initial INotifyDataErrorInfo echo text");
        AssertEqual(false, viewModel.HasErrors, "initial INotifyDataErrorInfo source error state");

        var bindingExpression = Require<BindingExpression>(
            BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty),
            "INotifyDataErrorInfo TextBox binding expression");
        textBox.Text = "broken";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual("broken", viewModel.NotifyDataErrorText, "invalid INotifyDataErrorInfo source update");
        AssertEqual(true, viewModel.HasErrors, "invalid INotifyDataErrorInfo source error state");
        AssertEqual(true, Validation.GetHasError(textBox), "invalid INotifyDataErrorInfo error flag");
        AssertEqual(
            "Notify value must start with notify:",
            GetSingleValidationErrorContent(textBox, "invalid INotifyDataErrorInfo error"),
            "invalid INotifyDataErrorInfo error content");
        AssertEqual("Notify: broken", echoText.Text, "invalid INotifyDataErrorInfo echo text");

        textBox.Text = "notify: updated";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual(false, viewModel.HasErrors, "valid INotifyDataErrorInfo source error state");
        AssertEqual(false, Validation.GetHasError(textBox), "valid INotifyDataErrorInfo clears error flag");
        AssertEqual(
            "notify: updated",
            viewModel.NotifyDataErrorText,
            "valid INotifyDataErrorInfo updates source");
        AssertEqual("Notify: notify: updated", echoText.Text, "updated INotifyDataErrorInfo echo text");
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
        Button clickButton,
        bool expectLoadedStoryboardApplied)
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

        AssertClose(
            expectLoadedStoryboardApplied ? 0.42 : 1.0,
            loadedText.Opacity,
            0.0001,
            expectLoadedStoryboardApplied
                ? "loaded storyboard applied opacity"
                : "loaded storyboard initial opacity");
        AssertEqual(1.0, clickButton.Opacity, "click storyboard initial opacity");
    }

    private static void ValidateNativeEffects(Border dropShadowEffectBorder, Border blurEffectBorder)
    {
        var dropShadowEffect = Require<DropShadowEffect>(
            dropShadowEffectBorder.Effect,
            "MVP DropShadowEffect");
        AssertEqual(9.0, dropShadowEffect.BlurRadius, "DropShadowEffect BlurRadius");
        AssertEqual(Color.FromRgb(0x33, 0x41, 0x55), dropShadowEffect.Color, "DropShadowEffect Color");
        AssertEqual(315.0, dropShadowEffect.Direction, "DropShadowEffect Direction");
        AssertEqual(0.55, dropShadowEffect.Opacity, "DropShadowEffect Opacity");
        AssertEqual(RenderingBias.Quality, dropShadowEffect.RenderingBias, "DropShadowEffect RenderingBias");
        AssertEqual(4.0, dropShadowEffect.ShadowDepth, "DropShadowEffect ShadowDepth");

        var blurEffect = Require<BlurEffect>(
            blurEffectBorder.Effect,
            "MVP BlurEffect");
        AssertEqual(KernelType.Gaussian, blurEffect.KernelType, "BlurEffect KernelType");
        AssertEqual(2.5, blurEffect.Radius, "BlurEffect Radius");
        AssertEqual(RenderingBias.Quality, blurEffect.RenderingBias, "BlurEffect RenderingBias");
    }

    private static void ValidateMvpRoutedEvent(
        MainWindow window,
        StackPanel scope,
        MvpRoutedEventButton button,
        TextBlock statusText)
    {
        AssertEqual(RoutingStrategy.Bubble, MvpRoutedEventButton.MvpActivatedEvent.RoutingStrategy, "MVP routed event strategy");
        AssertEqual(nameof(MvpRoutedEventButton.MvpActivated), MvpRoutedEventButton.MvpActivatedEvent.Name, "MVP routed event name");
        AssertEqual(typeof(MvpRoutedEventHandler), MvpRoutedEventButton.MvpActivatedEvent.HandlerType, "MVP routed event handler type");
        AssertEqual(typeof(MvpRoutedEventButton), MvpRoutedEventButton.MvpActivatedEvent.OwnerType, "MVP routed event owner type");
        AssertEqual("Routed event idle", statusText.Text, "MVP routed event initial status");
        AssertEqual(0, button.ClassHandlerCount, "MVP routed event initial class-handler count");
        AssertEqual(0, window.MvpRoutedEventSourceCount, "MVP routed event initial source count");
        AssertEqual(0, window.MvpRoutedEventScopeCount, "MVP routed event initial scope count");
        AssertEqual(0, window.MvpRoutedEventHandledTooCount, "MVP routed event initial handled-too count");

        var args = button.RaiseMvpActivated("mvp routed payload");
        DrainDispatcher(window);

        AssertEqual(true, args.Handled, "MVP routed event handled flag");
        AssertEqual(1, button.ClassHandlerCount, "MVP routed event class-handler count");
        AssertEqual(1, window.MvpRoutedEventSourceCount, "MVP routed event source handler count");
        AssertEqual(1, window.MvpRoutedEventScopeCount, "MVP routed event scope handler count");
        AssertEqual(1, window.MvpRoutedEventHandledTooCount, "MVP routed event handled-too handler count");
        AssertEqual("MvpActivated", window.LastMvpRoutedEventName, "MVP routed event last name");
        AssertEqual("mvp routed payload", window.LastMvpRoutedEventPayload, "MVP routed event payload");
        AssertEqual(scope.Name, window.LastMvpRoutedEventSenderName, "MVP routed event sender name");
        AssertEqual(button.Name, window.LastMvpRoutedEventOriginalSourceName, "MVP routed event original source name");
        AssertEqual("Handled mvp routed payload", statusText.Text, "MVP routed event status text");
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

    private static string GetBindingPath(DependencyObject target, DependencyProperty property)
    {
        return BindingOperations.GetBinding(target, property)?.Path.Path
            ?? throw new InvalidOperationException($"Expected {property.Name} to have a Binding.");
    }

    private static string GetSelectedDateBindingPath(Control control)
    {
        DependencyProperty property = control switch
        {
            WpfCalendar => WpfCalendar.SelectedDateProperty,
            DatePicker => DatePicker.SelectedDateProperty,
            _ => throw new InvalidOperationException($"Unsupported selected-date control {control.GetType().Name}.")
        };

        return BindingOperations.GetBinding(control, property)?.Path.Path
            ?? throw new InvalidOperationException($"Expected {control.Name} SelectedDate to have a Binding.");
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

    private static void ValidateApplicationLoadComponent()
    {
        var component = Application.LoadComponent(
            new Uri("/ProGPU.Wpf.MvpApp;component/OverviewPage.xaml", UriKind.Relative));
        var overviewPage = Require<OverviewPage>(component, "Application.LoadComponent overview page");
        var overviewTitle = Require<TextBlock>(
            overviewPage.FindName("OverviewTitle"),
            "Application.LoadComponent overview title");
        AssertEqual("SDK overview page", overviewTitle.Text, "Application.LoadComponent overview title text");
    }

    private static void ValidateLooseXamlReaderWriter()
    {
        const string looseXaml = """
            <StackPanel
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Name="LooseXamlRoot"
                Orientation="Horizontal"
                Tag="loose-xaml">
                <TextBlock
                    x:Name="LooseXamlText"
                    Text="Loose XAML text" />
                <Button
                    x:Name="LooseXamlButton"
                    MinWidth="96"
                    Content="Loose action" />
            </StackPanel>
            """;

        var root = Require<StackPanel>(XamlReader.Parse(looseXaml), "loose XamlReader StackPanel");
        AssertEqual("LooseXamlRoot", root.Name, "loose XamlReader root name");
        AssertEqual(Orientation.Horizontal, root.Orientation, "loose XamlReader root orientation");
        AssertEqual("loose-xaml", root.Tag, "loose XamlReader root tag");
        AssertEqual(2, root.Children.Count, "loose XamlReader child count");

        var text = Require<TextBlock>(root.Children[0], "loose XamlReader TextBlock");
        var button = Require<Button>(root.Children[1], "loose XamlReader Button");
        AssertEqual("LooseXamlText", text.Name, "loose XamlReader TextBlock name");
        AssertEqual("Loose XAML text", text.Text, "loose XamlReader TextBlock text");
        AssertEqual("LooseXamlButton", button.Name, "loose XamlReader Button name");
        AssertEqual("Loose action", button.Content, "loose XamlReader Button content");
        AssertEqual(96.0, button.MinWidth, "loose XamlReader Button MinWidth");

        string serialized = XamlWriter.Save(root);
        AssertContains("LooseXamlRoot", serialized, "loose XamlWriter serialized root name");
        AssertContains("LooseXamlButton", serialized, "loose XamlWriter serialized Button name");

        var roundTripped = Require<StackPanel>(
            XamlReader.Parse(serialized),
            "loose XamlWriter round-trip StackPanel");
        AssertEqual("LooseXamlRoot", roundTripped.Name, "loose XamlWriter round-trip root name");
        AssertEqual(Orientation.Horizontal, roundTripped.Orientation, "loose XamlWriter round-trip orientation");
        AssertEqual(2, roundTripped.Children.Count, "loose XamlWriter round-trip child count");
        var roundTrippedButton = Require<Button>(
            roundTripped.Children[1],
            "loose XamlWriter round-trip Button");
        AssertEqual("Loose action", roundTrippedButton.Content, "loose XamlWriter round-trip Button content");
        AssertEqual(96.0, roundTrippedButton.MinWidth, "loose XamlWriter round-trip Button MinWidth");
    }

    private static void ValidateDispatcherOperations(Window window)
    {
        AssertEqual(true, window.Dispatcher.CheckAccess(), "dispatcher CheckAccess on validation thread");
        string invokeResult = window.Dispatcher.Invoke(
            static () => "dispatcher invoke result",
            DispatcherPriority.Send);
        AssertEqual("dispatcher invoke result", invokeResult, "dispatcher Invoke result");

        int beginInvokeCount = 0;
        var operation = window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => beginInvokeCount++));
        DrainDispatcher(window);
        AssertEqual(1, beginInvokeCount, "dispatcher BeginInvoke execution count");
        AssertEqual(DispatcherOperationStatus.Completed, operation.Status, "dispatcher BeginInvoke status");
    }

    private static void ValidateSecondaryWindow(MainWindow window, MenuItem aboutMenuItem)
    {
        AssertEqual("_About", aboutMenuItem.Header, "secondary window menu header");

        var dialog = new AboutWindow();
        AssertEqual(null, dialog.Owner, "secondary window initial owner");
        AssertEqual("About ProGPU WPF MVP", dialog.Title, "secondary window title");
        AssertEqual(SizeToContent.Height, dialog.SizeToContent, "secondary window SizeToContent");
        AssertEqual(ResizeMode.NoResize, dialog.ResizeMode, "secondary window resize mode");
        AssertEqual(WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation, "secondary window startup location");

        var titleText = Require<TextBlock>(
            dialog.FindName("AboutTitleText"),
            "secondary window title TextBlock");
        var bodyText = Require<TextBlock>(
            dialog.FindName("AboutBodyText"),
            "secondary window body TextBlock");
        var closeButton = Require<Button>(
            dialog.FindName("AboutCloseButton"),
            "secondary window close Button");

        AssertEqual("ProGPU WPF MVP", titleText.Text, "secondary window title text");
        AssertEqual(
            "Standard secondary WPF Window compiled through the ProGPU SDK.",
            bodyText.Text,
            "secondary window body text");
        AssertEqual(TextWrapping.Wrap, bodyText.TextWrapping, "secondary window body wrapping");
        AssertEqual("OK", closeButton.Content, "secondary window close button content");
        AssertEqual(true, closeButton.IsDefault, "secondary window close button default state");
        AssertEqual(true, closeButton.IsCancel, "secondary window close button cancel state");
    }

    private static void ValidateEditor(
        MainWindow window,
        PasswordBox passwordBox,
        RichTextBox richTextBox,
        TextBox dataObjectPayloadTextBox,
        Button dataObjectRoundTripButton,
        TextBlock dataObjectStatusText)
    {
        AssertEqual(16, passwordBox.MaxLength, "editor PasswordBox max length");
        AssertEqual('*', passwordBox.PasswordChar, "editor PasswordBox password char");
        AssertEqual(0, window.EditorPasswordChangedCount, "editor PasswordBox initial changed count");
        AssertEqual("data object payload", dataObjectPayloadTextBox.Text, "DataObject initial payload");
        AssertEqual("DataObject idle", dataObjectStatusText.Text, "DataObject initial status");

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

        dataObjectPayloadTextBox.Text = "mvp data object";
        dataObjectRoundTripButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, dataObjectRoundTripButton));
        DrainDispatcher(window);
        AssertEqual(1, window.DataObjectRoundTripCount, "DataObject round-trip count");
        AssertEqual("mvp data object", window.LastDataObjectText, "DataObject unicode text");
        AssertEqual("custom:mvp data object", window.LastDataObjectCustomText, "DataObject custom text");
        AssertEqual("mvp data object | custom:mvp data object", dataObjectStatusText.Text, "DataObject status text");
    }

    private static void ValidateDocument(
        MainWindow window,
        FlowDocumentScrollViewer documentViewer,
        FlowDocumentPageViewer documentPageViewer,
        FlowDocumentReader documentReader)
    {
        AssertEqual(ScrollBarVisibility.Auto, documentViewer.VerticalScrollBarVisibility, "document FlowDocumentScrollViewer vertical visibility");
        var document = Require<FlowDocument>(documentViewer.Document, "document FlowDocument");
        AssertEqual(new Thickness(12), document.PagePadding, "document FlowDocument page padding");
        AssertEqual(3, document.Blocks.Count, "document FlowDocument block count");

        var bodyParagraph = Require<Paragraph>(
            document.Blocks.FirstBlock?.NextBlock,
            "document body Paragraph");
        var hyperlink = FindDirectHyperlink(bodyParagraph, "document Hyperlink");
        AssertEqual(
            new Uri("https://github.com/wieslawsoltes/ProGPU", UriKind.Absolute),
            hyperlink.NavigateUri,
            "document Hyperlink NavigateUri");

        var documentText = new TextRange(document.ContentStart, document.ContentEnd).Text;
        AssertContains("Managed WPF document content", documentText, "document FlowDocument title text");
        AssertContains("ProGPU renderer", documentText, "document FlowDocument hyperlink text");
        AssertContains("Application and window lifecycle", documentText, "document FlowDocument list text");

        int initialNavigateCount = window.DocumentLinkRequestNavigateCount;
        hyperlink.RaiseEvent(new RequestNavigateEventArgs(hyperlink.NavigateUri, string.Empty));
        DrainDispatcher(window);
        AssertEqual(initialNavigateCount + 1, window.DocumentLinkRequestNavigateCount, "document Hyperlink RequestNavigate count");
        AssertEqual("ProGPU renderer", window.LastDocumentLinkRequestNavigateText, "document Hyperlink RequestNavigate text");
        AssertEqual(
            "https://github.com/wieslawsoltes/ProGPU",
            window.LastDocumentLinkRequestNavigateUri,
            "document Hyperlink RequestNavigate URI");
        AssertEqual(
            "RequestNavigate",
            window.LastDocumentLinkRequestNavigateRoutedEventName,
            "document Hyperlink RequestNavigate routed event");

        AssertEqual(125.0, documentPageViewer.Zoom, "document FlowDocumentPageViewer zoom");
        AssertEqual(50.0, documentPageViewer.MinZoom, "document FlowDocumentPageViewer min zoom");
        AssertEqual(250.0, documentPageViewer.MaxZoom, "document FlowDocumentPageViewer max zoom");
        var pageViewerDocument = Require<FlowDocument>(
            documentPageViewer.Document,
            "document FlowDocumentPageViewer FlowDocument");
        AssertEqual(new Thickness(5), pageViewerDocument.PagePadding, "document FlowDocumentPageViewer page padding");
        AssertEqual(360.0, pageViewerDocument.ColumnWidth, "document FlowDocumentPageViewer column width");
        AssertEqual(2, pageViewerDocument.Blocks.Count, "document FlowDocumentPageViewer block count");
        var pageViewerList = Require<System.Windows.Documents.List>(
            pageViewerDocument.Blocks.LastBlock,
            "document FlowDocumentPageViewer List");
        AssertEqual(TextMarkerStyle.Square, pageViewerList.MarkerStyle, "document FlowDocumentPageViewer list marker style");
        var pageViewerText = new TextRange(pageViewerDocument.ContentStart, pageViewerDocument.ContentEnd).Text;
        AssertContains("Page viewer document", pageViewerText, "document FlowDocumentPageViewer title text");
        AssertContains("MVP page viewer item", pageViewerText, "document FlowDocumentPageViewer list text");

        AssertEqual(FlowDocumentReaderViewingMode.Scroll, documentReader.ViewingMode, "document FlowDocumentReader viewing mode");
        var readerDocument = Require<FlowDocument>(
            documentReader.Document,
            "document FlowDocumentReader FlowDocument");
        AssertEqual(new Thickness(3), readerDocument.PagePadding, "document FlowDocumentReader page padding");
        AssertEqual(1, readerDocument.Blocks.Count, "document FlowDocumentReader block count");
        var readerText = new TextRange(readerDocument.ContentStart, readerDocument.ContentEnd).Text;
        AssertContains("MVP reader document", readerText, "document FlowDocumentReader text");
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

    private static void UpdateSource(DependencyObject target, DependencyProperty property)
    {
        BindingOperations.GetBindingExpression(target, property)?.UpdateSource();
    }

    private static string GetSingleValidationErrorContent(DependencyObject target, string description)
    {
        var errors = Validation.GetErrors(target);
        AssertEqual(1, errors.Count, $"{description} count");
        return errors[0].ErrorContent?.ToString() ?? string.Empty;
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

    private static Hyperlink FindDirectHyperlink(Paragraph paragraph, string description)
    {
        foreach (Inline inline in paragraph.Inlines)
        {
            if (inline is Hyperlink hyperlink)
            {
                return hyperlink;
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

    private static void AssertClose(double expected, double actual, double tolerance, string description)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be close to '{expected}', but found '{actual}'.");
        }
    }

    private static void AssertGreaterThan(int minimumExclusive, int actual, string description)
    {
        if (actual <= minimumExclusive)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be greater than '{minimumExclusive}', but found '{actual}'.");
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
