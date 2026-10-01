// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Controls;
using System.Windows.Input;
using ProGPU.Wpf.Interop;

namespace System.Windows;

public sealed partial class PortableScrollSourceTests
{
    [PortableScrollFact]
    public void NativeScrollOpenComboBoxContainsUnhandledAxesWithoutLegacyWheel()
    {
        Run(() =>
        {
            foreach (var unit in new[] { PortablePointerScrollUnit.Points, PortablePointerScrollUnit.Lines })
            foreach (bool focus in new[] { false, true })
            foreach (var motion in new[] { new Vector(-0.25, 0), new Vector(0, -0.5), new Vector(-0.25, -0.5) })
            {
                using var fixture = new ComboBoxScrollFixture();
                fixture.DisableChildAxes();
                if (focus) Assert.Same(fixture.ComboBox, Keyboard.Focus(fixture.ComboBox));
                else Keyboard.ClearFocus();
                Assert.Equal(focus, fixture.ComboBox.IsKeyboardFocusWithin);
                var packet = Packet(motion.X, motion.Y, unit);
                var calls = new List<string>();
                int ordinary = 0, wheel = 0;
                fixture.Child.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
                {
                    Assert.False(value.Handled);
                    calls.Add("child");
                }), true);
                fixture.ComboBox.AddHandler(PortableScroll.ScrollEvent,
                    new RoutedEventHandler((_, _) => ++ordinary));
                fixture.ComboBox.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
                {
                    var input = Assert.IsType<PortableScrollEventArgs>(value);
                    Assert.True(input.Handled);
                    Assert.Same(packet, input.NativeInput);
                    Assert.Equal(motion, input.RemainingScroll);
                    calls.Add("combo");
                }), true);
                fixture.ComboBox.MouseWheel += (_, _) => ++wheel;

                Assert.True(Route(fixture.Child, packet, out bool handled));
                Assert.True(handled);
                fixture.Layout();
                Assert.Equal(new[] { "child", "combo" }, calls);
                Assert.Equal(0, ordinary); Assert.Equal(0, wheel);
                Assert.Equal(1, fixture.ComboBox.SelectedIndex);
                fixture.AssertAncestorUnchanged();
            }
        });
    }

    [PortableScrollFact]
    public void NativeScrollOpenComboBoxContainsDeferredChildBoundaryOverflow()
    {
        Run(() =>
        {
            foreach (var unit in new[] { PortablePointerScrollUnit.Points, PortablePointerScrollUnit.Lines })
            {
                using var fixture = new ComboBoxScrollFixture();
                bool lines = unit == PortablePointerScrollUnit.Lines;
                fixture.Child.Info.DeferredOffsets = true;
                fixture.Child.Info.ExtentHeight = lines ? 151.25 : 142;
                int observed = 0;
                fixture.ComboBox.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
                {
                    Assert.True(value.Handled); // The child already provisionally claimed the packet.
                    Assert.Equal(default, ((PortableScrollEventArgs)value).RemainingScroll);
                    ++observed;
                }), true);

                Assert.True(Route(fixture.Child, RoutedPacket(50, lines ? -2.25 : -5,
                    phase: 1, unit: unit), out bool handled));
                Assert.True(handled);
                Assert.Equal(40, fixture.Child.Info.VerticalOffset);
                fixture.Layout();
                Assert.Equal(lines ? 51.25 : 42, fixture.Child.Info.VerticalOffset);
                Assert.Equal(lines ? new[] { "down" } : Array.Empty<string>(), fixture.Child.Info.Lines);
                fixture.AssertAncestorUnchanged();
                fixture.Layout();
                fixture.AssertAncestorUnchanged();
                Assert.Equal(1, observed); // No application replay during overflow.
            }
        });
    }

    [PortableScrollFact]
    public void NativeScrollOpenComboBoxContainsOnlyTheUnclaimedAxisAfterChildAdmission()
    {
        Run(() =>
        {
            using var fixture = new ComboBoxScrollFixture();
            fixture.Child.Viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            fixture.Child.Viewer.UpdateLayout();
            Vector remainder = default;
            fixture.ComboBox.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                Assert.True(value.Handled);
                remainder = ((PortableScrollEventArgs)value).RemainingScroll;
            }), true);

            Assert.True(Route(fixture.Child, Packet(-3, -5, PortablePointerScrollUnit.Points), out bool handled));
            Assert.True(handled);
            fixture.Layout();
            Assert.Equal(new Vector(-3, 0), remainder);
            Assert.Equal(40, fixture.Child.Info.HorizontalOffset);
            Assert.Equal(45, fixture.Child.Info.VerticalOffset);
            fixture.AssertAncestorUnchanged();
        });
    }

    [PortableScrollFact]
    public void NativeScrollOpenComboBoxContainsOverflowCompletedBeforeRouteSealing()
    {
        Run(() =>
        {
            using var fixture = new ComboBoxScrollFixture();
            fixture.Child.Info.ExtentHeight = 142;
            int observed = 0;
            fixture.Child.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, _) =>
            {
                fixture.Child.Viewer.UpdateLayout();
                Assert.Equal(42, fixture.Child.Info.VerticalOffset);
                fixture.AssertAncestorUnchanged();
                ++observed;
            }), true);

            Assert.True(Route(fixture.Child, RoutedPacket(50, -5, phase: 1), out bool handled));
            Assert.True(handled);
            fixture.Layout();
            fixture.AssertAncestorUnchanged();
            Assert.Equal(1, observed);
        });
    }

    [PortableScrollFact]
    public void NativeScrollComboBoxApplicationCanClearContainmentBeforeAncestorAdmission()
    {
        Run(() =>
        {
            foreach (bool childAccepts in new[] { false, true })
            {
                using var fixture = new ComboBoxScrollFixture();
                if (childAccepts) fixture.Child.Info.ExtentHeight = 142;
                else fixture.DisableChildAxes();
                var calls = new List<string>();
                fixture.ComboBox.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
                {
                    Assert.True(value.Handled);
                    value.Handled = false;
                    calls.Add("release");
                }), true);
                fixture.ComboBox.AddHandler(PortableScroll.ScrollEvent,
                    new RoutedEventHandler((_, _) => calls.Add("ordinary")));

                Assert.True(Route(fixture.Child, RoutedPacket(50, -5, phase: 1), out bool handled));
                Assert.True(handled);
                fixture.Layout();
                Assert.Equal(childAccepts ? 42 : 40, fixture.Child.Info.VerticalOffset);
                Assert.Equal(childAccepts ? 43 : 45, fixture.Ancestor.Info.VerticalOffset);
                Assert.Equal(new[] { "release", "ordinary" }, calls);
            }
        });
    }

    [PortableScrollFact]
    public void NativeScrollClosedComboBoxDoesNotInventWheelSelectionOrContainment()
    {
        Run(() =>
        {
            foreach (var unit in new[] { PortablePointerScrollUnit.Points, PortablePointerScrollUnit.Lines })
            {
                using var fixture = new ComboBoxScrollFixture();
                fixture.ComboBox.IsDropDownOpen = false;
                fixture.DisableChildAxes();
                Assert.Same(fixture.ComboBox, Keyboard.Focus(fixture.ComboBox));
                int observed = 0, wheel = 0;
                fixture.ComboBox.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
                {
                    Assert.False(value.Handled);
                    ++observed;
                }));
                fixture.ComboBox.MouseWheel += (_, _) => ++wheel;

                Assert.True(Route(fixture.Child, Packet(0, -1, unit), out bool handled));
                Assert.True(handled); // The actual ancestor, not ComboBox, owns this motion.
                fixture.Layout();
                Assert.Equal(unit == PortablePointerScrollUnit.Lines ? 51.25 : 41,
                    fixture.Ancestor.Info.VerticalOffset);
                Assert.Equal(1, fixture.ComboBox.SelectedIndex);
                Assert.Equal(1, observed); Assert.Equal(0, wheel);
            }
        });
    }

    [PortableScrollFact]
    public void NativeScrollComboBoxCannotClaimReentrantRaiseOfTheSameArguments()
    {
        Run(() =>
        {
            using var fixture = new ComboBoxScrollFixture();
            fixture.ComboBox.IsDropDownOpen = false;
            fixture.DisableChildAxes();
            var peer = fixture.AddOpenPeer();
            int nested = 0;
            fixture.Child.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                peer.RaiseEvent(value);
                Assert.False(value.Handled);
                ++nested;
            }));

            Assert.True(Route(fixture.Child, Packet(0, -5, PortablePointerScrollUnit.Points), out bool handled));
            Assert.True(handled);
            fixture.Layout();
            Assert.Equal(45, fixture.Ancestor.Info.VerticalOffset);
            Assert.Equal(1, nested);
            peer.IsDropDownOpen = false;
        });
    }

    [PortableScrollFact]
    public void NativeScrollComboBoxRejectsOldOriginAfterSourceReopen()
    {
        Run(() =>
        {
            using var fixture = new ComboBoxScrollFixture();
            fixture.DisableChildAxes();
            bool replace = true;
            int stale = 0, current = 0;
            fixture.Child.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, _) =>
            {
                if (!replace) return;
                replace = false;
                fixture.Child.Host.RootVisual = null;
                fixture.Child.Host.RootVisual = fixture.Child.Root;
                fixture.Child.Host.SetClientSize(400, 200);
            }));
            fixture.ComboBox.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                if (((PortableScrollEventArgs)value).IsCurrent)
                {
                    Assert.True(value.Handled);
                    ++current;
                }
                else
                {
                    Assert.False(value.Handled);
                    ++stale;
                }
            }), true);

            Assert.True(Route(fixture.Child, Packet(0, -5, PortablePointerScrollUnit.Points), out bool handled));
            Assert.True(handled); // Stale source admission must not replay the original packet.
            fixture.Layout();
            fixture.AssertAncestorUnchanged();
            Assert.True(Route(fixture.Child, Packet(0, -5, PortablePointerScrollUnit.Points), out handled));
            Assert.True(handled);
            fixture.Layout();
            fixture.AssertAncestorUnchanged();
            Assert.Equal(1, stale); Assert.Equal(1, current);
        });
    }

    [PortableScrollFact]
    public void NativeScrollComboBoxCancellationRetiresAcceptedChildWorkWithoutClaimingNewMotion()
    {
        Run(() =>
        {
            using var fixture = new ComboBoxScrollFixture();
            Assert.True(Route(fixture.Child, RoutedPacket(50, -5, phase: 1), out bool handled));
            Assert.True(handled);
            // Bypass the child ScrollViewer's cancellation handler so its own
            // Handled assignment cannot mask an unwanted ComboBox claim.
            fixture.Child.Host.HitTestOverride = (_, _) => fixture.Child.Root;
            int cancelled = 0;
            fixture.ComboBox.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                Assert.True(((PortableScrollEventArgs)value).IsCancellation);
                Assert.False(value.Handled);
                ++cancelled;
            }), true);

            Assert.True(Route(fixture.Child, RoutedPacket(50, -500, phase: 16), out _));
            fixture.Layout();
            Assert.Equal(40, fixture.Child.Info.VerticalOffset);
            fixture.AssertAncestorUnchanged();
            Assert.Equal(1, fixture.ComboBox.SelectedIndex);
            Assert.Equal(1, cancelled);
        });
    }

    private sealed class ComboBoxScrollFixture : IDisposable
    {
        internal ScrollFixture Owner { get; } = new(PortableScrollAxes.Pixels);
        internal ScrollFixture Child { get; }
        internal (ScrollViewer Viewer, MeasuredScrollInfo Info) Ancestor { get; }
        internal ComboBox ComboBox { get; }

        internal ComboBoxScrollFixture()
        {
            Ancestor = Owner.AddAncestor();
            Owner.Root.Children.Clear();
            ComboBox = AddOpenPeer();
            Child = new ScrollFixture(PortableScrollAxes.Pixels,
                root: new ScrollRouteRoot { InputParent = ComboBox });
            Child.Host.HitTestOverride = (_, _) => Child.Viewer;
        }

        internal ComboBox AddOpenPeer()
        {
            var comboBox = new ComboBox
            {
                Width = 200, Height = 100,
                Template = new ControlTemplate(typeof(ComboBox)) { VisualTree = new FrameworkElementFactory(typeof(Border)) }
            };
            comboBox.Items.Add("first"); comboBox.Items.Add("selected"); comboBox.Items.Add("last");
            comboBox.SelectedIndex = 1;
            Owner.Root.Children.Add(comboBox);
            Ancestor.Viewer.UpdateLayout();
            // Exercise the real source load/open path, without native popup
            // creation or private IsDropDownOpen/IsLoaded field substitutions.
            BroadcastEventHelper.BroadcastLoadedSynchronously(Ancestor.Viewer, false);
            Assert.True(comboBox.IsLoaded);
            comboBox.IsDropDownOpen = true;
            Assert.True(comboBox.IsDropDownOpen);
            return comboBox;
        }

        internal void DisableChildAxes()
        {
            Child.Viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Child.Viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Child.Viewer.UpdateLayout();
        }

        internal void Layout()
        {
            Child.Viewer.UpdateLayout();
            Ancestor.Viewer.UpdateLayout();
        }

        internal void AssertAncestorUnchanged()
        {
            Assert.Equal(40, Ancestor.Info.HorizontalOffset);
            Assert.Equal(40, Ancestor.Info.VerticalOffset);
            Assert.Empty(Ancestor.Info.Lines);
        }

        public void Dispose()
        {
            ComboBox.IsDropDownOpen = false;
            Keyboard.ClearFocus();
            Mouse.Capture(null);
            Child.Dispose();
            Owner.Dispose();
        }
    }
}
