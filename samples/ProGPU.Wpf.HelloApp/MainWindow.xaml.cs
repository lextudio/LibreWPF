using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace ProGPU.Wpf.HelloApp;

public partial class MainWindow : Window
{
    internal HelloViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void OnUpdateButtonClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Status = "Updated for " + ViewModel.Name;
        ViewModel.Items.Add("Clicked at " + DateTimeOffset.Now.ToString("HH:mm:ss"));
    }
}

internal sealed class HelloViewModel : INotifyPropertyChanged
{
    private string _name = "WPF";
    private string _status = "Running through ProGPU.Wpf.Sdk";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                Footer = "Ready for " + value;
                OnPropertyChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    public string Footer
    {
        get => _footer;
        private set
        {
            if (_footer != value)
            {
                _footer = value;
                OnPropertyChanged();
            }
        }
    }

    private string _footer = "Ready for WPF";

    public ObservableCollection<string> Items { get; } =
    [
        "Compiled App.xaml",
        "Compiled MainWindow.xaml",
        "Binding and collection view"
    ];

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal static class HelloSelfTest
{
    public static void Validate(MainWindow window, bool expectStartupActivation)
    {
        AssertEqual("ProGPU WPF Hello", window.Title, "window title");
        AssertEqual(true, window.IsVisible, "window visibility");
        AssertEqual(true, ReferenceEquals(window.DataContext, window.ViewModel), "data context");
        AssertEqual("ProGPU WPF Hello", Require<TextBlock>(window, "TitleText").Text, "title text");
        AssertEqual("Running through ProGPU.Wpf.Sdk", Require<TextBlock>(window, "SubtitleText").Text, "bound status text");
        AssertEqual("Ready for WPF", Require<TextBlock>(window, "FooterText").Text, "bound footer text");
        AssertEqual(3, Require<ListBox>(window, "ItemsList").Items.Count, "initial item count");
        AssertEqual("WPF", Require<TextBox>(window, "NameBox").Text, "initial text binding");

        Require<TextBox>(window, "NameBox").Text = "ProGPU";
        Require<TextBox>(window, "NameBox").GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Require<Button>(window, "UpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        AssertEqual("ProGPU", window.ViewModel.Name, "updated view model name");
        AssertEqual("Updated for ProGPU", window.ViewModel.Status, "button command status");
        AssertEqual("Updated for ProGPU", Require<TextBlock>(window, "SubtitleText").Text, "updated bound status text");
        AssertEqual("Ready for ProGPU", Require<TextBlock>(window, "FooterText").Text, "updated footer text");
        AssertEqual(4, Require<ListBox>(window, "ItemsList").Items.Count, "updated item count");

        if (expectStartupActivation)
        {
            AssertEqual(1, App.StartupEventCount, "startup event count");
            AssertEqual(2, App.StartupArgumentCount, "startup argument count");
            AssertEqual(2, Application.Current.Properties["HelloStartupArgumentCount"], "startup argument count property");
            AssertEqual("hello-alpha|hello beta", Application.Current.Properties["HelloStartupArguments"], "startup arguments property");
        }
    }

    private static T Require<T>(FrameworkElement root, string name)
        where T : class
    {
        return root.FindName(name) as T
            ?? throw new InvalidOperationException($"Expected {name} to be a {typeof(T).Name}.");
    }

    private static void AssertEqual<T>(T expected, T actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expected}' but was '{actual}'.");
        }
    }
}
