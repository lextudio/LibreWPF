using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;

namespace ProGPU.Wpf.RealXamlCompilerHarness;

public partial class MainWindow : Window
{
    public static RoutedUICommand SmokeRoutedCommand { get; } = new(
        "Smoke routed command",
        "SmokeRoutedCommand",
        typeof(MainWindow));

    public MainWindow()
    {
        DataContext = new SmokeViewModel();
        InitializeComponent();
    }

    public int RoutedCommandCanExecuteCount { get; private set; }

    public int RoutedCommandExecutionCount { get; private set; }

    public string? LastRoutedCommandParameter { get; private set; }

    public int XamlClickCount { get; private set; }

    public string? LastXamlClickSenderName { get; private set; }

    public string? LastXamlClickRoutedEventName { get; private set; }

    public int StyledClickCount { get; private set; }

    public string? LastStyledClickSenderName { get; private set; }

    public string? LastStyledClickRoutedEventName { get; private set; }

    public int FilteredItemsFilterCount { get; private set; }

    private void OnSmokeCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        RoutedCommandCanExecuteCount++;
        e.CanExecute = true;
        e.Handled = true;
    }

    private void OnSmokeCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        RoutedCommandExecutionCount++;
        LastRoutedCommandParameter = e.Parameter?.ToString();
        e.Handled = true;
    }

    private void OnXamlClick(object sender, RoutedEventArgs e)
    {
        XamlClickCount++;
        LastXamlClickSenderName = sender is FrameworkElement element ? element.Name : null;
        LastXamlClickRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnStyledButtonClick(object sender, RoutedEventArgs e)
    {
        StyledClickCount++;
        LastStyledClickSenderName = sender is FrameworkElement element ? element.Name : null;
        LastStyledClickRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnFilteredItemsViewFilter(object sender, FilterEventArgs e)
    {
        FilteredItemsFilterCount++;
        e.Accepted = e.Item is SmokeItem smokeItem &&
            string.Equals(smokeItem.Name, "item beta", StringComparison.Ordinal);
    }

    public sealed class SmokeViewModel : INotifyPropertyChanged, IDataErrorInfo
    {
        private string _greeting = "bound greeting from real WPF";
        private bool _isWarning;
        private bool _isCritical;
        private SmokeItem? _selectedItem;
        private string _validatedText = "valid binding text";

        public SmokeViewModel()
        {
            Items.Add(new SmokeItem("item alpha"));
            Items.Add(new SmokeItem("item beta"));
            Nodes.Add(new SmokeNode(
                "root node",
                new SmokeNode("child alpha"),
                new SmokeNode("child beta")));
            _selectedItem = Items[1];
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Greeting
        {
            get => _greeting;
            set
            {
                if (!string.Equals(_greeting, value, StringComparison.Ordinal))
                {
                    _greeting = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ButtonText => "run bound command";

        public string TriggerButtonText => "style trigger target";

        public string Error => string.Empty;

        public string this[string columnName] =>
            string.Equals(columnName, nameof(ValidatedText), StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(_validatedText)
                ? "ValidatedText is required"
                : string.Empty;

        public string ValidatedText
        {
            get => _validatedText;
            set
            {
                if (!string.Equals(_validatedText, value, StringComparison.Ordinal))
                {
                    _validatedText = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsWarning
        {
            get => _isWarning;
            set
            {
                if (_isWarning != value)
                {
                    _isWarning = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsCritical
        {
            get => _isCritical;
            set
            {
                if (_isCritical != value)
                {
                    _isCritical = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<SmokeItem> Items { get; } = new();

        public ObservableCollection<SmokeNode> Nodes { get; } = new();

        public SmokeDetail Detail { get; } = new("detail from implicit template");

        public SmokeItem? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (!ReferenceEquals(_selectedItem, value))
                {
                    _selectedItem = value;
                    OnPropertyChanged();
                }
            }
        }

        public SmokeCommand SmokeCommand { get; } = new();

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class SmokeCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public int ExecutionCount { get; private set; }

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            ExecutionCount++;
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class ProviderDataFactory
{
    public string CreateProviderGreeting(string prefix, string value)
    {
        return $"{prefix} data {value}";
    }
}

public sealed class SmokeTextExtension : MarkupExtension
{
    public string Prefix { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return $"{Prefix} {Value} extension";
    }
}

public sealed class SmokeItem
{
    public SmokeItem(string name)
    {
        Name = name;
        Category = string.Equals(name, "item beta", StringComparison.Ordinal)
            ? "secondary group"
            : "primary group";
    }

    public string Name { get; }

    public string Category { get; }
}

public sealed class SmokeDetail
{
    public SmokeDetail(string title)
    {
        Title = title;
    }

    public string Title { get; }
}

public sealed class SmokeNode
{
    public SmokeNode(string name, params SmokeNode[] children)
    {
        Name = name;
        foreach (SmokeNode child in children)
        {
            Children.Add(child);
        }
    }

    public string Name { get; }

    public ObservableCollection<SmokeNode> Children { get; } = new();
}

public sealed class SmokeItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? AlphaTemplate { get; set; }

    public DataTemplate? DefaultTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is SmokeItem smokeItem &&
            string.Equals(smokeItem.Name, "item alpha", StringComparison.Ordinal) &&
            AlphaTemplate != null)
        {
            return AlphaTemplate;
        }

        return DefaultTemplate ?? base.SelectTemplate(item, container);
    }
}
