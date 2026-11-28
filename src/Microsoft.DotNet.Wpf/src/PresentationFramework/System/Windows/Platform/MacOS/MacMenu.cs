// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows.Platform.MacOS
{
    /// <summary>
    /// Opt-in native macOS menu bar for an ordinary WPF <see cref="Menu"/> (see
    /// docs/macos-window-integration.md section 4.6). On macOS a menu with
    /// <see cref="UsesNativeMenuBarProperty"/> becomes the application menu bar and collapses in the
    /// window; on other platforms the property has no effect and the menu stays where it is.
    /// </summary>
    public static class MacMenu
    {
        private const int QuitId = -1;

        /// <summary>Makes this menu the application menu bar on macOS.</summary>
        public static readonly DependencyProperty UsesNativeMenuBarProperty =
            DependencyProperty.RegisterAttached(
                "UsesNativeMenuBar",
                typeof(bool),
                typeof(MacMenu),
                new PropertyMetadata(false, OnUsesNativeMenuBarChanged));

        private static readonly DependencyPropertyKey s_isNativeMenuBarActivePropertyKey =
            DependencyProperty.RegisterAttachedReadOnly(
                "IsNativeMenuBarActive",
                typeof(bool),
                typeof(MacMenu),
                new PropertyMetadata(false));

        /// <summary>Whether this menu is the one currently installed as the application menu bar.</summary>
        public static readonly DependencyProperty IsNativeMenuBarActiveProperty =
            s_isNativeMenuBarActivePropertyKey.DependencyProperty;

        private static readonly List<Menu> s_registered = new List<Menu>();
        private static readonly Dictionary<Menu, MenuWatch> s_watches = new Dictionary<Menu, MenuWatch>();
        private static MacMenuMapper s_mapper = new MacMenuMapper();
        private static Menu s_installed;
        private static DispatcherOperation s_pendingRebuild;

        /// <summary>The name used by the generated About, Hide and Quit items; defaults to the entry assembly name.</summary>
        public static string ApplicationName { get; set; }

        public static bool GetUsesNativeMenuBar(Menu menu) => (bool)ValidateMenu(menu).GetValue(UsesNativeMenuBarProperty);

        public static void SetUsesNativeMenuBar(Menu menu, bool value) => ValidateMenu(menu).SetValue(UsesNativeMenuBarProperty, value);

        public static bool GetIsNativeMenuBarActive(Menu menu) => (bool)ValidateMenu(menu).GetValue(IsNativeMenuBarActiveProperty);

        /// <summary>Rebuilds the installed menu bar now (for example after a header or gesture changed).</summary>
        public static void Refresh()
        {
            s_pendingRebuild?.Abort();
            s_pendingRebuild = null;
            Rebuild();
        }

        private static Menu ValidateMenu(Menu menu)
        {
            ArgumentNullException.ThrowIfNull(menu);
            return menu;
        }

        private static void OnUsesNativeMenuBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Menu menu)
            {
                return;
            }

            if ((bool)e.NewValue)
            {
                menu.Loaded += OnMenuLoaded;
                menu.Unloaded += OnMenuUnloaded;
                if (menu.IsLoaded)
                {
                    Register(menu);
                }
            }
            else
            {
                menu.Loaded -= OnMenuLoaded;
                menu.Unloaded -= OnMenuUnloaded;
                Unregister(menu);
            }
        }

        private static void OnMenuLoaded(object sender, RoutedEventArgs e) => Register((Menu)sender);

        private static void OnMenuUnloaded(object sender, RoutedEventArgs e) => Unregister((Menu)sender);

        private static void Register(Menu menu)
        {
            if (!s_registered.Contains(menu))
            {
                s_registered.Add(menu);
                var watch = new MenuWatch(menu, () => ScheduleRebuild(menu));
                s_watches[menu] = watch;
                watch.Attach();

                // Activation is also when the key window changes (the bar follows it) and when the
                // host finishes launching, which replaces any menu bar installed before that.
                Window window = Window.GetWindow(menu);
                if (window != null)
                {
                    window.Activated += OnWindowActivated;
                }
            }

            ScheduleRebuild(menu);
        }

        private static void Unregister(Menu menu)
        {
            if (s_registered.Remove(menu))
            {
                if (s_watches.Remove(menu, out MenuWatch watch))
                {
                    watch.Detach();
                }

                Window window = Window.GetWindow(menu);
                if (window != null)
                {
                    window.Activated -= OnWindowActivated;
                }

                ScheduleRebuild(menu);
            }
        }

        private static void OnWindowActivated(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                ScheduleRebuild(window);
            }
        }

        /// <summary>
        /// Coalesces rebuilds to one per dispatcher turn. Pending is the live operation, not a flag,
        /// so a dispatcher that shut down with a rebuild queued cannot block later ones.
        /// </summary>
        private static void ScheduleRebuild(DispatcherObject source)
        {
            if (s_pendingRebuild != null &&
                s_pendingRebuild.Status == DispatcherOperationStatus.Pending &&
                ReferenceEquals(s_pendingRebuild.Dispatcher, source.Dispatcher))
            {
                return;
            }

            s_pendingRebuild = source.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                s_pendingRebuild = null;
                Rebuild();
            }));
        }

        /// <summary>The active window's menu, else the main window's, else the most recently registered.</summary>
        private static Menu ChooseMenu()
        {
            foreach (Menu menu in s_registered)
            {
                Window window = Window.GetWindow(menu);
                if (window != null && window.IsActive)
                {
                    return menu;
                }
            }

            Window main = Application.Current?.MainWindow;
            if (main != null)
            {
                foreach (Menu menu in s_registered)
                {
                    if (ReferenceEquals(Window.GetWindow(menu), main))
                    {
                        return menu;
                    }
                }
            }

            return s_registered.Count > 0 ? s_registered[s_registered.Count - 1] : null;
        }

        private static void Rebuild()
        {
            Menu chosen = ChooseMenu();
            if (chosen == null)
            {
                SetInstalled(null);
                return;
            }

            var mapper = new MacMenuMapper();
            PortableMenuItem[] model = mapper.Map(chosen);
            Dispatcher dispatcher = chosen.Dispatcher;
            bool installed = PortableWindowActivationService.TryInstallMenuBar(
                ResolveApplicationName(),
                model,
                id => dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => Activate(id))));

            s_mapper = mapper;
            SetInstalled(installed ? chosen : null);
        }

        private static void SetInstalled(Menu menu)
        {
            if (ReferenceEquals(s_installed, menu))
            {
                return;
            }

            if (s_installed != null)
            {
                s_installed.SetValue(s_isNativeMenuBarActivePropertyKey, false);
                s_installed.InvalidateProperty(UIElement.VisibilityProperty);
            }

            s_installed = menu;
            if (menu != null)
            {
                menu.SetValue(s_isNativeMenuBarActivePropertyKey, true);
                menu.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            }
        }

        private static string ResolveApplicationName() =>
            !string.IsNullOrEmpty(ApplicationName) ? ApplicationName : Assembly.GetEntryAssembly()?.GetName().Name ?? "Application";

        /// <summary>Runs on the dispatcher: an activated native item, or Quit.</summary>
        private static void Activate(int id)
        {
            if (id == QuitId)
            {
                Quit();
                return;
            }

            MenuItem item = s_mapper.GetItem(id);
            if (item != null && item.IsEnabled)
            {
                // The public peer is the supported way to click a MenuItem: it runs the command and
                // raises Click (asynchronously, as a mouse click does).
                (UIElementAutomationPeer.CreatePeerForElement(item)?.GetPattern(PatternInterface.Invoke) as IInvokeProvider)?.Invoke();
            }
        }

        /// <summary>
        /// Closes the non-main windows first and the main window last, stopping at the first window
        /// that cancels, then shuts the application down; independent of ShutdownMode.
        /// </summary>
        internal static void Quit()
        {
            Application app = Application.Current;
            if (app == null)
            {
                return;
            }

            var order = new List<Window>();
            Window main = app.MainWindow;
            foreach (Window window in app.Windows)
            {
                if (!ReferenceEquals(window, main))
                {
                    order.Add(window);
                }
            }

            if (main != null)
            {
                order.Add(main);
            }

            foreach (Window window in order)
            {
                window.Close();
                if (IsOpen(app, window))
                {
                    return;
                }
            }

            app.Shutdown();
        }

        private static bool IsOpen(Application app, Window window)
        {
            foreach (Window open in app.Windows)
            {
                if (ReferenceEquals(open, window))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Watches everything the mapped model reads: item collections at every level, and per item
        /// Header, Command, CommandParameter, InputGestureText, IsEnabled, IsChecked, IsCheckable and
        /// Visibility (separators included). A change to a collection re-walks the tree so new items
        /// are watched; any change asks for one coalesced rebuild. (Changes to the gestures inside a
        /// RoutedCommand are not observable and need MacMenu.Refresh.)
        /// </summary>
        private sealed class MenuWatch
        {
            private static readonly DependencyProperty[] s_itemProperties =
            {
                HeaderedItemsControl.HeaderProperty,
                MenuItem.CommandProperty,
                MenuItem.CommandParameterProperty,
                MenuItem.InputGestureTextProperty,
                UIElement.IsEnabledProperty,
                MenuItem.IsCheckedProperty,
                MenuItem.IsCheckableProperty,
                UIElement.VisibilityProperty,
            };

            private readonly Menu _menu;
            private readonly Action _changed;
            private readonly List<ItemCollection> _collections = new List<ItemCollection>();
            private readonly List<MenuItem> _items = new List<MenuItem>();
            private readonly List<UIElement> _separators = new List<UIElement>();
            private readonly EventHandler _onPropertyChanged;
            private readonly NotifyCollectionChangedEventHandler _onCollectionChanged;

            internal MenuWatch(Menu menu, Action changed)
            {
                _menu = menu;
                _changed = changed;
                _onPropertyChanged = (sender, e) => _changed();
                _onCollectionChanged = (sender, e) =>
                {
                    Detach();
                    Attach();
                    _changed();
                };
            }

            internal void Attach()
            {
                Walk(_menu.Items);
            }

            internal void Detach()
            {
                foreach (ItemCollection collection in _collections)
                {
                    ((INotifyCollectionChanged)collection).CollectionChanged -= _onCollectionChanged;
                }

                foreach (MenuItem item in _items)
                {
                    foreach (DependencyProperty property in s_itemProperties)
                    {
                        DependencyPropertyDescriptor.FromProperty(property, typeof(MenuItem))?.RemoveValueChanged(item, _onPropertyChanged);
                    }
                }

                foreach (UIElement separator in _separators)
                {
                    DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement))?.RemoveValueChanged(separator, _onPropertyChanged);
                }

                _collections.Clear();
                _items.Clear();
                _separators.Clear();
            }

            private void Walk(ItemCollection items)
            {
                ((INotifyCollectionChanged)items).CollectionChanged += _onCollectionChanged;
                _collections.Add(items);
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] is MenuItem item)
                    {
                        foreach (DependencyProperty property in s_itemProperties)
                        {
                            DependencyPropertyDescriptor.FromProperty(property, typeof(MenuItem))?.AddValueChanged(item, _onPropertyChanged);
                        }

                        _items.Add(item);
                        Walk(item.Items);
                    }
                    else if (items[i] is Separator separator)
                    {
                        DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement))?.AddValueChanged(separator, _onPropertyChanged);
                        _separators.Add(separator);
                    }
                }
            }
        }
    }
}
