using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Platform;
using Xunit;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using ProGpuDrawingContext = ProGPU.Scene.DrawingContext;
using ProGpuRenderCommandType = ProGPU.Scene.RenderCommandType;

namespace ProGPU.Wpf.Tests;

public sealed class ProGpuWpfWindowHostTests
{
    [Fact]
    public void SettingWpfRootVisualRequestsRender()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var root = new object();

        host.WpfRootVisual = root;

        Assert.Same(root, host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
        Assert.True(scheduler.HasPendingRenderRequest);
    }

    [Fact]
    public void SettingSameWpfRootVisualDoesNotRequestRenderAgain()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var root = new object();

        host.WpfRootVisual = root;
        host.WpfRootVisual = root;

        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void DefaultPlatformServicesUseCrossPlatformLauncherBoundary()
    {
        using var host = new ProGpuWpfWindowHost();

        var services = Assert.IsType<CrossPlatformWpfPlatformServices>(host.PlatformServices);
        Assert.IsType<ProcessWpfClipboard>(services.Clipboard);
        Assert.IsType<SilkNetWpfCursorService>(services.Cursors);
        Assert.IsType<QueuedWpfDispatcherService>(services.Dispatcher);
        Assert.IsType<SilkNetWpfDragDropService>(services.DragDrop);
        Assert.IsType<ProcessWpfFileDialogService>(services.FileDialogs);
        Assert.IsType<SilkNetWpfInputService>(services.Input);
        Assert.IsType<ProcessWpfLauncher>(services.Launcher);
        Assert.IsType<SilkNetWpfMonitorService>(services.Monitors);
        Assert.IsType<ThreadPoolWpfTimerService>(services.Timers);
        Assert.IsType<SilkNetWpfWindowEventService>(services.WindowEvents);
    }

    [Fact]
    public void SetCursorReturnsFalseBeforeWindowIsCreated()
    {
        using var host = new ProGpuWpfWindowHost();

        Assert.False(host.SetCursor(WpfCursor.Hand));
    }

    [Fact]
    public void ProcessDispatcherQueueRunsQueuedPlatformCallbacks()
    {
        using var host = new ProGpuWpfWindowHost
        {
            PlatformServices = new CrossPlatformWpfPlatformServices()
        };
        var ran = false;

        host.PlatformServices.Dispatcher.Post(() => ran = true);

        Assert.True(host.ProcessDispatcherQueue());
        Assert.True(ran);
    }

    [Fact]
    public void InvokeSourceDrawRunsWpfDrawAndCapturesResult()
    {
        using var host = new ProGpuWpfWindowHost();
        var nativeContext = new ProGpuDrawingContext();
        using var mediaContext = new MediaDrawingContext(nativeContext);
        using var sourceContext = new WpfCompositionDrawingContext(
            new ProGpuCompositionCommandSink(mediaContext));
        var args = new ProGpuWpfFrameEventArgs(mediaContext, 100, 50, 0.016, 2);

        host.WpfDraw = (context, frame) =>
        {
            Assert.Same(args, frame);
            context.DrawRectangle(Brushes.Red, null, new Rect(1, 2, 3, 4));
            context.PushOpacity(0.5);
        };

        host.InvokeSourceDraw(sourceContext, args);

        Assert.Equal(new WpfCompositionDrawingContextResult(3, 3, 0), host.LastSourceDrawingResult);
        Assert.Equal(new[]
        {
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.PushOpacity,
            ProGpuRenderCommandType.PopOpacity
        }, nativeContext.Commands.Select(command => command.Type).ToArray());
    }

    [Fact]
    public void FrameEventArgsCanExposeActiveDrawingFrame()
    {
        var frame = new ProGpuWpfDrawingFrame(new ProGPU.Scene.DrawingVisual(), 100, 50);
        using var mediaContext = frame.OpenDrawingContext();

        var args = new ProGpuWpfFrameEventArgs(mediaContext, 100, 50, 0.016, 2, frame);

        Assert.Same(frame, args.DrawingFrame);
    }

    [Fact]
    public void InvokeSourceDrawResetsResultWhenNoSourceCallbackIsRegistered()
    {
        using var host = new ProGpuWpfWindowHost();
        var nativeContext = new ProGpuDrawingContext();
        using var mediaContext = new MediaDrawingContext(nativeContext);
        using var sourceContext = new WpfCompositionDrawingContext(
            new ProGpuCompositionCommandSink(mediaContext));
        var args = new ProGpuWpfFrameEventArgs(mediaContext, 100, 50, 0.016, 2);

        sourceContext.DrawVideo(new object(), new Rect(0, 0, 1, 1));
        host.InvokeSourceDraw(sourceContext, args);

        Assert.Equal(default, host.LastSourceDrawingResult);
        Assert.Empty(nativeContext.Commands);
    }

    [Fact]
    public void DefaultRenderDataSinkProviderRegistrationReturnsNullWhenProviderIsAbsent()
    {
        using var host = new ProGpuWpfWindowHost();
        var frame = new ProGpuWpfDrawingFrame(new ProGPU.Scene.DrawingVisual(), 100, 50);

        using IDisposable? registration = host.RegisterRenderDataSinkProvider(frame);

        Assert.Null(registration);
    }

    [Fact]
    public void RenderDataSinkProviderRegistrationFactoryCanBeScopedAndDisposed()
    {
        using var host = new ProGpuWpfWindowHost();
        var frame = new ProGpuWpfDrawingFrame(new ProGPU.Scene.DrawingVisual(), 100, 50);
        var registration = new TestRegistration();
        ProGpuWpfDrawingFrame? capturedFrame = null;
        host.RenderDataSinkProviderRegistrationFactory = drawingFrame =>
        {
            capturedFrame = drawingFrame;
            return registration;
        };

        using (host.RegisterRenderDataSinkProvider(frame))
        {
            Assert.Same(frame, capturedFrame);
            Assert.False(registration.IsDisposed);
        }

        Assert.True(registration.IsDisposed);
    }

    private sealed class TestRenderScheduler : IWpfRenderScheduler
    {
        public event EventHandler? RenderRequested;

        public bool HasPendingRenderRequest { get; private set; }

        public int RequestCount { get; private set; }

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

    private sealed class TestRegistration : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
