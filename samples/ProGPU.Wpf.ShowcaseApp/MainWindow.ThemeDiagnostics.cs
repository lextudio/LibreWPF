using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ProGPU.Wpf.ShowcaseApp;

public partial class MainWindow
{
    private sealed class ThemeMenuDiagnostics : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MenuItem _item;
        private readonly MenuLifecycleJournal _journal = new();
        private readonly List<Popup> _popups = new(4);
        private readonly KeyboardFocusChangedEventHandler _focus;
        private readonly MouseEventHandler _capture;
        private bool _disposed;

        internal ThemeMenuDiagnostics(MainWindow window, MenuItem item)
        {
            _window = window;
            _item = item;
            _focus = (_, e) => Record(e.RoutedEvent.Name);
            _capture = (_, e) => Record(e.RoutedEvent.Name);
            item.Loaded += OnTransition;
            item.Unloaded += OnTransition;
            item.SubmenuOpened += OnTransition;
            item.SubmenuClosed += OnTransition;
            window.Activated += OnActivation;
            window.Deactivated += OnDeactivation;
            window.Closed += OnClosed;
            window.AddHandler(Keyboard.GotKeyboardFocusEvent, _focus, handledEventsToo: true);
            window.AddHandler(Keyboard.LostKeyboardFocusEvent, _focus, handledEventsToo: true);
            window.AddHandler(Mouse.GotMouseCaptureEvent, _capture, handledEventsToo: true);
            window.AddHandler(Mouse.LostMouseCaptureEvent, _capture, handledEventsToo: true);
            Record("attached");
        }

        private void OnTransition(object sender, RoutedEventArgs e) => Record(e.RoutedEvent.Name);
        private void OnActivation(object? sender, EventArgs e) => Record("window-activated");
        private void OnDeactivation(object? sender, EventArgs e) => Record("window-deactivated");
        private void OnClosed(object? sender, EventArgs e) => Dispose();
        private void OnPopupOpened(object? sender, EventArgs e) => Record($"popup-opened:{Identity(sender)}");
        private void OnPopupClosed(object? sender, EventArgs e) => Record($"popup-closed:{Identity(sender)}");

        private static int Identity(object? value) => value is null ? 0 : RuntimeHelpers.GetHashCode(value);

        internal void Record(string transition)
        {
            if (_disposed) return;
            _journal.Record(transition, () =>
            {
                // FindName reads only the current template; never ApplyTemplate,
                // UpdateLayout, change IsOpen, capture, focus, or native state.
                Popup? popup = _item.Template?.FindName("PART_Popup", _item) as Popup;
                if (popup is not null && !_popups.Contains(popup) && _popups.Count < 4)
                {
                    _popups.Add(popup);
                    popup.Opened += OnPopupOpened;
                    popup.Closed += OnPopupClosed;
                }

                return $"item={Identity(_item)}, template={Identity(_item.Template)}, popup={Identity(popup)}, " +
                    $"loaded={_item.IsLoaded}, visible={_item.IsVisible}, highlighted={_item.IsHighlighted}, " +
                    $"submenu={_item.IsSubmenuOpen}, popupOpen={popup?.IsOpen}, popupVisible={popup?.IsVisible}, " +
                    $"active={_window.IsActive}, focus={DescribeInputElement(Keyboard.FocusedElement)}, " +
                    $"capture={DescribeInputElement(Mouse.Captured)}, left={Mouse.LeftButton}, " +
                    $"popupObservers={_popups.Count}";
            });
        }

        internal void WriteFailure()
        {
            Record("failed");
            Console.Error.WriteLine(_journal.FormatFailure());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _item.Loaded -= OnTransition;
            _item.Unloaded -= OnTransition;
            _item.SubmenuOpened -= OnTransition;
            _item.SubmenuClosed -= OnTransition;
            _window.Activated -= OnActivation;
            _window.Deactivated -= OnDeactivation;
            _window.Closed -= OnClosed;
            _window.RemoveHandler(Keyboard.GotKeyboardFocusEvent, _focus);
            _window.RemoveHandler(Keyboard.LostKeyboardFocusEvent, _focus);
            _window.RemoveHandler(Mouse.GotMouseCaptureEvent, _capture);
            _window.RemoveHandler(Mouse.LostMouseCaptureEvent, _capture);
            foreach (Popup popup in _popups)
            {
                popup.Opened -= OnPopupOpened;
                popup.Closed -= OnPopupClosed;
            }
            _popups.Clear();
        }
    }
}
