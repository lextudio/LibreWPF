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
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var linesOnly));
            Assert.False(linesOnly.TryQueue(Packet(-1, -1, PortablePointerScrollUnit.Points)));
        });
    }

    [PortableScrollFact]
    public void NativeScrollSourceProvidersDeclareTheirActualOffsetUnits()
    {
        Run(() =>
        {
            Assert.True(typeof(IPortableScrollInfo).IsPublic);
            Assert.True(typeof(PortableScrollAxes).IsPublic);
            Assert.True(typeof(IScrollInfo).IsAssignableFrom(typeof(IPortableScrollInfo)));
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
    public void NativeScrollPointOverflowRetainsFractionsAndCannotReplayCompletedCommands()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels, deferredOffsets: true);
            fixture.Info.ExtentWidth = 242.5;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var command = new PortableScrollCommand(session, PortablePointerScrollUnit.Points, new Vector(5.5, -42.25));
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(command));
            Assert.Equal(default, command.Unconsumed);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(42.5, fixture.Info.HorizontalOffset); Assert.Equal(0, fixture.Info.VerticalOffset);
            Assert.Equal(new Vector(3, -2.25), command.Unconsumed);
            Assert.True(command.Advance());
            fixture.Viewer.UpdateLayout();
            Assert.Equal(42.5, fixture.Info.HorizontalOffset); Assert.Equal(0, fixture.Info.VerticalOffset);
            Assert.Equal(new Vector(3, -2.25), command.Unconsumed);
        });
    }

    [PortableScrollFact]
    public void NativeScrollLogicalOverflowTransfersTheCarriedFractionWithoutBoundaryDebt()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.VerticalItems);
            fixture.Info.ViewportHeight = 4; fixture.Info.ExtentHeight = 46;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(
                new PortableScrollCommand(session, PortablePointerScrollUnit.Points, new Vector(0, 0.25))));
            var command = new PortableScrollCommand(session, PortablePointerScrollUnit.Points, new Vector(0, 2));
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(command));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(new Vector(0, 0.25), command.Unconsumed); // Provider item units, not guessed source points.
            foreach (double delta in new[] { -0.25, -0.75 })
                Assert.True(fixture.Viewer.TryEnqueuePortableScroll(
                    new PortableScrollCommand(session, PortablePointerScrollUnit.Points, new Vector(0, delta))));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(41, fixture.Info.VerticalOffset);
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(
                new PortableScrollCommand(session, PortablePointerScrollUnit.Points, new Vector(0, 0.25))));
            fixture.Viewer.ScrollToVerticalOffset(42);
            var reversed = new PortableScrollCommand(session, PortablePointerScrollUnit.Points, new Vector(0, -1));
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(reversed));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(41, fixture.Info.VerticalOffset);
            Assert.Equal(new Vector(0, 0.25), reversed.Unconsumed);
        });
    }

    [PortableScrollFact]
    public void NativeScrollLineOverflowWaitsForPublishedOffsetsAndKeepsFollowingCommandsOrdered()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels, deferredOffsets: true);
            fixture.Info.ExtentWidth = 255; fixture.Info.ExtentHeight = 151.25;
            fixture.Info.AfterHorizontalOffsetSet = fixture.Viewer.UpdateLayout;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var command = new PortableScrollCommand(session, PortablePointerScrollUnit.Lines, new Vector(4.5, 2.25));
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(command));
            fixture.Viewer.ScrollToVerticalOffset(50);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { "right", "right", "down" }, fixture.Info.Lines);
            Assert.Equal(55, fixture.Info.HorizontalOffset); Assert.Equal(50, fixture.Info.VerticalOffset);
            Assert.Equal(new Vector(2.5, 1.25), command.Unconsumed);
        });
    }

    [PortableScrollFact]
    public void NativeScrollFractionalLinesAtTheBoundaryDoNotCreateReverseScrollDebt()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            fixture.Info.SetHorizontalOffset(800); fixture.Info.SetVerticalOffset(0);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var command = new PortableScrollCommand(session, PortablePointerScrollUnit.Lines, new Vector(0.25, -0.5));
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(command));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new Vector(0.25, -0.5), command.Unconsumed);
            Assert.Empty(fixture.Info.Lines);
            foreach (Vector delta in new[] { new Vector(-0.25, 0.5), new Vector(-0.75, 0.5) })
                Assert.True(fixture.Viewer.TryEnqueuePortableScroll(
                    new PortableScrollCommand(session, PortablePointerScrollUnit.Lines, delta)));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { "left", "down" }, fixture.Info.Lines);
            Assert.Equal(792.5, fixture.Info.HorizontalOffset); Assert.Equal(11.25, fixture.Info.VerticalOffset);
            Assert.True(session.TryQueue(Packet(-0.25, 0, PortablePointerScrollUnit.Lines)));
            fixture.Viewer.ScrollToHorizontalOffset(800);
            var reversed = new PortableScrollCommand(session, PortablePointerScrollUnit.Lines, new Vector(-1, 0));
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(reversed));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { "left", "down", "left" }, fixture.Info.Lines);
            Assert.Equal(792.5, fixture.Info.HorizontalOffset);
            Assert.Equal(new Vector(0.25, 0), reversed.Unconsumed);
        });
    }

    [PortableScrollFact]
    public void NativeScrollLineOverflowUsesChangedMetricsWithoutInventingPartialLineDistances()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels, deferredOffsets: true);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var changed = new PortableScrollCommand(session, PortablePointerScrollUnit.Lines, new Vector(3.5, 0));
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(changed));
            fixture.Info.AfterHorizontalOffsetSet = () => fixture.Info.ExtentWidth = 247.5;
            fixture.Viewer.UpdateLayout();
            Assert.Equal(47.5, fixture.Info.HorizontalOffset);
            Assert.Equal(new Vector(2.5, 0), changed.Unconsumed);
            Assert.Equal(new[] { "right" }, fixture.Info.Lines);
            fixture.Info.AfterHorizontalOffsetSet = null;
            fixture.Info.ExtentWidth = 250;
            var partial = new PortableScrollCommand(session, PortablePointerScrollUnit.Lines, new Vector(2.5, 0));
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(partial));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(50, fixture.Info.HorizontalOffset);
            Assert.Equal(new Vector(1.5, 0), partial.Unconsumed); // One real LineRight consumed; its distance is provider-owned.
            Assert.Equal(new[] { "right", "right" }, fixture.Info.Lines);
        });
    }

    [PortableScrollFact]
    public void NativeScrollInvalidLineMetricsCannotMutatePreviouslyCarriedFractions()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            Assert.True(session.TryQueue(Packet(-0.25, 0, PortablePointerScrollUnit.Lines)));
            fixture.Viewer.UpdateLayout();
            fixture.Info.ViewportHeight = double.NaN;
            Assert.False(session.TryQueue(Packet(-3, 0, PortablePointerScrollUnit.Lines)));
            var invalid = new PortableScrollCommand(session, PortablePointerScrollUnit.Lines, new Vector(3, 0));
            Assert.Throws<InvalidOperationException>(() => invalid.Advance());
            Assert.Empty(fixture.Info.Lines); Assert.Equal(default, invalid.Unconsumed);
            fixture.Info.ViewportHeight = 100;
            Assert.True(session.TryQueue(Packet(-0.75, 0, PortablePointerScrollUnit.Lines)));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { "right" }, fixture.Info.Lines);
        });
    }

    [PortableScrollFact]
    public void NativeScrollCancelledCommandsCannotPublishOverflowOrWriteTheSecondAxis()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            fixture.Info.ExtentWidth = 245; fixture.Info.ExtentHeight = 145;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var lifetime = new PortableScrollLifetime();
            var command = new PortableScrollCommand(session, PortablePointerScrollUnit.Points, new Vector(20, 20), lifetime);
            fixture.Info.AfterHorizontalOffsetSet = lifetime.Cancel;
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(command));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(45, fixture.Info.HorizontalOffset); Assert.Equal(40, fixture.Info.VerticalOffset);
            Assert.Equal(default, command.Unconsumed);
            Assert.True(command.Advance());
            Assert.Equal(40, fixture.Info.VerticalOffset);
            fixture.Info.AfterHorizontalOffsetSet = null;
            var lineLifetime = new PortableScrollLifetime();
            var lines = new PortableScrollCommand(session, PortablePointerScrollUnit.Lines, new Vector(3, 2), lineLifetime);
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(lines));
            fixture.Info.ReadHorizontalOffset = lineLifetime.Cancel;
            fixture.Viewer.UpdateLayout();
            Assert.Empty(fixture.Info.Lines); Assert.Equal(default, lines.Unconsumed);
            Assert.Equal(45, fixture.Info.HorizontalOffset); Assert.Equal(40, fixture.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollShrinkingExtentCorrectionIsNotAdditionalPointInput()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            fixture.Info.ExtentWidth = 220; fixture.Info.ExtentHeight = 120;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var lifetime = new PortableScrollLifetime();
            var command = new PortableScrollCommand(session, PortablePointerScrollUnit.Points, new Vector(5, -1), lifetime);
            Assert.True(fixture.Viewer.TryEnqueuePortableScroll(command));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(20, fixture.Info.HorizontalOffset); Assert.Equal(20, fixture.Info.VerticalOffset);
            Assert.Equal(new Vector(5, 0), command.Unconsumed);
            lifetime.Cancel();
            Assert.Equal(default, command.Unconsumed);
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
                Assert.Same(routed == 0 ? fixture.Viewer : peer.Viewer, args.OriginalSource);
                Assert.Same(args.OriginalSource, args.Source);
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
            // This source-only fixture does not load application theme templates.
            // Connect the content visually before checking source-owned sessions.
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            window.Template = new ControlTemplate(typeof(Window)) { VisualTree = presenter };
            PortableWindowActivationService.Register(activate: value => value, getHandle: _ => fixture.Host.Handle);
            try
            {
                window.Show(); fixture.Host.RootVisual = window; fixture.Host.SetClientSize(400, 200);
                window.UpdateLayout();
                Assert.Same(window, PresentationSource.FromVisual(fixture.Viewer)?.RootVisual);
                Assert.True(fixture.Viewer.IsVisible);
                Assert.Same(fixture.Info, fixture.Viewer.ScrollInfo);
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

    [PortableScrollFact]
    public void NativeScrollNestedOwnersConsumeIndependentLineAxesOnce()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var ancestor = fixture.AddAncestor();
            fixture.Info.CanVerticallyScroll = false;
            ancestor.Info.CanHorizontallyScroll = false;
            fixture.Host.HitTestOverride = (_, _) => fixture.Viewer;
            Vector observed = default;
            int legacy = 0;
            ancestor.Viewer.MouseWheel += (_, _) => ++legacy;
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                var args = Assert.IsType<PortableScrollEventArgs>(value);
                Assert.False(args.Handled);
                Assert.Equal(-2, args.NativeInput.ScrollX); Assert.Equal(-3, args.NativeInput.ScrollY);
                observed = args.RemainingScroll;
            }));
            Assert.True(Route(fixture, Packet(-2, -3, PortablePointerScrollUnit.Lines), out bool handled));
            Assert.True(handled);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(new Vector(0, -3), observed);
            Assert.Equal(new[] { "right", "right" }, fixture.Info.Lines);
            Assert.Equal(new[] { "down", "down", "down" }, ancestor.Info.Lines);
            Assert.Equal(55, fixture.Info.HorizontalOffset); Assert.Equal(40, fixture.Info.VerticalOffset);
            Assert.Equal(40, ancestor.Info.HorizontalOffset); Assert.Equal(73.75, ancestor.Info.VerticalOffset);
            Assert.Equal(0, legacy);
        });
    }

    [PortableScrollFact]
    public void NativeScrollPointRemainderReturnsToSourceAxesAfterRotation()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var ancestor = fixture.AddAncestor();
            fixture.Info.CanVerticallyScroll = false;
            ancestor.Info.CanVerticallyScroll = false;
            fixture.Viewer.RenderTransform = new MatrixTransform(0, 2, -4, 0, 0, 0);
            ancestor.Viewer.UpdateLayout();
            GeneralTransform transform = fixture.Viewer.TransformToAncestor(ancestor.Viewer);
            Assert.Equal(new Vector(0, 2), transform.Transform(new Point(1, 0)) - transform.Transform(default));
            Assert.Equal(new Vector(-4, 0), transform.Transform(new Point(0, 1)) - transform.Transform(default));
            fixture.Host.HitTestOverride = (_, _) => fixture.Viewer;
            Vector remaining = default;
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
                remaining = ((PortableScrollEventArgs)value).RemainingScroll));
            Assert.True(Route(fixture, Packet(-8, -6, PortablePointerScrollUnit.Points), out bool handled));
            Assert.True(handled);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(new Vector(-8, 0), remaining);
            Assert.Equal(43, fixture.Info.HorizontalOffset); Assert.Equal(40, fixture.Info.VerticalOffset);
            Assert.Equal(48, ancestor.Info.HorizontalOffset); Assert.Equal(40, ancestor.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollPartialQueueFailureLeavesTheOriginalRemainderUntouched()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            fixture.Info.CanVerticallyScroll = false;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            Vector original = new(-1, -2);
            for (int i = 0; i < 31; ++i)
            {
                Assert.True(session.TryQueueRemaining(Packet(-1, -2, PortablePointerScrollUnit.Points),
                    null, original, out Vector remaining, out bool full));
                Assert.Equal(new Vector(0, -2), remaining); Assert.False(full);
            }
            Assert.False(session.TryQueueRemaining(Packet(-1, -2, PortablePointerScrollUnit.Points),
                null, original, out Vector rejected, out bool queueFull));
            Assert.True(queueFull); Assert.Equal(original, rejected);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(71, fixture.Info.HorizontalOffset); Assert.Equal(40, fixture.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollUnclaimedAxisRemainsVisibleWithoutReplayingAcceptedMotion()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            fixture.Info.CanVerticallyScroll = false;
            fixture.Host.HitTestOverride = (_, _) => fixture.Viewer;
            Vector remaining = default;
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                var args = (PortableScrollEventArgs)value;
                Assert.False(args.Handled);
                remaining = args.RemainingScroll;
            }));
            Assert.True(Route(fixture, Packet(-1, -2, PortablePointerScrollUnit.Points), out bool handled));
            Assert.True(handled); // Some motion was accepted; do not replay the entire native packet.
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new Vector(0, -2), remaining);
            Assert.Equal(41, fixture.Info.HorizontalOffset); Assert.Equal(40, fixture.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollExistingCrossSourceRouteUsesDesktopAndRootFramesNotRasterDpi()
    {
        Run(() =>
        {
            using var owner = new ScrollFixture(PortableScrollAxes.Pixels);
            var bridge = new ScrollRouteRoot { InputParent = owner.Viewer };
            using var origin = new ScrollFixture(PortableScrollAxes.Pixels, root: bridge);
            // DPI relayout republishes these flags from ScrollBarVisibility.
            // Use the real source policy rather than a transient provider edit.
            origin.Viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            origin.Viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            origin.Host.HitTestOverride = (_, _) => origin.Viewer;
            ((IPortableDesktopGeometryHost)origin.Host).SetDesktopTransform(new(-1920.25, 31.5, 2, 3));
            ((IPortableDesktopGeometryHost)owner.Host).SetDesktopTransform(new(2560.5, -1440.25, 4, 0.75));
            origin.Host.SetDeviceScale(4, 5); owner.Host.SetDeviceScale(7, 9);
            origin.Root.RenderTransform = new ScaleTransform(3, 2);
            owner.Root.RenderTransform = new ScaleTransform(2, 4);
            origin.Root.UpdateLayout(); owner.Root.UpdateLayout();
            Assert.False(origin.Info.CanHorizontallyScroll); Assert.False(origin.Info.CanVerticallyScroll);
            Assert.True(owner.Info.CanHorizontallyScroll); Assert.True(owner.Info.CanVerticallyScroll);
            Assert.Equal(new Matrix(3, 0, 0, 2, 0, 0), VisualTreeHelper.GetTransform(origin.Root).Value);
            Assert.Equal(new Matrix(2, 0, 0, 4, 0, 0), VisualTreeHelper.GetTransform(owner.Root).Value);
            Assert.True(Route(origin, Packet(-8, -6, PortablePointerScrollUnit.Points), out bool handled));
            Assert.True(handled);
            owner.Viewer.UpdateLayout(); origin.Viewer.UpdateLayout();
            Assert.Equal(46, owner.Info.HorizontalOffset); // 8 * 3 * 2 / 4 / 2
            Assert.Equal(52, owner.Info.VerticalOffset); // 6 * 2 * 3 / .75 / 4
            // Translation cannot alter vector magnitude, even when subtracting
            // mapped endpoint positions would lose whole units of motion.
            ((IPortableDesktopGeometryHost)origin.Host).SetDesktopTransform(new(1e16, -1e16, 2, 3));
            ((IPortableDesktopGeometryHost)owner.Host).SetDesktopTransform(new(-1e16, 1e16, 4, 0.75));
            Assert.True(Route(origin, Packet(-8, -6, PortablePointerScrollUnit.Points), out handled));
            Assert.True(handled);
            owner.Viewer.UpdateLayout();
            Assert.Equal(52, owner.Info.HorizontalOffset); Assert.Equal(64, owner.Info.VerticalOffset);
            Assert.Equal(40, origin.Info.HorizontalOffset); Assert.Equal(40, origin.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollCrossSourceLinesRemainUnscaledAndRemaindersKeepOriginalFrame()
    {
        Run(() =>
        {
            using var owner = new ScrollFixture(PortableScrollAxes.Pixels);
            var bridge = new ScrollRouteRoot { InputParent = owner.Viewer };
            using var origin = new ScrollFixture(PortableScrollAxes.Pixels, root: bridge);
            origin.Info.CanHorizontallyScroll = origin.Info.CanVerticallyScroll = false;
            owner.Info.CanVerticallyScroll = false;
            origin.Host.HitTestOverride = (_, _) => origin.Viewer;
            ((IPortableDesktopGeometryHost)origin.Host).SetDesktopTransform(new(-1234.5, 98.75, 2, 3));
            ((IPortableDesktopGeometryHost)owner.Host).SetDesktopTransform(new(987.25, -12.5, 4, 6));
            Vector observed = default;
            PortablePointerInput packet = Packet(-8, -6, PortablePointerScrollUnit.Points);
            owner.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                var args = (PortableScrollEventArgs)value;
                Assert.Same(packet, args.NativeInput);
                Assert.False(args.Handled);
                observed = args.RemainingScroll;
            }));
            Assert.True(Route(origin, packet, out bool handled)); Assert.True(handled);
            owner.Viewer.UpdateLayout();
            Assert.Equal(new Vector(0, -6), observed);
            Assert.Equal(44, owner.Info.HorizontalOffset); Assert.Equal(40, owner.Info.VerticalOffset);
            packet = Packet(-2, -3, PortablePointerScrollUnit.Lines);
            Assert.True(Route(origin, packet, out handled)); Assert.True(handled);
            owner.Viewer.UpdateLayout();
            Assert.Equal(new Vector(0, -3), observed);
            Assert.Equal(new[] { "right", "right" }, owner.Info.Lines);
            Assert.Equal(59, owner.Info.HorizontalOffset); Assert.Equal(40, owner.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollCrossSourceQueueRetainsOriginRetirementAfterGestureEnd()
    {
        Run(() =>
        {
            foreach (int retirement in new[] { 0, 1, 2 })
            {
                using var owner = new ScrollFixture(PortableScrollAxes.Pixels);
                var bridge = new ScrollRouteRoot { InputParent = owner.Viewer };
                using var origin = new ScrollFixture(PortableScrollAxes.Pixels, root: bridge);
                origin.Info.CanHorizontallyScroll = origin.Info.CanVerticallyScroll = false;
                origin.Host.HitTestOverride = (_, _) => origin.Viewer;
                Assert.True(Route(origin, RoutedPacket(150, -5, phase: 1), out bool handled)); Assert.True(handled);
                Assert.True(Route(origin, RoutedPacket(150, 0, phase: 8), out _));
                switch (retirement)
                {
                    case 0:
                        Assert.True(PortableWindowActivationService.TryProcessNativePointerInput((PresentationSource)origin.Host,
                            new(PortablePointerEventKind.Cancel, 150, 75, 4, -1, 0, 0), 0, out _));
                        break;
                    case 1: origin.Host.RootVisual = new Grid(); break;
                    case 2: origin.Host.Dispose(); break;
                }
                owner.Viewer.UpdateLayout();
                Assert.Equal(40, owner.Info.HorizontalOffset); Assert.Equal(40, owner.Info.VerticalOffset);
            }
        });
    }

    [PortableScrollFact]
    public void NativeScrollSingularCrossSourceFrameCannotPublishPartialConsumption()
    {
        Run(() =>
        {
            using var owner = new ScrollFixture(PortableScrollAxes.Pixels);
            var bridge = new ScrollRouteRoot { InputParent = owner.Viewer };
            using var origin = new ScrollFixture(PortableScrollAxes.Pixels, root: bridge);
            origin.Info.CanHorizontallyScroll = origin.Info.CanVerticallyScroll = false;
            origin.Host.HitTestOverride = (_, _) => origin.Viewer;
            owner.Root.RenderTransform = new ScaleTransform(0, 1);
            owner.Root.UpdateLayout();
            Vector remaining = default;
            owner.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
                remaining = ((PortableScrollEventArgs)value).RemainingScroll));
            Assert.True(Route(origin, Packet(-8, -6, PortablePointerScrollUnit.Points), out bool handled));
            Assert.False(handled);
            owner.Viewer.UpdateLayout();
            Assert.Equal(new Vector(-8, -6), remaining);
            Assert.Equal(40, owner.Info.HorizontalOffset); Assert.Equal(40, owner.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollUndeclaredProvidersAcceptRealLinesWithoutGuessingPointUnits()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var custom = new RecordingScrollInfo { ScrollOwner = fixture.Viewer };
            fixture.Viewer.ScrollInfo = custom;
            fixture.Host.HitTestOverride = (_, _) => fixture.Viewer;
            Assert.True(Route(fixture, Packet(-1, -1, PortablePointerScrollUnit.Points), out bool handled));
            Assert.False(handled);
            Assert.True(Route(fixture, Packet(-0.25, -0.5, PortablePointerScrollUnit.Lines), out handled));
            Assert.True(handled);
            Assert.True(Route(fixture, Packet(-1.75, -2.5, PortablePointerScrollUnit.Lines), out handled));
            Assert.True(handled);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { "right", "right", "down", "down", "down" }, custom.Lines);
            Assert.Equal(55, custom.HorizontalOffset); Assert.Equal(73.75, custom.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollCustomCapabilityCallbacksCannotPublishRetiredSessionsOrCommands()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            fixture.Info.ReadUnits = () =>
            {
                fixture.Info.ReadUnits = null;
                fixture.Viewer.ScrollInfo = new RecordingScrollInfo { ScrollOwner = fixture.Viewer };
            };
            Assert.False(PortableScrollSession.TryCreate(fixture.Viewer, out var rejected));
            Assert.Null(rejected);
            fixture.Viewer.ScrollInfo = fixture.Info;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            fixture.Info.ReadUnits = () =>
            {
                fixture.Info.ReadUnits = null;
                fixture.Host.RootVisual = null;
            };
            Assert.False(session.TryQueue(Packet(-1, -2, PortablePointerScrollUnit.Points)));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.HorizontalOffset); Assert.Equal(40, fixture.Info.VerticalOffset);

            using var metrics = new ScrollFixture(PortableScrollAxes.Pixels);
            Assert.True(PortableScrollSession.TryCreate(metrics.Viewer, out var metricSession));
            var phase = new PortableScrollLifetime();
            Assert.True(metricSession.TryQueue(Packet(-3, -4, PortablePointerScrollUnit.Points), phase, out _));
            metrics.Info.ReadHorizontalOffset = () =>
            {
                metrics.Info.ReadHorizontalOffset = null;
                phase.Cancel();
            };
            metrics.Viewer.UpdateLayout();
            Assert.Equal(40, metrics.Info.HorizontalOffset); Assert.Equal(40, metrics.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollNestedMetricCallbackRetiresOnlyUnacceptedDispatch()
    {
        Run(() =>
        {
            foreach (var unit in new[] { PortablePointerScrollUnit.Points, PortablePointerScrollUnit.Lines })
            foreach (uint nestedPhase in new uint[] { 1, 4 })
            {
                using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
                var peer = fixture.AddPeer();
                int legacy = 0;
                fixture.Root.MouseWheel += (_, _) => ++legacy;
                Assert.True(Route(fixture, RoutedPacket(50, -2, phase: 1), out _));
                PortablePointerInput outer = RoutedPacket(50, -9, phase: 4, unit: unit);
                PortableScrollEventArgs? retired = null;
                fixture.Root.AddHandler(PortableScroll.PreviewScrollEvent, new RoutedEventHandler((_, value) =>
                {
                    var args = (PortableScrollEventArgs)value;
                    if (!ReferenceEquals(args.NativeInput, outer)) return;
                    retired = args;
                    fixture.Info.ReadHorizontalOffset = () =>
                    {
                        fixture.Info.ReadHorizontalOffset = null;
                        Assert.True(Route(fixture, RoutedPacket(250, -3, phase: nestedPhase, unit: unit), out bool handled));
                        Assert.True(handled);
                    };
                }));
                Assert.True(Route(fixture, outer, out bool retiredHandled));
                Assert.True(retiredHandled); // Never replay the stale packet through host fallback.
                Assert.NotNull(retired);
                Assert.False(retired.HasConsumedMotion);
                Assert.Equal(new Vector(0, -9), retired.RemainingScroll);
                fixture.Viewer.UpdateLayout();
                Assert.Equal(42, fixture.Info.VerticalOffset); // Previously accepted work survives.
                Assert.Equal(unit == PortablePointerScrollUnit.Points ? 43 : 73.75, peer.Info.VerticalOffset);
                Assert.Empty(fixture.Info.Lines);
                Assert.Equal(0, legacy);
            }
        });
    }

    [PortableScrollFact]
    public void NativeScrollFinalCapabilityCallbackCannotQueueRetiredDispatch()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var peer = fixture.AddPeer();
            bool nested = false;
            fixture.Info.ReadHorizontalOffset = () =>
            {
                fixture.Info.ReadHorizontalOffset = null;
                // The next capability read is the final session check, after
                // all metrics and vector mapping but before queue publication.
                fixture.Info.ReadUnits = () =>
                {
                    fixture.Info.ReadUnits = null;
                    nested = true;
                    Assert.True(Route(fixture, RoutedPacket(250, -3, phase: 4), out bool handled));
                    Assert.True(handled);
                };
            };
            Assert.True(Route(fixture, RoutedPacket(50, -9, phase: 1), out bool retiredHandled));
            Assert.True(nested);
            Assert.True(retiredHandled);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.VerticalOffset);
            Assert.Equal(43, peer.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollReentrantSessionCreationPreservesNewerFractionalState()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.VerticalItems);
            fixture.Info.ViewportHeight = 4; // 20 source points per provider item.
            PortablePointerInput nested = RoutedPacket(150, -12, phase: 1);
            PortableScrollEventArgs? nestedArgs = null;
            PortableScrollSession? nestedSession = null;
            fixture.Root.AddHandler(PortableScroll.PreviewScrollEvent, new RoutedEventHandler((_, value) =>
            {
                var args = (PortableScrollEventArgs)value;
                if (ReferenceEquals(args.NativeInput, nested)) nestedArgs = args;
            }));
            fixture.Info.ReadUnits = () =>
            {
                fixture.Info.ReadUnits = null;
                Assert.True(Route(fixture, nested, out bool handled));
                Assert.True(handled);
                Assert.NotNull(nestedArgs);
                Assert.True(fixture.Viewer.TryGetPortableScrollSession(nestedArgs.Sequence, out nestedSession));
                fixture.Viewer.UpdateLayout();
                Assert.Equal(40, fixture.Info.VerticalOffset); // Retain the first 0.6 item.
            };
            Assert.True(Route(fixture, RoutedPacket(150, -180, phase: 1), out bool retiredHandled));
            Assert.True(retiredHandled);
            Assert.NotNull(nestedArgs);
            Assert.NotNull(nestedSession);
            Assert.True(fixture.Viewer.TryGetPortableScrollSession(nestedArgs.Sequence, out var retained));
            Assert.Same(nestedSession, retained);
            Assert.True(Route(fixture, RoutedPacket(150, -12, phase: 4), out bool nextHandled));
            Assert.True(nextHandled);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(41, fixture.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollIndependentOriginCannotReplaceNewerSessionDuringCreation()
    {
        Run(() =>
        {
            using var owner = new ScrollFixture(PortableScrollAxes.VerticalItems);
            owner.Info.ViewportHeight = 4;
            var bridge = new ScrollRouteRoot { InputParent = owner.Viewer };
            using var origin = new ScrollFixture(PortableScrollAxes.Pixels, root: bridge);
            origin.Viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            origin.Viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            origin.Viewer.UpdateLayout();
            origin.Host.HitTestOverride = (_, _) => origin.Viewer;
            PortablePointerInput nested = RoutedPacket(150, -12, phase: 1);
            PortableScrollEventArgs? nestedArgs = null;
            PortableScrollSession? nestedSession = null;
            owner.Root.AddHandler(PortableScroll.PreviewScrollEvent, new RoutedEventHandler((_, value) =>
            {
                var args = (PortableScrollEventArgs)value;
                if (ReferenceEquals(args.NativeInput, nested)) nestedArgs = args;
            }));
            owner.Info.ReadUnits = () =>
            {
                owner.Info.ReadUnits = null;
                Assert.True(Route(owner, nested, out bool handled));
                Assert.True(handled);
                Assert.NotNull(nestedArgs);
                Assert.True(owner.Viewer.TryGetPortableScrollSession(nestedArgs.Sequence, out nestedSession));
                owner.Viewer.UpdateLayout();
                Assert.Equal(40, owner.Info.VerticalOffset); // First 0.6 item belongs to B.
            };
            Assert.True(Route(origin, RoutedPacket(150, -20, phase: 1), out bool outerHandled));
            Assert.True(outerHandled); // Origin A remains valid and contributes one item.
            Assert.NotNull(nestedArgs);
            Assert.NotNull(nestedSession);
            Assert.True(owner.Viewer.TryGetPortableScrollSession(nestedArgs.Sequence, out var retained));
            Assert.Same(nestedSession, retained);
            owner.Viewer.UpdateLayout();
            Assert.Equal(41, owner.Info.VerticalOffset);
            Assert.True(Route(owner, RoutedPacket(150, -12, phase: 4), out bool nextHandled));
            Assert.True(nextHandled);
            owner.Viewer.UpdateLayout();
            Assert.Equal(42, owner.Info.VerticalOffset); // B's two fractions remain connected.
            Assert.Equal(40, origin.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollFailingMetricCallbackCannotCancelNewerAcceptedInput()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var peer = fixture.AddPeer();
            Assert.True(Route(fixture, RoutedPacket(50, -2, phase: 1), out _));
            var failure = new InvalidOperationException("Source metric callback failed after newer input.");
            fixture.Info.ReadHorizontalOffset = () =>
            {
                fixture.Info.ReadHorizontalOffset = null;
                Assert.True(Route(fixture, RoutedPacket(250, -3, phase: 4), out bool handled));
                Assert.True(handled);
                throw failure;
            };
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                Route(fixture, RoutedPacket(50, -9, phase: 4), out _)));
            fixture.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(43, peer.Info.VerticalOffset);
            Assert.Equal(ModifierKeys.None, Keyboard.Modifiers);
        });
    }

    private sealed class ScrollRouteRoot : Grid
    {
        internal DependencyObject InputParent { get; init; } = null!;
        protected internal override DependencyObject GetUIParentCore() => InputParent;
    }

    private static PortablePointerInput RoutedPacket(double x, double delta, uint phase = 0, uint momentum = 0,
        PortablePointerScrollUnit unit = PortablePointerScrollUnit.Points) =>
        new(PortablePointerEventKind.Scroll, PortablePointerScrollProtocol.AppKit, x, 75, 3.125, -1, 0,
            PortablePointerModifiers.Super, 0, delta, unit, phase, momentum);

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
        internal Grid Root { get; }

        internal ScrollFixture(PortableScrollAxes axes, bool deferredOffsets = false, Grid? root = null)
        {
            Root = root ?? new Grid();
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

        internal (ScrollViewer Viewer, MeasuredScrollInfo Info) AddAncestor()
        {
            Host.RootVisual = null;
            var viewer = CreateViewer();
            viewer.Width = 400; viewer.Height = 200;
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            viewer.Template = new ControlTemplate(typeof(ScrollViewer)) { VisualTree = presenter };
            viewer.Content = Root;
            Host.RootVisual = viewer; Host.SetClientSize(400, 200);
            viewer.ApplyTemplate();
            var info = new MeasuredScrollInfo { ScrollOwner = viewer };
            viewer.Info = info; viewer.ScrollInfo = info;
            viewer.UpdateLayout();
            // Public source lookup intentionally projects a compatibility HWND
            // wrapper. Its actual root must still be this portable source's root.
            Assert.Same(Host.RootVisual, PresentationSource.FromVisual(Viewer)?.RootVisual);
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
        private double _horizontalOffset = 40;
        internal Action? ReadHorizontalOffset { get; set; }
        public double HorizontalOffset
        {
            get { ReadHorizontalOffset?.Invoke(); return _horizontalOffset; }
            private set => _horizontalOffset = value;
        }
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
        internal Action? ReadUnits { get; set; }
        public PortableScrollAxes ScrollAxes { get { ReadUnits?.Invoke(); return Axes; } }
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
