using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Platform;
using Silk.NET.Maths;
using Xunit;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using ProGpuDrawingContext = ProGPU.Scene.DrawingContext;
using ProGpuRenderCommandType = ProGPU.Scene.RenderCommandType;

namespace ProGPU.Wpf.Tests;

public sealed class ProGpuWpfWindowHostTests
{
    [Fact]
    public void SetTitleAndClientSizeUpdateCachedWindowStateBeforeNativeWindowExists()
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

        host.SetTitle("Updated");
        host.SetClientSize(321, 123);

        Assert.Equal("Updated", host.Title);
        Assert.Equal(321, host.Width);
        Assert.Equal(123, host.Height);
        Assert.Equal(2, scheduler.RequestCount);
    }

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
        Assert.IsType<ProcessWpfMessageBoxService>(services.MessageBoxes);
        Assert.IsType<SilkNetWpfMonitorService>(services.Monitors);
        Assert.IsType<ThreadPoolWpfTimerService>(services.Timers);
        Assert.IsType<SilkNetWpfWindowDecorationService>(services.WindowDecorations);
        Assert.IsType<SilkNetWpfWindowEventService>(services.WindowEvents);
        Assert.IsType<DispatcherWpfRenderScheduler>(host.WpfRenderScheduler);
    }

    [Fact]
    public void DefaultWindowOptionsUseEventDrivenNativeLoop()
    {
        var options = new ProGpuWpfWindowOptions();

        Assert.True(options.IsEventDriven);
    }

    [Fact]
    public void SetCursorReturnsFalseBeforeWindowIsCreated()
    {
        using var host = new ProGpuWpfWindowHost();

        Assert.False(host.SetCursor(WpfCursor.Hand));
    }

    [Fact]
    public void TryBeginDragMoveReturnsFalseBeforeWindowIsCreated()
    {
        using var host = new ProGpuWpfWindowHost();

        Assert.False(host.TryBeginDragMove());
    }

    [Fact]
    public void SettingPlatformServicesRebuildsDefaultRenderScheduler()
    {
        using var host = new ProGpuWpfWindowHost();
        var originalScheduler = host.WpfRenderScheduler;

        host.PlatformServices = new CrossPlatformWpfPlatformServices();

        Assert.IsType<DispatcherWpfRenderScheduler>(host.WpfRenderScheduler);
        Assert.NotSame(originalScheduler, host.WpfRenderScheduler);
    }

    [Fact]
    public void CustomRenderSchedulerIsPreservedWhenPlatformServicesChange()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };

        host.PlatformServices = new CrossPlatformWpfPlatformServices();

        Assert.Same(scheduler, host.WpfRenderScheduler);
    }

    [Fact]
    public void RenderSchedulerWakeupIsObservedByHost()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var wakeupCount = 0;
        host.RenderWakeupRequested += (_, _) => wakeupCount++;

        scheduler.RequestRender();

        Assert.Equal(1, host.RenderSchedulerWakeupCount);
        Assert.Equal(1, wakeupCount);
    }

    [Fact]
    public void NativeLoopWakeupInvokesContinueEventsAndCountsSuccessfulRequests()
    {
        using var host = new ProGpuWpfWindowHost();
        var continueEventsCount = 0;

        Assert.True(host.TryRequestNativeLoopWakeup(() => continueEventsCount++));
        Assert.True(host.TryRequestNativeLoopWakeup(() => continueEventsCount++));

        Assert.Equal(2, continueEventsCount);
        Assert.Equal(2, host.NativeLoopWakeupCount);
    }

    [Fact]
    public void NativeLoopWakeupReturnsFalseWhenContinueEventsFails()
    {
        using var host = new ProGpuWpfWindowHost();

        Assert.False(host.TryRequestNativeLoopWakeup(() => throw new InvalidOperationException()));

        Assert.Equal(0, host.NativeLoopWakeupCount);
    }

    [Fact]
    public void ReplacingRenderSchedulerDisconnectsPreviousWakeupSource()
    {
        var firstScheduler = new TestRenderScheduler();
        var secondScheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = firstScheduler
        };

        host.WpfRenderScheduler = secondScheduler;

        firstScheduler.RequestRender();
        Assert.Equal(0, host.RenderSchedulerWakeupCount);

        secondScheduler.RequestRender();
        Assert.Equal(1, host.RenderSchedulerWakeupCount);
    }

    [Fact]
    public void DisposingHostDisconnectsRenderSchedulerWakeups()
    {
        var scheduler = new TestRenderScheduler();
        var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        host.Dispose();

        scheduler.RequestRender();

        Assert.Equal(0, host.RenderSchedulerWakeupCount);
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueBeforeAnyFrameIsPresented()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);

        Assert.True(host.ShouldRenderFrame(frameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsFalseWhenPresentedFrameStateIsUnchanged()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);

        host.RecordPresentedFrame(frameState);

        Assert.True(host.HasPresentedFrame);
        Assert.Equal(frameState, host.LastPresentedFrameState);
        Assert.False(host.ShouldRenderFrame(frameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenSchedulerHasPendingRequest()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        host.RecordPresentedFrame(frameState);

        scheduler.RequestRender();

        Assert.True(host.ShouldRenderFrame(frameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenNativeVersionChanges()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        host.RecordPresentedFrame(frameState);

        var changedFrameState = new ProGpuWpfFrameState(100, 50, 1, 4, 3);

        Assert.True(host.ShouldRenderFrame(changedFrameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenRetainedBranchTargetabilityChanges()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        host.RecordPresentedFrame(frameState);

        var changedFrameState = new ProGpuWpfFrameState(
            100,
            50,
            1,
            2,
            3,
            retainedBranchInvalidationCount: 1,
            retainedBranchDirtySourceCount: 1,
            retainedBranchMappedSourceCount: 1,
            retainedBranchUnmappedSourceCount: 0,
            retainedBranchSharedWithCleanSourceVisualCount: 1,
            retainedBranchReplayTargetConflictCount: 1,
            retainedBranchInvalidationUsedFallback: true);

        Assert.True(host.ShouldRenderFrame(changedFrameState));
        Assert.True(changedFrameState.RetainedBranchInvalidationUsedFallback);
        Assert.Equal(1, changedFrameState.RetainedBranchSharedWithCleanSourceVisualCount);
        Assert.Equal(1, changedFrameState.RetainedBranchReplayTargetConflictCount);
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenPixelSizeChanges()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        host.RecordPresentedFrame(frameState);

        var resizedFrameState = new ProGpuWpfFrameState(200, 100, 1, 2, 3);

        Assert.True(host.ShouldRenderFrame(resizedFrameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenCoalescingIsDisabled()
    {
        using var host = new ProGpuWpfWindowHost
        {
            EnableFrameCoalescing = false
        };
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        host.RecordPresentedFrame(frameState);

        Assert.True(host.ShouldRenderFrame(frameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenExplicitFrameCallbacksAreRegistered()
    {
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        using var drawHost = new ProGpuWpfWindowHost();
        drawHost.RecordPresentedFrame(frameState);
        drawHost.Draw = (_, _) => { };

        using var wpfDrawHost = new ProGpuWpfWindowHost();
        wpfDrawHost.RecordPresentedFrame(frameState);
        wpfDrawHost.WpfDraw = (_, _) => { };

        using var renderHost = new ProGpuWpfWindowHost();
        renderHost.RecordPresentedFrame(frameState);
        renderHost.Render += (_, _) => { };

        Assert.True(drawHost.ShouldRenderFrame(frameState));
        Assert.True(wpfDrawHost.ShouldRenderFrame(frameState));
        Assert.True(renderHost.ShouldRenderFrame(frameState));
    }

    [Fact]
    public void ResolveRenderSurfaceGeometryScalesLogicalFramebufferOnHighDpiMonitor()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 420,
            clientHeight: 840,
            framebufferSize: new Vector2D<int>(420, 840),
            monitorDpiScale: 2.0);

        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(840u, geometry.PixelWidth);
        Assert.Equal(1680u, geometry.PixelHeight);
        Assert.Equal(2.0, geometry.DpiScaleX);
        Assert.Equal(2.0, geometry.DpiScaleY);
        Assert.Equal(2.0, geometry.DpiScale);
    }

    [Fact]
    public void ResolveRenderSurfaceGeometryKeepsReportedPhysicalFramebuffer()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 420,
            clientHeight: 840,
            framebufferSize: new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0);

        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(840u, geometry.PixelWidth);
        Assert.Equal(1680u, geometry.PixelHeight);
        Assert.Equal(2.0, geometry.DpiScaleX);
        Assert.Equal(2.0, geometry.DpiScaleY);
        Assert.Equal(2.0, geometry.DpiScale);
    }

    [Fact]
    public void NativeResizeCorrectsStalePhysicalClientSizeBeforeTargetLoad()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 840,
            Height = 1680
        });

        Assert.True(host.UpdateClientSizeFromNativeResize(new Vector2D<int>(420, 840)));

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);

        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            host.Width,
            host.Height,
            framebufferSize: new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0);
        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(840u, geometry.PixelWidth);
        Assert.Equal(1680u, geometry.PixelHeight);
        Assert.Equal(2.0, geometry.DpiScaleX);
        Assert.Equal(2.0, geometry.DpiScaleY);
    }

    [Fact]
    public void NativeResizeIgnoresZeroSizeAndReturnsFalseForUnchangedClientSize()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.False(host.UpdateClientSizeFromNativeResize(new Vector2D<int>(420, 840)));
        Assert.True(host.UpdateClientSizeFromNativeResize(new Vector2D<int>(0, -4)));

        Assert.Equal(1, host.Width);
        Assert.Equal(1, host.Height);
    }

    [Fact]
    public void ProcessDispatcherQueueRunsQueuedPlatformCallbacks()
    {
        var dispatcher = new TestDispatcherService(raiseWorkAvailableOnPost: false);
        using var host = new ProGpuWpfWindowHost
        {
            PlatformServices = CreatePlatformServices(dispatcher)
        };
        var ran = false;

        host.PlatformServices.Dispatcher.Post(() => ran = true);

        Assert.True(host.ProcessDispatcherQueue());
        Assert.True(ran);
    }

    [Fact]
    public void DispatcherWorkAvailableProcessesQueuedPlatformCallbacksOnOwnerThread()
    {
        var dispatcher = new TestDispatcherService(raiseWorkAvailableOnPost: true);
        using var host = new ProGpuWpfWindowHost
        {
            PlatformServices = CreatePlatformServices(dispatcher)
        };
        var ran = false;

        dispatcher.Post(() => ran = true, WpfDispatcherPriority.Render);

        Assert.True(ran);
        Assert.Equal(1, host.DispatcherWakeupCount);
        Assert.False(host.ProcessDispatcherQueue());
    }

    [Fact]
    public void DispatcherWorkAvailableFromWorkerThreadWaitsForOwnerThreadPump()
    {
        using var host = new ProGpuWpfWindowHost
        {
            PlatformServices = new CrossPlatformWpfPlatformServices()
        };
        var dispatcher = Assert.IsType<QueuedWpfDispatcherService>(host.PlatformServices.Dispatcher);
        var ran = false;
        var worker = new Thread(() => dispatcher.Post(() => ran = true));

        worker.Start();
        worker.Join();

        Assert.False(ran);
        Assert.Equal(1, host.DispatcherWakeupCount);
        Assert.Equal(0, host.NativeLoopWakeupCount);
        Assert.True(host.ProcessDispatcherQueue());
        Assert.True(ran);
    }

    [Fact]
    public void ReplacingPlatformServicesDisconnectsPreviousDispatcherWakeupSource()
    {
        var firstDispatcher = new TestDispatcherService(raiseWorkAvailableOnPost: true);
        var secondDispatcher = new TestDispatcherService(raiseWorkAvailableOnPost: true);
        using var host = new ProGpuWpfWindowHost
        {
            PlatformServices = CreatePlatformServices(firstDispatcher)
        };
        host.PlatformServices = CreatePlatformServices(secondDispatcher);
        var firstRan = false;
        var secondRan = false;

        firstDispatcher.Post(() => firstRan = true);
        secondDispatcher.Post(() => secondRan = true);

        Assert.False(firstRan);
        Assert.True(secondRan);
        Assert.Equal(1, host.DispatcherWakeupCount);
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
        host.RenderDataSinkProviderRegistrationFactory = (drawingFrame, _) =>
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

    [Fact]
    public void TryBindPortablePresentationSourceMirrorsRootIntoHost()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };

        var bound = host.TryBindPortablePresentationSource(source);

        Assert.True(bound);
        Assert.Same(source, host.PortablePresentationSource);
        Assert.NotNull(host.PortablePresentationSourceBridge);
        Assert.Same(source.RootVisual, host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void ReplacingPortablePresentationSourceUnsubscribesPreviousSource()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var first = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        var second = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };

        Assert.True(host.TryBindPortablePresentationSource(first));
        Assert.True(host.TryBindPortablePresentationSource(second));
        var requestCountAfterReplacement = scheduler.RequestCount;

        first.RootVisual = new object();

        Assert.Same(second, host.PortablePresentationSource);
        Assert.Same(second.RootVisual, host.WpfRootVisual);
        Assert.Equal(requestCountAfterReplacement, scheduler.RequestCount);
    }

    [Fact]
    public void UpdatePortablePresentationSourceDpiScaleCoalescesUnchangedScale()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();

        Assert.True(host.TryBindPortablePresentationSource(source));

        Assert.True(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));
        Assert.False(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));
        Assert.True(host.UpdatePortablePresentationSourceDpiScale(2.5, 2.0));

        Assert.Equal(2.5, source.DpiScaleX);
        Assert.Equal(2.0, source.DpiScaleY);
        Assert.Equal(2, source.DeviceScaleChangeCount);
        Assert.Equal(2, scheduler.RequestCount);
    }

    [Fact]
    public void DisposingHostDetachesPortablePresentationSource()
    {
        var scheduler = new TestRenderScheduler();
        var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        Assert.True(host.TryBindPortablePresentationSource(source));

        host.Dispose();
        var requestCountAfterDispose = scheduler.RequestCount;
        source.RootVisual = new object();

        Assert.Null(host.WpfRootVisual);
        Assert.Equal(requestCountAfterDispose, scheduler.RequestCount);
        Assert.False(source.IsDisposed);
    }

    private static CrossPlatformWpfPlatformServices CreatePlatformServices(IWpfDispatcherService dispatcher)
    {
        return new CrossPlatformWpfPlatformServices(
            new ProcessWpfLauncher(),
            new SilkNetWpfMonitorService(),
            new ProcessWpfClipboard(),
            new SilkNetWpfCursorService(),
            dispatcher,
            new ProcessWpfFileDialogService());
    }

    private sealed class TestDispatcherService : IWpfDispatcherService
    {
        private readonly Queue<TestDispatcherOperation> _operations = new();
        private readonly bool _raiseWorkAvailableOnPost;

        public TestDispatcherService(bool raiseWorkAvailableOnPost)
        {
            _raiseWorkAvailableOnPost = raiseWorkAvailableOnPost;
        }

        public event EventHandler? WorkAvailable;

        public bool CheckAccess()
        {
            return true;
        }

        public IWpfDispatcherOperation Post(Action callback, WpfDispatcherPriority priority = WpfDispatcherPriority.Normal)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var operation = new TestDispatcherOperation(callback, priority);
            _operations.Enqueue(operation);
            if (_raiseWorkAvailableOnPost)
            {
                WorkAvailable?.Invoke(this, EventArgs.Empty);
            }

            return operation;
        }

        public bool ProcessPending()
        {
            var processed = false;
            while (_operations.Count > 0)
            {
                var operation = _operations.Dequeue();
                if (operation.IsCanceled)
                {
                    continue;
                }

                operation.Invoke();
                operation.MarkCompleted();
                processed = true;
            }

            return processed;
        }
    }

    private sealed class TestDispatcherOperation : IWpfDispatcherOperation
    {
        private readonly Action _callback;

        public TestDispatcherOperation(Action callback, WpfDispatcherPriority priority)
        {
            _callback = callback;
            Priority = priority;
        }

        public WpfDispatcherPriority Priority { get; }

        public bool IsCanceled { get; private set; }

        public bool IsCompleted { get; private set; }

        public bool Cancel()
        {
            if (IsCanceled || IsCompleted)
            {
                return false;
            }

            IsCanceled = true;
            return true;
        }

        public void Dispose()
        {
            Cancel();
        }

        public void Invoke()
        {
            _callback();
        }

        public void MarkCompleted()
        {
            IsCompleted = true;
        }
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

        public double DpiScaleX { get; private set; } = 1.0;

        public double DpiScaleY { get; private set; } = 1.0;

        public int DeviceScaleChangeCount { get; private set; }

        public bool IsDisposed { get; private set; }

        internal void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            DpiScaleX = dpiScaleX;
            DpiScaleY = dpiScaleY;
            DeviceScaleChangeCount++;
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
