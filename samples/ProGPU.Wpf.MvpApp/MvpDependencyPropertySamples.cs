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
