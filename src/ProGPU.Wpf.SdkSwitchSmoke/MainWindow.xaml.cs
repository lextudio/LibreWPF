using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public partial class MainWindow : Window
{
    public static RoutedUICommand SmokeCommand { get; } = new(
        "Smoke Command",
        "SmokeCommand",
        typeof(MainWindow));

    public MainWindow()
    {
        DataContext = new SmokeViewModel();
        InitializeComponent();
    }

    public int SmokeCommandCanExecuteCount { get; private set; }

    public int SmokeCommandExecutionCount { get; private set; }

    public string? LastSmokeCommandParameter { get; private set; }

    public int EventSetterClickCount { get; private set; }

    public string? LastEventSetterSenderName { get; private set; }

    public string? LastEventSetterRoutedEventName { get; private set; }

    public int SmokeRoutedEventCount { get; private set; }

    public int MenuClickCount { get; private set; }

    public int MenuCheckedCount { get; private set; }

    public int MenuUncheckedCount { get; private set; }

    public int ManagedCheckBoxCheckedCount { get; private set; }

    public int ManagedCheckBoxUncheckedCount { get; private set; }

    public int ManagedRadioCheckedCount { get; private set; }

    public int ManagedRadioUncheckedCount { get; private set; }

    public string? LastManagedRadioCheckedName { get; private set; }

    public int PasswordChangedCount { get; private set; }

    public string? LastPasswordChangedSenderName { get; private set; }

    public string? LastPasswordChangedRoutedEventName { get; private set; }

    public int DateSelectionChangedCount { get; private set; }

    public string? LastDateSelectionChangedSenderName { get; private set; }

    public int SelectorSelectionChangedCount { get; private set; }

    public int TabSelectionChangedCount { get; private set; }

    public int ExpanderExpandedCount { get; private set; }

    public int ExpanderCollapsedCount { get; private set; }

    public int RangeValueChangedCount { get; private set; }

    public int SmokeFrameNavigatingCount { get; private set; }

    public int SmokeFrameNavigatedCount { get; private set; }

    public int SmokeFrameLoadCompletedCount { get; private set; }

    public string? LastSmokeFrameNavigatingUri { get; private set; }

    public string? LastSmokeFrameNavigationMode { get; private set; }

    public string? LastSmokeFrameNavigatedUri { get; private set; }

    public string? LastSmokeFrameNavigatedContentType { get; private set; }

    public string? LastSmokeFrameLoadCompletedUri { get; private set; }

    public int DocumentLinkRequestNavigateCount { get; private set; }

    public string? LastDocumentLinkRequestNavigateUri { get; private set; }

    public string? LastDocumentLinkRequestNavigateRoutedEventName { get; private set; }

    public int LoadedStoryboardTextLoadedCount { get; private set; }

    public string? LastLoadedStoryboardTextRoutedEventName { get; private set; }

    public object? LastSmokeRoutedEventSender { get; private set; }

    public object? LastSmokeRoutedEventSource { get; private set; }

    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        ClickStatus.Text = "clicked";
    }

    private void OnEventSetterButtonClick(object sender, RoutedEventArgs e)
    {
        EventSetterClickCount++;
        LastEventSetterSenderName = (sender as FrameworkElement)?.Name;
        LastEventSetterRoutedEventName = e.RoutedEvent?.Name;
        EventSetterStatus.Text = "event setter clicked";
        e.Handled = true;
    }

    private void OnSmokeCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        SmokeCommandCanExecuteCount++;
        e.CanExecute = true;
        e.Handled = true;
    }

    private void OnSmokeCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SmokeCommandExecutionCount++;
        LastSmokeCommandParameter = e.Parameter?.ToString();
        CommandStatus.Text = LastSmokeCommandParameter ?? "executed";
        e.Handled = true;
    }

    private void OnMenuItemClick(object sender, RoutedEventArgs e)
    {
        MenuClickCount++;
        MenuStatus.Text = "menu click";
        e.Handled = true;
    }

    private void OnCheckableMenuItemChecked(object sender, RoutedEventArgs e)
    {
        MenuCheckedCount++;
        if (MenuStatus != null)
        {
            MenuStatus.Text = "menu checked";
        }

        e.Handled = true;
    }

    private void OnCheckableMenuItemUnchecked(object sender, RoutedEventArgs e)
    {
        MenuUncheckedCount++;
        if (MenuStatus != null)
        {
            MenuStatus.Text = "menu unchecked";
        }

        e.Handled = true;
    }

    private void OnManagedCheckBoxChecked(object sender, RoutedEventArgs e)
    {
        ManagedCheckBoxCheckedCount++;
        if (CheckChoiceStatus != null)
        {
            CheckChoiceStatus.Text = "check checked";
        }

        e.Handled = true;
    }

    private void OnManagedCheckBoxUnchecked(object sender, RoutedEventArgs e)
    {
        ManagedCheckBoxUncheckedCount++;
        if (CheckChoiceStatus != null)
        {
            CheckChoiceStatus.Text = "check unchecked";
        }

        e.Handled = true;
    }

    private void OnManagedRadioChecked(object sender, RoutedEventArgs e)
    {
        ManagedRadioCheckedCount++;
        LastManagedRadioCheckedName = (sender as FrameworkElement)?.Name;
        if (CheckChoiceStatus != null)
        {
            CheckChoiceStatus.Text = $"radio checked: {LastManagedRadioCheckedName}";
        }

        e.Handled = true;
    }

    private void OnManagedRadioUnchecked(object sender, RoutedEventArgs e)
    {
        ManagedRadioUncheckedCount++;
        if (CheckChoiceStatus != null)
        {
            CheckChoiceStatus.Text = "radio unchecked";
        }

        e.Handled = true;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        PasswordChangedCount++;
        LastPasswordChangedSenderName = (sender as FrameworkElement)?.Name;
        LastPasswordChangedRoutedEventName = e.RoutedEvent?.Name;
        if (PasswordStatus != null)
        {
            PasswordStatus.Text = "password changed";
        }

        e.Handled = true;
    }

    private void OnDateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DateSelectionChangedCount++;
        LastDateSelectionChangedSenderName = (sender as FrameworkElement)?.Name;
        if (DateStatus != null)
        {
            DateStatus.Text = $"date changed: {LastDateSelectionChangedSenderName}";
        }

        e.Handled = true;
    }

    private void OnSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectorSelectionChangedCount++;
        if (SelectorStatus != null)
        {
            SelectorStatus.Text = $"selector selected: {SmokeComboBox.SelectedValue}";
        }

        e.Handled = true;
    }

    private void OnTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender))
        {
            return;
        }

        TabSelectionChangedCount++;
        if (TabStatus != null && SmokeTabs.SelectedItem is TabItem selectedTab)
        {
            TabStatus.Text = $"tab selected: {selectedTab.Header}";
        }

        e.Handled = true;
    }

    private void OnSmokeExpanderExpanded(object sender, RoutedEventArgs e)
    {
        ExpanderExpandedCount++;
        if (RangeStatus != null)
        {
            RangeStatus.Text = "range expanded";
        }

        e.Handled = true;
    }

    private void OnSmokeExpanderCollapsed(object sender, RoutedEventArgs e)
    {
        ExpanderCollapsedCount++;
        if (RangeStatus != null)
        {
            RangeStatus.Text = "range collapsed";
        }

        e.Handled = true;
    }

    private void OnRangeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        RangeValueChangedCount++;
        if (RangeStatus != null)
        {
            RangeStatus.Text = "range value: " + e.NewValue.ToString("0.##", CultureInfo.InvariantCulture);
        }

        e.Handled = true;
    }

    private void OnSmokeFrameNavigating(object sender, NavigatingCancelEventArgs e)
    {
        SmokeFrameNavigatingCount++;
        LastSmokeFrameNavigatingUri = e.Uri?.ToString();
        LastSmokeFrameNavigationMode = e.NavigationMode.ToString();
    }

    private void OnSmokeFrameNavigated(object sender, NavigationEventArgs e)
    {
        SmokeFrameNavigatedCount++;
        LastSmokeFrameNavigatedUri = e.Uri?.ToString();
        LastSmokeFrameNavigatedContentType = e.Content?.GetType().FullName;
    }

    private void OnSmokeFrameLoadCompleted(object sender, NavigationEventArgs e)
    {
        SmokeFrameLoadCompletedCount++;
        LastSmokeFrameLoadCompletedUri = e.Uri?.ToString();
    }

    private void OnSmokeBubbled(object sender, RoutedEventArgs e)
    {
        SmokeRoutedEventCount++;
        LastSmokeRoutedEventSender = sender;
        LastSmokeRoutedEventSource = e.OriginalSource;
        RoutedEventStatus.Text = e.RoutedEvent.Name;
        e.Handled = true;
    }

    private void OnDocumentLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        DocumentLinkRequestNavigateCount++;
        LastDocumentLinkRequestNavigateUri = e.Uri?.ToString();
        LastDocumentLinkRequestNavigateRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnLoadedStoryboardTextLoaded(object sender, RoutedEventArgs e)
    {
        LoadedStoryboardTextLoadedCount++;
        LastLoadedStoryboardTextRoutedEventName = e.RoutedEvent?.Name;
    }
}

public sealed class SmokeViewModel : INotifyPropertyChanged
{
    private string _mutableStatus = "initial binding status";

    public SmokeViewModel()
    {
        Items = new ObservableCollection<SmokeItem>
        {
            new SmokeItem(
                "Window",
                "portable",
                "Framework",
                new SmokeItem("Startup", "managed", "Framework")),
            new SmokeItem("Scene", "ProGPU", "Rendering", false),
            new SmokeItem("XAML", "compiled", "Framework")
        };
    }

    public string Title { get; } = "ProGPU WPF SDK switch managed subsystem smoke";

    public string InputText { get; set; } = "editable package text";

    public string ValidationText { get; set; } = "valid package text";

    public SmokeRequeryCommand RequeryCommand { get; } = new();

    public string MutableStatus
    {
        get => _mutableStatus;
        set
        {
            if (_mutableStatus == value)
            {
                return;
            }

            _mutableStatus = value;
            OnPropertyChanged();
        }
    }

    public bool IsHighlighted { get; } = true;

    public bool IsCritical { get; } = true;

    public ObservableCollection<SmokeItem> Items { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SmokeRequeryCommand : ICommand
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

public sealed class SmokeItem
{
    public SmokeItem(string name, string value, string category)
        : this(name, value, category, [])
    {
    }

    public SmokeItem(string name, string value, string category, params SmokeItem[] children)
        : this(name, value, category, true, children)
    {
    }

    public SmokeItem(string name, string value, string category, bool isActive, params SmokeItem[] children)
    {
        Name = name;
        Value = value;
        Category = category;
        IsActive = isActive;
        Children = new ObservableCollection<SmokeItem>(children);
    }

    public string Name { get; }

    public string Value { get; }

    public string Category { get; }

    public bool IsActive { get; set; }

    public ObservableCollection<SmokeItem> Children { get; }
}

public sealed class SmokeItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FrameworkTemplate { get; set; }

    public DataTemplate? RenderingTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item is SmokeItem { Category: "Rendering" }
            ? RenderingTemplate
            : FrameworkTemplate;
    }
}

public sealed class SmokeRoutedEventSource : FrameworkElement
{
    public static readonly RoutedEvent SmokeBubbledEvent = EventManager.RegisterRoutedEvent(
        "SmokeBubbled",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(SmokeRoutedEventSource));

    public event RoutedEventHandler SmokeBubbled
    {
        add => AddHandler(SmokeBubbledEvent, value);
        remove => RemoveHandler(SmokeBubbledEvent, value);
    }

    public void RaiseSmokeBubbled()
    {
        RaiseEvent(new RoutedEventArgs(SmokeBubbledEvent, this));
    }
}

public sealed class SmokeNonEmptyValidationRule : ValidationRule
{
    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        string text = value as string ?? value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ValidationResult(false, "Value is required");
        }

        return ValidationResult.ValidResult;
    }
}

public static class SmokeResourceFactory
{
    public static string CreateGreeting(string prefix, int value)
    {
        return $"{prefix}:{value}";
    }
}
