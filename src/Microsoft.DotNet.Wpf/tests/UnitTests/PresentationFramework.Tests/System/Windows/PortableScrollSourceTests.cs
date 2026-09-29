// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            fixture.Viewer.RenderTransform = new ScaleTransform(2, 4);
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
            fixture.Viewer.RenderTransform = new ScaleTransform(2, 4);
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
            foreach (int retirement in new[] { 0, 1, 2, 3, 4, 5, 6 })
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

    private static PortablePointerInput Packet(double x, double y, PortablePointerScrollUnit unit) =>
        new(PortablePointerEventKind.Scroll, 10, 10, 1, -1, 0, 0, x, y, unit);

    private sealed class ScrollFixture : IDisposable
    {
        internal IPortablePresentationSourceHost Host { get; } = PortablePresentationSourceHost.Create();
        internal ScrollViewer Viewer { get; }
        internal MeasuredScrollInfo Info { get; }

        internal ScrollFixture(PortableScrollAxes axes, bool deferredOffsets = false)
        {
            var viewer = new LayoutScrollViewer
            {
                Width = 200, Height = 100, CanContentScroll = true,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Template = new ControlTemplate(typeof(ScrollViewer)) { VisualTree = new FrameworkElementFactory(typeof(Border)) }
            };
            Viewer = viewer;
            var root = new Grid(); root.Children.Add(Viewer);
            Host.RootVisual = root; Host.SetClientSize(400, 200);
            Info = new MeasuredScrollInfo { ScrollOwner = Viewer, Axes = axes, DeferredOffsets = deferredOffsets };
            viewer.Info = Info;
            Viewer.ScrollInfo = Info;
            Viewer.UpdateLayout();
        }

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
