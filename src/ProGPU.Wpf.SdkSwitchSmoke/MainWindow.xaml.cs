using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

    public int SmokeRoutedEventCount { get; private set; }

    public object? LastSmokeRoutedEventSender { get; private set; }

    public object? LastSmokeRoutedEventSource { get; private set; }

    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        ClickStatus.Text = "clicked";
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

    private void OnSmokeBubbled(object sender, RoutedEventArgs e)
    {
        SmokeRoutedEventCount++;
        LastSmokeRoutedEventSender = sender;
        LastSmokeRoutedEventSource = e.OriginalSource;
        RoutedEventStatus.Text = e.RoutedEvent.Name;
        e.Handled = true;
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
            new SmokeItem("Scene", "ProGPU", "Rendering"),
            new SmokeItem("XAML", "compiled", "Framework")
        };
    }

    public string Title { get; } = "ProGPU WPF SDK switch managed subsystem smoke";

    public string InputText { get; set; } = "editable package text";

    public string ValidationText { get; set; } = "valid package text";

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

public sealed class SmokeItem
{
    public SmokeItem(string name, string value, string category)
        : this(name, value, category, [])
    {
    }

    public SmokeItem(string name, string value, string category, params SmokeItem[] children)
    {
        Name = name;
        Value = value;
        Category = category;
        Children = new ObservableCollection<SmokeItem>(children);
    }

    public string Name { get; }

    public string Value { get; }

    public string Category { get; }

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
