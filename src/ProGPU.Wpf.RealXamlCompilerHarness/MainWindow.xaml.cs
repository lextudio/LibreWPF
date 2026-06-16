using System;
using System.Windows;
using System.Windows.Input;

namespace ProGPU.Wpf.RealXamlCompilerHarness;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        DataContext = new SmokeViewModel();
        InitializeComponent();
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
