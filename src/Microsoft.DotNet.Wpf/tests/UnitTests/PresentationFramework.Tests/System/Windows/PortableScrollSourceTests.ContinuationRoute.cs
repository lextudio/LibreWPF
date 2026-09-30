// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Controls;
using System.Windows.Input;
using ProGPU.Wpf.Interop;

namespace System.Windows;

public sealed partial class PortableScrollSourceTests
{
    [PortableScrollFact]
    public void NativeScrollContinuationUsesActualDefaultHandlersWithoutReplayingApplications()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var ancestor = PrepareBoundaryRoute(fixture);
            int ordinary = 0, observed = 0, ancestorObserved = 0, hits = 0;
            fixture.Host.HitTestOverride = (_, _) => { ++hits; return fixture.Viewer; };
            fixture.Root.AddHandler(PortableScroll.ScrollEvent,
                new RoutedEventHandler((_, _) => ++ordinary));
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                Assert.True(value.Handled);
                ++observed;
            }), true);
            ancestor.Viewer.AddHandler(PortableScroll.ScrollEvent,
                new RoutedEventHandler((_, _) => ++ancestorObserved), true);

            Assert.True(Route(fixture, RoutedPacket(150, -5, phase: 1), out bool handled));
            Assert.True(handled);
            Assert.Equal(40, fixture.Info.VerticalOffset);
            Assert.Equal(40, ancestor.Info.VerticalOffset);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(43, ancestor.Info.VerticalOffset);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(43, ancestor.Info.VerticalOffset);
            Assert.Equal(0, ordinary);
            Assert.Equal(1, observed);
            Assert.Equal(1, ancestorObserved);
            Assert.Equal(1, hits);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationLinesWaitForPublishedBoundaryAndCarryAncestorFraction()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels, deferredOffsets: true);
            var ancestor = PrepareBoundaryRoute(fixture);
            fixture.Info.ExtentHeight = 151.25; // One actual 11.25-unit LineDown reaches the boundary.
            ancestor.Info.DeferredOffsets = true;
            int observed = 0;
            fixture.Root.AddHandler(PortableScroll.ScrollEvent,
                new RoutedEventHandler((_, _) => ++observed), true);

            Assert.True(Route(fixture, RoutedPacket(150, -2.25, phase: 1,
                unit: PortablePointerScrollUnit.Lines), out bool firstHandled));
            Assert.True(firstHandled);
            Assert.Empty(fixture.Info.Lines);
            Assert.Empty(ancestor.Info.Lines);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(new[] { "down" }, fixture.Info.Lines);
            Assert.Equal(new[] { "down" }, ancestor.Info.Lines);
            Assert.Equal(51.25, fixture.Info.VerticalOffset);
            Assert.Equal(51.25, ancestor.Info.VerticalOffset);

            // The first command forwards 1.25 native lines only after the inner
            // provider publishes its last line. The ancestor retains the final .25.
            Assert.True(Route(fixture, RoutedPacket(150, -0.75, phase: 4,
                unit: PortablePointerScrollUnit.Lines), out bool secondHandled));
            Assert.True(secondHandled);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(new[] { "down" }, fixture.Info.Lines);
            Assert.Equal(new[] { "down", "down" }, ancestor.Info.Lines);
            Assert.Equal(51.25, fixture.Info.VerticalOffset);
            Assert.Equal(62.5, ancestor.Info.VerticalOffset);
            Assert.Equal(2, observed);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationSameValueHandledAssignmentBelongsToApplication()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var ancestor = PrepareBoundaryRoute(fixture);
            int observed = 0;
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                Assert.True(value.Handled); // Already set by the inner default handler.
                value.Handled = true;       // Nevertheless an explicit application claim.
                ++observed;
            }), true);

            Assert.True(Route(fixture, RoutedPacket(150, -5, phase: 1), out bool handled));
            Assert.True(handled);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(40, ancestor.Info.VerticalOffset);
            Assert.Equal(1, observed);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationApplicationCanClearHandlingBeforeLaterDefault()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var ancestor = PrepareBoundaryRoute(fixture);
            var calls = new List<string>();
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                Assert.True(value.Handled);
                value.Handled = true;
                calls.Add("claim");
            }), true);
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                value.Handled = false;
                value.Handled = false; // A repeated assignment does not restore the default claim.
                calls.Add("release");
            }), true);
            fixture.Root.AddHandler(PortableScroll.ScrollEvent,
                new RoutedEventHandler((_, _) => calls.Add("ordinary")));

            Assert.True(Route(fixture, RoutedPacket(150, -5, phase: 1), out bool handled));
            Assert.True(handled);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(43, ancestor.Info.VerticalOffset);
            Assert.Equal(new[] { "claim", "release", "ordinary" }, calls);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationWaitsForSealAfterReentrantLayout()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var ancestor = PrepareBoundaryRoute(fixture);
            int observed = 0;
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, _) =>
            {
                fixture.Viewer.UpdateLayout();
                Assert.Equal(42, fixture.Info.VerticalOffset);
                Assert.Equal(40, ancestor.Info.VerticalOffset);
                ancestor.Viewer.UpdateLayout();
                Assert.Equal(40, ancestor.Info.VerticalOffset);
                ++observed;
            }), true);

            Assert.True(Route(fixture, RoutedPacket(150, -5, phase: 1), out bool handled));
            Assert.True(handled);
            Assert.Equal(1, observed);
            Assert.Equal(40, ancestor.Info.VerticalOffset);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(43, ancestor.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationAbandonsOnlyFailingUnsealedRoute()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var ancestor = PrepareBoundaryRoute(fixture);
            var failure = new InvalidOperationException("Application failed after source layout.");
            RoutedEventHandler fail = (_, _) =>
            {
                fixture.Viewer.UpdateLayout();
                Assert.Equal(42, fixture.Info.VerticalOffset);
                Assert.Equal(40, ancestor.Info.VerticalOffset);
                throw failure;
            };
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, fail, true);

            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                Route(fixture, RoutedPacket(150, -5, phase: 1), out _)));
            fixture.Root.RemoveHandler(PortableScroll.ScrollEvent, fail);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset); // Already applied source work is not undone.
            Assert.Equal(40, ancestor.Info.VerticalOffset);
            Assert.True(Route(fixture, RoutedPacket(150, -3, phase: 1), out bool handled));
            Assert.True(handled);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(43, ancestor.Info.VerticalOffset); // No abandoned three-point residual replay.
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationAcceptedMotionSurvivesNewerDispatchRevision()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var peer = fixture.AddPeer();
            var ancestor = PrepareBoundaryRoute(fixture);
            PortablePointerInput original = RoutedPacket(50, -5, phase: 1);
            PortablePointerInput nested = RoutedPacket(250, -3, phase: 4);
            fixture.Host.HitTestOverride = (x, _) => x < 200 ? fixture.Viewer : peer.Viewer;
            int observed = 0;
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                ++observed;
                if (!ReferenceEquals(((PortableScrollEventArgs)value).NativeInput, original)) return;
                Assert.True(Route(fixture, nested, out bool nestedHandled));
                Assert.True(nestedHandled);
            }), true);

            Assert.True(Route(fixture, original, out bool handled));
            Assert.True(handled);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(43, peer.Info.VerticalOffset);
            Assert.Equal(43, ancestor.Info.VerticalOffset);
            Assert.Equal(2, observed);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationCannotCaptureReentrantRaiseOfTheSameArgumentsAtAPeer()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var peer = fixture.AddPeer();
            var ancestor = PrepareBoundaryRoute(fixture);
            bool nested = false;
            int observed = 0;
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                ++observed;
                Assert.True(value.Handled);
                if (nested) return;
                nested = true;
                // Ordinary routed handlers may synchronously raise these same arguments.
                // Their delivery must not append a second route to the native continuation.
                peer.Viewer.RaiseEvent(value);
            }), true);

            Assert.True(Route(fixture, RoutedPacket(50, -5, phase: 1), out bool handled));
            Assert.True(handled);
            Assert.Equal(2, observed); // Application delivery itself is not suppressed.
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(40, peer.Info.VerticalOffset);
            Assert.Equal(43, ancestor.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationCannotBeClaimedByReentryDuringOriginalRouteConstruction()
    {
        Run(() =>
        {
            var root = new ContinuationRouteConstructionRoot();
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels, root: root);
            var peer = fixture.AddPeer();
            var ancestor = PrepareBoundaryRoute(fixture);
            int constructions = 0, observed = 0;
            root.AddHandler(PortableScroll.PreviewScrollEvent, new RoutedEventHandler((_, value) =>
            {
                // The next bubble is armed but has not constructed its EventRoute yet.
                // GetUIParentCore is actual application code within that construction.
                root.BeforeParentLookup = () =>
                {
                    root.BeforeParentLookup = null;
                    Assert.Equal(PortableScroll.ScrollEvent, value.RoutedEvent);
                    ++constructions;
                    peer.Viewer.RaiseEvent(value);
                };
            }));
            root.AddHandler(PortableScroll.ScrollEvent,
                new RoutedEventHandler((_, _) => ++observed), true);

            Assert.True(Route(fixture, RoutedPacket(50, -5, phase: 1), out bool handled));
            Assert.True(handled);
            Assert.Equal(1, constructions);
            Assert.Equal(2, observed);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(40, peer.Info.VerticalOffset);
            Assert.Equal(43, ancestor.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationAppendsBehindAcceptedAncestorCommands()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var ancestor = PrepareBoundaryRoute(fixture);
            ancestor.Viewer.ScrollToVerticalOffset(100);
            Assert.True(Route(fixture, RoutedPacket(150, -5, phase: 1), out bool firstHandled));
            Assert.True(firstHandled);
            Assert.True(Route(fixture, RoutedPacket(150, -1, phase: 4), out bool secondHandled));
            Assert.True(secondHandled);

            ancestor.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(104, ancestor.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationFullAncestorQueueFailsWithoutDiscardingAcceptedCommands()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var ancestor = PrepareBoundaryRoute(fixture);
            fixture.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, _) =>
            {
                // Complete the inner command while its actual route is still unsealed.
                fixture.Viewer.UpdateLayout();
                Assert.Equal(42, fixture.Info.VerticalOffset);
                Assert.Equal(40, ancestor.Info.VerticalOffset);
                Assert.True(PortableScrollSession.TryCreate(ancestor.Viewer, out var session));
                for (int i = 0; i < 31; ++i)
                    Assert.True(session.TryQueue(Packet(0, -1, PortablePointerScrollUnit.Points)));
            }), true);

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                Route(fixture, RoutedPacket(150, -5, phase: 1), out _));
            Assert.Equal("The native scroll ancestor queue is full.", failure.Message);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(71, ancestor.Info.VerticalOffset);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(71, ancestor.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationCannotReplaceCapturedProviderAfterItsHandlerOccurrence()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            var ancestor = PrepareBoundaryRoute(fixture);
            var replacement = new MeasuredScrollInfo
            {
                ScrollOwner = ancestor.Viewer,
                CanHorizontallyScroll = true,
                CanVerticallyScroll = true
            };
            ancestor.Viewer.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, _) =>
                ancestor.Viewer.ScrollInfo = replacement), true);

            Assert.True(Route(fixture, RoutedPacket(150, -5, phase: 1), out bool handled));
            Assert.True(handled);
            ancestor.Viewer.UpdateLayout();
            Assert.Equal(42, fixture.Info.VerticalOffset);
            Assert.Equal(40, ancestor.Info.VerticalOffset);
            Assert.Equal(40, replacement.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationRetainsOriginalFrameAcrossBranchSourceAndGeometryChanges()
    {
        Run(() =>
        {
            using var owner = new ScrollFixture(PortableScrollAxes.Pixels);
            var bridge = new ScrollRouteRoot { InputParent = owner.Viewer };
            using var origin = new ScrollFixture(PortableScrollAxes.Pixels, root: bridge);
            origin.Info.ExtentHeight = 142;
            origin.Host.HitTestOverride = (_, _) => origin.Viewer;
            ((IPortableDesktopGeometryHost)origin.Host).SetDesktopTransform(new(125, -40, 2, 2));
            ((IPortableDesktopGeometryHost)owner.Host).SetDesktopTransform(new(-20, 90, 1, 1));
            object redirectedSource = new();
            int observed = 0;
            origin.Root.AddHandler(PortableScroll.ScrollEvent, new RoutedEventHandler((_, value) =>
            {
                Assert.Same(origin.Viewer, value.OriginalSource);
                value.Source = redirectedSource;
                Assert.Same(redirectedSource, value.Source);
                ++observed;
            }), true);

            Assert.True(Route(origin, RoutedPacket(150, -5, phase: 1), out bool handled));
            Assert.True(handled);
            // Accepted motion retains its original physical frame, not a later desktop transform.
            ((IPortableDesktopGeometryHost)origin.Host).SetDesktopTransform(new(600, 300, 4, 4));
            origin.Viewer.UpdateLayout();
            owner.Viewer.UpdateLayout();
            Assert.Equal(42, origin.Info.VerticalOffset);
            Assert.Equal(46, owner.Info.VerticalOffset);
            Assert.Equal(1, observed);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationPreviewFrameMutationCannotSplitOnePacketAcrossFrames()
    {
        Run(() =>
        {
            using var owner = new ScrollFixture(PortableScrollAxes.Pixels);
            var bridge = new ScrollRouteRoot { InputParent = owner.Viewer };
            using var origin = new ScrollFixture(PortableScrollAxes.Pixels, root: bridge);
            origin.Info.ExtentHeight = 142;
            origin.Host.HitTestOverride = (_, _) => origin.Viewer;
            ((IPortableDesktopGeometryHost)origin.Host).SetDesktopTransform(new(125, -40, 2, 2));
            ((IPortableDesktopGeometryHost)owner.Host).SetDesktopTransform(new(-20, 90, 1, 1));
            int observed = 0;
            origin.Root.AddHandler(PortableScroll.PreviewScrollEvent, new RoutedEventHandler((_, _) =>
            {
                ++observed;
                ((IPortableDesktopGeometryHost)origin.Host).SetDesktopTransform(new(600, 300, 4, 4));
            }));

            Assert.True(Route(origin, RoutedPacket(150, -5, phase: 1), out bool handled));
            Assert.True(handled);
            origin.Viewer.UpdateLayout();
            owner.Viewer.UpdateLayout();
            // The original five points represented ten desktop units. The current inner
            // frame consumes two offsets at scale four, leaving exactly two desktop units.
            Assert.Equal(42, origin.Info.VerticalOffset);
            Assert.Equal(42, owner.Info.VerticalOffset);
            Assert.Equal(1, observed);
        });
    }

    private static (ScrollViewer Viewer, MeasuredScrollInfo Info) PrepareBoundaryRoute(ScrollFixture fixture)
    {
        var ancestor = fixture.AddAncestor();
        fixture.Info.ExtentHeight = 142; // Current offset 40, viewport 100: two points remain.
        fixture.Host.HitTestOverride = (_, _) => fixture.Viewer;
        return ancestor;
    }

    private sealed class ContinuationRouteConstructionRoot : Grid
    {
        internal Action? BeforeParentLookup { get; set; }

        protected internal override DependencyObject GetUIParentCore()
        {
            BeforeParentLookup?.Invoke();
            return base.GetUIParentCore();
        }
    }
}
