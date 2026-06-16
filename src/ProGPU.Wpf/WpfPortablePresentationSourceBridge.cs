using System;
using System.Reflection;

namespace System.Windows.Media.ProGPU;

public sealed class WpfPortablePresentationSourceBridge : IDisposable
{
    internal const string SourceTypeName = "System.Windows.PortablePresentationSource";
    private const string RootVisualPropertyName = "RootVisual";
    private const string CompositionTargetPropertyName = "CompositionTarget";
    private const string RenderRequestedEventName = "RenderRequested";
    private const string SetDeviceScaleMethodName = "SetDeviceScale";

    private readonly ProGpuWpfWindowHost _host;
    private readonly PropertyInfo _rootVisualProperty;
    private readonly PropertyInfo _compositionTargetProperty;
    private readonly MethodInfo? _setDeviceScaleMethod;
    private readonly MethodInfo? _disposeMethod;
    private readonly bool _ownsSource;
    private MethodInfo? _removeRenderRequestedMethod;
    private Delegate? _renderRequestedHandler;
    private bool _isDisposed;

    private WpfPortablePresentationSourceBridge(
        ProGpuWpfWindowHost host,
        object source,
        PropertyInfo rootVisualProperty,
        PropertyInfo compositionTargetProperty,
        MethodInfo? setDeviceScaleMethod,
        MethodInfo? disposeMethod,
        bool ownsSource)
    {
        _host = host;
        Source = source;
        _rootVisualProperty = rootVisualProperty;
        _compositionTargetProperty = compositionTargetProperty;
        _setDeviceScaleMethod = setDeviceScaleMethod;
        _disposeMethod = disposeMethod;
        _ownsSource = ownsSource;
    }

    public object Source { get; }

    public object? CompositionTarget => _compositionTargetProperty.GetValue(Source);

    public object? RootVisual
    {
        get => _rootVisualProperty.GetValue(Source);
        set
        {
            ThrowIfDisposed();
            _rootVisualProperty.SetValue(Source, value);
            SyncHostRootVisual();
        }
    }

    public static bool TryCreate(
        ProGpuWpfWindowHost host,
        Assembly presentationCoreAssembly,
        out WpfPortablePresentationSourceBridge? bridge)
    {
        return TryCreate(host, presentationCoreAssembly, 1.0, 1.0, out bridge);
    }

    public static bool TryCreate(
        ProGpuWpfWindowHost host,
        Assembly presentationCoreAssembly,
        double dpiScaleX,
        double dpiScaleY,
        out WpfPortablePresentationSourceBridge? bridge)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(presentationCoreAssembly);

        Type? sourceType = presentationCoreAssembly.GetType(SourceTypeName, throwOnError: false);
        if (sourceType == null)
        {
            bridge = null;
            return false;
        }

        object? source = Activator.CreateInstance(
            sourceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { dpiScaleX, dpiScaleY },
            culture: null);
        if (source == null)
        {
            bridge = null;
            return false;
        }

        if (TryBind(host, source, ownsSource: true, out bridge))
        {
            return true;
        }

        if (source is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return false;
    }

    public static bool TryBind(
        ProGpuWpfWindowHost host,
        object presentationSource,
        out WpfPortablePresentationSourceBridge? bridge)
    {
        return TryBind(host, presentationSource, ownsSource: false, out bridge);
    }

    public bool TrySetDeviceScale(double dpiScaleX, double dpiScaleY)
    {
        ThrowIfDisposed();

        if (_setDeviceScaleMethod == null)
        {
            return false;
        }

        _setDeviceScaleMethod.Invoke(Source, new object[] { dpiScaleX, dpiScaleY });
        return true;
    }

    public bool SyncHostRootVisual()
    {
        ThrowIfDisposed();

        object? rootVisual = RootVisual;
        if (ReferenceEquals(_host.WpfRootVisual, rootVisual))
        {
            return false;
        }

        _host.WpfRootVisual = rootVisual;
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_removeRenderRequestedMethod != null && _renderRequestedHandler != null)
        {
            _removeRenderRequestedMethod.Invoke(Source, new object[] { _renderRequestedHandler });
        }

        object? rootVisual = _rootVisualProperty.GetValue(Source);
        if (ReferenceEquals(_host.WpfRootVisual, rootVisual))
        {
            _host.WpfRootVisual = null;
        }

        if (_ownsSource && _disposeMethod != null)
        {
            _disposeMethod.Invoke(Source, Array.Empty<object>());
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private static bool TryBind(
        ProGpuWpfWindowHost host,
        object presentationSource,
        bool ownsSource,
        out WpfPortablePresentationSourceBridge? bridge)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(presentationSource);

        Type sourceType = presentationSource.GetType();
        PropertyInfo? rootVisualProperty = FindProperty(sourceType, RootVisualPropertyName);
        PropertyInfo? compositionTargetProperty = FindProperty(sourceType, CompositionTargetPropertyName);
        if (rootVisualProperty == null ||
            !rootVisualProperty.CanRead ||
            !rootVisualProperty.CanWrite ||
            compositionTargetProperty == null ||
            !compositionTargetProperty.CanRead)
        {
            bridge = null;
            return false;
        }

        MethodInfo? setDeviceScaleMethod = FindSetDeviceScaleMethod(sourceType);
        MethodInfo? disposeMethod = sourceType.GetMethod(
            nameof(IDisposable.Dispose),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        EventInfo? renderRequestedEvent = sourceType.GetEvent(
            RenderRequestedEventName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        bridge = new WpfPortablePresentationSourceBridge(
            host,
            presentationSource,
            rootVisualProperty,
            compositionTargetProperty,
            setDeviceScaleMethod,
            disposeMethod,
            ownsSource);
        bridge.TrySubscribeToRenderRequested(renderRequestedEvent);

        bridge.SyncHostRootVisual();
        return true;
    }

    private static PropertyInfo? FindProperty(Type sourceType, string name)
    {
        return sourceType.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private static MethodInfo? FindSetDeviceScaleMethod(Type sourceType)
    {
        MethodInfo? method = sourceType.GetMethod(
            SetDeviceScaleMethodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(double), typeof(double) },
            modifiers: null);

        return method?.ReturnType == typeof(void) ? method : null;
    }

    private bool TrySubscribeToRenderRequested(EventInfo? renderRequestedEvent)
    {
        if (renderRequestedEvent == null)
        {
            return false;
        }

        MethodInfo? addMethod = renderRequestedEvent.GetAddMethod(nonPublic: true);
        MethodInfo? removeMethod = renderRequestedEvent.GetRemoveMethod(nonPublic: true);
        Type? eventHandlerType = renderRequestedEvent.EventHandlerType;
        if (addMethod == null || removeMethod == null || eventHandlerType == null)
        {
            return false;
        }

        Delegate handler = Delegate.CreateDelegate(
            eventHandlerType,
            this,
            nameof(OnSourceRenderRequested));
        addMethod.Invoke(Source, new object[] { handler });
        _removeRenderRequestedMethod = removeMethod;
        _renderRequestedHandler = handler;
        return true;
    }

    private void OnSourceRenderRequested(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!SyncHostRootVisual())
        {
            _host.WpfRenderScheduler.RequestRender();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
