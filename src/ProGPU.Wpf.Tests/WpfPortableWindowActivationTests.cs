using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class WpfPortableWindowActivationTests
{
    [Fact]
    public void PresentationFrameworkActivationRegistrationUsesTypedInteropBeforeReflectionFallback()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);

        var registered = WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation(
            service.SourceAssembly);

        Assert.True(registered);
        Assert.Equal(1, service.RegisterCount);
        Assert.NotNull(service.Callbacks);
        Assert.NotNull(service.Callbacks.Activate);
        Assert.NotNull(service.Callbacks.Show);
        Assert.NotNull(service.Callbacks.Hide);
        Assert.NotNull(service.Callbacks.SetWindowState);
        Assert.NotNull(service.Callbacks.SetTitle);
        Assert.NotNull(service.Callbacks.SetClientSize);
        Assert.NotNull(service.Callbacks.SetPosition);
        Assert.NotNull(service.Callbacks.SetTopmost);
        Assert.NotNull(service.Callbacks.SetWindowBorder);
        Assert.NotNull(service.Callbacks.Close);
        Assert.NotNull(service.Callbacks.Run);
        Assert.NotNull(service.Callbacks.Dispose);
        Assert.NotNull(service.Callbacks.DragMove);
        Assert.NotNull(service.Callbacks.GetHandle);
    }

    [Fact]
    public void ClipboardRegistrationUsesTypedInteropServiceBeforeReflectionFallback()
    {
        var service = new TestClipboardServiceRegistrar();
        using var registration = PortableWpfServiceRegistry.RegisterClipboardService(service);

        var registered = WpfPortableWindowActivation.TryRegisterPresentationCoreClipboardService(service.SourceAssembly);

        Assert.True(registered);
        Assert.Equal(1, service.RegisterCount);
        Assert.NotNull(service.GetText);
        Assert.NotNull(service.SetText);
    }

    [Fact]
    public void PresentationFrameworkServiceRegistrationUsesTypedInteropBeforeReflectionFallback()
    {
        var launcherService = new TestLauncherServiceRegistrar();
        var messageBoxService = new TestMessageBoxServiceRegistrar();
        var fileDialogService = new TestFileDialogServiceRegistrar();
        using var launcherRegistration = PortableWpfServiceRegistry.RegisterLauncherService(launcherService);
        using var messageBoxRegistration = PortableWpfServiceRegistry.RegisterMessageBoxService(messageBoxService);
        using var fileDialogRegistration = PortableWpfServiceRegistry.RegisterFileDialogService(fileDialogService);

        var launcherRegistered = WpfPortableWindowActivation.TryRegisterPresentationFrameworkLauncherService(
            launcherService.SourceAssembly);
        var messageBoxRegistered = WpfPortableWindowActivation.TryRegisterPresentationFrameworkMessageBoxService(
            messageBoxService.SourceAssembly);
        var fileDialogRegistered = WpfPortableWindowActivation.TryRegisterPresentationFrameworkFileDialogService(
            fileDialogService.SourceAssembly);

        Assert.True(launcherRegistered);
        Assert.True(messageBoxRegistered);
        Assert.True(fileDialogRegistered);
        Assert.Equal(1, launcherService.RegisterCount);
        Assert.Equal(1, messageBoxService.RegisterCount);
        Assert.Equal(1, fileDialogService.RegisterCount);
        Assert.NotNull(launcherService.Launch);
        Assert.NotNull(messageBoxService.Show);
        Assert.NotNull(fileDialogService.ShowDialog);
    }

    [Fact]
    public void AttachUsesTypedMediaContextRenderInteropServiceBeforeReflectionFallback()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();
        var service = new TestMediaContextRenderServiceRegistrar();
        using var registration = PortableWpfServiceRegistry.RegisterMediaContextRenderService(service);

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);
        Assert.Equal(1, service.RegisterCount);
        Assert.NotNull(service.RequestRender);
        Assert.False(service.LastRegistration?.IsDisposed);

        activation.Dispose();

        Assert.True(service.LastRegistration?.IsDisposed);
    }

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
        activation.SetPosition(31.4, 47.6);
        activation.SetTopmost(true);

        Assert.Equal("Updated", host.Title);
        Assert.Equal(321, host.Width);
        Assert.Equal(241, host.Height);
        Assert.Equal(31, host.Left);
        Assert.Equal(48, host.Top);
        Assert.True(host.Topmost);
        Assert.True(scheduler.RequestCount >= 6);
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
    public void SetWindowBorderMapsLiveResizeModeAndWindowStyleChanges()
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

        activation.SetWindowBorder(FakeResizeMode.NoResize, FakeWindowStyle.SingleBorderWindow);

        Assert.Equal(ProGpuWpfWindowBorder.Fixed, host.WindowBorder);

        activation.SetWindowBorder(FakeResizeMode.CanResizeWithGrip, FakeWindowStyle.None);

        Assert.Equal(ProGpuWpfWindowBorder.Hidden, host.WindowBorder);
        Assert.True(scheduler.RequestCount >= 3);
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
            Left = 1,
            Top = 2,
            Topmost = false,
            WindowBorder = ProGpuWpfWindowBorder.Hidden,
            VSync = true
        };
        var window = new FakeWindow
        {
            Title = "Portable WPF",
            Width = 640.2,
            Height = double.NaN,
            ActualHeight = 480.1,
            Left = 10.4,
            Top = 20.6,
            Topmost = true,
            WindowState = FakeWindowState.Minimized,
            ResizeMode = FakeResizeMode.CanResizeWithGrip
        };

        var options = WpfPortableWindowActivation.CreateHostOptions(window, fallback);

        Assert.Equal("Portable WPF", options.Title);
        Assert.Equal(641, options.Width);
        Assert.Equal(481, options.Height);
        Assert.Equal(10, options.Left);
        Assert.Equal(21, options.Top);
        Assert.True(options.Topmost);
        Assert.True(options.VSync);
        Assert.Equal(ProGpuWpfWindowState.Minimized, options.WindowState);
        Assert.Equal(ProGpuWpfWindowBorder.Resizable, options.WindowBorder);
    }

    [Fact]
    public void CreateHostOptionsMapsWindowStyleNoneToHiddenBorder()
    {
        var window = new FakeWindow
        {
            ResizeMode = FakeResizeMode.CanResize,
            WindowStyle = FakeWindowStyle.None
        };

        var options = WpfPortableWindowActivation.CreateHostOptions(window);

        Assert.Equal(ProGpuWpfWindowBorder.Hidden, options.WindowBorder);
    }

    [Fact]
    public void TryAttachSynchronizesInitialWindowShapeBeforeFirstRender()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Title = "Fallback",
            Width = 1280,
            Height = 800
        })
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeWindow
        {
            Title = "Portable WPF",
            Width = 420,
            Height = 840,
            Left = 32,
            Top = 48,
            Topmost = true,
            WindowState = FakeWindowState.Normal,
            ResizeMode = FakeResizeMode.NoResize
        };
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);
        Assert.Equal("Portable WPF", host.Title);
        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
        Assert.Equal(32, host.Left);
        Assert.Equal(48, host.Top);
        Assert.True(host.Topmost);
        Assert.Equal(ProGpuWpfWindowBorder.Fixed, host.WindowBorder);
        Assert.Equal(0, source.ClientSizeChangeCount);

        activation.SetClientSize(window.Width, window.Height);

        Assert.Equal(420, source.ClientWidth);
        Assert.Equal(840, source.ClientHeight);
        Assert.Equal(1, source.ClientSizeChangeCount);
        Assert.True(scheduler.RequestCount >= 1);
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
    public void TryDragMoveReturnsFalseBeforeNativeWindowExists()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);
        Assert.False(activation.TryDragMove());
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
    public void HostActivationEventsUseTypedWindowActivationServiceBeforeReflectionFallback()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeActivatableWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        RaiseHostWindowEvent(host, WpfWindowEventKind.Activated);
        RaiseHostWindowEvent(host, WpfWindowEventKind.Deactivated);

        Assert.Equal(2, service.SetActivationStateCount);
        Assert.Same(window, service.LastActivationStateWindow);
        Assert.False(service.LastActivationState);
        Assert.False(window.IsActive);
        Assert.Equal(0, window.ActivatedCount);
        Assert.Equal(0, window.DeactivatedCount);
    }

    [Fact]
    public void HostDeactivationDoesNotBubblePortableCaptureCleanupFailure()
    {
        System.Windows.PortableWindowActivationService.Reset();
        System.Windows.PortableWindowActivationService.ThrowOnDeactivate = true;
        try
        {
            using var host = new ProGpuWpfWindowHost();
            var window = new FakePortableServiceActivationWindow();
            var source = new FakePortablePresentationSource();

            var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

            Assert.True(attached);
            Assert.NotNull(activation);

            RaiseHostWindowEvent(host, WpfWindowEventKind.Deactivated);

            Assert.Equal(1, System.Windows.PortableWindowActivationService.ActivationStateCallCount);
            Assert.False(System.Windows.PortableWindowActivationService.LastActivationState);
        }
        finally
        {
            System.Windows.PortableWindowActivationService.Reset();
        }
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
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
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
        int requestCountBeforeInput = scheduler.RequestCount;
        RaiseHostInputEvent(host, args);

        Assert.Equal(1, window.InputCount);
        Assert.Same(args, window.LastInputArgs);
        Assert.True(args.Handled);
        Assert.True(scheduler.RequestCount > requestCountBeforeInput);
    }

    [Fact]
    public void HostInputFromNonDispatcherThreadQueuesInputAndRenderWakeupProcessesIt()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeDispatchingPortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        int requestCountBeforeInput = scheduler.RequestCount;
        var args = new WpfInputEventArgs(WpfInputEventKind.MouseDown, x: 12, y: 24, button: WpfMouseButton.Left);
        RaiseHostInputEvent(host, args);

        Assert.Equal(1, window.Dispatcher.BeginInvokeCount);
        Assert.Equal(0, window.Dispatcher.InvokeCount);
        Assert.Equal(1, window.InputCount);
        Assert.Same(args, window.LastInputArgs);
        Assert.Contains("Input", window.FlushedPriorities);
        Assert.Contains("Render", window.FlushedPriorities);
        Assert.True(
            window.FlushedPriorities.IndexOf("Input") < window.FlushedPriorities.IndexOf("Render"),
            "Input-priority WPF work must run before render-priority work on a render wakeup.");
        Assert.True(scheduler.RequestCount > requestCountBeforeInput);
    }

    [Fact]
    public void HostInputActivatesWindowBeforeForwardingInput()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeActivatablePortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(WpfInputEventKind.KeyDown, key: "A", scanCode: 42);
        RaiseHostInputEvent(host, args);

        Assert.True(window.IsActive);
        Assert.Equal(1, window.ActivatedCount);
        Assert.Equal(1, window.InputCount);
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
    public void HostInputMapsPayloadToPresentationFrameworkPortableInputArgs()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePresentationFrameworkPortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 12,
            y: 24,
            button: WpfMouseButton.XButton1,
            modifiers: WpfInputModifiers.Shift | WpfInputModifiers.Alt);
        RaiseHostInputEvent(host, args);

        Assert.Equal(1, window.InputCount);
        Assert.NotNull(window.LastInputArgs);
        Assert.Equal(PortableInputEventKind.MouseDown, window.LastInputArgs.Kind);
        Assert.Equal(12, window.LastInputArgs.X);
        Assert.Equal(24, window.LastInputArgs.Y);
        Assert.Equal(PortableMouseButton.XButton1, window.LastInputArgs.Button);
        Assert.Equal(PortableInputModifiers.Shift | PortableInputModifiers.Alt, window.LastInputArgs.Modifiers);
        Assert.True(args.Handled);
    }

    [Fact]
    public void RenderWakeupFlushesQueuedDispatcherInputBeforeRendering()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeDispatchingPortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        window.FlushedPriorities.Clear();
        var args = new WpfInputEventArgs(WpfInputEventKind.TextInput, character: 'x');
        RaiseHostInputEvent(host, args);

        Assert.Equal(1, window.Dispatcher.BeginInvokeCount);
        Assert.Equal(1, window.InputCount);
        Assert.Same(args, window.LastInputArgs);
        Assert.Contains("Input", window.FlushedPriorities);
        Assert.Contains("Render", window.FlushedPriorities);
        Assert.True(
            window.FlushedPriorities.IndexOf("Input") < window.FlushedPriorities.IndexOf("Render"),
            "Input-priority WPF work must run before render-priority work on a render wakeup.");
        Assert.True(scheduler.RequestCount >= 2);
    }

    [Fact]
    public void RenderWakeupUsesTypedDispatcherFlushBeforeReflectionFallback()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
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

        scheduler.RequestRender();

        Assert.Contains("Input", service.FlushedPriorities);
        Assert.Contains("Render", service.FlushedPriorities);
        Assert.Contains("ApplicationIdle", service.FlushedPriorities);
        Assert.Contains(service.FlushTimeouts, timeout => timeout.HasValue);
        Assert.Same(window, service.LastFlushWindow);
    }

    [Fact]
    public void RenderWakeupTreatsSuspendedDispatcherFlushAsDeferred()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeSuspendedDispatcherFlushWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        Exception? exception = Record.Exception(scheduler.RequestRender);

        Assert.Null(exception);
        Assert.True(window.FlushCount > 0);
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
    public void HostDragDropUsesPortableWindowActivationServiceBeforeFallback()
    {
        System.Windows.PortableWindowActivationService.Reset();
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableServiceDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfDragDropEventArgs(
            WpfDragDropEventKind.Drop,
            new WpfDragDropData(new[] { "/tmp/a.txt" }, "portable text"),
            WpfDragDropEffects.Copy | WpfDragDropEffects.Move,
            WpfDragDropEffects.Copy,
            x: 12,
            y: 24);
        RaiseHostDragDropEvent(host, args);

        Assert.Equal(0, window.DropCount);
        Assert.Equal(1, System.Windows.PortableWindowActivationService.DropCount);
        Assert.Equal(new[] { "/tmp/a.txt" }, System.Windows.PortableWindowActivationService.LastFiles);
        Assert.Equal("portable text", System.Windows.PortableWindowActivationService.LastText);
        Assert.Equal(12, System.Windows.PortableWindowActivationService.LastX);
        Assert.Equal(24, System.Windows.PortableWindowActivationService.LastY);
        Assert.Equal((int)(WpfDragDropEffects.Copy | WpfDragDropEffects.Move), System.Windows.PortableWindowActivationService.LastAllowedEffects);
        Assert.Equal((int)WpfDragDropEffects.Copy, System.Windows.PortableWindowActivationService.LastAcceptedEffect);
        Assert.Equal(WpfDragDropEffects.Move, args.AcceptedEffect);
    }

    [Fact]
    public void HostDragEnterUsesPortableWindowActivationServiceWithoutFallback()
    {
        System.Windows.PortableWindowActivationService.Reset();
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableServiceDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfDragDropEventArgs(
            WpfDragDropEventKind.DragEnter,
            new WpfDragDropData(new[] { "/tmp/enter.txt" }, "enter text"),
            WpfDragDropEffects.Copy | WpfDragDropEffects.Move,
            WpfDragDropEffects.Copy,
            x: 7,
            y: 9);
        RaiseHostDragDropEvent(host, args);

        Assert.Equal(0, window.DropCount);
        Assert.Equal(1, System.Windows.PortableWindowActivationService.DropCount);
        Assert.Equal((int)WpfDragDropEventKind.DragEnter, System.Windows.PortableWindowActivationService.LastKind);
        Assert.Equal(new[] { "/tmp/enter.txt" }, System.Windows.PortableWindowActivationService.LastFiles);
        Assert.Equal("enter text", System.Windows.PortableWindowActivationService.LastText);
        Assert.Equal(7, System.Windows.PortableWindowActivationService.LastX);
        Assert.Equal(9, System.Windows.PortableWindowActivationService.LastY);
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

        public double Left { get; set; } = double.NaN;

        public double Top { get; set; } = double.NaN;

        public bool Topmost { get; set; }

        public FakeWindowState WindowState { get; set; } = FakeWindowState.Normal;

        public FakeResizeMode ResizeMode { get; set; } = FakeResizeMode.CanResize;

        public FakeWindowStyle WindowStyle { get; set; } = FakeWindowStyle.SingleBorderWindow;

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

    private sealed class FakePortableServiceActivationWindow :
        System.Windows.IPortableWindowActivationServiceTestTarget
    {
    }

    private sealed class FakeDispatchingPortableInputWindow :
        System.Windows.IPortableWindowActivationServiceTestTarget,
        System.Windows.IPortableDispatcherFlushTarget
    {
        public FakeDispatcher Dispatcher { get; } = new();

        public List<string> FlushedPriorities { get; } = new();

        public int InputCount { get; private set; }

        public WpfInputEventArgs? LastInputArgs { get; private set; }

        private void OnPortableInput(WpfInputEventArgs e)
        {
            InputCount++;
            LastInputArgs = e;
        }

        public void FlushDispatcherOperations(string priorityName)
        {
            FlushedPriorities.Add(priorityName);
            if (string.Equals(priorityName, "Input", StringComparison.Ordinal))
            {
                Dispatcher.TryInvokeQueuedCallback();
            }
        }

        public void FlushDispatcherOperations(string priorityName, TimeSpan timeout)
        {
            FlushedPriorities.Add(priorityName);
        }
    }

    private sealed class FakeSuspendedDispatcherFlushWindow :
        System.Windows.IPortableWindowActivationServiceTestTarget,
        System.Windows.IPortableDispatcherFlushTarget
    {
        public int FlushCount { get; private set; }

        public void FlushDispatcherOperations(string priorityName)
        {
            FlushCount++;
            throw new InvalidOperationException("Cannot perform this operation while dispatcher processing is suspended.");
        }

        public void FlushDispatcherOperations(string priorityName, TimeSpan timeout)
        {
            FlushCount++;
            throw new InvalidOperationException("Cannot perform this operation while dispatcher processing is suspended.");
        }
    }

    private sealed class FakeDispatcher
    {
        private Delegate? _queuedCallback;
        private object[] _queuedArgs = Array.Empty<object>();

        public int BeginInvokeCount { get; private set; }

        public int InvokeCount { get; private set; }

        public bool CheckAccess()
        {
            return false;
        }

        public object BeginInvoke(Delegate callback, object[] args)
        {
            BeginInvokeCount++;
            _queuedCallback = callback;
            _queuedArgs = args;
            return new object();
        }

        public object? Invoke(Action callback)
        {
            InvokeCount++;
            throw new InvalidOperationException("Input must be queued to the WPF dispatcher instead of invoked synchronously.");
        }

        public bool TryInvokeQueuedCallback()
        {
            if (_queuedCallback == null)
            {
                return false;
            }

            InvokeQueuedCallback();
            return true;
        }

        public void InvokeQueuedCallback()
        {
            var callback = _queuedCallback
                ?? throw new InvalidOperationException("Expected a dispatcher callback to be queued.");
            _queuedCallback = null;
            callback.DynamicInvoke(_queuedArgs);
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

    private sealed class FakeActivatablePortableInputWindow
    {
        public bool IsActive { get; private set; }

        public int ActivatedCount { get; private set; }

        public int InputCount { get; private set; }

        internal void HandleActivate(bool isActive)
        {
            if (!isActive || IsActive)
            {
                return;
            }

            IsActive = true;
            ActivatedCount++;
        }

        private void OnPortableInput(WpfInputEventArgs e)
        {
            InputCount++;
        }
    }

    private sealed class FakePresentationFrameworkPortableInputWindow
    {
        public int InputCount { get; private set; }

        public PortableInputEventArgs? LastInputArgs { get; private set; }

        internal void HandlePortableInput(PortableInputEventArgs e)
        {
            InputCount++;
            LastInputArgs = e;
            e.Handled = true;
        }
    }

    private enum PortableInputEventKind
    {
        KeyDown,
        KeyUp,
        TextInput,
        MouseMove,
        MouseDown,
        MouseUp,
        MouseWheel
    }

    private enum PortableMouseButton
    {
        None,
        Left,
        Right,
        Middle,
        XButton1,
        XButton2,
        Other
    }

    [Flags]
    private enum PortableInputModifiers
    {
        None = 0,
        Shift = 1,
        Control = 2,
        Alt = 4,
        Super = 8
    }

    private sealed class PortableInputEventArgs : EventArgs
    {
        public PortableInputEventArgs(
            PortableInputEventKind kind,
            string? key = null,
            int scanCode = 0,
            char? character = null,
            double x = 0,
            double y = 0,
            double deltaX = 0,
            double deltaY = 0,
            PortableMouseButton button = PortableMouseButton.None,
            PortableInputModifiers modifiers = PortableInputModifiers.None)
        {
            Kind = kind;
            Key = key;
            ScanCode = scanCode;
            Character = character;
            X = x;
            Y = y;
            DeltaX = deltaX;
            DeltaY = deltaY;
            Button = button;
            Modifiers = modifiers;
        }

        public PortableInputEventKind Kind { get; }

        public string? Key { get; }

        public int ScanCode { get; }

        public char? Character { get; }

        public double X { get; }

        public double Y { get; }

        public double DeltaX { get; }

        public double DeltaY { get; }

        public PortableMouseButton Button { get; }

        public PortableInputModifiers Modifiers { get; }

        public bool Handled { get; set; }
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

    private sealed class FakePortableServiceDropWindow : System.Windows.IPortableWindowActivationServiceTestTarget
    {
        public int DropCount { get; private set; }

        private void OnPortableDrop(WpfDragDropEventArgs e)
        {
            DropCount++;
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

    private sealed class TestClipboardServiceRegistrar : IPortableClipboardServiceRegistrar
    {
        public int RegisterCount { get; private set; }

        public Func<string?>? GetText { get; private set; }

        public Action<string?>? SetText { get; private set; }

        public Assembly SourceAssembly
        {
            get
            {
                return typeof(TestClipboardServiceRegistrar).Assembly;
            }
        }

        public IDisposable Register(Func<string?> getText, Action<string?> setText)
        {
            RegisterCount++;
            GetText = getText;
            SetText = setText;
            return new TestClipboardRegistration();
        }

        public void Clear()
        {
            GetText = null;
            SetText = null;
        }
    }

    private sealed class TestClipboardRegistration : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class TestLauncherServiceRegistrar : IPortableLauncherServiceRegistrar
    {
        public int RegisterCount { get; private set; }

        public Func<object, bool>? Launch { get; private set; }

        public Assembly SourceAssembly
        {
            get
            {
                return typeof(TestLauncherServiceRegistrar).Assembly;
            }
        }

        public IDisposable Register(Func<object, bool> launch)
        {
            RegisterCount++;
            Launch = launch;
            return new TestPortableServiceRegistration();
        }

        public void Clear()
        {
            Launch = null;
        }
    }

    private sealed class TestMessageBoxServiceRegistrar : IPortableMessageBoxServiceRegistrar
    {
        public int RegisterCount { get; private set; }

        public Func<object, object?>? Show { get; private set; }

        public Assembly SourceAssembly
        {
            get
            {
                return typeof(TestMessageBoxServiceRegistrar).Assembly;
            }
        }

        public IDisposable Register(Func<object, object?> show)
        {
            RegisterCount++;
            Show = show;
            return new TestPortableServiceRegistration();
        }

        public void Clear()
        {
            Show = null;
        }
    }

    private sealed class TestFileDialogServiceRegistrar : IPortableFileDialogServiceRegistrar
    {
        public int RegisterCount { get; private set; }

        public Func<object, string?>? ShowDialog { get; private set; }

        public Assembly SourceAssembly
        {
            get
            {
                return typeof(TestFileDialogServiceRegistrar).Assembly;
            }
        }

        public IDisposable Register(Func<object, string?> showDialog)
        {
            RegisterCount++;
            ShowDialog = showDialog;
            return new TestPortableServiceRegistration();
        }

        public void Clear()
        {
            ShowDialog = null;
        }
    }

    private sealed class TestWindowActivationServiceRegistrar : IPortableWindowActivationServiceRegistrar
    {
        public int RegisterCount { get; private set; }

        public PortableWindowActivationCallbacks? Callbacks { get; private set; }

        public int SetActivationStateCount { get; private set; }

        public object? LastActivationStateWindow { get; private set; }

        public bool LastActivationState { get; private set; }

        public object? LastFlushWindow { get; private set; }

        public List<string> FlushedPriorities { get; } = new List<string>();

        public List<TimeSpan?> FlushTimeouts { get; } = new List<TimeSpan?>();

        public Assembly SourceAssembly
        {
            get
            {
                return typeof(TestWindowActivationServiceRegistrar).Assembly;
            }
        }

        public void Register(PortableWindowActivationCallbacks callbacks)
        {
            RegisterCount++;
            Callbacks = callbacks;
        }

        public bool TrySetActivationState(object window, bool isActive)
        {
            SetActivationStateCount++;
            LastActivationStateWindow = window;
            LastActivationState = isActive;
            return true;
        }

        public bool TryFlushDispatcherOperations(object window, string markerPriorityName, TimeSpan? timeout)
        {
            LastFlushWindow = window;
            FlushedPriorities.Add(markerPriorityName);
            FlushTimeouts.Add(timeout);
            return true;
        }

        public void Clear()
        {
            Callbacks = null;
            LastActivationStateWindow = null;
            LastFlushWindow = null;
            FlushedPriorities.Clear();
            FlushTimeouts.Clear();
        }
    }

    private sealed class TestMediaContextRenderServiceRegistrar : IPortableMediaContextRenderServiceRegistrar
    {
        public int RegisterCount { get; private set; }

        public Action<object?, TimeSpan>? RequestRender { get; private set; }

        public TestPortableServiceRegistration? LastRegistration { get; private set; }

        public Assembly SourceAssembly
        {
            get
            {
                return typeof(TestMediaContextRenderServiceRegistrar).Assembly;
            }
        }

        public IDisposable Register(Action<object?, TimeSpan> requestRender)
        {
            RegisterCount++;
            RequestRender = requestRender;
            LastRegistration = new TestPortableServiceRegistration();
            return LastRegistration;
        }

        public void Clear()
        {
            RequestRender = null;
        }
    }

    private sealed class TestPortableServiceRegistration : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private enum FakeWindowState
    {
        Normal,
        Minimized,
        Maximized
    }

    private enum FakeResizeMode
    {
        NoResize,
        CanMinimize,
        CanResize,
        CanResizeWithGrip
    }

    private enum FakeWindowStyle
    {
        None,
        SingleBorderWindow,
        ThreeDBorderWindow,
        ToolWindow
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

        public Func<double, double, double, double, object?[]?>? HitTestBoundsOverride { get; set; }

        public Func<double, double, double, double, object?[]?>? HitTestEllipseBoundsOverride { get; set; }

        public object? RootVisual
        {
            get => _rootVisual;
            set
            {
                _rootVisual = value;
                RenderRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        public double ClientWidth { get; private set; }

        public double ClientHeight { get; private set; }

        public int ClientSizeChangeCount { get; private set; }

        public void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetClientSize(double width, double height)
        {
            ClientWidth = width;
            ClientHeight = height;
            ClientSizeChangeCount++;
            RenderRequested?.Invoke(this, EventArgs.Empty);
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
