using System;
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

    public sealed class SmokeViewModel
    {
        public string Greeting => "bound greeting from real WPF";

        public string ButtonText => "run bound command";

        public SmokeCommand SmokeCommand { get; } = new();
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
