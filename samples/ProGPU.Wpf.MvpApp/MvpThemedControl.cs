using System.Windows;
using System.Windows.Controls;

namespace ProGPU.Wpf.MvpApp;

public sealed class MvpThemedControl : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(MvpThemedControl),
        new FrameworkPropertyMetadata(string.Empty));

    static MvpThemedControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MvpThemedControl),
            new FrameworkPropertyMetadata(typeof(MvpThemedControl)));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
