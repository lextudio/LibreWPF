using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ProGPU.Wpf.MvpApp;

/// <summary>
/// Strips the Fluent menu drop shadow so popups show only their item panel.
/// </summary>
/// <remarks>
/// The Fluent theme declares the shadow inline on the SubmenuBorder part
/// (<c>Margin="12,0,12,18"</c> plus a <c>DropShadowEffect</c>) rather than through a
/// resource key, so it cannot be replaced by a brush or style override. Retemplating
/// MenuItem in app resources would mean copying every role template and re-resolving
/// the theme resources they reference, so this app clears the two properties on the
/// realized template part instead. The popup also carries a negative HorizontalOffset
/// that only exists to cancel the border margin, so it has to be zeroed with it or the
/// menu lands 12px left of its header.
/// </remarks>
internal static class FlatMenuChrome
{
    internal static void Install()
    {
        // Escape hatch for comparing against the stock Fluent menu chrome.
        if (Environment.GetEnvironmentVariable("MVP_FLAT_MENU") == "0")
        {
            return;
        }

        EventManager.RegisterClassHandler(
            typeof(MenuItem),
            MenuItem.SubmenuOpenedEvent,
            new RoutedEventHandler(OnSubmenuOpened));
    }

    private static void OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Template == null)
        {
            return;
        }

        // SubmenuOpened bubbles, so a nested submenu also raises it on its ancestors.
        // Only the item that owns the newly opened popup should be flattened.
        if (!ReferenceEquals(sender, e.OriginalSource))
        {
            return;
        }

        menuItem.ApplyTemplate();
        if (menuItem.Template.FindName("PART_Popup", menuItem) is not Popup popup)
        {
            return;
        }

        Border? border = menuItem.Template.FindName("SubmenuBorder", menuItem) as Border;
        if (border == null && popup.Child is DependencyObject child)
        {
            border = FindSubmenuBorder(child);
        }

        if (border == null)
        {
            return;
        }

        // The template offsets already account for the margin it is about to lose, so
        // give back exactly that much instead of zeroing them. Top-level headers use
        // HorizontalOffset -12 against a 12 left margin, while submenu headers use
        // VerticalOffset -20 against a 10 top margin, which must stay at -10.
        Thickness margin = border.Margin;
        popup.HorizontalOffset += margin.Left;
        popup.VerticalOffset += margin.Top;

        Flatten(border);
    }

    private static void Flatten(Border border)
    {
        border.Effect = null;
        border.Margin = new Thickness(0);
    }

    private static Border? FindSubmenuBorder(DependencyObject root)
    {
        if (root is Border { Name: "SubmenuBorder" } border)
        {
            return border;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            if (FindSubmenuBorder(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
