using System.Collections.Generic;
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

    [Fact]
    public void HostInputForwardsPayloadToPortableWindowInputHandler()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(
            WpfInputEventKind.KeyDown,
            key: "A",
            scanCode: 42,
            modifiers: WpfInputModifiers.Control);
        RaiseHostInputEvent(host, args);

        Assert.Equal(1, window.InputCount);
        Assert.Same(args, window.LastInputArgs);
        Assert.True(args.Handled);
    }

    [Fact]
    public void HostInputForwardsPayloadToPortableInputFallbackHandler()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableInputFallbackWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(
            WpfInputEventKind.MouseWheel,
            x: 12,
            y: 24,
            deltaY: -1);
        RaiseHostInputEvent(host, args);

        Assert.Equal(1, window.InputCount);
        Assert.Same(args, window.LastInputArgs);
    }

    [Fact]
    public void DisposingActivationStopsInputForwarding()
    {
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = new TestRenderScheduler()
        };
        var window = new FakePortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.Dispose();
        RaiseHostInputEvent(
            host,
            new WpfInputEventArgs(WpfInputEventKind.TextInput, character: 'x'));

        Assert.Equal(0, window.InputCount);
    }

    [Fact]
    public void HostDragDropForwardsPayloadToPortableWindowDropHandler()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfDragDropEventArgs(
            WpfDragDropEventKind.Drop,
            new WpfDragDropData(new[] { "/tmp/a.txt", "/tmp/b.txt" }),
            WpfDragDropEffects.Copy,
            WpfDragDropEffects.None);
        RaiseHostDragDropEvent(host, args);

        Assert.Equal(1, window.DropCount);
        Assert.Same(args, window.LastDropArgs);
        Assert.Equal(WpfDragDropEffects.Move, args.AcceptedEffect);
    }

    [Fact]
    public void HostDragDropForwardsFilesToPortableFileDropFallback()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableFileDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        RaiseHostDragDropEvent(
            host,
            new WpfDragDropEventArgs(
                WpfDragDropEventKind.Drop,
                new WpfDragDropData(new[] { "/tmp/document.txt" })));

        Assert.Equal(1, window.DropCount);
        Assert.Equal(new[] { "/tmp/document.txt" }, window.LastFiles);
    }

    [Fact]
    public void DisposingActivationStopsDragDropForwarding()
    {
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = new TestRenderScheduler()
        };
        var window = new FakePortableDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.Dispose();
        RaiseHostDragDropEvent(
            host,
            new WpfDragDropEventArgs(
                WpfDragDropEventKind.Drop,
                new WpfDragDropData(new[] { "/tmp/ignored.txt" })));

        Assert.Equal(0, window.DropCount);
    }

    private static void RaiseHostWindowEvent(ProGpuWpfWindowHost host, WpfWindowEventKind kind)
    {
        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnPlatformWindowEventReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object?[] { null, new WpfWindowEventArgs(kind) });
    }

    private static void RaiseHostInputEvent(ProGpuWpfWindowHost host, WpfInputEventArgs args)
    {
        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnPlatformInputReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object?[] { null, args });
    }

    private static void RaiseHostDragDropEvent(ProGpuWpfWindowHost host, WpfDragDropEventArgs args)
    {
        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnPlatformDragDropReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object?[] { null, args });
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

    private sealed class FakePortableInputWindow
    {
        public int InputCount { get; private set; }

        public WpfInputEventArgs? LastInputArgs { get; private set; }

        private void OnPortableInput(WpfInputEventArgs e)
        {
            InputCount++;
            LastInputArgs = e;
            e.Handled = true;
        }
    }

    private sealed class FakePortableInputFallbackWindow
    {
        public int InputCount { get; private set; }

        public WpfInputEventArgs? LastInputArgs { get; private set; }

        internal void HandlePortableInput(WpfInputEventArgs e)
        {
            InputCount++;
            LastInputArgs = e;
        }
    }

    private sealed class FakePortableDropWindow
    {
        public int DropCount { get; private set; }

        public WpfDragDropEventArgs? LastDropArgs { get; private set; }

        private void OnPortableDrop(WpfDragDropEventArgs e)
        {
            DropCount++;
            LastDropArgs = e;
            e.AcceptedEffect = WpfDragDropEffects.Move;
        }
    }

    private sealed class FakePortableFileDropWindow
    {
        public int DropCount { get; private set; }

        public IReadOnlyList<string> LastFiles { get; private set; } = Array.Empty<string>();

        internal void OnPortableFileDrop(IReadOnlyList<string> files)
        {
            DropCount++;
            LastFiles = files;
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
