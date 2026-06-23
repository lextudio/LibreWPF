using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProGPU.Wpf.MvpApp;

public static class MvpStateProperties
{
    public static readonly DependencyProperty SectionNameProperty =
        DependencyProperty.RegisterAttached(
            "SectionName",
            typeof(string),
            typeof(MvpStateProperties),
            new FrameworkPropertyMetadata(
                "Unassigned section",
                FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ImportanceProperty =
        DependencyProperty.RegisterAttached(
            "Importance",
            typeof(double),
            typeof(MvpStateProperties),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnImportanceChanged,
                CoerceImportance));

    public static int ImportanceChangedCount { get; private set; }

    public static string GetSectionName(DependencyObject element)
    {
        return (string)element.GetValue(SectionNameProperty);
    }

    public static void SetSectionName(DependencyObject element, string value)
    {
        element.SetValue(SectionNameProperty, value);
    }

    public static double GetImportance(DependencyObject element)
    {
        return (double)element.GetValue(ImportanceProperty);
    }

    public static void SetImportance(DependencyObject element, double value)
    {
        element.SetValue(ImportanceProperty, value);
    }

    private static void OnImportanceChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        ImportanceChangedCount++;
    }

    private static object CoerceImportance(DependencyObject element, object baseValue)
    {
        return Math.Clamp((double)baseValue, 0d, 100d);
    }
}

public class MvpHeaderTextBlock : TextBlock
{
    public static readonly DependencyProperty HeaderTextProperty =
        SummaryPanel.HeaderTextProperty.AddOwner(
            typeof(MvpHeaderTextBlock),
            new FrameworkPropertyMetadata(
                "Header text",
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    static MvpHeaderTextBlock()
    {
        FontWeightProperty.OverrideMetadata(
            typeof(MvpHeaderTextBlock),
            new FrameworkPropertyMetadata(FontWeights.SemiBold));
        ForegroundProperty.OverrideMetadata(
            typeof(MvpHeaderTextBlock),
            new FrameworkPropertyMetadata(Brushes.DarkSlateBlue));
    }

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }
}

public delegate void MvpRoutedEventHandler(object sender, MvpRoutedEventArgs e);

public sealed class MvpRoutedEventArgs : RoutedEventArgs
{
    public MvpRoutedEventArgs(RoutedEvent routedEvent, object source, string payload)
        : base(routedEvent, source)
    {
        Payload = payload;
    }

    public string Payload { get; }
}

public sealed class MvpRoutedEventButton : Button
{
    public static readonly RoutedEvent MvpActivatedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(MvpActivated),
            RoutingStrategy.Bubble,
            typeof(MvpRoutedEventHandler),
            typeof(MvpRoutedEventButton));

    static MvpRoutedEventButton()
    {
        EventManager.RegisterClassHandler(
            typeof(MvpRoutedEventButton),
            MvpActivatedEvent,
            new MvpRoutedEventHandler(OnMvpActivatedClassHandler),
            handledEventsToo: true);
    }

    public int ClassHandlerCount { get; private set; }

    public event MvpRoutedEventHandler MvpActivated
    {
        add => AddHandler(MvpActivatedEvent, value);
        remove => RemoveHandler(MvpActivatedEvent, value);
    }

    public MvpRoutedEventArgs RaiseMvpActivated(string payload)
    {
        var args = new MvpRoutedEventArgs(MvpActivatedEvent, this, payload);
        RaiseEvent(args);
        return args;
    }

    private static void OnMvpActivatedClassHandler(object sender, MvpRoutedEventArgs e)
    {
        if (sender is MvpRoutedEventButton button)
        {
            button.ClassHandlerCount++;
        }
    }
}
