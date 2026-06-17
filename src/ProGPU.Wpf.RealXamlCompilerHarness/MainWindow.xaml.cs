using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

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

    public sealed class SmokeViewModel : INotifyPropertyChanged, IDataErrorInfo
    {
        private string _greeting = "bound greeting from real WPF";
        private bool _isWarning;
        private SmokeItem? _selectedItem;
        private string _validatedText = "valid binding text";

        public SmokeViewModel()
        {
            Items.Add(new SmokeItem("item alpha"));
            Items.Add(new SmokeItem("item beta"));
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

        public ObservableCollection<SmokeItem> Items { get; } = new();

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

public sealed class SmokeItem
{
    public SmokeItem(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
