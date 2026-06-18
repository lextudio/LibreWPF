using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
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
}

public sealed class SmokeViewModel : INotifyPropertyChanged
{
    private string _mutableStatus = "initial binding status";

    public SmokeViewModel()
    {
        Items = new ObservableCollection<SmokeItem>
        {
            new SmokeItem("Window", "portable", "Framework"),
            new SmokeItem("Scene", "ProGPU", "Rendering"),
            new SmokeItem("XAML", "compiled", "Framework")
        };
    }

    public string Title { get; } = "ProGPU WPF SDK switch managed subsystem smoke";

    public string InputText { get; set; } = "editable package text";

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
    {
        Name = name;
        Value = value;
        Category = category;
    }

    public string Name { get; }

    public string Value { get; }

    public string Category { get; }
}

public static class SmokeResourceFactory
{
    public static string CreateGreeting(string prefix, int value)
    {
        return $"{prefix}:{value}";
    }
}
