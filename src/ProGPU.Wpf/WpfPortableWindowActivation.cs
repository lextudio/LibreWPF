using System.Globalization;
using System.Reflection;

namespace System.Windows.Media.ProGPU;

public sealed class WpfPortableWindowActivation : IDisposable
{
    private const string PortableWindowActivationServiceTypeName = "System.Windows.PortableWindowActivationService";
    private bool _isDisposed;
    private bool _isClosingFromNative;
    private bool _isClosingFromWpf;

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
                typeof(Action<object>),
                typeof(Action<object>),
                typeof(Action<object>)
            },
            modifiers: null);
        if (registerMethod == null)
        {
            return false;
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
        Action<object> close = activation =>
            ((WpfPortableWindowActivation)activation).Close();
        Action<object> run = activation =>
            ((WpfPortableWindowActivation)activation).Run();
        Action<object> dispose = activation =>
            ((WpfPortableWindowActivation)activation).Dispose();

        registerMethod.Invoke(
            obj: null,
            parameters: new object[] { activate, show, hide, setWindowState, close, run, dispose });
        return true;
    }

    public void Show()
    {
        ThrowIfDisposed();
        Host.Show();
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
        Host.Run();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Host.Closing -= OnHostClosing;
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
            VSync = fallback.VSync,
            IsVisible = fallback.IsVisible,
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
            options.Width = ToPixelDimension(width);
        }

        if (TryReadPositiveDimension(window, "Height", out var height) ||
            TryReadPositiveDimension(window, "ActualHeight", out height))
        {
            options.Height = ToPixelDimension(height);
        }

        if (TryReadProperty(window, "WindowState", out object? windowState) &&
            TryMapWindowState(windowState, out ProGpuWpfWindowState mappedWindowState))
        {
            options.WindowState = mappedWindowState;
        }

        return options;
    }

    private static object ResolveRootVisual(object window)
    {
        return window;
    }

    private void OnHostClosing(object? sender, EventArgs e)
    {
        if (_isDisposed || _isClosingFromWpf)
        {
            return;
        }

        _isClosingFromNative = true;
        try
        {
            TryInvokeWindowClose(Window);
        }
        finally
        {
            _isClosingFromNative = false;
        }
    }

    private static bool TryInvokeWindowClose(object window)
    {
        var closeMethod = window.GetType().GetMethod(
            "Close",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (closeMethod == null)
        {
            return false;
        }

        closeMethod.Invoke(window, Array.Empty<object>());
        return true;
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
        value = 0.0;
        if (!TryReadProperty(instance, propertyName, out object? rawValue) ||
            rawValue == null)
        {
            return false;
        }

        try
        {
            value = Convert.ToDouble(rawValue, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }

        return double.IsFinite(value) && value > 0.0;
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

    private static int ToPixelDimension(double value)
    {
        return Math.Max(1, (int)Math.Ceiling(value));
    }
}
