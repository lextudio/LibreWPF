// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Controls;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows;

public sealed partial class PortableScrollSourceTests
{
    private sealed class RecordingContinuation : IPortableScrollContinuation
    {
        public bool IsCurrent { get; set; } = true;
        internal List<Vector> Overflow { get; } = new();
        internal Exception? Failure { get; set; }
        public void Continue(Vector value)
        {
            Overflow.Add(value);
            if (Failure != null) throw Failure;
        }
    }

    private static void QueueOwned(PortableScrollSession session, double x, double y,
        PortablePointerScrollUnit unit, RecordingContinuation continuation, PortableScrollLifetime? lifetime = null,
        Matrix? originToDesktop = null)
    {
        Assert.True(session.TryQueueRemaining(Packet(x, y, unit), lifetime ?? new PortableScrollLifetime(),
            new Vector(x, y), session.Source, continuation, out Vector remaining, out bool full, originToDesktop));
        Assert.False(full);
        Assert.Equal(default, remaining);
    }

    [PortableScrollFact]
    public void NativeScrollOverflowUsesFrozenVisualAndLogicalAdmissionFrame()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.VerticalItems);
            fixture.Info.ViewportHeight = 4;
            PublishVisualScale(fixture);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var continuation = new RecordingContinuation();
            // 40 original points / visual scale 4 / logical scale 20 = 0.5 items.
            QueueOwned(session, 0, -40, PortablePointerScrollUnit.Points, continuation);
            fixture.Viewer.RenderTransform = new ScaleTransform(8, 8);
            fixture.Info.ViewportHeight = 9; fixture.Info.ExtentHeight = 49;
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.VerticalOffset);
            Assert.Equal(new[] { new Vector(0, -40) }, continuation.Overflow);
        });
    }

    [PortableScrollFact]
    public void NativeScrollOpposingFractionsKeepTheOlderFrameAndOwner()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.VerticalItems);
            fixture.Info.ViewportHeight = 4; fixture.Info.ExtentHeight = 46;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var old = new RecordingContinuation(); var opposite = new RecordingContinuation();
            var inward = new RecordingContinuation();
            QueueOwned(session, 0, -7.5, PortablePointerScrollUnit.Points, old); // +0.375 items.
            fixture.Viewer.UpdateLayout();
            fixture.Viewer.RenderTransform = new ScaleTransform(2, 2); fixture.Viewer.UpdateLayout();
            QueueOwned(session, 0, 5, PortablePointerScrollUnit.Points, opposite); // -0.125 items.
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.VerticalOffset);
            fixture.Info.SetVerticalOffset(42);
            QueueOwned(session, 0, 40, PortablePointerScrollUnit.Points, inward);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(41, fixture.Info.VerticalOffset);
            Assert.Equal(new[] { new Vector(0, -5) }, old.Overflow); // Original 20 points/item, not current 40.
            Assert.Empty(opposite.Overflow); Assert.Empty(inward.Overflow);
        });
    }

    [PortableScrollFact]
    public void NativeScrollRoundedItemDebtRetainsItsOriginalPacketFrame()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.VerticalItems);
            fixture.Info.ViewportHeight = 4;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var old = new RecordingContinuation(); var current = new RecordingContinuation();
            QueueOwned(session, 0, -15, PortablePointerScrollUnit.Points, old);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(41, fixture.Info.VerticalOffset); // +0.75 rounded, -0.25 remains owned.
            fixture.Viewer.RenderTransform = new ScaleTransform(2, 2); fixture.Viewer.UpdateLayout();
            fixture.Info.SetVerticalOffset(0);
            QueueOwned(session, 0, -40, PortablePointerScrollUnit.Points, current);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(1, fixture.Info.VerticalOffset);
            Assert.Equal(new[] { new Vector(0, 5) }, old.Overflow);
            Assert.Empty(current.Overflow);
        });
    }

    [PortableScrollFact]
    public void NativeScrollUnitChangesForwardFractionsUnderTheirOriginalContinuations()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.VerticalItems);
            fixture.Info.ViewportHeight = 4;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var points = new RecordingContinuation(); var lines = new RecordingContinuation();
            var next = new RecordingContinuation();
            QueueOwned(session, 0, -5, PortablePointerScrollUnit.Points, points);
            fixture.Viewer.UpdateLayout();
            QueueOwned(session, 0, -0.25, PortablePointerScrollUnit.Lines, lines);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { new Vector(0, -5) }, points.Overflow);
            Assert.Empty(lines.Overflow);
            QueueOwned(session, 0, -2.5, PortablePointerScrollUnit.Points, next);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { new Vector(0, -0.25) }, lines.Overflow);
            Assert.Empty(next.Overflow); Assert.Empty(fixture.Info.Lines);
            Assert.Equal(40, fixture.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollCancelledFractionsCannotContributeToNewPackets()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.VerticalItems);
            fixture.Info.ViewportHeight = 4;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var lifetime = new PortableScrollLifetime();
            var old = new RecordingContinuation(); var next = new RecordingContinuation();
            QueueOwned(session, 0, -7.5, PortablePointerScrollUnit.Points, old, lifetime);
            fixture.Viewer.UpdateLayout(); lifetime.Cancel();
            QueueOwned(session, 0, -7.5, PortablePointerScrollUnit.Points, next);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.VerticalOffset); // Not the sum 0.75 -> one item.
            Assert.Empty(old.Overflow); Assert.Empty(next.Overflow);
        });
    }

    [PortableScrollFact]
    public void NativeScrollLineFractionsRetainEachOwnerWithoutVisualScaling()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var first = new RecordingContinuation(); var second = new RecordingContinuation();
            var inward = new RecordingContinuation();
            QueueOwned(session, -0.25, 0, PortablePointerScrollUnit.Lines, first);
            fixture.Viewer.UpdateLayout(); PublishVisualScale(fixture);
            QueueOwned(session, -0.25, 0, PortablePointerScrollUnit.Lines, second);
            fixture.Viewer.UpdateLayout(); fixture.Info.SetHorizontalOffset(800);
            QueueOwned(session, 1, 0, PortablePointerScrollUnit.Lines, inward);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { new Vector(-0.25, 0) }, first.Overflow);
            Assert.Equal(new[] { new Vector(-0.25, 0) }, second.Overflow);
            Assert.Empty(inward.Overflow);
            Assert.Equal(new[] { "left" }, fixture.Info.Lines);
            Assert.Equal(792.5, fixture.Info.HorizontalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollContinuationUsesCapturedOriginEvenWhenReceivingSourceIsTheSame()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            Matrix captured = Matrix.Identity;
            ((IPortableDesktopGeometryHost)fixture.Host).SetDesktopTransform(new(500, -200, 2, 3));
            var moved = new RecordingContinuation();
            QueueOwned(session, 0, -12, PortablePointerScrollUnit.Points, moved, originToDesktop: captured);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(44, fixture.Info.VerticalOffset); // Original desktop vector 12 / receiving scale 3.
            Assert.Empty(moved.Overflow);
            fixture.Info.SetVerticalOffset(900);
            QueueOwned(session, 0, -12, PortablePointerScrollUnit.Points, moved, originToDesktop: captured);
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { new Vector(0, -12) }, moved.Overflow);
        });
    }

    [PortableScrollFact]
    public void NativeScrollProvenanceBudgetRejectsBeforeAcceptanceAndReclaimsCancelledFractions()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.VerticalItems);
            fixture.Info.ViewportHeight = 4;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var lifetime = new PortableScrollLifetime();
            for (int i = 0; i < PortableScrollSession.MaximumProvenancePackets; ++i)
            {
                QueueOwned(session, 0, -20.0 / 1024, PortablePointerScrollUnit.Points,
                    new RecordingContinuation(), lifetime);
                fixture.Viewer.UpdateLayout();
            }
            var input = Packet(0, -20.0 / 1024, PortablePointerScrollUnit.Points);
            Assert.False(session.TryQueueRemaining(input, lifetime, new(0, input.ScrollY), session.Source,
                new RecordingContinuation(), out Vector remaining, out bool full));
            Assert.True(full); Assert.Equal(new Vector(0, input.ScrollY), remaining);
            Assert.Equal(40, fixture.Info.VerticalOffset);
            lifetime.Cancel();
            QueueOwned(session, 0, -20.0 / 1024, PortablePointerScrollUnit.Points, new RecordingContinuation());
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.VerticalOffset);
        });
    }

    [PortableScrollFact]
    public void NativeScrollAbandonedContinuationCannotExecuteAcceptedMovement()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.Pixels);
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var continuation = new RecordingContinuation();
            QueueOwned(session, -20, -20, PortablePointerScrollUnit.Points, continuation);
            continuation.IsCurrent = false;
            fixture.Viewer.UpdateLayout();
            Assert.Equal(40, fixture.Info.HorizontalOffset); Assert.Equal(40, fixture.Info.VerticalOffset);
            Assert.Empty(continuation.Overflow);
            var replaced = new RecordingContinuation();
            QueueOwned(session, -2, 0, PortablePointerScrollUnit.Lines, replaced);
            fixture.Info.AfterHorizontalOffsetSet = () => replaced.IsCurrent = false;
            fixture.Viewer.UpdateLayout();
            Assert.Equal(new[] { "right" }, fixture.Info.Lines);
            Assert.Equal(47.5, fixture.Info.HorizontalOffset);
            Assert.Empty(replaced.Overflow);
        });
    }

    [PortableScrollFact]
    public void NativeScrollOverflowRetiresOnceAndPreservesTheFirstContinuationFailure()
    {
        Run(() =>
        {
            using var fixture = new ScrollFixture(PortableScrollAxes.HorizontalItems | PortableScrollAxes.VerticalItems);
            fixture.Info.ViewportWidth = fixture.Info.ViewportHeight = 4;
            fixture.Info.ExtentWidth = fixture.Info.ExtentHeight = 46;
            Assert.True(PortableScrollSession.TryCreate(fixture.Viewer, out var session));
            var firstError = new InvalidOperationException("first continuation");
            var first = new RecordingContinuation { Failure = firstError };
            var second = new RecordingContinuation { Failure = new InvalidOperationException("second continuation") };
            QueueOwned(session, 0, -2.5, PortablePointerScrollUnit.Points, first);
            fixture.Viewer.UpdateLayout();
            QueueOwned(session, -5, 0, PortablePointerScrollUnit.Points, second);
            fixture.Viewer.UpdateLayout(); fixture.Info.SetHorizontalOffset(42); fixture.Info.SetVerticalOffset(42);
            QueueOwned(session, 40, 20, PortablePointerScrollUnit.Points, new RecordingContinuation());
            Assert.Same(firstError, Assert.Throws<InvalidOperationException>(() => fixture.Viewer.UpdateLayout()));
            Assert.Single(first.Overflow); Assert.Single(second.Overflow);
            fixture.Viewer.UpdateLayout();
            Assert.Single(first.Overflow); Assert.Single(second.Overflow);
        });
    }
}
