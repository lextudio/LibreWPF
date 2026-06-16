using System.Reflection;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class WpfPortableWindowActivationTests
{
    [Fact]
    public void SetTitleAndClientSizeForwardWindowPropertyChangesToHost()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Title = "Initial",
            Width = 640,
            Height = 480
        })
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.SetTitle("Updated");
        activation.SetClientSize(320.2, double.NaN);
        activation.SetClientSize(double.NaN, 240.1);

        Assert.Equal("Updated", host.Title);
        Assert.Equal(321, host.Width);
        Assert.Equal(241, host.Height);
        Assert.True(scheduler.RequestCount >= 4);
    }

    [Fact]
    public void TryAttachBindsExistingPortableSourceAndUsesWindowAsRootVisual()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);
        Assert.Same(host, activation.Host);
        Assert.Same(window, activation.Window);
        Assert.Same(window, activation.RootVisual);
        Assert.Same(source, activation.PortablePresentationSource);
        Assert.Same(window, source.RootVisual);
        Assert.Same(window, host.WpfRootVisual);
        Assert.True(scheduler.RequestCount >= 1);
    }

    [Fact]
    public void TryAttachReturnsFalseWhenSourceShapeIsMissing()
    {
        using var host = new ProGpuWpfWindowHost();

        var attached = WpfPortableWindowActivation.TryAttach(
            host,
            new FakeWindow(),
            new object(),
            out var activation);

        Assert.False(attached);
        Assert.Null(activation);
        Assert.Null(host.PortablePresentationSource);
        Assert.Null(host.WpfRootVisual);
    }

    [Fact]
    public void NativeHostClosingInvokesWindowClose()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, Array.Empty<object>());

        Assert.Equal(1, window.CloseCount);
    }

    [Fact]
    public void NativeHostClosingCancelsWhenWindowCloseIsCanceled()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow
        {
            CancelClose = true
        };
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        bool? canceled = null;
        host.Closing += (_, args) => canceled = args.Cancel;

        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, Array.Empty<object>());

        Assert.Equal(1, window.CloseCount);
        Assert.False(window.IsClosed);
        Assert.True(canceled);
    }

    [Fact]
    public void NativeHostClosingInfersCancellationFromWpfDisposedField()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeDisposedWindow
        {
            CancelClose = true
        };
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        bool? canceled = null;
        host.Closing += (_, args) => canceled = args.Cancel;

        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, Array.Empty<object>());

        Assert.Equal(1, window.CloseCount);
        Assert.False(window.DisposedStateForTest);
        Assert.True(canceled);
    }

    [Fact]
    public void CreateHostOptionsReadsFiniteWindowShape()
    {
        var fallback = new ProGpuWpfWindowOptions
        {
            Title = "Fallback",
            Width = 800,
            Height = 600,
            VSync = true
        };
        var window = new FakeWindow
        {
            Title = "Portable WPF",
            Width = 640.2,
            Height = double.NaN,
            ActualHeight = 480.1,
            WindowState = FakeWindowState.Minimized
        };

        var options = WpfPortableWindowActivation.CreateHostOptions(window, fallback);

        Assert.Equal("Portable WPF", options.Title);
        Assert.Equal(641, options.Width);
        Assert.Equal(481, options.Height);
        Assert.True(options.VSync);
        Assert.Equal(ProGpuWpfWindowState.Minimized, options.WindowState);
    }

    [Fact]
    public void HideAndSetWindowStateUpdateHostWithoutNativeWindow()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.Hide();
        activation.SetWindowState(FakeWindowState.Maximized);

        Assert.False(host.IsVisible);
        Assert.Equal(ProGpuWpfWindowState.Maximized, host.WindowState);
        Assert.True(scheduler.RequestCount >= 3);
    }

    [Fact]
    public void HostActivationEventsForwardToWpfWindowActivationState()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeActivatableWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        RaiseHostWindowEvent(host, WpfWindowEventKind.Activated);
        RaiseHostWindowEvent(host, WpfWindowEventKind.Activated);
        RaiseHostWindowEvent(host, WpfWindowEventKind.FilesDropped);
        RaiseHostWindowEvent(host, WpfWindowEventKind.Deactivated);
        RaiseHostWindowEvent(host, WpfWindowEventKind.Deactivated);

        Assert.False(window.IsActive);
        Assert.Equal(1, window.ActivatedCount);
        Assert.Equal(1, window.DeactivatedCount);
    }

    [Fact]
    public void DisposingActivationStopsWindowEventForwarding()
    {
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = new TestRenderScheduler()
        };
        var window = new FakeActivatableWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.Dispose();
        RaiseHostWindowEvent(host, WpfWindowEventKind.Activated);

        Assert.False(window.IsActive);
        Assert.Equal(0, window.ActivatedCount);
    }

    private static void RaiseHostWindowEvent(ProGpuWpfWindowHost host, WpfWindowEventKind kind)
    {
        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnPlatformWindowEventReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object?[] { null, new WpfWindowEventArgs(kind) });
    }

    private sealed class FakeWindow
    {
        public string? Title { get; set; }

        public double Width { get; set; } = double.NaN;

        public double Height { get; set; } = double.NaN;

        public double ActualWidth { get; set; }

        public double ActualHeight { get; set; }

        public FakeWindowState WindowState { get; set; } = FakeWindowState.Normal;

        public bool CancelClose { get; set; }

        public bool IsClosed { get; private set; }

        public int CloseCount { get; private set; }

        public void Close()
        {
            CloseCount++;
            if (!CancelClose)
            {
                IsClosed = true;
            }
        }
    }

    private sealed class FakeDisposedWindow
    {
        private bool _disposed;

        public bool CancelClose { get; set; }

        public bool DisposedStateForTest => _disposed;

        public int CloseCount { get; private set; }

        public void Close()
        {
            CloseCount++;
            if (!CancelClose)
            {
                _disposed = true;
            }
        }
    }

    private sealed class FakeActivatableWindow
    {
        public bool IsActive { get; private set; }

        public int ActivatedCount { get; private set; }

        public int DeactivatedCount { get; private set; }

        internal void HandleActivate(bool isActive)
        {
            if (isActive && !IsActive)
            {
                IsActive = true;
                ActivatedCount++;
            }
            else if (!isActive && IsActive)
            {
                IsActive = false;
                DeactivatedCount++;
            }
        }
    }

    private enum FakeWindowState
    {
        Normal,
        Minimized,
        Maximized
    }

    private sealed class FakePortablePresentationSource : IDisposable
    {
        private object? _rootVisual;

        internal event EventHandler? RenderRequested;

        public object CompositionTarget { get; } = new();

        public object? RootVisual
        {
            get => _rootVisual;
            set
            {
                _rootVisual = value;
                RenderRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
        }
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
