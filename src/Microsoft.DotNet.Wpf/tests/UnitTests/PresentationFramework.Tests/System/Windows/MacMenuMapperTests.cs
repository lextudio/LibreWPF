// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
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
