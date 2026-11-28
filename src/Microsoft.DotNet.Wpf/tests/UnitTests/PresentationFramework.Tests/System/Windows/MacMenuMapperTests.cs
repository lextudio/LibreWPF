// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Platform.MacOS;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class MacMenuMapperTests
{
    [Theory]
    [InlineData("_View", "View")]
    [InlineData("Save _As", "Save As")]
    [InlineData("Snake__case", "Snake_case")]
    [InlineData("", "")]
    public void AccessKeyUnderscoresAreRemoved(string header, string expected)
    {
        Assert.Equal(expected, MacMenuMapper.StripAccessKey(header));
    }

    [Theory]
    [InlineData("Cmd+1", true, "1", MacMenuMapper.ModifierCommand)]
    [InlineData("Command+Shift+z", true, "z", MacMenuMapper.ModifierCommand | MacMenuMapper.ModifierShift)]
    [InlineData("Win+Alt+Q", true, "q", MacMenuMapper.ModifierCommand | MacMenuMapper.ModifierOption)]
    [InlineData("Ctrl+K", true, "k", MacMenuMapper.ModifierControl)]
    [InlineData("A", false, "", 0)]
    [InlineData("Cmd+Enter", false, "", 0)]
    [InlineData("Hyper+1", false, "", 0)]
    [InlineData("", false, "", 0)]
    public void GestureTextParses(string text, bool ok, string key, int modifiers)
    {
        Assert.Equal(ok, MacMenuMapper.TryParseGestureText(text, out string actualKey, out int actualModifiers));
        Assert.Equal(key, actualKey);
        Assert.Equal(modifiers, actualModifiers);
    }

    [Fact]
    public void TreeMapsTitlesStatesGesturesAndRoles()
    {
        RunInUiApartment(() =>
        {
            var vitals = new MenuItem { Header = "_Vitals", InputGestureText = "Cmd+1" };
            var hidden = new MenuItem { Header = "Hidden", Visibility = Visibility.Collapsed };
            var disabled = new MenuItem { Header = "Off", IsEnabled = false };
            var checkedItem = new MenuItem { Header = "Auto", IsCheckable = true, IsChecked = true };
            var unchecked1 = new MenuItem { Header = "Light", IsCheckable = true };
            var notCheckable = new MenuItem { Header = "Plain", IsChecked = true };
            var view = new MenuItem { Header = "_View" };
            view.Items.Add(vitals);
            view.Items.Add(hidden);
            view.Items.Add(new Separator());
            view.Items.Add(disabled);
            view.Items.Add(checkedItem);
            view.Items.Add(unchecked1);
            view.Items.Add(notCheckable);
            var edit = new MenuItem { Header = "_Edit" };
            var menu = new Menu();
            menu.Items.Add(view);
            menu.Items.Add(edit);
            menu.Items.Add(new MenuItem { Header = "Window" });

            var mapper = new MacMenuMapper();
            PortableMenuItem[] top = mapper.Map(menu);

            Assert.Equal(new[] { "View", "Edit", "Window" }, Array.ConvertAll(top, t => t.Title));
            Assert.Equal(new[] { 0, 1, 2 }, Array.ConvertAll(top, t => t.Role));

            PortableMenuItem[] children = top[0].Children;
            // Hidden is omitted; the separator keeps its place.
            Assert.Equal(6, children.Length);
            Assert.Equal("Vitals", children[0].Title);
            Assert.Equal("1", children[0].KeyEquivalent);
            Assert.Equal(MacMenuMapper.ModifierCommand, children[0].Modifiers);
            Assert.True(children[1].IsSeparator);
            Assert.False(children[2].IsEnabled);
            Assert.True(children[3].IsChecked);
            Assert.False(children[4].IsChecked);
            Assert.False(children[5].IsChecked);
            Assert.True(children[0].IsEnabled);

            // Ids are unique and resolve back to the WPF item (separators have no item).
            var ids = new HashSet<int>();
            void Collect(PortableMenuItem[] items)
            {
                foreach (PortableMenuItem item in items)
                {
                    Assert.True(ids.Add(item.Id));
                    Collect(item.Children);
                }
            }

            Collect(top);
            Assert.Same(vitals, mapper.GetItem(children[0].Id));
            Assert.Null(mapper.GetItem(children[1].Id));
            Assert.Same(view, mapper.GetItem(top[0].Id));
        });
    }

    [Fact]
    public void RoutedCommandGestureWinsOverGestureText()
    {
        RunInUiApartment(() =>
        {
            var command = new RoutedCommand("Show", typeof(MacMenuMapperTests));
            command.InputGestures.Add(new KeyGesture(Key.D2, ModifierKeys.Windows | ModifierKeys.Shift));
            var item = new MenuItem { Header = "Silicon", Command = command, InputGestureText = "Cmd+9" };

            Assert.True(MacMenuMapper.TryGetKeyEquivalent(item, out string key, out int modifiers));
            Assert.Equal("2", key);
            Assert.Equal(MacMenuMapper.ModifierCommand | MacMenuMapper.ModifierShift, modifiers);
        });
    }

    [Fact]
    public void UnmappableOrModifierlessGesturesFallBackToGestureText()
    {
        RunInUiApartment(() =>
        {
            var enter = new RoutedCommand("Enter", typeof(MacMenuMapperTests));
            enter.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.Windows));

            Assert.True(MacMenuMapper.TryGetKeyEquivalent(
                new MenuItem { Command = enter, InputGestureText = "Cmd+7" }, out string key, out int modifiers));
            Assert.Equal("7", key);
            Assert.Equal(MacMenuMapper.ModifierCommand, modifiers);

            // WPF itself rejects an unmodified letter gesture; unmodified gesture text is rejected here.
            Assert.False(MacMenuMapper.TryGetKeyEquivalent(new MenuItem { InputGestureText = "A" }, out key, out modifiers));
            Assert.Equal(0, modifiers);

            // Gesture text on an item whose command is not a RoutedCommand.
            Assert.True(MacMenuMapper.TryGetKeyEquivalent(
                new MenuItem { Command = new RelayStub(), InputGestureText = "Cmd+Shift+3" }, out key, out modifiers));
            Assert.Equal("3", key);
        });
    }

    [Fact]
    public void ElementHeadersHaveNoTitle()
    {
        RunInUiApartment(() =>
        {
            var menu = new Menu();
            menu.Items.Add(new MenuItem { Header = new Border() });
            menu.Items.Add(new MenuItem { Header = 42 });
            PortableMenuItem[] top = new MacMenuMapper().Map(menu);
            Assert.Equal(string.Empty, top[0].Title);
            Assert.Equal("42", top[1].Title);
        });
    }

    private sealed class RelayStub : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }

    [PortableInputFact]
    public void NativeMenuBarIsInstalledCollapsesTheMenuAndRoutesActivations()
    {
        RunInUiApartment(() =>
        {
            string? name = null;
            PortableMenuItem[]? model = null;
            Action<int>? activate = null;
            bool accept = true;
            PortableWindowActivationService.Register(
                activate: value => value,
                installMenuBar: (application, items, callbacks) =>
                {
                    name = application.ApplicationName;
                    model = items;
                    activate = callbacks.Activate;
                    return accept;
                });
            try
            {
                int runs = 0;
                var vitals = new MenuItem { Header = "_Vitals" };
                vitals.Click += (_, _) => runs++;
                var view = new MenuItem { Header = "View" };
                view.Items.Add(vitals);
                var menu = new Menu();
                menu.Items.Add(view);

                MacMenu.ApplicationName = "Pulse";
                MacMenu.SetUsesNativeMenuBar(menu, true);
                menu.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                MacMenu.Refresh();

                Assert.Equal("Pulse", name);
                Assert.Equal("View", model![0].Title);
                Assert.Equal("Vitals", model[0].Children[0].Title);
                Assert.True(MacMenu.GetIsNativeMenuBarActive(menu));
                Assert.Equal(Visibility.Collapsed, menu.Visibility);

                // Activating the item through the host callback raises its Click once the dispatcher turns.
                activate!(model[0].Children[0].Id);
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                Assert.True(runs >= 1);

                // A host without a native bar leaves the in-window menu alone.
                accept = false;
                MacMenu.Refresh();
                Assert.False(MacMenu.GetIsNativeMenuBarActive(menu));
                Assert.Equal(Visibility.Visible, menu.Visibility);

                MacMenu.SetUsesNativeMenuBar(menu, false);
                Assert.False(MacMenu.GetIsNativeMenuBarActive(menu));
            }
            finally
            {
                MacMenu.ApplicationName = null!;
                PortableWindowActivationService.Clear();
            }
        });
    }

    private sealed class PortableInputFactAttribute : FactAttribute
    {
        public PortableInputFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before input-manager construction, including on Windows.";
        }
    }

    [PortableInputFact]
    public void NativeMenuBarRebuildsWhenWhatTheModelReadsChanges()
    {
        RunInUiApartment(() =>
        {
            PortableMenuItem[]? model = null;
            int installs = 0;
            PortableWindowActivationService.Register(
                activate: value => value,
                installMenuBar: (application, items, callbacks) =>
                {
                    model = items;
                    installs++;
                    return true;
                });
            void Flush()
            {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            }

            try
            {
                var vitals = new MenuItem { Header = "Vitals" };
                var separator = new Separator();
                var silicon = new MenuItem { Header = "Silicon", IsCheckable = true };
                var view = new MenuItem { Header = "View" };
                view.Items.Add(vitals);
                view.Items.Add(separator);
                view.Items.Add(silicon);
                var menu = new Menu();
                menu.Items.Add(view);

                MacMenu.SetUsesNativeMenuBar(menu, true);
                menu.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Flush();
                Assert.NotNull(model);
                int baseline = installs;
                Assert.Equal(3, model![0].Children.Length);

                vitals.Header = "_Overview";
                Flush();
                Assert.Equal("Overview", model![0].Children[0].Title);

                silicon.IsChecked = true;
                Flush();
                Assert.True(model![0].Children[2].IsChecked);

                vitals.IsEnabled = false;
                Flush();
                Assert.False(model![0].Children[0].IsEnabled);

                vitals.InputGestureText = "Cmd+1";
                Flush();
                Assert.Equal("1", model![0].Children[0].KeyEquivalent);

                separator.Visibility = Visibility.Collapsed;
                Flush();
                Assert.Equal(2, model![0].Children.Length);

                // A nested item added later is itself watched.
                var added = new MenuItem { Header = "Proof" };
                view.Items.Add(added);
                Flush();
                Assert.Equal("Proof", model![0].Children[2].Title);
                added.Header = "Facts";
                Flush();
                Assert.Equal("Facts", model![0].Children[2].Title);

                // Several changes in one turn coalesce into one install.
                int before = installs;
                vitals.Header = "A";
                silicon.Header = "B";
                added.Header = "C";
                Flush();
                Assert.Equal(before + 1, installs);

                // The Role attached property is a model input too: changing it rebuilds with the Edit items.
                var edit = new MenuItem { Header = "Bearbeiten" };
                menu.Items.Add(edit);
                Flush();
                int beforeRole = installs;
                Assert.Empty(model![1].Children);
                MacMenu.SetRole(edit, MacMenuRole.Edit);
                Flush();
                Assert.Equal(beforeRole + 1, installs);
                Assert.Equal(8, model![1].Children.Length);

                // Detached watches stop reacting.
                MacMenu.SetUsesNativeMenuBar(menu, false);
                // Unregistering replaces the installed bar with the generated menu alone, once.
                Flush();
                Assert.Empty(model!);
                int after = installs;
                vitals.Header = "Z";
                Flush();
                Assert.Equal(after, installs);
                Assert.True(installs > baseline);
            }
            finally
            {
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void ExplicitRoleBeatsTheHeaderAndAnEmptyEditMenuGetsTheStandardItems()
    {
        RunInUiApartment(() =>
        {
            var edit = new MenuItem { Header = "Bearbeiten" };
            MacMenu.SetRole(edit, MacMenuRole.Edit);
            var window = new MenuItem { Header = "Fenster" };
            MacMenu.SetRole(window, MacMenuRole.Window);
            var english = new MenuItem { Header = "Edit" };
            var own = new MenuItem { Header = "Edit" };
            own.Items.Add(new MenuItem { Header = "Mine" });
            var menu = new Menu();
            menu.Items.Add(edit);
            menu.Items.Add(window);
            menu.Items.Add(english);
            menu.Items.Add(own);

            var mapper = new MacMenuMapper();
            PortableMenuItem[] top = mapper.Map(menu);

            Assert.Equal(new[] { 1, 2, 1, 1 }, Array.ConvertAll(top, t => t.Role));
            string[] titles = Array.ConvertAll(top[0].Children, c => c.Title);
            Assert.Equal(new[] { "Undo", "Redo", "", "Cut", "Copy", "Paste", "", "Select All" }, titles);
            Assert.Equal("z", top[0].Children[1].KeyEquivalent);
            Assert.Equal(MacMenuMapper.ModifierCommand | MacMenuMapper.ModifierShift, top[0].Children[1].Modifiers);
            Assert.Equal("c", top[0].Children[4].KeyEquivalent);
            Assert.Equal(8, top[2].Children.Length);
            // Items the app collapsed still count as its own: nothing is generated in their place.
            var hiddenOnly = new MenuItem { Header = "Edit" };
            hiddenOnly.Items.Add(new MenuItem { Header = "Hidden", Visibility = Visibility.Collapsed });
            menu.Items.Add(hiddenOnly);
            top = mapper.Map(menu);
            Assert.Empty(top[4].Children);

            // An Edit menu with items of its own keeps them and gets nothing added.
            Assert.Equal(new[] { "Mine" }, Array.ConvertAll(top[3].Children, c => c.Title));
        });
    }

    [Fact]
    public void RoutedCommandItemsAreEvaluatedAndRunAgainstTheirTargetNotTheDetachedMenuItem()
    {
        RunInUiApartment(() =>
        {
            int executed = 0;
            bool allowed = false;
            var command = new RoutedCommand("Probe", typeof(MacMenuMapperTests));
            var target = new Border();
            target.CommandBindings.Add(new CommandBinding(
                command,
                (_, _) => executed++,
                (_, e) => { e.CanExecute = allowed; e.Handled = true; }));
            int clicks = 0;
            var item = new MenuItem { Header = "Probe", Command = command, CommandTarget = target };
            item.Click += (_, _) => clicks++;
            var disabled = new MenuItem { Header = "Off", Command = command, CommandTarget = target, IsEnabled = false };
            var top = new MenuItem { Header = "Top" };
            top.Items.Add(item);
            top.Items.Add(disabled);
            var menu = new Menu();
            menu.Items.Add(top);

            var mapper = new MacMenuMapper();
            PortableMenuItem[] model = mapper.Map(menu);
            int id = model[0].Children[0].Id;
            int offId = model[0].Children[1].Id;

            Assert.False(model[0].Children[0].IsEnabled);
            Assert.False(mapper.IsEnabledNow(id));
            Assert.True(mapper.TryExecute(id));
            Assert.Equal(0, executed);

            allowed = true;
            Assert.True(mapper.IsEnabledNow(id));
            Assert.True(mapper.TryExecute(id));
            Assert.Equal(1, executed);
            Assert.Equal(1, clicks);

            // An explicitly disabled item stays disabled whatever the command says.
            Assert.False(mapper.IsEnabledNow(offId));
            Assert.True(mapper.TryExecute(offId));
            Assert.Equal(1, executed);

            // An app-bound false is honoured even though the command could run, and so is a disabled parent.
            var gate = new GateStub { Enabled = false };
            var bound = new MenuItem { Header = "Bound", Command = command, CommandTarget = target };
            bound.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding(nameof(GateStub.Enabled)) { Source = gate });
            var holder = new MenuItem { Header = "Holder", IsEnabled = false };
            var child = new MenuItem { Header = "Child", Command = command, CommandTarget = target };
            holder.Items.Add(child);
            top.Items.Add(bound);
            top.Items.Add(holder);
            model = mapper.Map(menu);
            int boundId = model[0].Children[2].Id;
            int childId = model[0].Children[3].Children[0].Id;
            Assert.False(mapper.IsEnabledNow(boundId));
            Assert.False(model[0].Children[2].IsEnabled);
            Assert.False(mapper.IsEnabledNow(childId));
            int ran = executed;
            Assert.True(mapper.TryExecute(boundId));
            Assert.True(mapper.TryExecute(childId));
            Assert.Equal(ran, executed);
            gate.Enabled = true;
            Assert.True(mapper.IsEnabledNow(boundId));

            // A checkable routed-command item toggles when run.
            var check = new MenuItem { Header = "Check", Command = command, CommandTarget = target, IsCheckable = true };
            top.Items.Add(check);
            model = mapper.Map(menu);
            int checkId = model[0].Children[4].Id;
            Assert.False(check.IsChecked);
            Assert.True(mapper.TryExecute(checkId));
            Assert.True(check.IsChecked);
            Assert.True(mapper.TryExecute(checkId));
            Assert.False(check.IsChecked);

            // An item with no routed command is left for the caller to click.
            var plain = new MenuItem { Header = "Plain" };
            top.Items.Add(plain);
            model = mapper.Map(menu);
            Assert.False(mapper.TryExecute(model[0].Children[5].Id));
        });
    }

    [PortableInputFact]
    public void ApplicationMenuItemsAboutCommandAndDockMenuReachTheHost()
    {
        RunInUiApartment(() =>
        {
            PortableApplicationMenu? application = null;
            PortableMenuItem[]? model = null;
            PortableMenuCallbacks? callbacks = null;
            int installs = 0;
            PortableWindowActivationService.Register(
                activate: value => value,
                installMenuBar: (app, items, cb) =>
                {
                    application = app;
                    model = items;
                    callbacks = cb;
                    installs++;
                    return true;
                });
            void Flush()
            {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            }

            var menu = new Menu();
            menu.Items.Add(new MenuItem { Header = "View" });
            int about = 0;
            int preferences = 0;
            var preferencesItem = new MenuItem { Header = "Preferences" };
            preferencesItem.Click += (_, _) => preferences++;
            try
            {
                MacMenu.ApplicationName = "Pulse";
                MacMenu.SetUsesNativeMenuBar(menu, true);
                menu.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Flush();
                Assert.False(application!.HasCustomAbout);
                Assert.Empty(application.ApplicationItems);
                Assert.Empty(application.DockItems);

                MacMenu.AboutCommand = new RelayCommand(() => about++);
                MacMenu.ApplicationMenuItems.Add(preferencesItem);
                var dock = new Menu();
                int dockClicks = 0;
                var dockItem = new MenuItem { Header = "New Window" };
                dockItem.Click += (_, _) => dockClicks++;
                dock.Items.Add(dockItem);
                MacMenu.DockMenu = dock;
                Flush();

                Assert.True(application!.HasCustomAbout);
                Assert.Equal(new[] { "Preferences" }, Array.ConvertAll(application.ApplicationItems, i => i.Title));
                Assert.Equal(new[] { "New Window" }, Array.ConvertAll(application.DockItems, i => i.Title));
                Assert.Equal(new[] { "View" }, Array.ConvertAll(model!, i => i.Title));

                // The application items are real model items with distinct ids.
                Assert.NotEqual(model![0].Id, application.ApplicationItems[0].Id);
                callbacks!.Activate(application.ApplicationItems[0].Id);
                callbacks.Activate(-2);
                Flush();
                Flush();
                Assert.Equal(1, about);
                Assert.True(preferences >= 1);
                // A Dock item is clicked through its automation peer even though it sits in no visual tree.
                callbacks.Activate(application.DockItems[0].Id);
                Flush();
                Flush();
                Assert.True(dockClicks >= 1);
                Assert.True(callbacks.Validate(application.ApplicationItems[0].Id));

                // Editing an application item rebuilds.
                int before = installs;
                preferencesItem.Header = "Settings";
                Flush();
                Assert.Equal(before + 1, installs);
                Assert.Equal("Settings", application!.ApplicationItems[0].Title);

                // Removing everything replaces the installed bar with the generated menu alone: no
                // old items stay live and the Dock menu is cleared.
                MacMenu.SetUsesNativeMenuBar(menu, false);
                MacMenu.AboutCommand = null!;
                MacMenu.ApplicationMenuItems.Clear();
                MacMenu.DockMenu = null!;
                int beforeClear = installs;
                MacMenu.Refresh();
                Assert.Equal(beforeClear + 1, installs);
                Assert.Empty(model!);
                Assert.Empty(application!.ApplicationItems);
                Assert.Empty(application.DockItems);
                Assert.False(application.HasCustomAbout);
                int afterClear = installs;
                MacMenu.Refresh();
                Assert.Equal(afterClear, installs);
            }
            finally
            {
                MacMenu.SetUsesNativeMenuBar(menu, false);
                MacMenu.AboutCommand = null!;
                MacMenu.ApplicationMenuItems.Clear();
                MacMenu.DockMenu = null!;
                MacMenu.ApplicationName = null!;
                PortableWindowActivationService.Clear();
            }
        });
    }

    private sealed class GateStub : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _enabled;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Enabled)));
            }
        }
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();
    }

    [Fact]
    public void NullMenuMapsToNothing()
    {
        Assert.Empty(new MacMenuMapper().Map(null!));
    }

    private static void RunInUiApartment(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        })
        {
            IsBackground = true
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Mac menu mapper test did not complete within 30 seconds.");
        }

        if (exception != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
