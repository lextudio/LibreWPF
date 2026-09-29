// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public sealed class PortableScrollSourceTests
{
    private sealed class PortableScrollFactAttribute : FactAttribute
    {
        public PortableScrollFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable source media selected before input construction.";
        }
    }

    [PortableScrollFact]
    public void NativeScrollPointsRetainFractionsAndActualVisualVectorMapping()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            PublishVisualScale(fixture);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            Assert.True(session.TryQueue(Packet(-5.5, -6.75, PortablePointerScrollUnit.Points)));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(42.75, fixture.Info.HorizontalOffset);
            Assert.Equal(41.6875, fixture.Info.VerticalOffset);
            Assert.Empty(fixture.Info.Lines);
        });
    }

    [PortableScrollFact]
    public void NativeScrollLogicalPointsUseSourceViewportMetricsAndCarryFractions()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.VerticalItems);
            fixture.Info.ViewportHeight = 4;
            Assert.Equal(100, fixture.Viewer.ActualHeight);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            // The existing WPF panning policy is 100 / (4 + 1) points per item.
            for (int i = 0; i < 8; ++i)
                Assert.True(session.TryQueue(Packet(0, -5, PortablePointerScrollUnit.Points)));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(40, fixture.Info.HorizontalOffset);
            Assert.Empty(fixture.Info.Lines);
        });
    }

    [PortableScrollFact]
    public void NativeScrollLinesUseProviderCommandsWithoutScalingAndKeepQueueOrder()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            PublishVisualScale(fixture);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            Assert.True(session.TryQueue(Packet(-0.4, -0.4, PortablePointerScrollUnit.Lines)));
            Assert.True(session.TryQueue(Packet(-1.6, -2.6, PortablePointerScrollUnit.Lines)));
            fixture.Viewer.ScrollToVerticalOffset(300); // Must follow every queued line.
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { "right", "right", "down", "down", "down" }, fixture.Info.Lines);
            Assert.Equal(55, fixture.Info.HorizontalOffset); // Provider's own 7.5 DIP line policy.
            Assert.Equal(300, fixture.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollQueuedCommandsRejectCancelledRetargetedAndRetiredSources()
    {
        Run(() =>
        {
            foreach (int retirement in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
                Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
                var command = new PortableScrollCommand(session, PortablePointerScrollUnit.Points, new Vector(5, 6));
                switch (retirement)
                {
                    case 0: session.Cancel(); break;
                    case 1: fixture.Viewer.ScrollInfo = new MeasuredScrollInfo { ScrollOwner = fixture.Viewer }; break;
                    case 2: fixture.Host.RootVisual = new Grid(); break;
                    case 3:
                        Assert.True(PortableWindowActivationService.TryProcessNativePointerInput(
                            (PresentationSource)fixture.Host,
                            new(PortablePointerEventKind.Cancel, 0, 0, 1, -1, 0, 0), 0, out _));
                        break;
                    case 4: fixture.Info.CanVerticallyScroll = false; break;
                    case 5: fixture.Info.Axes = PortableScrollAxes.VerticalItems; break;
                    case 6: fixture.Info.ScrollOwner = new ScrollViewer(); break;
                    case 7: fixture.Viewer.IsEnabled = false; break;
                    case 8: fixture.Viewer.Visibility = Visibility.Hidden; break;
                }
                Assert.True(command.Advance());
                Assert.False(session.IsCurrent);
                Assert.Equal(40, fixture.Info.HorizontalOffset);
                Assert.Equal(40, fixture.Info.VerticalOffset);
            }
        });
    }

    [PortableScrollFact]
    public void NativeScrollQueueCapacityRejectsInsteadOfDroppingAcceptedPackets()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            for (int i = 0; i < 31; ++i)
                Assert.True(session.TryQueue(Packet(0, -1, PortablePointerScrollUnit.Points)));
            Assert.False(session.TryQueue(Packet(0, -20, PortablePointerScrollUnit.Points)));
            Assert.Throws<InvalidOperationException>(() => fixture.Viewer.LineDown());
            fixture.Viewer.UpdateLayout();
            Assert.Equal(71, fixture.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollRejectsUnknownUnitsInvalidMetricsAndDisabledAxesBeforeQueue()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.VerticalItems);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            Assert.False(session.TryQueue(Packet(0, double.MaxValue, PortablePointerScrollUnit.Lines)));
            fixture.Info.ViewportHeight = double.NaN;
            Assert.False(session.TryQueue(Packet(-1, -1, PortablePointerScrollUnit.Points)));
            fixture.Info.ViewportHeight = 4;
            fixture.Info.ExtentWidth = double.NaN;
            Assert.False(session.TryQueue(Packet(-1, 0, PortablePointerScrollUnit.Points)));
            fixture.Info.ExtentWidth = 1000;
            fixture.Info.CanHorizontallyScroll = false;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out session));
            Assert.False(session.TryQueue(Packet(-1, -1, PortablePointerScrollUnit.Points)));
            Assert.False(session.TryQueue(Packet(-1, -1, PortablePointerScrollUnit.Lines)));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.HorizontalOffset); Assert.Equal(40, fixture.Info.VerticalOffset);
            fixture.Info.Axes = (PortableScrollAxes)4;
            Assert.False(PortableScrollSession.TryCreate(fixture.Viewer, out _));
            fixture.Viewer.ScrollInfo = new RecordingScrollInfo { ScrollOwner = fixture.Viewer };
            Assert.False(PortableScrollSession.TryCreate(fixture.Viewer, out _));
        });
    }

    [PortableScrollFact]
    public void NativeScrollSourceProvidersDeclareTheirActualOffsetUnits()
    {
        Run(() =>
        {
            Assert.Equal(PortableScrollAxes.Pixels, ((IPortableScrollInfo)new ScrollContentPresenter()).ScrollAxes);
            var stack = new StackPanel();
            Assert.Equal(PortableScrollAxes.VerticalItems, ((IPortableScrollInfo)stack).ScrollAxes);
            stack.Orientation = Orientation.Horizontal;
            Assert.Equal(PortableScrollAxes.HorizontalItems, ((IPortableScrollInfo)stack).ScrollAxes);
            var virtualStack = new VirtualizingStackPanel { IsPixelBased = false };
            Assert.Equal(PortableScrollAxes.VerticalItems, ((IPortableScrollInfo)virtualStack).ScrollAxes);
            virtualStack.IsPixelBased = true;
            Assert.Equal(PortableScrollAxes.Pixels, ((IPortableScrollInfo)virtualStack).ScrollAxes);
        });
    }

    [PortableScrollFact]
    public void NativeScrollRevalidatesBothPointAxesBeforeWritingAndRetiresAfterCallbacks()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var invalid = new PortableScrollCommand(session, PortablePointerScrollUnit.Points, new Vector(5, 6));
            fixture.Info.ViewportHeight = double.NaN;
            Assert.Throws<InvalidOperationException>(() => invalid.Advance());
            Assert.Equal(40, fixture.Info.HorizontalOffset); Assert.Equal(40, fixture.Info.VerticalOffset);
            fixture.Info.ViewportHeight = 100;
            fixture.Info.AfterHorizontalOffsetSet = session.Cancel;
            Assert.True(session.TryQueue(Packet(-5, -6, PortablePointerScrollUnit.Points)));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(45, fixture.Info.HorizontalOffset); Assert.Equal(40, fixture.Info.VerticalOffset);
            Assert.False(session.IsCurrent);
        });
    }

    [PortableScrollFact]
    public void NativeScrollWaitsForPublishedOffsetsBetweenCommandsAndLayoutReentry()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels, deferredOffsets: true);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            // Application-owned providers can synchronously enter layout from an
            // offset callback. That must not drain the remaining command early.
            fixture.Info.AfterHorizontalOffsetSet = fixture.Viewer.UpdateLayout;
            Assert.True(session.TryQueue(Packet(-3, -2, PortablePointerScrollUnit.Lines)));
            Assert.True(session.TryQueue(Packet(-0.25, -0.5, PortablePointerScrollUnit.Points)));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { "right", "right", "right", "down", "down" }, fixture.Info.Lines);
            Assert.Equal(62.75, fixture.Info.HorizontalOffset);
            Assert.Equal(63, fixture.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollRoutingRetargetsNormalInputAndPreservesRoutedMetadata()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var peer = fixture.AddPeer();
            int routed = 0, legacy = 0;
            fixture.Root.MouseWheel += (_, _) => ++legacy;
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                var args = Assert.IsType<PortableScrollEventArgs>(value);
                Assert.Equal(3125, args.Timestamp);
                Assert.Equal(ModifierKeys.Control, args.Modifiers);
                Assert.Equal(ModifierKeys.Control, Keyboard.Modifiers);
                Assert.Equal(PortablePointerModifiers.Super, args.NativeInput.Modifiers);
                Assert.Equal(PortablePointerScrollProtocol.AppKit, args.NativeInput.ScrollProtocol);
                Assert.True(args.Handled);
                ++routed;
            }), true);
            Assert.True(Route(fixture, RoutedPacket(50, -1.25, phase: 1), out bool firstHandled));
            Assert.True(firstHandled);
            Assert.True(Route(fixture, RoutedPacket(250, -2.5, phase: 4), out bool secondHandled));
            Assert.True(secondHandled);
            Assert.True(Route(fixture, RoutedPacket(250, 0, phase: 8), out _));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(41.25, fixture.Info.VerticalOffset);
            Assert.Equal(42.5, peer.Info.VerticalOffset);
            Assert.Equal(3, routed); Assert.Equal(0, legacy);
            Assert.Equal(ModifierKeys.None, Keyboard.Modifiers);
        });
    }

    [PortableScrollFact]
    public void NativeScrollMomentumPinsTargetAndCannotRetargetAfterEnding()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var peer = fixture.AddPeer();
            Assert.True(Route(fixture, RoutedPacket(50, -2, momentum: 1), out _));
            Assert.True(Route(fixture, RoutedPacket(250, -3, momentum: 4), out _));
            Assert.True(Route(fixture, RoutedPacket(250, -1, momentum: 8), out _));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(46, fixture.Info.VerticalOffset); Assert.Equal(40, peer.Info.VerticalOffset);
            Assert.True(Route(fixture, RoutedPacket(250, -9, momentum: 4), out bool retired));
            Assert.True(retired);
            Assert.True(Route(fixture, RoutedPacket(250, -4, phase: 1), out _));
            Assert.True(Route(fixture, RoutedPacket(50, -7, momentum: 1), out _));
            Assert.True(Route(fixture, RoutedPacket(250, -2, phase: 1), out _));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(46, fixture.Info.VerticalOffset); Assert.Equal(46, peer.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollLogicalRemaindersSurviveTheNormalToMomentumHandoff()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.VerticalItems);
            fixture.Info.ViewportHeight = 4; // 20 physical points per source item.
            Assert.True(Route(fixture, RoutedPacket(150, -5, phase: 1), out _));
            Assert.True(Route(fixture, RoutedPacket(150, -5, phase: 4), out _));
            Assert.True(Route(fixture, RoutedPacket(150, 0, phase: 8), out _));
            Assert.True(Route(fixture, RoutedPacket(150, -5, momentum: 1), out _));
            Assert.True(Route(fixture, RoutedPacket(150, 0, momentum: 8), out _));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(41, fixture.Info.VerticalOffset);

            Assert.True(Route(fixture, RoutedPacket(150, -20, phase: 1), out _));
            Assert.True(Route(fixture, RoutedPacket(150, 0, phase: 8), out _));
            Assert.True(Route(fixture, RoutedPacket(150, -20, momentum: 1), out _));
            Assert.True(Route(fixture, RoutedPacket(150, 0, phase: 1), out _));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset); // Touch motion survives cancelled momentum.
        });
    }

    [PortableScrollFact]
    public void NativeScrollCancellationRetiresAllQueuedTargetsAndSourceMomentum()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var peer = fixture.AddPeer();
            Assert.True(Route(fixture, RoutedPacket(50, -3, phase: 1), out _));
            Assert.True(Route(fixture, RoutedPacket(250, -4, phase: 4), out _));
            Assert.True(Route(fixture, RoutedPacket(250, 0, phase: 16), out _));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.VerticalOffset); Assert.Equal(40, peer.Info.VerticalOffset);
            Assert.True(Route(fixture, RoutedPacket(50, -5, momentum: 1), out _));
            Assert.True(PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)fixture.Host,
                new(PortablePointerEventKind.Cancel, 50, 75, 4, -1, 0, 0), 0, out _));
            Assert.True(Route(fixture, RoutedPacket(250, -5, momentum: 4), out _));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.VerticalOffset); Assert.Equal(40, peer.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollInvalidPhasePacketsCannotMutateMomentumOwnership()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var peer = fixture.AddPeer();
            Assert.True(Route(fixture, RoutedPacket(50, -2, momentum: 1), out _));
            var invalid = new[]
            {
                RoutedPacket(250, -9, phase: 64), RoutedPacket(250, -9, phase: 3),
                RoutedPacket(250, -9, phase: 1, momentum: 1), RoutedPacket(250, -9, momentum: 32),
                new PortablePointerInput(PortablePointerEventKind.Scroll, 250, 75, 1, -1, 0, 0, 0, -9, scrollPhase: 1)
            };
            foreach (var input in invalid)
            {
                Assert.False(Route(fixture, input, out bool handled)); Assert.False(handled);
            }
            Assert.True(Route(fixture, RoutedPacket(250, -3, momentum: 4), out _));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(45, fixture.Info.VerticalOffset); Assert.Equal(40, peer.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollNestedPreviewRetiresTheOlderDispatchWithoutLegacyWheel()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var peer = fixture.AddPeer();
            bool nested = false;
            int legacy = 0;
            fixture.Root.MouseWheel += (_, _) => ++legacy;
            fixture.Root.AddHandler(PortableScroll.PreviewScrollEvent, new RoutedEventHandler((_, _) =>
            {
                if (nested) return;
                nested = true;
                Assert.True(Route(fixture, RoutedPacket(250, -3, phase: 1), out bool handled));
                Assert.True(handled);
            }));
            Assert.True(Route(fixture, RoutedPacket(50, -9, phase: 1), out _));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.VerticalOffset); Assert.Equal(43, peer.Info.VerticalOffset);
            Assert.Equal(0, legacy);
        });
    }

    [PortableScrollFact]
    public void NativeScrollPreviewHandlingAndFailurePreserveQueueOwnership()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            RoutedEventHandler handle = (_, input) => input.Handled = true;
            fixture.Root.AddHandler(PortableScroll.PreviewScrollEvent, handle);
            Assert.True(Route(fixture, RoutedPacket(150, -4, phase: 1), out bool handled));
            Assert.True(handled);
            fixture.Root.RemoveHandler(PortableScroll.PreviewScrollEvent, handle);
            Assert.True(Route(fixture, RoutedPacket(150, -2, phase: 4), out _));
            var failure = new InvalidOperationException("Scroll preview failed.");
            RoutedEventHandler fail = (_, _) => throw failure;
            fixture.Root.AddHandler(PortableScroll.PreviewScrollEvent, fail);
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                Route(fixture, RoutedPacket(150, -3, phase: 4), out _)));
            fixture.Root.RemoveHandler(PortableScroll.PreviewScrollEvent, fail);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.VerticalOffset);
            Assert.Equal(ModifierKeys.None, Keyboard.Modifiers);
            Assert.True(Route(fixture, RoutedPacket(150, -1, phase: 1), out _));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(41, fixture.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollSourceHitOverridePreservesCacheMissAndRetiresReentrantResults()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var peer = fixture.AddPeer();
            fixture.Host.HitTestOverride = (_, _) => fixture.Host; // Authoritative native miss.
            Assert.True(Route(fixture, RoutedPacket(50, -5, phase: 1), out bool handled));
            Assert.False(handled);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.VerticalOffset); Assert.Equal(40, peer.Info.VerticalOffset);
            fixture.Host.HitTestOverride = (_, _) => peer.Viewer;
            Assert.True(Route(fixture, RoutedPacket(50, -3, phase: 4), out handled));
            Assert.True(handled);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.VerticalOffset); Assert.Equal(43, peer.Info.VerticalOffset);
            fixture.Host.HitTestOverride = (_, _) =>
            {
                fixture.Host.RootVisual = new Grid();
                return peer.Viewer;
            };
            Assert.True(Route(fixture, RoutedPacket(50, -7, phase: 4), out _));
            peer.Viewer.UpdateLayout();
            Assert.Equal(43, peer.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollModalAdmissionUsesOwningSourceRoot()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            fixture.Host.RootVisual = null;
            var window = new Window { Content = fixture.Root, Width = 400, Height = 200 };
            PortableWindowActivationService.Register(activate: value => value, getHandle: _ => fixture.Host.Handle);
            try
            {
                window.Show(); fixture.Host.RootVisual = window; fixture.Host.SetClientSize(400, 200);
                Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
                using (PortableModalInputScope.Enter(window))
                {
                    Assert.True(session.IsCurrent);
                    using (PortableModalInputScope.Enter(new object())) Assert.False(session.IsCurrent);
                    Assert.True(session.IsCurrent);
                }
            }
            finally
            {
                fixture.Host.RootVisual = null;
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    private static PortablePointerInput RoutedPacket(double x, double delta, uint phase = 0, uint momentum = 0) =>
        new(PortablePointerEventKind.Scroll, PortablePointerScrollProtocol.AppKit, x, 75, 3.125, -1, 0,
            PortablePointerModifiers.Super, 0, delta, PortablePointerScrollUnit.Points, phase, momentum);

    private static bool Route(ScrollFixture fixture, PortablePointerInput input, out bool handled) =>
        PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)fixture.Host,
            input, PortableInputModifiers.Control, out handled);

    private static PortablePointerInput Packet(double x, double y, PortablePointerScrollUnit unit) =>
        new(PortablePointerEventKind.Scroll, 10, 10, 1, -1, 0, 0, x, y, unit);

    private static void PublishVisualScale(ScrollFixture fixture)
    {
        fixture.Viewer.RenderTransform = new ScaleTransform(2, 4);
        // RenderTransform assignment invalidates arrange. Native input uses the
        // published visual frame, so arrange before delivering the scaled packet.
        fixture.Viewer.UpdateLayout();
        GeneralTransform toRoot = fixture.Viewer.TransformToAncestor((Visual)fixture.Host.RootVisual);
        Assert.Equal(new Vector(2, 4), toRoot.Transform(new Point(1, 1)) - toRoot.Transform(default));
    }

    private sealed class ScrollFixture : IDisposable
    {
        internal IPortablePresentationSourceHost Host { get; } = PortablePresentationSourceHost.Create();
        internal ScrollViewer Viewer { get; }
        internal MeasuredScrollInfo Info { get; }
        internal Grid Root { get; } = new();

        internal ScrollFixture(PortableScrollAxes axes, bool deferredOffsets = false)
        {
            var viewer = CreateViewer();
            Viewer = viewer;
            Root.Children.Add(Viewer);
            Host.RootVisual = Root; Host.SetClientSize(400, 200);
            Info = new MeasuredScrollInfo { ScrollOwner = Viewer, Axes = axes, DeferredOffsets = deferredOffsets };
            viewer.Info = Info;
            Viewer.ScrollInfo = Info;
            Viewer.UpdateLayout();
        }

        internal (ScrollViewer Viewer, MeasuredScrollInfo Info) AddPeer()
        {
            Root.ColumnDefinitions.Add(new ColumnDefinition()); Root.ColumnDefinitions.Add(new ColumnDefinition());
            var viewer = CreateViewer(); Grid.SetColumn(viewer, 1); Root.Children.Add(viewer);
            var info = new MeasuredScrollInfo { ScrollOwner = viewer };
            viewer.Info = info; viewer.ScrollInfo = info;
            Viewer.UpdateLayout();
            return (viewer, info);
        }

        private static LayoutScrollViewer CreateViewer() => new()
        {
            Width = 200, Height = 100, CanContentScroll = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Template = new ControlTemplate(typeof(ScrollViewer)) { VisualTree = new FrameworkElementFactory(typeof(Border)) }
        };

        public void Dispose() => Host.Dispose();
    }

    private class RecordingScrollInfo : IScrollInfo
    {
        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public double ExtentWidth { get; set; } = 1000;
        public double ExtentHeight { get; set; } = 1000;
        public double ViewportWidth { get; set; } = 200;
        public double ViewportHeight { get; set; } = 100;
        public double HorizontalOffset { get; private set; } = 40;
        public double VerticalOffset { get; private set; } = 40;
        public ScrollViewer ScrollOwner { get; set; } = null!;
        internal List<string> Lines { get; } = new();
        internal bool DeferredOffsets { get; set; }
        internal Action? AfterHorizontalOffsetSet { get; set; }
        private double? _pendingHorizontal, _pendingVertical;
        public void SetHorizontalOffset(double value)
        {
            value = Math.Clamp(value, 0, ExtentWidth - ViewportWidth);
            if (DeferredOffsets) _pendingHorizontal = value; else HorizontalOffset = value;
            AfterHorizontalOffsetSet?.Invoke();
        }
        public void SetVerticalOffset(double value)
        {
            value = Math.Clamp(value, 0, ExtentHeight - ViewportHeight);
            if (DeferredOffsets) _pendingVertical = value; else VerticalOffset = value;
        }
        internal void PublishOffsets()
        {
            if (_pendingHorizontal is double horizontal) HorizontalOffset = horizontal;
            if (_pendingVertical is double vertical) VerticalOffset = vertical;
            _pendingHorizontal = _pendingVertical = null;
        }
        public void LineLeft() { Lines.Add("left"); SetHorizontalOffset(HorizontalOffset - 7.5); }
        public void LineRight() { Lines.Add("right"); SetHorizontalOffset(HorizontalOffset + 7.5); }
        public void LineUp() { Lines.Add("up"); SetVerticalOffset(VerticalOffset - 11.25); }
        public void LineDown() { Lines.Add("down"); SetVerticalOffset(VerticalOffset + 11.25); }
        public void PageUp() => throw new NotSupportedException();
        public void PageDown() => throw new NotSupportedException();
        public void PageLeft() => throw new NotSupportedException();
        public void PageRight() => throw new NotSupportedException();
        public void MouseWheelUp() => throw new InvalidOperationException("Native lines must not become wheel notches.");
        public void MouseWheelDown() => throw new InvalidOperationException("Native lines must not become wheel notches.");
        public void MouseWheelLeft() => throw new InvalidOperationException("Native lines must not become wheel notches.");
        public void MouseWheelRight() => throw new InvalidOperationException("Native lines must not become wheel notches.");
        public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
    }

    private sealed class MeasuredScrollInfo : RecordingScrollInfo, IPortableScrollInfo
    {
        internal PortableScrollAxes Axes { get; set; }
        public PortableScrollAxes ScrollAxes => Axes;
    }

    private sealed class LayoutScrollViewer : ScrollViewer
    {
        internal RecordingScrollInfo? Info { get; set; }
        protected override HitTestResult HitTestCore(PointHitTestParameters parameters) =>
            new PointHitTestResult(this, parameters.HitPoint);
        protected override Size ArrangeOverride(Size finalSize)
        {
            Info?.PublishOffsets();
            return base.ArrangeOverride(finalSize);
        }
    }

    private static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } })
        { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Native scroll source fixture timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
