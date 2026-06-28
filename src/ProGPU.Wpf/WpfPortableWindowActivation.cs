using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows.Media.ProGPU.Platform;

namespace System.Windows.Media.ProGPU;

public sealed class WpfPortableWindowActivation : IDisposable
{
    private const string PortableWindowActivationServiceTypeName = "System.Windows.PortableWindowActivationService";
    private const string PortableClipboardServiceTypeName = "System.Windows.PortableClipboardService";
    private const string PortableLauncherServiceTypeName = "System.Windows.PortableLauncherService";
    private const string PortableMessageBoxServiceTypeName = "System.Windows.PortableMessageBoxService";
    private const string PortableFileDialogServiceTypeName = "Microsoft.Win32.PortableFileDialogService";
    private const string PortableMediaContextRenderServiceTypeName = "System.Windows.Media.PortableMediaContextRenderService";
    private static readonly TimeSpan ApplicationIdleFlushTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan UpdateTickFlushTimeout = TimeSpan.FromMilliseconds(8);
    private bool _isDisposed;
    private bool _isClosingFromNative;
    private bool _isClosingFromWpf;
    private bool _isFlushingWpfDispatcher;
    private bool _isNativeRunStarted;
    private IDisposable? _mediaContextRenderRegistration;

    private WpfPortableWindowActivation(
        ProGpuWpfWindowHost host,
        object window,
        object rootVisual,
        object portablePresentationSource)
    {
        Host = host;
        Window = window;
        RootVisual = rootVisual;
        PortablePresentationSource = portablePresentationSource;
        Host.Closing += OnHostClosing;
        Host.InputReceived += OnHostInputReceived;
        Host.WindowEventReceived += OnHostWindowEventReceived;
        Host.DragDropReceived += OnHostDragDropReceived;
        Host.RenderWakeupRequested += OnHostRenderWakeupRequested;
        Host.UpdateTick += OnHostUpdateTick;
        SynchronizeInitialWindowState(updatePortablePresentationSource: false);
    }

    public ProGpuWpfWindowHost Host { get; }

    public object Window { get; }

    public object RootVisual { get; }

    public object PortablePresentationSource { get; }

    public static bool TryRegisterPresentationFrameworkActivation(
        Assembly presentationFrameworkAssembly,
        Func<object, ProGpuWpfWindowHost>? hostFactory = null)
    {
        ArgumentNullException.ThrowIfNull(presentationFrameworkAssembly);

        var serviceType = presentationFrameworkAssembly.GetType(
            PortableWindowActivationServiceTypeName,
            throwOnError: false);
        if (serviceType == null)
        {
            return false;
        }

        var registerMethod = serviceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[]
            {
                typeof(Func<object, object?>),
                typeof(Action<object>),
                typeof(Action<object>),
                typeof(Action<object, object>),
                typeof(Action<object, string>),
                typeof(Action<object, double, double>),
                typeof(Action<object, double, double>),
                typeof(Action<object, bool>),
                typeof(Action<object, object, object>),
                typeof(Action<object>),
                typeof(Action<object>),
                typeof(Action<object>),
                typeof(Func<object, bool>),
                typeof(Func<object, IntPtr>)
            },
            modifiers: null);
        if (registerMethod == null)
        {
            registerMethod = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[]
            {
                typeof(Func<object, object?>),
                typeof(Action<object>),
                typeof(Action<object>),
                typeof(Action<object, object>),
                typeof(Action<object, string>),
                typeof(Action<object, double, double>),
                typeof(Action<object, double, double>),
                typeof(Action<object, bool>),
                typeof(Action<object, object, object>),
                typeof(Action<object>),
                typeof(Action<object>),
                typeof(Action<object>),
                typeof(Func<object, bool>)
            },
            modifiers: null);
        }
        if (registerMethod == null)
        {
            registerMethod = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    typeof(Func<object, object?>),
                    typeof(Action<object>),
                    typeof(Action<object>),
                    typeof(Action<object, object>),
                    typeof(Action<object, string>),
                    typeof(Action<object, double, double>),
                    typeof(Action<object, double, double>),
                    typeof(Action<object, bool>),
                    typeof(Action<object>),
                    typeof(Action<object>),
                    typeof(Action<object>),
                    typeof(Func<object, bool>)
                },
                modifiers: null);
        }
        if (registerMethod == null)
        {
            registerMethod = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    typeof(Func<object, object?>),
                    typeof(Action<object>),
                    typeof(Action<object>),
                    typeof(Action<object, object>),
                    typeof(Action<object, string>),
                    typeof(Action<object, double, double>),
                    typeof(Action<object, double, double>),
                    typeof(Action<object>),
                    typeof(Action<object>),
                    typeof(Action<object>),
                    typeof(Func<object, bool>)
                },
                modifiers: null);
        }

        if (registerMethod == null)
        {
            registerMethod = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    typeof(Func<object, object?>),
                    typeof(Action<object>),
                    typeof(Action<object>),
                    typeof(Action<object, object>),
                    typeof(Action<object, string>),
                    typeof(Action<object, double, double>),
                    typeof(Action<object>),
                    typeof(Action<object>),
                    typeof(Action<object>),
                    typeof(Func<object, bool>)
                },
                modifiers: null);
        }

        if (registerMethod == null)
        {
            registerMethod = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    typeof(Func<object, object?>),
                    typeof(Action<object>),
                    typeof(Action<object>),
                    typeof(Action<object, object>),
                    typeof(Action<object>),
                    typeof(Action<object>),
                    typeof(Action<object>)
                },
                modifiers: null);
            if (registerMethod == null)
            {
                return false;
            }
        }

        Func<object, object?> activate = window =>
        {
            return TryCreateActivation(window, hostFactory, out var activation)
                ? activation
                : null;
        };
        Action<object> show = activation =>
            ((WpfPortableWindowActivation)activation).Show();
        Action<object> hide = activation =>
            ((WpfPortableWindowActivation)activation).Hide();
        Action<object, object> setWindowState = (activation, windowState) =>
            ((WpfPortableWindowActivation)activation).SetWindowState(windowState);
        Action<object, string> setTitle = (activation, title) =>
            ((WpfPortableWindowActivation)activation).SetTitle(title);
        Action<object, double, double> setClientSize = (activation, width, height) =>
            ((WpfPortableWindowActivation)activation).SetClientSize(width, height);
        Action<object, double, double> setPosition = (activation, left, top) =>
            ((WpfPortableWindowActivation)activation).SetPosition(left, top);
        Action<object, bool> setTopmost = (activation, topmost) =>
            ((WpfPortableWindowActivation)activation).SetTopmost(topmost);
        Action<object, object, object> setWindowBorder = (activation, resizeMode, windowStyle) =>
            ((WpfPortableWindowActivation)activation).SetWindowBorder(resizeMode, windowStyle);
        Action<object> close = activation =>
            ((WpfPortableWindowActivation)activation).Close();
        Action<object> run = activation =>
            ((WpfPortableWindowActivation)activation).Run();
        Action<object> dispose = activation =>
            ((WpfPortableWindowActivation)activation).Dispose();
        Func<object, bool> dragMove = activation =>
            ((WpfPortableWindowActivation)activation).TryDragMove();
        Func<object, IntPtr> getHandle = activation =>
            ((WpfPortableWindowActivation)activation).Host.PortablePresentationSourceBridge?.Handle ?? IntPtr.Zero;

        var parameters = registerMethod.GetParameters().Length switch
        {
            14 => new object[] { activate, show, hide, setWindowState, setTitle, setClientSize, setPosition, setTopmost, setWindowBorder, close, run, dispose, dragMove, getHandle },
            13 => new object[] { activate, show, hide, setWindowState, setTitle, setClientSize, setPosition, setTopmost, setWindowBorder, close, run, dispose, dragMove },
            12 => new object[] { activate, show, hide, setWindowState, setTitle, setClientSize, setPosition, setTopmost, close, run, dispose, dragMove },
            11 => new object[] { activate, show, hide, setWindowState, setTitle, setClientSize, setPosition, close, run, dispose, dragMove },
            10 => new object[] { activate, show, hide, setWindowState, setTitle, setClientSize, close, run, dispose, dragMove },
            9 => new object[] { activate, show, hide, setWindowState, setTitle, setClientSize, close, run, dispose },
            _ => new object[] { activate, show, hide, setWindowState, close, run, dispose }
        };
        registerMethod.Invoke(
            obj: null,
            parameters: parameters);
        TryRegisterPresentationFrameworkLauncherService(presentationFrameworkAssembly);
        TryRegisterPresentationFrameworkMessageBoxService(presentationFrameworkAssembly);
        TryRegisterPresentationFrameworkFileDialogService(presentationFrameworkAssembly);
        return true;
    }

    public static bool TryRegisterPresentationCoreClipboardService(Assembly presentationCoreAssembly)
    {
        ArgumentNullException.ThrowIfNull(presentationCoreAssembly);

        var serviceType = presentationCoreAssembly.GetType(
            PortableClipboardServiceTypeName,
            throwOnError: false);
        if (serviceType == null)
        {
            return false;
        }

        var registerMethod = serviceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Func<string?>), typeof(Action<string?>) },
            modifiers: null);
        if (registerMethod == null || !typeof(IDisposable).IsAssignableFrom(registerMethod.ReturnType))
        {
            return false;
        }

        registerMethod.Invoke(
            obj: null,
            parameters: new object[]
            {
                (Func<string?>)GetPortableClipboardText,
                (Action<string?>)SetPortableClipboardText
            });
        return true;
    }

    public static bool TryRegisterPresentationFrameworkLauncherService(Assembly presentationFrameworkAssembly)
    {
        ArgumentNullException.ThrowIfNull(presentationFrameworkAssembly);

        var serviceType = presentationFrameworkAssembly.GetType(
            PortableLauncherServiceTypeName,
            throwOnError: false);
        if (serviceType == null)
        {
            return false;
        }

        var registerMethod = serviceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Func<object, bool>) },
            modifiers: null);
        if (registerMethod == null || !typeof(IDisposable).IsAssignableFrom(registerMethod.ReturnType))
        {
            return false;
        }

        registerMethod.Invoke(
            obj: null,
            parameters: new object[] { (Func<object, bool>)LaunchPortableUri });
        return true;
    }

    public static bool TryRegisterPresentationFrameworkMessageBoxService(Assembly presentationFrameworkAssembly)
    {
        ArgumentNullException.ThrowIfNull(presentationFrameworkAssembly);

        var serviceType = presentationFrameworkAssembly.GetType(
            PortableMessageBoxServiceTypeName,
            throwOnError: false);
        if (serviceType == null)
        {
            return false;
        }

        var registerMethod = serviceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Func<object, object>) },
            modifiers: null);
        if (registerMethod == null || !typeof(IDisposable).IsAssignableFrom(registerMethod.ReturnType))
        {
            return false;
        }

        registerMethod.Invoke(
            obj: null,
            parameters: new object[] { (Func<object, object>)ShowPortableMessageBox });
        return true;
    }

    public static bool TryRegisterPresentationFrameworkFileDialogService(Assembly presentationFrameworkAssembly)
    {
        ArgumentNullException.ThrowIfNull(presentationFrameworkAssembly);

        var serviceType = presentationFrameworkAssembly.GetType(
            PortableFileDialogServiceTypeName,
            throwOnError: false);
        if (serviceType == null)
        {
            return false;
        }

        var registerMethod = serviceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Func<object, string?>) },
            modifiers: null);
        if (registerMethod == null || !typeof(IDisposable).IsAssignableFrom(registerMethod.ReturnType))
        {
            return false;
        }

        registerMethod.Invoke(
            obj: null,
            parameters: new object[] { (Func<object, string?>)ShowPortableFileDialog });
        return true;
    }

    public void Show()
    {
        ThrowIfDisposed();
        SynchronizeInitialWindowState(updatePortablePresentationSource: true);
        if (ShouldDeferNativeShowUntilRun())
        {
            Host.DeferShowUntilRun();
            return;
        }

        Host.Show();
        FlushWpfDispatcherOperations("Loaded", "Render");
    }

    public void Hide()
    {
        ThrowIfDisposed();
        Host.Hide();
    }

    public void SetWindowState(object? windowState)
    {
        ThrowIfDisposed();

        if (TryMapWindowState(windowState, out ProGpuWpfWindowState mappedWindowState))
        {
            Host.SetWindowState(mappedWindowState);
        }
    }

    public void SetTitle(string title)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(title);

        Host.SetTitle(title);
    }

    public void SetClientSize(object? width, object? height)
    {
        ThrowIfDisposed();

        var clientWidth = TryMapPositiveDimension(width, out double mappedWidth)
            ? ToLogicalClientDimension(mappedWidth)
            : Host.Width;
        var clientHeight = TryMapPositiveDimension(height, out double mappedHeight)
            ? ToLogicalClientDimension(mappedHeight)
            : Host.Height;

        Host.SetClientSize(clientWidth, clientHeight);
    }

    public void SetPosition(object? left, object? top)
    {
        ThrowIfDisposed();

        var windowLeft = TryMapFiniteDimension(left, out double mappedLeft)
            ? ToLogicalPositionDimension(mappedLeft)
            : Host.Left;
        var windowTop = TryMapFiniteDimension(top, out double mappedTop)
            ? ToLogicalPositionDimension(mappedTop)
            : Host.Top;

        if (windowLeft.HasValue && windowTop.HasValue)
        {
            Host.SetPosition(windowLeft.Value, windowTop.Value);
        }
    }

    public void SetTopmost(bool topmost)
    {
        ThrowIfDisposed();

        Host.SetTopmost(topmost);
    }

    public void SetWindowBorder(object? resizeMode, object? windowStyle)
    {
        ThrowIfDisposed();

        Host.SetWindowBorder(ResolveWindowBorder(resizeMode, windowStyle, Host.WindowBorder));
    }

    public void Close()
    {
        if (_isDisposed || _isClosingFromNative)
        {
            return;
        }

        _isClosingFromWpf = true;
        try
        {
            Host.Close();
        }
        finally
        {
            _isClosingFromWpf = false;
        }
    }

    public void Run()
    {
        ThrowIfDisposed();
        _isNativeRunStarted = true;
        SynchronizeInitialWindowState(updatePortablePresentationSource: true);
        if (_isDisposed)
        {
            return;
        }

        Host.Run();
    }

    private bool ShouldDeferNativeShowUntilRun()
    {
        return !_isNativeRunStarted && IsCurrentApplicationMainWindow(Window);
    }

    public bool TryDragMove()
    {
        ThrowIfDisposed();
        return Host.TryBeginDragMove();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Host.Closing -= OnHostClosing;
        Host.InputReceived -= OnHostInputReceived;
        Host.WindowEventReceived -= OnHostWindowEventReceived;
        Host.DragDropReceived -= OnHostDragDropReceived;
        Host.RenderWakeupRequested -= OnHostRenderWakeupRequested;
        Host.UpdateTick -= OnHostUpdateTick;
        _mediaContextRenderRegistration?.Dispose();
        _mediaContextRenderRegistration = null;
        Host.Dispose();
        _isDisposed = true;
    }

    public static bool TryAttach(
        ProGpuWpfWindowHost host,
        object window,
        Assembly presentationCoreAssembly,
        out WpfPortableWindowActivation? activation,
        double dpiScaleX = 1.0,
        double dpiScaleY = 1.0)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(presentationCoreAssembly);

        activation = null;
        var rootVisual = ResolveRootVisual(window);
        if (!host.TryCreatePortablePresentationSource(
                presentationCoreAssembly,
                rootVisual,
                dpiScaleX,
                dpiScaleY) ||
            host.PortablePresentationSource is not { } portablePresentationSource)
        {
            return false;
        }

        activation = new WpfPortableWindowActivation(host, window, rootVisual, portablePresentationSource);
        activation.TryRegisterMediaContextRenderService(presentationCoreAssembly);
        return true;
    }

    public static bool TryAttach(
        ProGpuWpfWindowHost host,
        object window,
        object portablePresentationSource,
        out WpfPortableWindowActivation? activation)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(portablePresentationSource);

        activation = null;
        if (!host.TryBindPortablePresentationSource(portablePresentationSource) ||
            host.PortablePresentationSourceBridge is not { } bridge)
        {
            return false;
        }

        var rootVisual = ResolveRootVisual(window);
        bridge.RootVisual = rootVisual;
        activation = new WpfPortableWindowActivation(host, window, rootVisual, portablePresentationSource);
        activation.TryRegisterMediaContextRenderService(portablePresentationSource.GetType().Assembly);
        return true;
    }

    public static ProGpuWpfWindowOptions CreateHostOptions(
        object window,
        ProGpuWpfWindowOptions? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        fallback ??= new ProGpuWpfWindowOptions();
        var options = new ProGpuWpfWindowOptions
        {
            Title = fallback.Title,
            Width = fallback.Width,
            Height = fallback.Height,
            Left = fallback.Left,
            Top = fallback.Top,
            VSync = fallback.VSync,
            IsVisible = fallback.IsVisible,
            Topmost = fallback.Topmost,
            WindowBorder = fallback.WindowBorder,
            WindowState = fallback.WindowState
        };

        if (TryReadStringProperty(window, "Title", out var title) &&
            !string.IsNullOrWhiteSpace(title))
        {
            options.Title = title;
        }

        if (TryReadPositiveDimension(window, "Width", out var width) ||
            TryReadPositiveDimension(window, "ActualWidth", out width))
        {
            options.Width = ToLogicalClientDimension(width);
        }

        if (TryReadPositiveDimension(window, "Height", out var height) ||
            TryReadPositiveDimension(window, "ActualHeight", out height))
        {
            options.Height = ToLogicalClientDimension(height);
        }

        if (TryReadFiniteDimension(window, "Left", out var left))
        {
            options.Left = ToLogicalPositionDimension(left);
        }

        if (TryReadFiniteDimension(window, "Top", out var top))
        {
            options.Top = ToLogicalPositionDimension(top);
        }

        if (TryReadProperty(window, "WindowState", out object? windowState) &&
            TryMapWindowState(windowState, out ProGpuWpfWindowState mappedWindowState))
        {
            options.WindowState = mappedWindowState;
        }

        if (TryReadBooleanProperty(window, "Topmost", out var topmost))
        {
            options.Topmost = topmost;
        }

        options.WindowBorder = ResolveWindowBorder(window, options.WindowBorder);

        return options;
    }

    private void SynchronizeInitialWindowState(bool updatePortablePresentationSource)
    {
        if (TryReadStringProperty(Window, "Title", out var title) &&
            !string.IsNullOrWhiteSpace(title))
        {
            Host.SetTitle(title);
        }

        if (TryReadProperty(Window, "WindowState", out object? windowState) &&
            TryMapWindowState(windowState, out ProGpuWpfWindowState mappedWindowState))
        {
            Host.SetWindowState(mappedWindowState);
        }

        if (TryReadBooleanProperty(Window, "Topmost", out var topmost))
        {
            Host.SetTopmost(topmost);
        }

        Host.SetWindowBorder(ResolveWindowBorder(Window, Host.WindowBorder));

        var hasWidth =
            TryReadPositiveDimension(Window, "Width", out var width) ||
            TryReadPositiveDimension(Window, "ActualWidth", out width);
        var hasHeight =
            TryReadPositiveDimension(Window, "Height", out var height) ||
            TryReadPositiveDimension(Window, "ActualHeight", out height);

        if (hasWidth || hasHeight)
        {
            SetHostClientSize(
                hasWidth ? ToLogicalClientDimension(width) : Host.Width,
                hasHeight ? ToLogicalClientDimension(height) : Host.Height,
                updatePortablePresentationSource);
        }
        else
        {
            SetHostClientSize(Host.Width, Host.Height, updatePortablePresentationSource);
        }

        var hasLeft = TryReadFiniteDimension(Window, "Left", out var left);
        var hasTop = TryReadFiniteDimension(Window, "Top", out var top);
        var windowLeft = hasLeft ? ToLogicalPositionDimension(left) : Host.Left;
        var windowTop = hasTop ? ToLogicalPositionDimension(top) : Host.Top;
        if (windowLeft.HasValue && windowTop.HasValue)
        {
            Host.SetPosition(windowLeft.Value, windowTop.Value);
        }
    }

    private void SetHostClientSize(int width, int height, bool updatePortablePresentationSource)
    {
        if (updatePortablePresentationSource)
        {
            Host.SetClientSize(width, height);
        }
        else
        {
            Host.SetInitialClientSize(width, height);
        }
    }

    private static object ResolveRootVisual(object window)
    {
        return window;
    }

    private void OnHostClosing(object? sender, ProGpuWpfWindowClosingEventArgs e)
    {
        if (_isDisposed || _isClosingFromWpf)
        {
            return;
        }

        _isClosingFromNative = true;
        try
        {
            if (TryInvokeWindowClose(Window) == WpfWindowCloseResult.Canceled)
            {
                e.Cancel = true;
            }
        }
        finally
        {
            _isClosingFromNative = false;
        }
    }

    private void OnHostWindowEventReceived(object? sender, WpfWindowEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        switch (e.Kind)
        {
            case WpfWindowEventKind.Activated:
                TrySetWindowActivationStateForHostEvent(isActive: true);
                break;
            case WpfWindowEventKind.Deactivated:
                TrySetWindowActivationStateForHostEvent(isActive: false);
                break;
        }
    }

    private void TrySetWindowActivationStateForHostEvent(bool isActive)
    {
        try
        {
            TrySetWindowActivationState(Window, isActive);
        }
        catch (TargetInvocationException ex) when (!isActive && IsRecoverablePortableDeactivationException(ex.InnerException ?? ex))
        {
            // A native focus-loss callback can arrive while a third-party control still owns mouse capture.
            // Keep the portable host alive if that capture-cancel path rejects an intermediate layout state.
        }
        catch (Exception ex) when (!isActive && IsRecoverablePortableDeactivationException(ex))
        {
            // See the TargetInvocationException path above.
        }
    }

    private static bool IsRecoverablePortableDeactivationException(Exception exception)
    {
        return exception is ArgumentException or InvalidOperationException;
    }

    private static bool IsRecoverableDispatcherFlushException(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: { } innerException })
        {
            exception = innerException;
        }

        return exception is InvalidOperationException invalidOperation &&
            invalidOperation.Message.IndexOf(
                "dispatcher processing is suspended",
                StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OnHostRenderWakeupRequested(object? sender, EventArgs e)
    {
        if (_isDisposed || _isFlushingWpfDispatcher)
        {
            return;
        }

        FlushWpfDispatcherOperations("Input", "Render", "ApplicationIdle");
    }

    private void OnHostUpdateTick(object? sender, EventArgs e)
    {
        if (_isDisposed || _isFlushingWpfDispatcher)
        {
            return;
        }

        FlushWpfDispatcherOperation("Background", UpdateTickFlushTimeout);
    }

    private void FlushWpfDispatcherOperations(params string[] markerPriorityNames)
    {
        if (_isFlushingWpfDispatcher)
        {
            return;
        }

        _isFlushingWpfDispatcher = true;
        try
        {
            foreach (string markerPriorityName in markerPriorityNames)
            {
                TimeSpan? timeout = string.Equals(markerPriorityName, "ApplicationIdle", StringComparison.Ordinal)
                    ? ApplicationIdleFlushTimeout
                    : null;
                FlushWpfDispatcherOperation(markerPriorityName, timeout);
            }
        }
        finally
        {
            _isFlushingWpfDispatcher = false;
        }
    }

    private void FlushWpfDispatcherOperation(string markerPriorityName, TimeSpan? timeout)
    {
        TryFlushDispatcherOperations(Window, markerPriorityName, timeout);
    }

    private void OnHostInputReceived(object? sender, WpfInputEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (TryDispatchHostInputToWindowDispatcher(e))
        {
            return;
        }

        ProcessHostInputAndRequestRender(e);
    }

    private void ProcessHostInputAndRequestRender(WpfInputEventArgs e)
    {
        ProcessHostInput(e);
        RequestRenderFromMediaContext(RootVisual, TimeSpan.Zero);
    }

    private void ProcessHostInput(WpfInputEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        TrySetWindowActivationState(Window, isActive: true);
        TryForwardInputToWindow(Window, e);
    }

    private bool TryDispatchHostInputToWindowDispatcher(WpfInputEventArgs e)
    {
        if (!TryReadProperty(Window, "Dispatcher", out object? dispatcher) || dispatcher == null)
        {
            return false;
        }

        var dispatcherType = dispatcher.GetType();
        var checkAccessMethod = dispatcherType.GetMethod(
            "CheckAccess",
            BindingFlags.Instance | BindingFlags.Public,
            Type.EmptyTypes);
        if (checkAccessMethod == null ||
            checkAccessMethod.Invoke(dispatcher, null) is not bool hasAccess ||
            hasAccess)
        {
            return false;
        }

        var callback = new Action(() => ProcessHostInputAndRequestRender(e));
        object? dispatcherPriority = TryCreateDispatcherPriority(dispatcherType, "Input");
        MethodInfo? beginInvokeMethod = null;
        object[]? beginInvokeArgs = null;
        foreach (var method in dispatcherType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!string.Equals(method.Name, "BeginInvoke", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 2 &&
                parameters[0].ParameterType.FullName == "System.Windows.Threading.DispatcherPriority" &&
                parameters[1].ParameterType == typeof(Delegate) &&
                dispatcherPriority != null)
            {
                beginInvokeMethod = method;
                beginInvokeArgs = new object[] { dispatcherPriority, callback };
                break;
            }

            if (parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(Delegate) &&
                parameters[1].ParameterType == typeof(object[]))
            {
                beginInvokeMethod = method;
                beginInvokeArgs = new object[] { callback, Array.Empty<object>() };
                break;
            }
        }

        if (beginInvokeMethod != null)
        {
            beginInvokeMethod.Invoke(dispatcher, beginInvokeArgs);
            Host.TryRequestNativeLoopWakeup();
            return true;
        }

        return false;
    }

    private static object? TryCreateDispatcherPriority(Type dispatcherType, string priorityName)
    {
        Type? priorityType = dispatcherType.Assembly.GetType("System.Windows.Threading.DispatcherPriority");
        if (priorityType == null)
        {
            return null;
        }

        try
        {
            return Enum.Parse(priorityType, priorityName);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryForwardInputToWindow(object window, WpfInputEventArgs e)
    {
        var windowType = window.GetType();
        var inputMethod =
            FindInstanceMethod(windowType, "OnPortableInput", typeof(WpfInputEventArgs)) ??
            FindInstanceMethod(windowType, "HandlePortableInput", typeof(WpfInputEventArgs));
        if (inputMethod == null)
        {
            return TryForwardCompatibleInputToWindow(window, e);
        }

        inputMethod.Invoke(window, new object[] { e });
        return true;
    }

    private static bool TryForwardCompatibleInputToWindow(object window, WpfInputEventArgs e)
    {
        foreach (var methodName in new[] { "OnPortableInput", "HandlePortableInput" })
        {
            foreach (var method in FindInstanceMethods(window.GetType(), methodName))
            {
                var parameters = method.GetParameters();
                if (parameters.Length != 1)
                {
                    continue;
                }

                var parameterType = parameters[0].ParameterType;
                if (parameterType.IsAssignableFrom(e.GetType()))
                {
                    method.Invoke(window, new object[] { e });
                    return true;
                }

                if (!TryCreateCompatibleInputEventArgs(parameterType, e, out object? mappedArgs) ||
                    mappedArgs == null)
                {
                    continue;
                }

                method.Invoke(window, new[] { mappedArgs });
                CopyHandledState(mappedArgs, e);
                return true;
            }
        }

        return false;
    }

    private void OnHostDragDropReceived(object? sender, WpfDragDropEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        TryForwardDropToWindow(Window, e);
    }

    private static bool TryForwardDropToWindow(object window, WpfDragDropEventArgs e)
    {
        if (TryProcessPortableDragDrop(window, e))
        {
            return true;
        }

        if (e.Kind != WpfDragDropEventKind.Drop)
        {
            return false;
        }

        var windowType = window.GetType();
        var dropMethod = FindInstanceMethod(windowType, "OnPortableDrop", typeof(WpfDragDropEventArgs));
        if (dropMethod != null)
        {
            dropMethod.Invoke(window, new object[] { e });
            return true;
        }

        var filesMethod =
            FindInstanceMethod(windowType, "OnPortableFileDrop", typeof(IReadOnlyList<string>)) ??
            FindInstanceMethod(windowType, "DropFiles", typeof(IReadOnlyList<string>));
        if (filesMethod != null)
        {
            filesMethod.Invoke(window, new object[] { e.Data.Files });
            return true;
        }

        filesMethod =
            FindInstanceMethod(windowType, "OnPortableFileDrop", typeof(string[])) ??
            FindInstanceMethod(windowType, "DropFiles", typeof(string[]));
        if (filesMethod != null)
        {
            filesMethod.Invoke(window, new object[] { e.Data.Files.ToArray() });
            return true;
        }

        return false;
    }

    private static bool TryProcessPortableDragDrop(object window, WpfDragDropEventArgs e)
    {
        Type? serviceType = FindPortableWindowActivationServiceType(window);
        if (serviceType == null)
        {
            return false;
        }

        foreach (var method in serviceType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, "ProcessDragDropEvent", StringComparison.Ordinal) ||
                method.ReturnType != typeof(int))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 8 ||
                !parameters[0].ParameterType.IsAssignableFrom(window.GetType()) ||
                parameters[1].ParameterType != typeof(int) ||
                parameters[2].ParameterType != typeof(string[]) ||
                parameters[3].ParameterType != typeof(string) ||
                parameters[4].ParameterType != typeof(double) ||
                parameters[5].ParameterType != typeof(double) ||
                parameters[6].ParameterType != typeof(int) ||
                parameters[7].ParameterType != typeof(int))
            {
                continue;
            }

            var acceptedEffect = (int)method.Invoke(
                obj: null,
                parameters: new object?[]
                {
                    window,
                    (int)e.Kind,
                    e.Data.Files.ToArray(),
                    e.Data.Text,
                    e.X,
                    e.Y,
                    (int)e.AllowedEffects,
                    (int)e.AcceptedEffect
                })!;
            e.AcceptedEffect = (WpfDragDropEffects)acceptedEffect;
            return true;
        }

        if (e.Kind != WpfDragDropEventKind.Drop)
        {
            return false;
        }

        foreach (var method in serviceType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, "ProcessDragDrop", StringComparison.Ordinal) ||
                method.ReturnType != typeof(int))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 7 ||
                !parameters[0].ParameterType.IsAssignableFrom(window.GetType()) ||
                parameters[1].ParameterType != typeof(string[]) ||
                parameters[2].ParameterType != typeof(string) ||
                parameters[3].ParameterType != typeof(double) ||
                parameters[4].ParameterType != typeof(double) ||
                parameters[5].ParameterType != typeof(int) ||
                parameters[6].ParameterType != typeof(int))
            {
                continue;
            }

            var acceptedEffect = (int)method.Invoke(
                obj: null,
                parameters: new object?[]
                {
                    window,
                    e.Data.Files.ToArray(),
                    e.Data.Text,
                    e.X,
                    e.Y,
                    (int)e.AllowedEffects,
                    (int)e.AcceptedEffect
                })!;
            e.AcceptedEffect = (WpfDragDropEffects)acceptedEffect;
            return true;
        }

        return false;
    }

    private static MethodInfo? FindInstanceMethod(Type type, string methodName, Type parameterType)
    {
        for (Type? currentType = type; currentType != null; currentType = currentType.BaseType)
        {
            var method = currentType.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { parameterType },
                modifiers: null);
            if (method != null)
            {
                return method;
            }
        }

        return null;
    }

    private static IEnumerable<MethodInfo> FindInstanceMethods(Type type, string methodName)
    {
        for (Type? currentType = type; currentType != null; currentType = currentType.BaseType)
        {
            foreach (var method in currentType.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly))
            {
                if (string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    yield return method;
                }
            }
        }
    }

    private static bool TryCreateCompatibleInputEventArgs(
        Type targetType,
        WpfInputEventArgs source,
        out object? mappedArgs)
    {
        mappedArgs = null;

        foreach (var constructor in targetType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length != 10 ||
                !TryConvertEnumByName(parameters[0].ParameterType, source.Kind, out object? kind) ||
                parameters[1].ParameterType != typeof(string) ||
                parameters[2].ParameterType != typeof(int) ||
                Nullable.GetUnderlyingType(parameters[3].ParameterType) != typeof(char) ||
                parameters[4].ParameterType != typeof(double) ||
                parameters[5].ParameterType != typeof(double) ||
                parameters[6].ParameterType != typeof(double) ||
                parameters[7].ParameterType != typeof(double) ||
                !TryConvertEnumByName(parameters[8].ParameterType, source.Button, out object? button) ||
                !TryConvertEnumByName(parameters[9].ParameterType, source.Modifiers, out object? modifiers))
            {
                continue;
            }

            mappedArgs = constructor.Invoke(new[]
            {
                kind,
                source.Key,
                source.ScanCode,
                source.Character,
                source.X,
                source.Y,
                source.DeltaX,
                source.DeltaY,
                button,
                modifiers
            });
            return true;
        }

        return false;
    }

    private static bool TryConvertEnumByName(Type targetType, object sourceValue, out object? mappedValue)
    {
        mappedValue = null;
        if (!targetType.IsEnum)
        {
            return false;
        }

        try
        {
            mappedValue = Enum.Parse(targetType, sourceValue.ToString()!, ignoreCase: false);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void CopyHandledState(object mappedArgs, WpfInputEventArgs source)
    {
        var handledProperty = mappedArgs.GetType().GetProperty(
            "Handled",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (handledProperty == null ||
            handledProperty.GetIndexParameters().Length != 0 ||
            handledProperty.PropertyType != typeof(bool) ||
            !handledProperty.CanRead)
        {
            return;
        }

        source.Handled = (bool)handledProperty.GetValue(mappedArgs)!;
    }

    private static WpfWindowCloseResult TryInvokeWindowClose(object window)
    {
        var closeMethod = window.GetType().GetMethod(
            "Close",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (closeMethod == null)
        {
            return WpfWindowCloseResult.NotInvoked;
        }

        closeMethod.Invoke(window, Array.Empty<object>());
        return TryReadWindowClosedState(window, out bool isClosed) && !isClosed
            ? WpfWindowCloseResult.Canceled
            : WpfWindowCloseResult.Closed;
    }

    private enum WpfWindowCloseResult
    {
        NotInvoked,
        Closed,
        Canceled
    }

    private static bool TryReadWindowClosedState(object window, out bool isClosed)
    {
        if (TryReadBooleanProperty(window, "IsClosed", out isClosed) ||
            TryReadBooleanProperty(window, "IsDisposed", out isClosed) ||
            TryReadBooleanField(window, "_disposed", out isClosed))
        {
            return true;
        }

        isClosed = false;
        return false;
    }

    private static bool TrySetWindowActivationState(object window, bool isActive)
    {
        if (TryInvokePortableWindowActivationService(window, isActive))
        {
            return true;
        }

        var handleActivateMethod = window.GetType().GetMethod(
            "HandleActivate",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(bool) },
            modifiers: null);
        if (handleActivateMethod == null)
        {
            return false;
        }

        handleActivateMethod.Invoke(window, new object[] { isActive });
        return true;
    }

    private bool TryRegisterMediaContextRenderService(Assembly presentationCoreAssembly)
    {
        var serviceType = presentationCoreAssembly.GetType(
            PortableMediaContextRenderServiceTypeName,
            throwOnError: false);
        if (serviceType == null)
        {
            return false;
        }

        var registerMethod = serviceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Action<object, TimeSpan>) },
            modifiers: null);
        var registerParameter = registerMethod == null
            ? null
            : (object)(Action<object?, TimeSpan>)RequestRenderFromMediaContext;
        if (registerMethod == null)
        {
            registerMethod = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action<TimeSpan>) },
                modifiers: null);
            registerParameter = registerMethod == null
                ? null
                : (object)(Action<TimeSpan>)RequestRenderFromMediaContext;
        }
        if (registerMethod == null)
        {
            registerMethod = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action) },
                modifiers: null);
            registerParameter = registerMethod == null
                ? null
                : (Action)RequestRenderFromMediaContext;
        }

        if (registerMethod == null ||
            registerParameter == null ||
            !typeof(IDisposable).IsAssignableFrom(registerMethod.ReturnType))
        {
            return false;
        }

        _mediaContextRenderRegistration?.Dispose();
        _mediaContextRenderRegistration = (IDisposable?)registerMethod.Invoke(
            obj: null,
            parameters: new[] { registerParameter });
        return _mediaContextRenderRegistration != null;
    }

    private void RequestRenderFromMediaContext()
    {
        RequestRenderFromMediaContext(null, TimeSpan.Zero);
    }

    private void RequestRenderFromMediaContext(TimeSpan delay)
    {
        RequestRenderFromMediaContext(null, delay);
    }

    private void RequestRenderFromMediaContext(object? invalidatedSource, TimeSpan delay)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            Host.InvalidateWpfSourceForPortableRender(invalidatedSource ?? RootVisual);

            if (Host.WpfRenderScheduler is IWpfDelayedRenderScheduler delayedScheduler)
            {
                delayedScheduler.RequestRender(delay);
            }
            else
            {
                Host.WpfRenderScheduler.RequestRender();
            }

            Host.TryRequestNativeLoopWakeup();
        }
        catch (ObjectDisposedException)
        {
            // Host-first disposal can leave one stale MediaContext callback until activation cleanup.
        }
    }

    private static bool TryFlushDispatcherOperations(object window, string markerPriorityName, TimeSpan? timeout = null)
    {
        Type? serviceType = FindPortableWindowActivationServiceType(window);
        if (serviceType == null)
        {
            return false;
        }

        if (timeout.HasValue)
        {
            foreach (var method in serviceType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, "FlushDispatcherOperations", StringComparison.Ordinal))
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length != 3 ||
                    !parameters[0].ParameterType.IsAssignableFrom(window.GetType()) ||
                    !parameters[1].ParameterType.IsEnum ||
                    parameters[2].ParameterType != typeof(TimeSpan))
                {
                    continue;
                }

                if (!Enum.TryParse(parameters[1].ParameterType, markerPriorityName, ignoreCase: false, out object? markerPriority))
                {
                    continue;
                }

                try
                {
                    object? result = method.Invoke(null, new object[] { window, markerPriority, timeout.Value });
                    return result is not bool completed || completed;
                }
                catch (Exception ex) when (IsRecoverableDispatcherFlushException(ex))
                {
                    return false;
                }
            }
        }

        foreach (var method in serviceType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, "FlushDispatcherOperations", StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 2 ||
                !parameters[0].ParameterType.IsAssignableFrom(window.GetType()) ||
                !parameters[1].ParameterType.IsEnum)
            {
                continue;
            }

            if (!Enum.TryParse(parameters[1].ParameterType, markerPriorityName, ignoreCase: false, out object? markerPriority))
            {
                continue;
            }

            try
            {
                method.Invoke(null, new[] { window, markerPriority });
                return true;
            }
            catch (Exception ex) when (IsRecoverableDispatcherFlushException(ex))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryInvokePortableWindowActivationService(object window, bool isActive)
    {
        Type? serviceType = FindPortableWindowActivationServiceType(window);
        if (serviceType == null)
        {
            return false;
        }

        var setActivationStateMethod = serviceType.GetMethod(
            "SetActivationState",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { window.GetType(), typeof(bool) },
            modifiers: null);
        setActivationStateMethod ??= serviceType
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "SetActivationState", StringComparison.Ordinal))
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 2 &&
                    parameters[0].ParameterType.IsAssignableFrom(window.GetType()) &&
                    parameters[1].ParameterType == typeof(bool);
            });

        if (setActivationStateMethod == null)
        {
            return false;
        }

        setActivationStateMethod.Invoke(null, new object[] { window, isActive });
        return true;
    }

    private static Type? FindPortableWindowActivationServiceType(object window)
    {
        for (Type? currentType = window.GetType(); currentType != null; currentType = currentType.BaseType)
        {
            Type? serviceType = currentType.Assembly.GetType(
                PortableWindowActivationServiceTypeName,
                throwOnError: false);
            if (serviceType != null)
            {
                return serviceType;
            }
        }

        return null;
    }

    private static bool IsCurrentApplicationMainWindow(object window)
    {
        Type? applicationType = null;
        for (Type? currentType = window.GetType(); currentType != null; currentType = currentType.BaseType)
        {
            applicationType = currentType.Assembly.GetType(
                "System.Windows.Application",
                throwOnError: false);
            if (applicationType != null)
            {
                break;
            }
        }

        if (applicationType == null)
        {
            return false;
        }

        var currentProperty = applicationType.GetProperty(
            "Current",
            BindingFlags.Static | BindingFlags.Public);
        var mainWindowProperty = applicationType.GetProperty(
            "MainWindow",
            BindingFlags.Instance | BindingFlags.Public);
        object? currentApplication = currentProperty?.GetValue(null);
        object? mainWindow = currentApplication == null
            ? null
            : mainWindowProperty?.GetValue(currentApplication);
        return ReferenceEquals(mainWindow, window);
    }

    private static object ShowPortableMessageBox(object request)
    {
        var options = new WpfMessageBoxOptions
        {
            MessageBoxText = ReadRequestString(request, "MessageBoxText", string.Empty),
            Caption = ReadRequestString(request, "Caption", string.Empty),
            Button = ReadRequestValueName(request, "Button", "OK"),
            Icon = ReadRequestValueName(request, "Icon", "None"),
            DefaultResult = ReadRequestValueName(request, "DefaultResult", "None"),
            Options = ReadRequestValueName(request, "Options", "None"),
            FallbackResult = ReadRequestValueName(request, "FallbackResult", "OK")
        };

        try
        {
            return CrossPlatformWpfPlatformServices.Instance.MessageBoxes.Show(options);
        }
        catch (PlatformNotSupportedException)
        {
            return options.FallbackResult;
        }
        catch (InvalidOperationException)
        {
            return options.FallbackResult;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return options.FallbackResult;
        }
    }

    private static bool LaunchPortableUri(object request)
    {
        if (!TryReadRequestUri(request, "Uri", out Uri? uri))
        {
            return false;
        }

        try
        {
            CrossPlatformWpfPlatformServices.Instance.Launcher
                .OpenUriAsync(uri!)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string? GetPortableClipboardText()
    {
        try
        {
            string? text = CrossPlatformWpfPlatformServices.Instance.Clipboard
                .GetTextAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static void SetPortableClipboardText(string? text)
    {
        try
        {
            CrossPlatformWpfPlatformServices.Instance.Clipboard
                .SetTextAsync(text)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static string? ShowPortableFileDialog(object request)
    {
        string kind = ReadRequestString(request, "Kind", "OpenFile");
        var options = new WpfFileDialogOptions
        {
            Title = ReadRequestString(request, "Title", string.Empty),
            SuggestedFileName = ReadRequestString(request, "SuggestedItemName", string.Empty),
            FileTypePatterns = ReadFileDialogPatterns(request)
        };

        try
        {
            var fileDialogs = CrossPlatformWpfPlatformServices.Instance.FileDialogs;
            return kind switch
            {
                "SaveFile" => fileDialogs.SaveFileAsync(options).AsTask().GetAwaiter().GetResult(),
                "PickFolder" => fileDialogs.PickFolderAsync().AsTask().GetAwaiter().GetResult(),
                _ => fileDialogs.OpenFileAsync(options).AsTask().GetAwaiter().GetResult()
            };
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string ReadRequestString(object request, string propertyName, string fallback)
    {
        return TryReadRequestProperty(request, propertyName, out object? value) && value is string text
            ? text
            : fallback;
    }

    private static string ReadRequestValueName(object request, string propertyName, string fallback)
    {
        return TryReadRequestProperty(request, propertyName, out object? value) && value != null
            ? value.ToString() ?? fallback
            : fallback;
    }

    private static bool TryReadRequestUri(object request, string propertyName, out Uri? uri)
    {
        uri = null;
        if (!TryReadRequestProperty(request, propertyName, out object? value) || value == null)
        {
            return false;
        }

        if (value is Uri typedUri)
        {
            uri = typedUri;
            return true;
        }

        if (value is string text &&
            Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out Uri? parsedUri))
        {
            uri = parsedUri;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ReadFileDialogPatterns(object request)
    {
        string filter = ReadRequestString(request, "Filter", string.Empty);
        if (string.IsNullOrEmpty(filter))
        {
            return Array.Empty<string>();
        }

        string[] tokens = filter.Split('|');
        var patterns = new List<string>();
        for (int i = 1; i < tokens.Length; i += 2)
        {
            foreach (string rawPattern in tokens[i].Split(';'))
            {
                string pattern = rawPattern.Trim();
                if (!string.IsNullOrEmpty(pattern))
                {
                    patterns.Add(pattern);
                }
            }
        }

        return patterns;
    }

    private static bool TryReadRequestProperty(object instance, string propertyName, out object? value)
    {
        value = null;
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            return false;
        }

        value = property.GetValue(instance);
        return true;
    }

    private static bool TryReadBooleanProperty(object instance, string propertyName, out bool value)
    {
        value = false;
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null ||
            property.GetIndexParameters().Length != 0 ||
            property.PropertyType != typeof(bool))
        {
            return false;
        }

        value = (bool)property.GetValue(instance)!;
        return true;
    }

    private static bool TryReadBooleanField(object instance, string fieldName, out bool value)
    {
        value = false;
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(bool))
            {
                continue;
            }

            value = (bool)field.GetValue(instance)!;
            return true;
        }

        return false;
    }

    private static bool TryMapWindowState(object? windowState, out ProGpuWpfWindowState mappedWindowState)
    {
        mappedWindowState = ProGpuWpfWindowState.Normal;
        if (windowState == null)
        {
            return false;
        }

        switch (windowState.ToString())
        {
            case "Minimized":
                mappedWindowState = ProGpuWpfWindowState.Minimized;
                return true;
            case "Maximized":
                mappedWindowState = ProGpuWpfWindowState.Maximized;
                return true;
            case "Normal":
                mappedWindowState = ProGpuWpfWindowState.Normal;
                return true;
            default:
                return false;
        }
    }

    private static ProGpuWpfWindowBorder ResolveWindowBorder(
        object window,
        ProGpuWpfWindowBorder fallback)
    {
        TryReadProperty(window, "ResizeMode", out object? resizeMode);
        TryReadProperty(window, "WindowStyle", out object? windowStyle);

        return ResolveWindowBorder(resizeMode, windowStyle, fallback);
    }

    private static ProGpuWpfWindowBorder ResolveWindowBorder(
        object? resizeMode,
        object? windowStyle,
        ProGpuWpfWindowBorder fallback)
    {
        if (string.Equals(windowStyle?.ToString(), "None", StringComparison.Ordinal))
        {
            return ProGpuWpfWindowBorder.Hidden;
        }

        return TryMapResizeModeToWindowBorder(resizeMode, out ProGpuWpfWindowBorder mappedBorder)
            ? mappedBorder
            : fallback;
    }

    private static bool TryMapResizeModeToWindowBorder(
        object? resizeMode,
        out ProGpuWpfWindowBorder windowBorder)
    {
        windowBorder = ProGpuWpfWindowBorder.Resizable;
        if (resizeMode == null)
        {
            return false;
        }

        switch (resizeMode.ToString())
        {
            case "NoResize":
            case "CanMinimize":
                windowBorder = ProGpuWpfWindowBorder.Fixed;
                return true;
            case "CanResize":
            case "CanResizeWithGrip":
                windowBorder = ProGpuWpfWindowBorder.Resizable;
                return true;
            default:
                return false;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private static bool TryCreateActivation(
        object window,
        Func<object, ProGpuWpfWindowHost>? hostFactory,
        out WpfPortableWindowActivation? activation)
    {
        activation = null;
        Assembly? presentationCoreAssembly = ResolvePresentationCoreAssembly(window);
        if (presentationCoreAssembly == null)
        {
            return false;
        }

        ProGpuWpfWindowHost host = hostFactory?.Invoke(window) ??
            new ProGpuWpfWindowHost(CreateHostOptions(window));
        if (TryAttach(host, window, presentationCoreAssembly, out activation))
        {
            return true;
        }

        host.Dispose();
        return false;
    }

    private static Assembly? ResolvePresentationCoreAssembly(object window)
    {
        for (Type? type = window.GetType(); type != null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, "System.Windows.Media.Visual", StringComparison.Ordinal))
            {
                return type.Assembly;
            }
        }

        return null;
    }

    private static bool TryReadStringProperty(object instance, string propertyName, out string? value)
    {
        value = null;
        if (!TryReadProperty(instance, propertyName, out object? rawValue))
        {
            return false;
        }

        value = rawValue as string;
        return value != null;
    }

    private static bool TryReadPositiveDimension(object instance, string propertyName, out double value)
    {
        if (!TryReadProperty(instance, propertyName, out object? rawValue) ||
            rawValue == null)
        {
            value = 0.0;
            return false;
        }

        return TryMapPositiveDimension(rawValue, out value);
    }

    private static bool TryReadFiniteDimension(object instance, string propertyName, out double value)
    {
        if (!TryReadProperty(instance, propertyName, out object? rawValue) ||
            rawValue == null)
        {
            value = 0.0;
            return false;
        }

        return TryMapFiniteDimension(rawValue, out value);
    }

    private static bool TryMapPositiveDimension(object? value, out double mappedValue)
    {
        if (!TryMapFiniteDimension(value, out mappedValue))
        {
            return false;
        }

        return mappedValue > 0.0;
    }

    private static bool TryMapFiniteDimension(object? value, out double mappedValue)
    {
        mappedValue = 0.0;
        if (value == null)
        {
            return false;
        }

        try
        {
            mappedValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }

        return double.IsFinite(mappedValue);
    }

    private static bool TryReadProperty(object instance, string propertyName, out object? value)
    {
        value = null;
        var property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            return false;
        }

        value = property.GetValue(instance);
        return true;
    }

    private static int ToLogicalClientDimension(double value)
    {
        return Math.Max(1, (int)Math.Ceiling(value));
    }

    private static int ToLogicalPositionDimension(double value)
    {
        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
