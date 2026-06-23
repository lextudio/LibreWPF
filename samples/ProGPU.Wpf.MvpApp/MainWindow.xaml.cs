using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProGPU.Wpf.MvpApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        DataContext = new MainViewModel();
        InitializeComponent();
    }
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _newItemName = "Gamma";
    private MvpItem? _selectedItem;
    private string _selectedCategory = "Framework";
    private bool _actionsEnabled = true;
    private double _progress = 35.0;

    public MainViewModel()
    {
        Items =
        [
            new MvpItem("Alpha", "Framework"),
            new MvpItem("Beta", "Rendering")
        ];
        Categories = ["Framework", "Rendering", "Input"];
        _selectedItem = Items[0];
        AddItemCommand = new RelayCommand(AddItem, () => ActionsEnabled);
        ResetCommand = new RelayCommand(Reset);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MvpItem> Items { get; }

    public ObservableCollection<string> Categories { get; }

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

    private void AddItem()
    {
        string name = string.IsNullOrWhiteSpace(NewItemName)
            ? $"Item {Items.Count + 1}"
            : NewItemName.Trim();
        var item = new MvpItem(name, SelectedCategory);
        Items.Add(item);
        SelectedItem = item;
        NewItemName = string.Empty;
    }

    private void Reset()
    {
        Items.Clear();
        Items.Add(new MvpItem("Alpha", "Framework"));
        Items.Add(new MvpItem("Beta", "Rendering"));
        SelectedItem = Items[0];
        SelectedCategory = Categories[0];
        NewItemName = "Gamma";
        Progress = 35.0;
        ActionsEnabled = true;
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

public sealed record MvpItem(string Name, string Category);

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

internal static class MvpSelfTest
{
    public static void Validate(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var viewModel = window.DataContext as MainViewModel
            ?? throw new InvalidOperationException("Expected MVP DataContext.");
        Require<TextBox>(window.FindName("NameTextBox"), "name TextBox");
        Require<ListBox>(window.FindName("ItemsList"), "items ListBox");
        Require<CheckBox>(window.FindName("EnabledCheckBox"), "enabled CheckBox");
        Require<Slider>(window.FindName("ProgressSlider"), "progress Slider");
        Require<ComboBox>(window.FindName("CategoryCombo"), "category ComboBox");

        int initialCount = viewModel.Items.Count;
        viewModel.NewItemName = "Validated";
        viewModel.SelectedCategory = "Input";
        viewModel.AddItemCommand.Execute(null);

        AssertEqual(initialCount + 1, viewModel.Items.Count, "added item count");
        AssertEqual("Validated", viewModel.SelectedItem?.Name, "selected item name");
        AssertEqual("Input", viewModel.SelectedItem?.Category, "selected item category");

        viewModel.Progress = 72.0;
        AssertEqual("Validated selected, progress 72%", viewModel.StatusText, "status text");
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
