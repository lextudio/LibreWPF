using System.Reflection;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class WpfPortableWindowActivationTests
{
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
            ActualHeight = 480.1
        };

        var options = WpfPortableWindowActivation.CreateHostOptions(window, fallback);

        Assert.Equal("Portable WPF", options.Title);
        Assert.Equal(641, options.Width);
        Assert.Equal(481, options.Height);
        Assert.True(options.VSync);
    }

    private sealed class FakeWindow
    {
        public string? Title { get; set; }

        public double Width { get; set; } = double.NaN;

        public double Height { get; set; } = double.NaN;

        public double ActualWidth { get; set; }

        public double ActualHeight { get; set; }

        public int CloseCount { get; private set; }

        public void Close()
        {
            CloseCount++;
        }
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
