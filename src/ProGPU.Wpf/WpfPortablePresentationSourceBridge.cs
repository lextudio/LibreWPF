using System;
using System.Reflection;
using System.Windows.Media.ProGPU.Platform;

namespace System.Windows.Media.ProGPU;

public sealed class WpfPortablePresentationSourceBridge : IDisposable
{
    internal const string SourceTypeName = "System.Windows.PortablePresentationSource";
    private const string RootVisualPropertyName = "RootVisual";
    private const string CompositionTargetPropertyName = "CompositionTarget";
    private const string HandlePropertyName = "Handle";
    private const string RenderRequestedEventName = "RenderRequested";
    private const string CursorRequestedEventName = "CursorRequested";
    private const string RequestedCursorPropertyName = "RequestedCursor";
    private const string HitTestOverridePropertyName = "HitTestOverride";
    private const string HitTestAllOverridePropertyName = "HitTestAllOverride";
    private const string HitTestBoundsOverridePropertyName = "HitTestBoundsOverride";
    private const string HitTestEllipseBoundsOverridePropertyName = "HitTestEllipseBoundsOverride";
    private const string SetDeviceScaleMethodName = "SetDeviceScale";
    private const string SetClientSizeMethodName = "SetClientSize";

    private readonly ProGpuWpfWindowHost _host;
    private readonly PropertyInfo _rootVisualProperty;
    private readonly PropertyInfo _compositionTargetProperty;
    private readonly PropertyInfo? _handleProperty;
    private readonly PropertyInfo? _requestedCursorProperty;
    private readonly PropertyInfo? _hitTestOverrideProperty;
    private readonly PropertyInfo? _hitTestAllOverrideProperty;
    private readonly PropertyInfo? _hitTestBoundsOverrideProperty;
    private readonly PropertyInfo? _hitTestEllipseBoundsOverrideProperty;
    private readonly MethodInfo? _setDeviceScaleMethod;
    private readonly MethodInfo? _setClientSizeMethod;
    private readonly MethodInfo? _disposeMethod;
    private readonly bool _ownsSource;
    private MethodInfo? _removeRenderRequestedMethod;
    private MethodInfo? _removeCursorRequestedMethod;
    private Delegate? _renderRequestedHandler;
    private Delegate? _cursorRequestedHandler;
    private Delegate? _hitTestOverrideHandler;
    private Delegate? _hitTestAllOverrideHandler;
    private Delegate? _hitTestBoundsOverrideHandler;
    private Delegate? _hitTestEllipseBoundsOverrideHandler;
    private bool _isDisposed;

    private WpfPortablePresentationSourceBridge(
        ProGpuWpfWindowHost host,
        object source,
        PropertyInfo rootVisualProperty,
        PropertyInfo compositionTargetProperty,
        PropertyInfo? handleProperty,
        PropertyInfo? requestedCursorProperty,
        PropertyInfo? hitTestOverrideProperty,
        PropertyInfo? hitTestAllOverrideProperty,
        PropertyInfo? hitTestBoundsOverrideProperty,
        PropertyInfo? hitTestEllipseBoundsOverrideProperty,
        MethodInfo? setDeviceScaleMethod,
        MethodInfo? setClientSizeMethod,
        MethodInfo? disposeMethod,
        bool ownsSource)
    {
        _host = host;
        Source = source;
        _rootVisualProperty = rootVisualProperty;
        _compositionTargetProperty = compositionTargetProperty;
        _handleProperty = handleProperty;
        _requestedCursorProperty = requestedCursorProperty;
        _hitTestOverrideProperty = hitTestOverrideProperty;
        _hitTestAllOverrideProperty = hitTestAllOverrideProperty;
        _hitTestBoundsOverrideProperty = hitTestBoundsOverrideProperty;
        _hitTestEllipseBoundsOverrideProperty = hitTestEllipseBoundsOverrideProperty;
        _setDeviceScaleMethod = setDeviceScaleMethod;
        _setClientSizeMethod = setClientSizeMethod;
        _disposeMethod = disposeMethod;
        _ownsSource = ownsSource;
    }

    public object Source { get; }

    public object? CompositionTarget => _compositionTargetProperty.GetValue(Source);

    public IntPtr Handle
    {
        get
        {
            if (_handleProperty == null ||
                _handleProperty.PropertyType != typeof(IntPtr) ||
                !_handleProperty.CanRead)
            {
                return IntPtr.Zero;
            }

            return (IntPtr)_handleProperty.GetValue(Source)!;
        }
    }

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

    public bool TrySetClientSize(double width, double height)
    {
        ThrowIfDisposed();

        if (_setClientSizeMethod == null)
        {
            return false;
        }

        _setClientSizeMethod.Invoke(Source, new object[] { width, height });
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

        if (_removeCursorRequestedMethod != null && _cursorRequestedHandler != null)
        {
            _removeCursorRequestedMethod.Invoke(Source, new object[] { _cursorRequestedHandler });
        }

        if (_hitTestOverrideProperty != null &&
            _hitTestOverrideHandler != null &&
            ReferenceEquals(_hitTestOverrideProperty.GetValue(Source), _hitTestOverrideHandler))
        {
            _hitTestOverrideProperty.SetValue(Source, null);
        }

        if (_hitTestAllOverrideProperty != null &&
            _hitTestAllOverrideHandler != null &&
            ReferenceEquals(_hitTestAllOverrideProperty.GetValue(Source), _hitTestAllOverrideHandler))
        {
            _hitTestAllOverrideProperty.SetValue(Source, null);
        }

        if (_hitTestBoundsOverrideProperty != null &&
            _hitTestBoundsOverrideHandler != null &&
            ReferenceEquals(_hitTestBoundsOverrideProperty.GetValue(Source), _hitTestBoundsOverrideHandler))
        {
            _hitTestBoundsOverrideProperty.SetValue(Source, null);
        }

        if (_hitTestEllipseBoundsOverrideProperty != null &&
            _hitTestEllipseBoundsOverrideHandler != null &&
            ReferenceEquals(_hitTestEllipseBoundsOverrideProperty.GetValue(Source), _hitTestEllipseBoundsOverrideHandler))
        {
            _hitTestEllipseBoundsOverrideProperty.SetValue(Source, null);
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
        PropertyInfo? handleProperty = FindProperty(sourceType, HandlePropertyName);
        PropertyInfo? requestedCursorProperty = FindProperty(sourceType, RequestedCursorPropertyName);
        PropertyInfo? hitTestOverrideProperty = FindProperty(sourceType, HitTestOverridePropertyName);
        PropertyInfo? hitTestAllOverrideProperty = FindProperty(sourceType, HitTestAllOverridePropertyName);
        PropertyInfo? hitTestBoundsOverrideProperty = FindProperty(sourceType, HitTestBoundsOverridePropertyName);
        PropertyInfo? hitTestEllipseBoundsOverrideProperty = FindProperty(sourceType, HitTestEllipseBoundsOverridePropertyName);
        if (rootVisualProperty == null ||
            !rootVisualProperty.CanRead ||
            !rootVisualProperty.CanWrite ||
            compositionTargetProperty == null ||
            !compositionTargetProperty.CanRead)
        {
            bridge = null;
            return false;
        }

        MethodInfo? setDeviceScaleMethod = FindVoidMethod(
            sourceType,
            SetDeviceScaleMethodName,
            typeof(double),
            typeof(double));
        MethodInfo? setClientSizeMethod = FindVoidMethod(
            sourceType,
            SetClientSizeMethodName,
            typeof(double),
            typeof(double));
        MethodInfo? disposeMethod = sourceType.GetMethod(
            nameof(IDisposable.Dispose),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        EventInfo? renderRequestedEvent = sourceType.GetEvent(
            RenderRequestedEventName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        EventInfo? cursorRequestedEvent = sourceType.GetEvent(
            CursorRequestedEventName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        bridge = new WpfPortablePresentationSourceBridge(
            host,
            presentationSource,
            rootVisualProperty,
            compositionTargetProperty,
            handleProperty,
            requestedCursorProperty,
            hitTestOverrideProperty,
            hitTestAllOverrideProperty,
            hitTestBoundsOverrideProperty,
            hitTestEllipseBoundsOverrideProperty,
            setDeviceScaleMethod,
            setClientSizeMethod,
            disposeMethod,
            ownsSource);
        bridge.TrySubscribeToRenderRequested(renderRequestedEvent);
        bridge.TrySubscribeToCursorRequested(cursorRequestedEvent);
        bridge.TryInstallHitTestOverride();
        bridge.TryInstallHitTestAllOverride();
        bridge.TryInstallHitTestBoundsOverride();
        bridge.TryInstallHitTestEllipseBoundsOverride();

        bridge.SyncHostRootVisual();
        return true;
    }

    private static PropertyInfo? FindProperty(Type sourceType, string name)
    {
        return sourceType.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private static MethodInfo? FindVoidMethod(Type sourceType, string name, params Type[] parameterTypes)
    {
        MethodInfo? method = sourceType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
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
            _host.RequestRenderAndWakeNativeLoop();
        }
    }

    private bool TrySubscribeToCursorRequested(EventInfo? cursorRequestedEvent)
    {
        if (cursorRequestedEvent == null || _requestedCursorProperty == null)
        {
            return false;
        }

        MethodInfo? addMethod = cursorRequestedEvent.GetAddMethod(nonPublic: true);
        MethodInfo? removeMethod = cursorRequestedEvent.GetRemoveMethod(nonPublic: true);
        Type? eventHandlerType = cursorRequestedEvent.EventHandlerType;
        if (addMethod == null || removeMethod == null || eventHandlerType == null)
        {
            return false;
        }

        Delegate handler = Delegate.CreateDelegate(
            eventHandlerType,
            this,
            nameof(OnSourceCursorRequested));
        addMethod.Invoke(Source, new object[] { handler });
        _removeCursorRequestedMethod = removeMethod;
        _cursorRequestedHandler = handler;
        return true;
    }

    private void OnSourceCursorRequested(object? sender, EventArgs e)
    {
        if (_isDisposed || _requestedCursorProperty == null)
        {
            return;
        }

        object? cursor = _requestedCursorProperty.GetValue(Source);
        _host.ApplyPortableCursor(ToWpfCursor(cursor));
    }

    private bool TryInstallHitTestOverride()
    {
        if (_hitTestOverrideProperty == null ||
            !_hitTestOverrideProperty.CanWrite ||
            !typeof(Delegate).IsAssignableFrom(_hitTestOverrideProperty.PropertyType))
        {
            return false;
        }

        MethodInfo? method = GetType().GetMethod(
            nameof(TryHitTestOwner),
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            return false;
        }

        Delegate? handler = Delegate.CreateDelegate(
            _hitTestOverrideProperty.PropertyType,
            this,
            method,
            throwOnBindFailure: false);
        if (handler == null)
        {
            return false;
        }

        _hitTestOverrideProperty.SetValue(Source, handler);
        _hitTestOverrideHandler = handler;
        return true;
    }

    private bool TryInstallHitTestAllOverride()
    {
        if (_hitTestAllOverrideProperty == null ||
            !_hitTestAllOverrideProperty.CanWrite ||
            !typeof(Delegate).IsAssignableFrom(_hitTestAllOverrideProperty.PropertyType))
        {
            return false;
        }

        MethodInfo? method = GetType().GetMethod(
            nameof(HitTestOwners),
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            return false;
        }

        Delegate? handler = Delegate.CreateDelegate(
            _hitTestAllOverrideProperty.PropertyType,
            this,
            method,
            throwOnBindFailure: false);
        if (handler == null)
        {
            return false;
        }

        _hitTestAllOverrideProperty.SetValue(Source, handler);
        _hitTestAllOverrideHandler = handler;
        return true;
    }

    private bool TryInstallHitTestBoundsOverride()
    {
        if (_hitTestBoundsOverrideProperty == null ||
            !_hitTestBoundsOverrideProperty.CanWrite ||
            !typeof(Delegate).IsAssignableFrom(_hitTestBoundsOverrideProperty.PropertyType))
        {
            return false;
        }

        MethodInfo? method = GetType().GetMethod(
            nameof(HitTestBoundsOwners),
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            return false;
        }

        Delegate? handler = Delegate.CreateDelegate(
            _hitTestBoundsOverrideProperty.PropertyType,
            this,
            method,
            throwOnBindFailure: false);
        if (handler == null)
        {
            return false;
        }

        _hitTestBoundsOverrideProperty.SetValue(Source, handler);
        _hitTestBoundsOverrideHandler = handler;
        return true;
    }

    private bool TryInstallHitTestEllipseBoundsOverride()
    {
        if (_hitTestEllipseBoundsOverrideProperty == null ||
            !_hitTestEllipseBoundsOverrideProperty.CanWrite ||
            !typeof(Delegate).IsAssignableFrom(_hitTestEllipseBoundsOverrideProperty.PropertyType))
        {
            return false;
        }

        MethodInfo? method = GetType().GetMethod(
            nameof(HitTestEllipseBoundsOwners),
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            return false;
        }

        Delegate? handler = Delegate.CreateDelegate(
            _hitTestEllipseBoundsOverrideProperty.PropertyType,
            this,
            method,
            throwOnBindFailure: false);
        if (handler == null)
        {
            return false;
        }

        _hitTestEllipseBoundsOverrideProperty.SetValue(Source, handler);
        _hitTestEllipseBoundsOverrideHandler = handler;
        return true;
    }

    private object? TryHitTestOwner(System.Windows.Point rootPoint)
    {
        return _host.TryHitTestOwner(rootPoint.X, rootPoint.Y, out object? owner) &&
            owner != null
                ? owner
                : _host.HasGpuHitTestCache ? Source : null;
    }

    private object?[]? HitTestOwners(System.Windows.Point rootPoint)
    {
        if (_host.TryHitTestOwners(rootPoint.X, rootPoint.Y, out object?[] owners))
        {
            return owners;
        }

        return _host.HasGpuHitTestCache ? Array.Empty<object>() : null;
    }

    private object?[]? HitTestBoundsOwners(System.Windows.Point rootMin, System.Windows.Point rootMax)
    {
        if (_host.TryQueryHitTestBoundsCandidates(
                rootMin.X,
                rootMin.Y,
                rootMax.X,
                rootMax.Y,
                out object?[] candidates))
        {
            return candidates;
        }

        return _host.HasGpuHitTestCache ? Array.Empty<object>() : null;
    }

    private object?[]? HitTestEllipseBoundsOwners(System.Windows.Point rootMin, System.Windows.Point rootMax)
    {
        if (_host.TryQueryHitTestEllipseCandidates(
                rootMin.X,
                rootMin.Y,
                rootMax.X,
                rootMax.Y,
                out object?[] candidates))
        {
            return candidates;
        }

        return _host.HasGpuHitTestCache ? Array.Empty<object>() : null;
    }

    private static WpfCursor ToWpfCursor(object? cursor)
    {
        string? cursorTypeName = cursor?.GetType()
            .GetProperty("CursorType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(cursor)
            ?.ToString();

        cursorTypeName ??= cursor?.ToString();
        return cursorTypeName switch
        {
            "No" => WpfCursor.No,
            "Arrow" => WpfCursor.Arrow,
            "AppStarting" => WpfCursor.AppStarting,
            "Cross" => WpfCursor.Crosshair,
            "IBeam" => WpfCursor.IBeam,
            "SizeAll" => WpfCursor.SizeAll,
            "SizeNESW" => WpfCursor.SizeNESW,
            "SizeNS" => WpfCursor.SizeNS,
            "SizeNWSE" => WpfCursor.SizeNWSE,
            "SizeWE" => WpfCursor.SizeWE,
            "Wait" => WpfCursor.Wait,
            "Hand" => WpfCursor.Hand,
            _ => WpfCursor.Default
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
