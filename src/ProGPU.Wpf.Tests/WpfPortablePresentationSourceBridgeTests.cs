using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class WpfPortablePresentationSourceBridgeTests
{
    [Fact]
    public void TryBindMirrorsBridgeRootVisualIntoHost()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        var root = new object();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);

        Assert.True(bound);
        Assert.NotNull(bridge);
        bridge.RootVisual = root;

        Assert.Same(root, source.RootVisual);
        Assert.Same(root, host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void SourceRenderRequestSynchronizesHostRootVisual()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        var root = new object();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);

        source.RootVisual = root;

        Assert.Same(root, bridge!.RootVisual);
        Assert.Same(root, host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void SourceDeviceScaleRequestSchedulesRenderWhenRootIsUnchanged()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);

        Assert.True(bridge!.TrySetDeviceScale(2.0, 1.5));

        Assert.Equal(2.0, source.DpiScaleX);
        Assert.Equal(1.5, source.DpiScaleY);
        Assert.Null(host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void SourceClientSizeRequestSchedulesRenderWhenRootIsUnchanged()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);

        Assert.True(bridge!.TrySetClientSize(420, 840));

        Assert.Equal(420, source.ClientWidth);
        Assert.Equal(840, source.ClientHeight);
        Assert.Null(host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void TryBindExposesPortableSourceHandle()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource
        {
            Handle = new IntPtr(0x50575046)
        };

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);

        Assert.True(bound);
        Assert.NotNull(bridge);
        Assert.Equal(source.Handle, bridge.Handle);
    }

    [Fact]
    public void DisposeUnsubscribesFromSourceRenderRequests()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        var root = new object();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);
        source.RootVisual = root;
        Assert.Same(root, host.WpfRootVisual);

        bridge!.Dispose();
        source.RootVisual = new object();

        Assert.Null(host.WpfRootVisual);
        Assert.Equal(2, scheduler.RequestCount);
        Assert.False(source.IsDisposed);
    }

    [Fact]
    public void SourceCursorRequestForwardsWpfCursorToHost()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);
        Assert.NotNull(bridge);

        source.RequestCursor(new FakeCursor("Hand"));

        Assert.Equal(WpfCursor.Hand, host.LastPortableCursor);
    }

    [Fact]
    public void DisposeUnsubscribesFromSourceCursorRequests()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);
        source.RequestCursor(new FakeCursor("IBeam"));
        Assert.Equal(WpfCursor.IBeam, host.LastPortableCursor);

        bridge!.Dispose();
        source.RequestCursor(new FakeCursor("Hand"));

        Assert.Equal(WpfCursor.IBeam, host.LastPortableCursor);
    }

    [Fact]
    public void TryBindInstallsGpuHitTestOverrideWhenSourceExposesHook()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);

        Assert.True(bound);
        Assert.NotNull(bridge);
        Assert.NotNull(source.HitTestOverride);
        Assert.Null(source.HitTestOverride(new System.Windows.Point(12, 24)));

        bridge!.Dispose();

        Assert.Null(source.HitTestOverride);
    }

    [Fact]
    public void TryBindReturnsFalseWhenSourceShapeIsMissing()
    {
        using var host = new ProGpuWpfWindowHost();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, new object(), out var bridge);

        Assert.False(bound);
        Assert.Null(bridge);
    }

    private sealed class FakePortablePresentationSource : IDisposable
    {
        private object? _rootVisual;

        internal event EventHandler? RenderRequested;

        internal event EventHandler? CursorRequested;

        public object CompositionTarget { get; } = new();

        public IntPtr Handle { get; init; }

        internal object? RequestedCursor { get; private set; }

        internal Func<System.Windows.Point, object?>? HitTestOverride { get; set; }

        public object? RootVisual
        {
            get => _rootVisual;
            set
            {
                _rootVisual = value;
                RenderRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        public double DpiScaleX { get; private set; } = 1.0;

        public double DpiScaleY { get; private set; } = 1.0;

        public double ClientWidth { get; private set; }

        public double ClientHeight { get; private set; }

        public bool IsDisposed { get; private set; }

        internal void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            DpiScaleX = dpiScaleX;
            DpiScaleY = dpiScaleY;
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        internal void SetClientSize(double width, double height)
        {
            ClientWidth = width;
            ClientHeight = height;
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        internal void RequestCursor(object cursor)
        {
            RequestedCursor = cursor;
            CursorRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class FakeCursor
    {
        public FakeCursor(string cursorType)
        {
            CursorType = cursorType;
        }

        public string CursorType { get; }
    }

    private sealed class TestRenderScheduler : IWpfRenderScheduler
    {
        public event EventHandler? RenderRequested;

        public int RequestCount { get; private set; }

        public bool HasPendingRenderRequest { get; private set; }

        public void RequestRender()
        {
            RequestCount++;
            HasPendingRenderRequest = true;
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public bool ConsumeRenderRequest()
        {
            var hadPendingRequest = HasPendingRenderRequest;
            HasPendingRenderRequest = false;
            return hadPendingRequest;
        }

        public void Reset()
        {
            HasPendingRenderRequest = false;
        }
    }
}
