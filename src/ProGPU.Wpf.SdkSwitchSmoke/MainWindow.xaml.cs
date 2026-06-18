using System.Collections.ObjectModel;
using System.Windows;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        DataContext = new SmokeViewModel();
        InitializeComponent();
    }

    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        ClickStatus.Text = "clicked";
    }
}

public sealed class SmokeViewModel
{
    public SmokeViewModel()
    {
        Items = new ObservableCollection<SmokeItem>
        {
            new SmokeItem("Window", "portable"),
            new SmokeItem("Scene", "ProGPU"),
            new SmokeItem("XAML", "compiled")
        };
    }

    public string Title { get; } = "ProGPU WPF SDK switch managed subsystem smoke";

    public string InputText { get; set; } = "editable package text";

    public ObservableCollection<SmokeItem> Items { get; }
}

public sealed class SmokeItem
{
    public SmokeItem(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }
}
