using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Wpf.Interop;
using Silk.NET.Maths;
using Xunit;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using PortableSize = ProGPU.Wpf.Interop.PortableSize;
using PortableVisualLayoutState = ProGPU.Wpf.Interop.PortableVisualLayoutState;
using PortableVisualLayoutStateSource = ProGPU.Wpf.Interop.IPortableVisualLayoutStateSource;
using ProGpuDrawingContext = ProGPU.Scene.DrawingContext;
using ProGpuRenderCommandType = ProGPU.Scene.RenderCommandType;

namespace ProGPU.Wpf.Tests;

[Collection(PortableRenderDataSinkProviderCollection.Name)]
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
            Height = 480,
            Left = 12,
            Top = 24,
            Topmost = true,
            WindowBorder = ProGpuWpfWindowBorder.Hidden
        })
        {
            WpfRenderScheduler = scheduler
        };

        host.SetTitle("Updated");
        host.SetClientSize(321, 123);
        host.SetPosition(32, 48);
        host.SetTopmost(false);
        host.SetWindowBorder(ProGpuWpfWindowBorder.Fixed);

        Assert.Equal("Updated", host.Title);
        Assert.Equal(321, host.Width);
        Assert.Equal(123, host.Height);
        Assert.Equal(32, host.Left);
        Assert.Equal(48, host.Top);
        Assert.False(host.Topmost);
        Assert.Equal(ProGpuWpfWindowBorder.Fixed, host.WindowBorder);
        Assert.Equal(5, scheduler.RequestCount);
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
    public void InvalidateWpfSourceForPortableRenderMarksSourceDirty()
    {
        using var host = new ProGpuWpfWindowHost();
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var root = new object();
        var dirtySource = new object();
        var renderInvalidationCount = 0;
        typeof(ProGpuWpfWindowHost)
            .GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, target);
        host.WpfRootVisual = root;
        target.WpfInvalidationTracker.Attach(root);
        target.WpfInvalidationTracker.ConsumeDirty();
        target.RenderInvalidated += (_, _) => renderInvalidationCount++;

        host.InvalidateWpfSourceForPortableRender(dirtySource);

        Assert.True(host.IsWpfRootVisualDirty);
        Assert.Same(dirtySource, target.LastDirtySource);
        Assert.Equal(1, target.DirtySourceCount);
        Assert.Equal(1, renderInvalidationCount);
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
    public void NativeUpdateRaisesUpdateTick()
    {
        using var host = new ProGpuWpfWindowHost();
        var updateTickCount = 0;
        host.UpdateTick += (_, _) => updateTickCount++;

        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object[] { 0.0 });

        Assert.Equal(1, updateTickCount);
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
    public void ShouldRenderFrameReturnsTrueWhenLogicalSizeChanges()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(
            200,
            100,
            1,
            2,
            3,
            logicalWidth: 100,
            logicalHeight: 50,
            dpiScale: 2.0);
        host.RecordPresentedFrame(frameState);

        var resizedFrameState = new ProGpuWpfFrameState(
            200,
            100,
            1,
            2,
            3,
            logicalWidth: 125,
            logicalHeight: 50,
            dpiScale: 2.0);

        Assert.True(host.ShouldRenderFrame(resizedFrameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenDpiScaleChanges()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(
            200,
            100,
            1,
            2,
            3,
            logicalWidth: 100,
            logicalHeight: 50,
            dpiScale: 2.0);
        host.RecordPresentedFrame(frameState);

        var scaledFrameState = new ProGpuWpfFrameState(
            200,
            100,
            1,
            2,
            3,
            logicalWidth: 100,
            logicalHeight: 50,
            dpiScale: 1.5);

        Assert.True(host.ShouldRenderFrame(scaledFrameState));
    }

    [Fact]
    public void RequestRenderAndWakeNativeLoopSchedulesRenderWithoutWindow()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };

        host.RequestRenderAndWakeNativeLoop();

        Assert.Equal(1, scheduler.RequestCount);
        Assert.Equal(0, host.NativeLoopWakeupCount);
    }

    [Fact]
    public void RequestRenderAndWakeNativeLoopIgnoresDisposedRenderScheduler()
    {
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = new DisposedRenderScheduler()
        };

        host.RequestRenderAndWakeNativeLoop();

        Assert.Equal(0, host.NativeLoopWakeupCount);
    }

    [Fact]
    public void LatePlatformInputAfterDisposeIsIgnored()
    {
        var scheduler = new TestRenderScheduler();
        var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var receivedCount = 0;
        host.InputReceived += (_, _) => receivedCount++;

        host.Dispose();
        RaisePlatformInput(host, new WpfInputEventArgs(WpfInputEventKind.MouseMove, x: 10, y: 20));

        Assert.Equal(0, receivedCount);
        Assert.Equal(0, scheduler.RequestCount);
    }

    [Fact]
    public void GpuHitTestingFailsClosedAfterHostDisposal()
    {
        var host = new ProGpuWpfWindowHost();
        var target = ProGpuWpfCompositionTarget.CreateHeadless();
        SetPrivateField(host, "_target", target);

        host.Dispose();

        object?[] owners = new object?[4];
        object?[] candidates = new object?[4];

        Assert.False(host.HasGpuHitTestCache);
        Assert.False(host.TryHitTestOwner(1, 1, out var owner));
        Assert.Null(owner);
        Assert.False(host.TryHitTestOwners(1, 1, owners, out var ownerCount));
        Assert.Equal(0, ownerCount);
        Assert.False(host.TryQueryHitTestBoundsOwners(0, 0, 10, 10, owners, out var boundsOwnerCount));
        Assert.Equal(0, boundsOwnerCount);
        Assert.False(host.TryGetGpuHitTestCacheSnapshot(out _));
        Assert.False(host.TryQueryHitTestBoundsCandidates(0, 0, 10, 10, candidates, out var boundsCandidateCount));
        Assert.Equal(0, boundsCandidateCount);
        Assert.False(host.TryQueryHitTestEllipseCandidates(0, 0, 10, 10, candidates, out var ellipseCandidateCount));
        Assert.Equal(0, ellipseCandidateCount);
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
    public void ResolveMonitorDpiScaleWithPlatformFallbackUsesNativeScaleWhenMonitorScaleIsUnavailable()
    {
        double dpiScale = ProGpuWpfWindowHost.ResolveMonitorDpiScaleWithPlatformFallback(
            monitorDpiScale: 1.0,
            platformDpiScaleProvider: () => 2.0);

        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 420,
            clientHeight: 840,
            framebufferSize: new Vector2D<int>(420, 840),
            monitorDpiScale: dpiScale);

        Assert.Equal(2.0, dpiScale);
        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(840u, geometry.PixelWidth);
        Assert.Equal(1680u, geometry.PixelHeight);
        Assert.Equal(2.0, geometry.DpiScale);
    }

    [Fact]
    public void ResolveMonitorDpiScaleWithPlatformFallbackKeepsUsableMonitorScale()
    {
        double dpiScale = ProGpuWpfWindowHost.ResolveMonitorDpiScaleWithPlatformFallback(
            monitorDpiScale: 1.5,
            platformDpiScaleProvider: () => 2.0);

        Assert.Equal(1.5, dpiScale);
    }

    [Fact]
    public void ResolveMonitorDpiScaleWithPlatformFallbackIgnoresInvalidNativeScale()
    {
        double dpiScale = ProGpuWpfWindowHost.ResolveMonitorDpiScaleWithPlatformFallback(
            monitorDpiScale: 1.0,
            platformDpiScaleProvider: () => 0.0);

        Assert.Equal(1.0, dpiScale);
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
    public void ResolveRenderSurfaceGeometryUsesFullPhysicalViewportWhenFramebufferHasExtraPixels()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 420,
            clientHeight: 840,
            framebufferSize: new Vector2D<int>(840, 1736),
            monitorDpiScale: 2.0);

        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(840u, geometry.PixelWidth);
        Assert.Equal(1736u, geometry.PixelHeight);
        Assert.Equal(0u, geometry.ViewportX);
        Assert.Equal(0u, geometry.ViewportY);
        Assert.Equal(840u, geometry.ViewportWidth);
        Assert.Equal(1736u, geometry.ViewportHeight);
        Assert.Equal(2.0, geometry.DpiScaleX);
        Assert.Equal(1736.0 / 840.0, geometry.DpiScaleY);
        Assert.Equal((2.0 + (1736.0 / 840.0)) / 2.0, geometry.DpiScale);
    }

    [Fact]
    public void ResolveRenderSurfaceGeometryKeepsFullViewportWhenOnlyFramebufferHeightGrows()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 420,
            clientHeight: 840,
            framebufferSize: new Vector2D<int>(420, 896),
            monitorDpiScale: 1.0);

        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(420u, geometry.PixelWidth);
        Assert.Equal(896u, geometry.PixelHeight);
        Assert.Equal(0u, geometry.ViewportX);
        Assert.Equal(0u, geometry.ViewportY);
        Assert.Equal(420u, geometry.ViewportWidth);
        Assert.Equal(896u, geometry.ViewportHeight);
        Assert.Equal(1.0, geometry.DpiScaleX);
        Assert.Equal(896.0 / 840.0, geometry.DpiScaleY);
        Assert.Equal((1.0 + (896.0 / 840.0)) / 2.0, geometry.DpiScale);
    }

    [Fact]
    public void ResolveRenderSurfaceGeometryUsesFullRetinaViewportForMvpWindow()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);

        Assert.Equal(760u, geometry.LogicalWidth);
        Assert.Equal(560u, geometry.LogicalHeight);
        Assert.Equal(1520u, geometry.PixelWidth);
        Assert.Equal(1120u, geometry.PixelHeight);
        Assert.Equal(0u, geometry.ViewportX);
        Assert.Equal(0u, geometry.ViewportY);
        Assert.Equal(1520u, geometry.ViewportWidth);
        Assert.Equal(1120u, geometry.ViewportHeight);
        Assert.Equal(2.0, geometry.DpiScaleX);
        Assert.Equal(2.0, geometry.DpiScaleY);
        Assert.Equal(2.0, geometry.DpiScale);
    }

    [Fact]
    public void NormalizeInputEventForRenderSurfaceGeometryMapsPhysicalPointerCoordinatesToLogicalDips()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 1000,
            y: 700,
            button: WpfMouseButton.Left,
            modifiers: WpfInputModifiers.Control)
        {
            Handled = true
        };

        var normalized = ProGpuWpfWindowHost.NormalizeInputEventForRenderSurfaceGeometry(
            input,
            geometry,
            inputCoordinatesArePhysical: true);

        Assert.NotSame(input, normalized);
        Assert.Equal(WpfInputEventKind.MouseDown, normalized.Kind);
        Assert.Equal(500.0, normalized.X);
        Assert.Equal(350.0, normalized.Y);
        Assert.Equal(WpfMouseButton.Left, normalized.Button);
        Assert.Equal(WpfInputModifiers.Control, normalized.Modifiers);
        Assert.True(normalized.Handled);
    }

    [Fact]
    public void NormalizeInputEventForRenderSurfaceGeometryMapsUpperLeftPhysicalPointerCoordinatesToLogicalDips()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 320,
            y: 180,
            button: WpfMouseButton.Left);

        var normalized = ProGpuWpfWindowHost.NormalizeInputEventForRenderSurfaceGeometry(
            input,
            geometry,
            inputCoordinatesArePhysical: true);

        Assert.NotSame(input, normalized);
        Assert.Equal(160.0, normalized.X);
        Assert.Equal(90.0, normalized.Y);
        Assert.Equal(WpfMouseButton.Left, normalized.Button);
    }

    [Fact]
    public void NormalizeInputEventForRenderSurfaceGeometryKeepsLogicalPointerCoordinates()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseMove,
            x: 500,
            y: 300);

        var normalized = ProGpuWpfWindowHost.NormalizeInputEventForRenderSurfaceGeometry(
            input,
            geometry,
            inputCoordinatesArePhysical: false);

        Assert.Same(input, normalized);
        Assert.Equal(500.0, normalized.X);
        Assert.Equal(300.0, normalized.Y);
    }

    [Fact]
    public void PointerInputCoordinateExceedsLogicalClientKeepsSilkLogicalRetinaCoordinates()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 500,
            y: 300,
            button: WpfMouseButton.Left);

        Assert.False(ProGpuWpfWindowHost.PointerInputCoordinateExceedsLogicalClient(input, geometry));
    }

    [Fact]
    public void PointerInputCoordinateExceedsLogicalClientDetectsFramebufferCoordinates()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 1000,
            y: 700,
            button: WpfMouseButton.Left);

        Assert.True(ProGpuWpfWindowHost.PointerInputCoordinateExceedsLogicalClient(input, geometry));
    }

    [Fact]
    public void NativeInputCoordinatesLookPhysicalKeepsRetinaPointerInputLogicalInsideClientBounds()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 320,
            y: 180,
            button: WpfMouseButton.Left);

        Assert.False(
            ProGpuWpfWindowHost.NativeInputCoordinatesLookPhysical(
                new Vector2D<int>(760, 560),
                geometry,
                input));
    }

    [Fact]
    public void NativeInputCoordinatesLookPhysicalKeepsSilkLogicalCoordinatesWhenNativeWindowLooksPhysical()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 320,
            y: 180,
            button: WpfMouseButton.Left);

        Assert.False(
            ProGpuWpfWindowHost.NativeInputCoordinatesLookPhysical(
                new Vector2D<int>(1520, 1120),
                geometry,
                input));
    }

    [Fact]
    public void NativeWindowSizeLooksPhysicalDetectsRetinaPhysicalNativeWindow()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);

        Assert.True(
            ProGpuWpfWindowHost.NativeWindowSizeLooksPhysical(
                new Vector2D<int>(1520, 1120),
                geometry));
    }

    [Fact]
    public void NativeInputCoordinatesLookPhysicalDetectsPointerCoordinatesOutsideLogicalBounds()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 1000,
            y: 700,
            button: WpfMouseButton.Left);

        Assert.True(
            ProGpuWpfWindowHost.NativeInputCoordinatesLookPhysical(
                new Vector2D<int>(760, 560),
                geometry,
                input));
    }

    [Fact]
    public void NativeInputCoordinatesLookPhysicalKeepsSingleScalePointerInputLogical()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(760, 560),
            monitorDpiScale: 1.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 320,
            y: 180,
            button: WpfMouseButton.Left);

        Assert.False(
            ProGpuWpfWindowHost.NativeInputCoordinatesLookPhysical(
                new Vector2D<int>(760, 560),
                geometry,
                input));
    }

    [Fact]
    public void NormalizeInputEventForRenderSurfaceGeometryLeavesKeyboardInputUnchanged()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.KeyDown,
            key: "A",
            scanCode: 1,
            modifiers: WpfInputModifiers.Shift);

        var normalized = ProGpuWpfWindowHost.NormalizeInputEventForRenderSurfaceGeometry(
            input,
            geometry,
            inputCoordinatesArePhysical: true);

        Assert.Same(input, normalized);
        Assert.Equal("A", normalized.Key);
        Assert.Equal(1, normalized.ScanCode);
        Assert.Equal(WpfInputModifiers.Shift, normalized.Modifiers);
    }

    [Fact]
    public void ResolveLogicalClientSizeKeepsDipsWhenNativeSizeReportsPhysicalFramebuffer()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(840, 1680),
            framebufferSize: new Vector2D<int>(840, 1680),
            cachedWidth: 420,
            cachedHeight: 840,
            monitorDpiScale: 2.0);

        Assert.Equal(new Vector2D<int>(420, 840), logicalSize);
    }

    [Fact]
    public void ResolveLogicalClientSizeKeepsCachedDipsWhenNativeSizeReportsPhysicalClient()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(840, 1680),
            framebufferSize: new Vector2D<int>(1680, 3360),
            cachedWidth: 420,
            cachedHeight: 840,
            monitorDpiScale: 2.0);

        Assert.Equal(new Vector2D<int>(420, 840), logicalSize);
    }

    [Fact]
    public void ResolveLogicalClientSizeInfersScaleWhenMonitorScaleIsUnavailable()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(840, 1680),
            framebufferSize: new Vector2D<int>(840, 1680),
            cachedWidth: 420,
            cachedHeight: 840,
            monitorDpiScale: 1.0);

        Assert.Equal(new Vector2D<int>(420, 840), logicalSize);
    }

    [Fact]
    public void ResolveLogicalClientSizeInfersScaleWhenNativeClientLooksPhysicalAndFramebufferIsScaledAgain()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(840, 1680),
            framebufferSize: new Vector2D<int>(1680, 3360),
            cachedWidth: 420,
            cachedHeight: 840,
            monitorDpiScale: 1.0);

        Assert.Equal(new Vector2D<int>(420, 840), logicalSize);
    }

    [Fact]
    public void ResolveLogicalClientSizeKeepsNativeSizeWhenSilkAlreadyReportsLogicalDips()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(420, 840),
            framebufferSize: new Vector2D<int>(840, 1680),
            cachedWidth: 420,
            cachedHeight: 840,
            monitorDpiScale: 2.0);

        Assert.Equal(new Vector2D<int>(420, 840), logicalSize);
    }

    [Fact]
    public void ResolveCachedLogicalClientDimensionKeepsRequestedDipsWhenSourceCacheIsPhysical()
    {
        var dimension = ProGpuWpfWindowHost.ResolveCachedLogicalClientDimension(
            portablePresentationSourceDimension: 840,
            requestedLogicalDimension: 420,
            currentClientDimension: 420);

        Assert.Equal(420, dimension);
    }

    [Fact]
    public void ResolveCachedLogicalClientDimensionKeepsPortableSourceDipsWhenRequestedCacheIsPhysical()
    {
        var dimension = ProGpuWpfWindowHost.ResolveCachedLogicalClientDimension(
            portablePresentationSourceDimension: 420,
            requestedLogicalDimension: 840,
            currentClientDimension: 840);

        Assert.Equal(420, dimension);
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
    public void NativeResizeNormalizesPhysicalFramebufferSizeOnHighDpiMonitor()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.False(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0));

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
    }

    [Fact]
    public void NativeResizeKeepsRequestedDipsWhenFramebufferReportIsMissing()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.False(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(0, 0),
            monitorDpiScale: 2.0));

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
    }

    [Fact]
    public void NativeResizeKeepsActualLogicalResizeWhenItIsNotDpiScaleMultiple()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(600, 900),
            new Vector2D<int>(1200, 1800),
            monitorDpiScale: 2.0));

        Assert.Equal(600, host.Width);
        Assert.Equal(900, host.Height);
    }

    [Fact]
    public void NativeResizeKeepsCachedDipsWhenNativeClientSizeIsPhysical()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.False(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(1680, 3360),
            monitorDpiScale: 2.0));

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
    }

    [Fact]
    public void NativeResizeRestoresRequestedDipsWhenPortableSourceCacheWasPhysical()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));
        Assert.True(host.UpdatePortablePresentationSourceClientSize(840, 1680));

        Assert.False(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0));

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
    }

    [Fact]
    public void NativeResizeKeepsCachedDipsWhenNativeClientLooksPhysicalAndFramebufferIsScaledAgain()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.False(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(1680, 3360),
            monitorDpiScale: 1.0));

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
    }

    [Fact]
    public void NativeResizeRestoresRequestedDipsWhenStartupNativeCacheWasPolluted()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        SetPrivateField(host, "_clientWidth", 840);
        SetPrivateField(host, "_clientHeight", 1680);

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(1680, 3360),
            monitorDpiScale: 2.0));

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
        Assert.Equal(2.0, geometry.DpiScale);
    }

    [Fact]
    public void NativeResizeRestoresDeclaredDipsWhenRequestedCacheWasPolluted()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        SetPrivateField(host, "_clientWidth", 840);
        SetPrivateField(host, "_clientHeight", 1680);
        SetPrivateField(host, "_requestedLogicalClientWidth", 840);
        SetPrivateField(host, "_requestedLogicalClientHeight", 1680);

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0));

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
        Assert.Equal(2.0, geometry.DpiScale);
    }

    [Fact]
    public void NativeResizeRestoresRootRenderDipsWhenAllStartupCachesArePhysical()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });
        var root = new TestRootElement();
        root.SetRenderSize(420, 840);
        host.WpfRootVisual = root;

        SetPrivateField(host, "_clientWidth", 840);
        SetPrivateField(host, "_clientHeight", 1680);
        SetPrivateField(host, "_requestedLogicalClientWidth", 840);
        SetPrivateField(host, "_requestedLogicalClientHeight", 1680);
        SetPrivateField(host, "_declaredLogicalClientWidth", 840);
        SetPrivateField(host, "_declaredLogicalClientHeight", 1680);

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0));

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
    }

    [Fact]
    public void NativeResizeDoesNotUseStaleRootRenderSizeForRealLogicalResize()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });
        var root = new TestRootElement();
        root.SetRenderSize(420, 840);
        host.WpfRootVisual = root;

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(600, 900),
            new Vector2D<int>(1200, 1800),
            monitorDpiScale: 2.0));

        Assert.Equal(600, host.Width);
        Assert.Equal(900, host.Height);
    }

    [Fact]
    public void NativeResizeKeepsRealLogicalResizeWhenItMatchesPreviousDpiScaleMultiple()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 760,
            Height = 560
        });
        var root = new TestRootElement();
        root.SetRenderSize(760, 560);
        host.WpfRootVisual = root;
        host.RecordPresentedFrame(new ProGpuWpfFrameState(
            pixelWidth: 1520,
            pixelHeight: 1120,
            sceneChangeVersion: 1,
            retainedWpfChangeVersion: 1,
            flatDrawingChangeVersion: 0,
            logicalWidth: 760,
            logicalHeight: 560,
            dpiScale: 2.0));

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(1520, 1120),
            new Vector2D<int>(3040, 2240),
            monitorDpiScale: 2.0));

        Assert.Equal(1520, host.Width);
        Assert.Equal(1120, host.Height);
    }

    [Fact]
    public void NativeResizeUsesPortablePresentationSourceLogicalCacheWhenHostCacheWasPhysical()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 840,
            Height = 1680
        });
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));
        Assert.True(host.UpdatePortablePresentationSourceClientSize(420, 840));

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(1680, 3360),
            monitorDpiScale: 1.0));

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
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
        Assert.False(host.UpdateClientSizeFromNativeResize(new Vector2D<int>(0, -4)));

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
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
    public void DefaultRenderDataSinkProviderRegistrationScopesTypedProvider()
    {
        using var host = new ProGpuWpfWindowHost();
        var frame = new ProGpuWpfDrawingFrame(new ProGPU.Scene.DrawingVisual(), 100, 50);

        using (IDisposable? registration = host.RegisterRenderDataSinkProvider(frame))
        {
            Assert.NotNull(registration);
            Assert.NotNull(PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory);
        }

        Assert.Null(PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory);
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
    public void PortablePopupHostCreatesAndControlsPopupForBoundOwner()
    {
        var scheduler = new TestRenderScheduler();
        using var popupSourceFactory = UsePortablePopupSourceFactory(() => new FakePortablePresentationSource());
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var owner = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        Assert.True(host.TryBindPortablePresentationSource(owner));

        var request = new PortablePopupCreateRequest(
            placementTarget: null,
            ownerPresentationSource: owner,
            ownerHandle: owner.Handle,
            x: 24,
            y: 32,
            isTransparent: false,
            isChildPopup: false);

        Assert.True(host.TryCreatePortablePopup(request, out object? popupSource));
        Assert.NotNull(popupSource);
        Assert.True(host.TrySetPortablePopupSize(popupSource!, 200, 80));
        Assert.True(host.TrySetPortablePopupPosition(popupSource!, 48, 64));
        Assert.True(host.TryShowPortablePopup(popupSource!));
        Assert.True(host.TrySetPortablePopupHitTestable(popupSource!, false));
        Assert.True(host.TryHidePortablePopup(popupSource!));
        Assert.True(host.TryDestroyPortablePopup(popupSource!));
        Assert.False(host.TryShowPortablePopup(popupSource!));
        Assert.True(scheduler.RequestCount > 1);
    }

    [Fact]
    public void PortablePopupInputRoutesToPopupPresentationSourceWithLocalCoordinates()
    {
        var activationService = new TestWindowActivationServiceRegistrar();
        using var activationRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(activationService);
        using var popupSourceFactory = UsePortablePopupSourceFactory(() => new FakePortablePresentationSource());
        using var host = new ProGpuWpfWindowHost();
        var owner = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        Assert.True(host.TryBindPortablePresentationSource(owner));

        var request = new PortablePopupCreateRequest(
            placementTarget: null,
            ownerPresentationSource: owner,
            ownerHandle: owner.Handle,
            x: 20,
            y: 30,
            isTransparent: false,
            isChildPopup: false);

        Assert.True(host.TryCreatePortablePopup(request, out object? popupSource));
        Assert.NotNull(popupSource);
        Assert.True(host.TrySetPortablePopupSize(popupSource!, 100, 80));
        Assert.True(host.TryShowPortablePopup(popupSource!));

        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 25,
            y: 35,
            button: WpfMouseButton.Left);

        Assert.True(host.TryProcessPortablePopupInput(input));

        Assert.True(input.Handled);
        Assert.Equal(1, activationService.PresentationSourceInputCount);
        Assert.Same(popupSource, activationService.LastPresentationSourceInputSource);
        Assert.NotNull(activationService.LastPresentationSourceInput);
        Assert.Equal(5, activationService.LastPresentationSourceInput!.X);
        Assert.Equal(5, activationService.LastPresentationSourceInput.Y);
    }

    [Fact]
    public void PortablePopupInputUsesLogicalCoordinatesAfterDpiScale()
    {
        var activationService = new TestWindowActivationServiceRegistrar();
        using var activationRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(activationService);
        using var popupSourceFactory = UsePortablePopupSourceFactory(() => new FakePortablePresentationSource());
        using var host = new ProGpuWpfWindowHost();
        var owner = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        Assert.True(host.TryBindPortablePresentationSource(owner));
        Assert.True(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));

        var request = new PortablePopupCreateRequest(
            placementTarget: null,
            ownerPresentationSource: owner,
            ownerHandle: owner.Handle,
            x: 20,
            y: 30,
            isTransparent: false,
            isChildPopup: false);

        Assert.True(host.TryCreatePortablePopup(request, out object? popupSource));
        Assert.NotNull(popupSource);
        Assert.True(host.TrySetPortablePopupSize(popupSource!, 100, 80));
        Assert.True(host.TryShowPortablePopup(popupSource!));

        var outsideInput = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 9,
            y: 14,
            button: WpfMouseButton.Left);
        Assert.False(host.TryProcessPortablePopupInput(outsideInput));
        Assert.Equal(0, activationService.PresentationSourceInputCount);

        var insideInput = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 15,
            y: 20,
            button: WpfMouseButton.Left);
        Assert.True(host.TryProcessPortablePopupInput(insideInput));

        Assert.True(insideInput.Handled);
        Assert.Equal(1, activationService.PresentationSourceInputCount);
        Assert.Same(popupSource, activationService.LastPresentationSourceInputSource);
        Assert.NotNull(activationService.LastPresentationSourceInput);
        Assert.Equal(5, activationService.LastPresentationSourceInput!.X);
        Assert.Equal(5, activationService.LastPresentationSourceInput.Y);
    }

    [Fact]
    public void PortablePopupSinkDoesNotReplaceMainWindowInvalidationRoot()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var mainRoot = new object();
        target.WpfInvalidationTracker.Attach(mainRoot);
        target.WpfInvalidationTracker.ConsumeDirty();

        var frame = target.BeginDrawingFrame(
            pixelWidth: 200,
            pixelHeight: 100,
            clearRetainedWpfVisualRoot: false,
            logicalWidth: 200,
            logicalHeight: 100,
            dpiScaleX: 1.0,
            dpiScaleY: 1.0);
        using var sink = new ProGpuRetainedCompositionCommandSink(
            frame,
            target.Context,
            target.Viewport3DTextureCache,
            ProGpuRetainedCompositionLayer.Popup);

        Assert.Same(mainRoot, target.WpfInvalidationTracker.Root);
        Assert.False(target.WpfInvalidationTracker.IsDirty);
        Assert.Empty(target.RetainedWpfVisualRoot.Children);
        Assert.Single(target.PopupRetainedWpfVisualRoot.Children);
    }

    [Fact]
    public void DrawingFrameKeepsPortablePopupLayerAboveMainWpfDrawingLayer()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();

        target.BeginDrawingFrame(
            pixelWidth: 200,
            pixelHeight: 100,
            clearRetainedWpfVisualRoot: true,
            logicalWidth: 200,
            logicalHeight: 100,
            dpiScaleX: 1.0,
            dpiScaleY: 1.0);

        Assert.Equal(3, target.SceneRootVisual.Children.Count);
        Assert.Same(target.RetainedWpfVisualRoot, target.SceneRootVisual.Children[0]);
        Assert.Same(target.RootVisual, target.SceneRootVisual.Children[1]);
        Assert.Same(target.PopupRetainedWpfVisualRoot, target.SceneRootVisual.Children[2]);
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
    public void UpdatePortablePresentationSourceClientSizeCoalescesUnchangedLogicalSize()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();

        Assert.True(host.TryBindPortablePresentationSource(source));

        Assert.True(host.UpdatePortablePresentationSourceClientSize(420, 840));
        Assert.False(host.UpdatePortablePresentationSourceClientSize(420, 840));
        Assert.True(host.UpdatePortablePresentationSourceClientSize(640, 480));

        Assert.Equal(640, source.ClientWidth);
        Assert.Equal(480, source.ClientHeight);
        Assert.Equal(2, source.ClientSizeChangeCount);
        Assert.Equal(2, scheduler.RequestCount);
    }

    [Fact]
    public void SetClientSizeSynchronizesBoundPortablePresentationSourceImmediately()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));

        host.SetClientSize(640, 480);

        Assert.Equal(640, host.Width);
        Assert.Equal(480, host.Height);
        Assert.Equal(640, source.ClientWidth);
        Assert.Equal(480, source.ClientHeight);
        Assert.Equal(1, source.ClientSizeChangeCount);
        Assert.Equal(2, scheduler.RequestCount);
    }

    [Fact]
    public void SetInitialClientSizeCachesLogicalSizeWithoutPortableSourceRelayout()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 1280,
            Height = 800
        })
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));

        host.SetInitialClientSize(420, 840);

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
        Assert.Equal(0, source.ClientSizeChangeCount);
        Assert.Equal(1, scheduler.RequestCount);

        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            host.Width,
            host.Height,
            framebufferSize: new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0);

        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(840u, geometry.PixelWidth);
        Assert.Equal(1680u, geometry.PixelHeight);
        Assert.Equal(2.0, geometry.DpiScale);
    }

    [Fact]
    public void SynchronizePortablePresentationSourceGeometryCachesHighDpiSurfaceGeometry()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        var geometry = new ProGpuWpfWindowHost.RenderSurfaceGeometry(
            LogicalWidth: 420,
            LogicalHeight: 840,
            PixelWidth: 840,
            PixelHeight: 1680,
            DpiScaleX: 2.0,
            DpiScaleY: 2.0,
            DpiScale: 2.0);

        Assert.True(host.TryBindPortablePresentationSource(source));

        Assert.True(host.SynchronizePortablePresentationSourceGeometry(geometry));

        Assert.Equal(geometry, host.LastResolvedRenderSurfaceGeometry);
        Assert.Equal(420, source.ClientWidth);
        Assert.Equal(840, source.ClientHeight);
        Assert.Equal(2.0, source.DpiScaleX);
        Assert.Equal(2.0, source.DpiScaleY);
        Assert.Equal(1, source.ClientSizeChangeCount);
        Assert.Equal(1, source.DeviceScaleChangeCount);
        Assert.Equal(new[] { "DeviceScale", "ClientSize" }, source.CallLog);
        Assert.Equal(2, scheduler.RequestCount);
        Assert.True(host.ForceFullWpfReplayForNextFrame);
    }

    [Fact]
    public void UpdatingPortablePresentationSourceClientSizeForcesFullWpfReplay()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));

        Assert.False(host.ForceFullWpfReplayForNextFrame);

        Assert.True(host.UpdatePortablePresentationSourceClientSize(420, 840));

        Assert.True(host.ForceFullWpfReplayForNextFrame);
    }

    [Fact]
    public void UpdatingPortablePresentationSourceDpiScaleForcesFullWpfReplay()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));

        Assert.False(host.ForceFullWpfReplayForNextFrame);

        Assert.True(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));

        Assert.True(host.ForceFullWpfReplayForNextFrame);
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

    private sealed class DisposedRenderScheduler : IWpfRenderScheduler
    {
        public event EventHandler? RenderRequested
        {
            add { }
            remove { }
        }

        public bool HasPendingRenderRequest => false;

        public void RequestRender()
        {
            throw new ObjectDisposedException(nameof(DisposedRenderScheduler));
        }

        public bool ConsumeRenderRequest()
        {
            return false;
        }

        public void Reset()
        {
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

    private sealed class TestRootElement : PortableVisualLayoutStateSource
    {
        public TestRenderSize RenderSize { get; private set; }

        public void SetRenderSize(double width, double height)
        {
            RenderSize = new TestRenderSize(width, height);
        }

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            state = new PortableVisualLayoutState
            {
                HasRenderSize = true,
                RenderSize = new PortableSize(RenderSize.Width, RenderSize.Height)
            };
            return true;
        }
    }

    private readonly record struct TestRenderSize(double Width, double Height);

    private static IDisposable UsePortablePopupSourceFactory(Func<IPortablePresentationSourceHost> factory)
    {
        var previousFactory = WpfPortablePopupBridge.PortablePresentationSourceFactory;
        WpfPortablePopupBridge.PortablePresentationSourceFactory = (_, _) => factory();
        return new DelegateDisposable(() =>
        {
            WpfPortablePopupBridge.PortablePresentationSourceFactory = previousFactory;
        });
    }

    private sealed class DelegateDisposable : IDisposable
    {
        private Action? _dispose;

        public DelegateDisposable(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }

    private sealed class TestWindowActivationServiceRegistrar : IPortableWindowActivationServiceRegistrar
    {
        public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.PresentationFramework;

        public int PresentationSourceInputCount { get; private set; }

        public object? LastPresentationSourceInputSource { get; private set; }

        public PortableWindowInputEvent? LastPresentationSourceInput { get; private set; }

        public void Register(PortableWindowActivationCallbacks callbacks)
        {
        }

        public bool TryIsCurrentApplicationMainWindow(object window, out bool isMainWindow)
        {
            isMainWindow = false;
            return false;
        }

        public bool TryCloseWindow(object window, out PortableWindowCloseResult result)
        {
            result = PortableWindowCloseResult.NotInvoked;
            return false;
        }

        public bool TrySetActivationState(object window, bool isActive)
        {
            return false;
        }

        public bool TryBeginInvokeInput(object window, Action callback)
        {
            return false;
        }

        public bool TryProcessInputEvent(object window, PortableWindowInputEvent input)
        {
            return false;
        }

        public bool TryProcessPresentationSourceInputEvent(object presentationSource, PortableWindowInputEvent input)
        {
            PresentationSourceInputCount++;
            LastPresentationSourceInputSource = presentationSource;
            LastPresentationSourceInput = input;
            input.Handled = true;
            return true;
        }

        public bool TryFlushDispatcherOperations(object window, string markerPriorityName, TimeSpan? timeout)
        {
            return false;
        }

        public bool TryProcessDragDropEvent(
            object window,
            int dragDropEventKind,
            string[] files,
            string? text,
            double x,
            double y,
            int allowedEffects,
            int acceptedEffect,
            out int result)
        {
            result = 0;
            return false;
        }

        public void Clear()
        {
        }
    }

    private sealed class FakePortablePresentationSource : IPortablePresentationSourceHost
    {
        private object? _rootVisual;

        public event EventHandler? RenderRequested;

        event EventHandler? IPortablePresentationSourceHost.CursorRequested
        {
            add { }
            remove { }
        }

        public object CompositionTarget { get; } = new();

        public IntPtr Handle => IntPtr.Zero;

        public object? RequestedCursor => null;

        public string? RequestedCursorName => null;

        public Func<double, double, object?>? HitTestOverride { get; set; }

        public Func<double, double, object?[]?>? HitTestAllOverride { get; set; }

        public PortableHitTestAllBufferOverride? HitTestAllBufferOverride { get; set; }

        public Func<double, double, double, double, object?[]?>? HitTestBoundsOverride { get; set; }

        public PortableGeometryHitTestBufferOverride? HitTestBoundsBufferOverride { get; set; }

        public Func<double, double, double, double, object?[]?>? HitTestEllipseBoundsOverride { get; set; }

        public PortableGeometryHitTestBufferOverride? HitTestEllipseBoundsBufferOverride { get; set; }

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

        public int DeviceScaleChangeCount { get; private set; }

        public int ClientSizeChangeCount { get; private set; }

        public System.Collections.Generic.List<string> CallLog { get; } = new();

        public bool IsDisposed { get; private set; }

        public void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            DpiScaleX = dpiScaleX;
            DpiScaleY = dpiScaleY;
            DeviceScaleChangeCount++;
            CallLog.Add("DeviceScale");
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetClientSize(double width, double height)
        {
            ClientWidth = width;
            ClientHeight = height;
            ClientSizeChangeCount++;
            CallLog.Add("ClientSize");
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public bool TryUpdateRootVisualClientSize(out double width, out double height)
        {
            width = ClientWidth;
            height = ClientHeight;
            return false;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        typeof(ProGpuWpfWindowHost)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);
    }

    private static void RaisePlatformInput(ProGpuWpfWindowHost host, WpfInputEventArgs args)
    {
        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnPlatformInputReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object?[] { null, args });
    }
}
