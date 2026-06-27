using System;
using System.Linq;
using System.Linq.Expressions;
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
    private static MethodInfo? s_visualTreeHelperGetParentMethod;
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
        if (!TryCreatePointHitTestDelegate(
                _hitTestOverrideProperty,
                nameof(TryHitTestOwner),
                pointParameterCount: 1,
                out Delegate? handler))
        {
            return false;
        }

        _hitTestOverrideProperty!.SetValue(Source, handler);
        _hitTestOverrideHandler = handler;
        return true;
    }

    private bool TryInstallHitTestAllOverride()
    {
        if (!TryCreatePointHitTestDelegate(
                _hitTestAllOverrideProperty,
                nameof(HitTestOwners),
                pointParameterCount: 1,
                out Delegate? handler))
        {
            return false;
        }

        _hitTestAllOverrideProperty!.SetValue(Source, handler);
        _hitTestAllOverrideHandler = handler;
        return true;
    }

    private bool TryInstallHitTestBoundsOverride()
    {
        if (!TryCreatePointHitTestDelegate(
                _hitTestBoundsOverrideProperty,
                nameof(HitTestBoundsOwners),
                pointParameterCount: 2,
                out Delegate? handler))
        {
            return false;
        }

        _hitTestBoundsOverrideProperty!.SetValue(Source, handler);
        _hitTestBoundsOverrideHandler = handler;
        return true;
    }

    private bool TryInstallHitTestEllipseBoundsOverride()
    {
        if (!TryCreatePointHitTestDelegate(
                _hitTestEllipseBoundsOverrideProperty,
                nameof(HitTestEllipseBoundsOwners),
                pointParameterCount: 2,
                out Delegate? handler))
        {
            return false;
        }

        _hitTestEllipseBoundsOverrideProperty!.SetValue(Source, handler);
        _hitTestEllipseBoundsOverrideHandler = handler;
        return true;
    }

    private bool TryCreatePointHitTestDelegate(
        PropertyInfo? property,
        string targetMethodName,
        int pointParameterCount,
        out Delegate? handler)
    {
        handler = null;
        if (property == null ||
            !property.CanWrite ||
            !typeof(Delegate).IsAssignableFrom(property.PropertyType))
        {
            return false;
        }

        MethodInfo? targetMethod = GetType().GetMethod(
            targetMethodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo? invokeMethod = property.PropertyType.GetMethod(nameof(Action.Invoke));
        if (targetMethod == null || invokeMethod == null)
        {
            return false;
        }

        ParameterInfo[] delegateParameters = invokeMethod.GetParameters();
        if (delegateParameters.Length != pointParameterCount ||
            !IsDelegateReturnCompatible(invokeMethod.ReturnType, targetMethod.ReturnType))
        {
            return false;
        }

        var parameters = new ParameterExpression[delegateParameters.Length];
        var arguments = new Expression[pointParameterCount * 2];
        for (int i = 0; i < delegateParameters.Length; i++)
        {
            ParameterInfo delegateParameter = delegateParameters[i];
            ParameterExpression point = Expression.Parameter(
                delegateParameter.ParameterType,
                delegateParameter.Name ?? $"point{i}");
            if (!TryCreatePointCoordinateRead(point, "X", out Expression? x) ||
                !TryCreatePointCoordinateRead(point, "Y", out Expression? y))
            {
                return false;
            }

            parameters[i] = point;
            arguments[i * 2] = x!;
            arguments[(i * 2) + 1] = y!;
        }

        Expression body = Expression.Call(Expression.Constant(this), targetMethod, arguments);
        if (body.Type != invokeMethod.ReturnType)
        {
            body = Expression.Convert(body, invokeMethod.ReturnType);
        }

        try
        {
            handler = Expression.Lambda(property.PropertyType, body, parameters).Compile();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsDelegateReturnCompatible(Type delegateReturnType, Type methodReturnType)
    {
        return delegateReturnType == methodReturnType ||
            delegateReturnType.IsAssignableFrom(methodReturnType);
    }

    private static bool TryCreatePointCoordinateRead(
        Expression point,
        string coordinateName,
        out Expression? coordinate)
    {
        coordinate = null;
        PropertyInfo? property = point.Type.GetProperty(
            coordinateName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null || !property.CanRead)
        {
            return false;
        }

        Expression value = Expression.Property(point, property);
        coordinate = value.Type == typeof(double)
            ? value
            : Expression.Convert(value, typeof(double));
        return true;
    }

    private object? TryHitTestOwner(double rootX, double rootY)
    {
        if (_host.TryHitTestOwners(rootX, rootY, out object?[] owners) &&
            TrySelectPointerInputOwner(owners, out object? selectedOwner))
        {
            TraceHitTestOwners(rootX, rootY, owners, selectedOwner);
            return selectedOwner;
        }

        object? fallbackOwner = _host.TryHitTestOwner(rootX, rootY, out object? owner) &&
            owner != null
                ? NormalizePointerInputOwner(owner)
                : _host.HasGpuHitTestCache ? Source : null;
        TraceHitTestOwners(rootX, rootY, owners: null, fallbackOwner);
        return fallbackOwner;
    }

    private static bool TrySelectPointerInputOwner(object?[] owners, out object? selectedOwner)
    {
        selectedOwner = null;
        int selectedDepth = -1;

        foreach (object? owner in owners)
        {
            if (owner == null)
            {
                continue;
            }

            if (!TryNormalizePointerInputOwner(owner, out object? normalizedOwner) ||
                normalizedOwner == null)
            {
                continue;
            }

            int depth = GetVisualDepth(normalizedOwner);
            if (depth > selectedDepth)
            {
                selectedOwner = normalizedOwner;
                selectedDepth = depth;
            }
        }

        if (selectedOwner != null)
        {
            return true;
        }

        object? deepestEnabledOwner = null;
        int deepestEnabledDepth = -1;
        foreach (object? owner in owners)
        {
            if (owner == null || IsTransparentPointerOverlay(owner))
            {
                continue;
            }

            object enabledOwner = NormalizePointerInputOwner(owner);
            int depth = GetVisualDepth(enabledOwner);
            if (depth > deepestEnabledDepth)
            {
                deepestEnabledOwner = enabledOwner;
                deepestEnabledDepth = depth;
            }
        }

        selectedOwner = deepestEnabledOwner;
        return selectedOwner != null;
    }

    private static object NormalizePointerInputOwner(object owner)
    {
        return TryNormalizePointerInputOwner(owner, out object? normalizedOwner)
            ? normalizedOwner!
            : owner;
    }

    private static bool TryNormalizePointerInputOwner(object owner, out object? normalizedOwner)
    {
        normalizedOwner = null;
        if (IsTransparentPointerOverlay(owner))
        {
            return false;
        }

        object? firstEnabledOwner = null;
        object? current = owner;
        for (int depth = 0; current != null && depth < 128; depth++)
        {
            if (IsEnabledInputOwner(current))
            {
                firstEnabledOwner ??= current;
                if (IsWindowOwner(current))
                {
                    normalizedOwner = firstEnabledOwner;
                    return normalizedOwner != null;
                }

                if (!IsPointerInputInfrastructure(current))
                {
                    normalizedOwner = current;
                    return true;
                }
            }

            current = TryGetVisualParent(current);
        }

        normalizedOwner = firstEnabledOwner;
        return normalizedOwner != null;
    }

    private static int GetVisualDepth(object owner)
    {
        int depth = 0;
        object? current = owner;
        while (current != null && depth < 128)
        {
            object? parent = TryGetVisualParent(current);
            if (parent == null)
            {
                break;
            }

            depth++;
            current = parent;
        }

        return depth;
    }

    private static bool IsEnabledInputOwner(object owner)
    {
        return ReadBooleanProperty(owner, "IsEnabled", defaultValue: true) &&
            ReadBooleanProperty(owner, "IsVisible", defaultValue: true) &&
            ReadBooleanProperty(owner, "IsHitTestVisible", defaultValue: true);
    }

    private static bool IsTransparentPointerOverlay(object owner)
    {
        string name = owner.GetType().Name;
        return string.Equals(name, "AdornerLayer", StringComparison.Ordinal) ||
            string.Equals(name, "AdornerDecorator", StringComparison.Ordinal);
    }

    private static bool IsPointerInputInfrastructure(object owner)
    {
        Type type = owner.GetType();
        string name = type.Name;
        if (string.Equals(name, "Border", StringComparison.Ordinal) ||
            string.Equals(name, "Decorator", StringComparison.Ordinal) ||
            string.Equals(name, "ContentPresenter", StringComparison.Ordinal) ||
            string.Equals(name, "ScrollContentPresenter", StringComparison.Ordinal) ||
            string.Equals(name, "ItemsPresenter", StringComparison.Ordinal))
        {
            return true;
        }

        for (Type? current = type; current != null; current = current.BaseType)
        {
            string currentName = current.Name;
            string? currentFullName = current.FullName;
            if (string.Equals(currentName, "Panel", StringComparison.Ordinal) ||
                string.Equals(currentName, "Grid", StringComparison.Ordinal) ||
                string.Equals(currentName, "StackPanel", StringComparison.Ordinal) ||
                string.Equals(currentName, "DockPanel", StringComparison.Ordinal) ||
                string.Equals(currentName, "Canvas", StringComparison.Ordinal) ||
                string.Equals(currentName, "WrapPanel", StringComparison.Ordinal) ||
                string.Equals(currentName, "UniformGrid", StringComparison.Ordinal) ||
                string.Equals(currentFullName, "System.Windows.Controls.Primitives.ToolBarPanel", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWindowOwner(object owner)
    {
        for (Type? current = owner.GetType(); current != null; current = current.BaseType)
        {
            if (string.Equals(current.FullName, "System.Windows.Window", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReadBooleanProperty(object owner, string propertyName, bool defaultValue)
    {
        PropertyInfo? property = owner.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null ||
            property.PropertyType != typeof(bool) ||
            !property.CanRead)
        {
            return defaultValue;
        }

        return (bool)(property.GetValue(owner) ?? defaultValue);
    }

    private static object? TryGetVisualParent(object current)
    {
        MethodInfo? getParentMethod = ResolveVisualTreeHelperGetParentMethod(current.GetType());
        if (getParentMethod == null)
        {
            return null;
        }

        try
        {
            return getParentMethod.Invoke(null, new[] { current });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static MethodInfo? ResolveVisualTreeHelperGetParentMethod(Type ownerType)
    {
        MethodInfo? cached = s_visualTreeHelperGetParentMethod;
        if (CanInvokeVisualTreeHelperGetParent(cached, ownerType))
        {
            return cached;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? helperType = assembly.GetType("System.Windows.Media.VisualTreeHelper", throwOnError: false);
            if (helperType == null)
            {
                continue;
            }

            MethodInfo? method = null;
            foreach (MethodInfo candidate in helperType.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (string.Equals(candidate.Name, "GetParent", StringComparison.Ordinal) &&
                    CanInvokeVisualTreeHelperGetParent(candidate, ownerType))
                {
                    method = candidate;
                    break;
                }
            }

            if (method == null)
            {
                continue;
            }

            s_visualTreeHelperGetParentMethod = method;
            return method;
        }

        return null;
    }

    private static bool CanInvokeVisualTreeHelperGetParent(MethodInfo? method, Type ownerType)
    {
        if (method == null)
        {
            return false;
        }

        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length == 1 &&
            parameters[0].ParameterType.IsAssignableFrom(ownerType);
    }

    private static void TraceHitTestOwners(
        double rootX,
        double rootY,
        object?[]? owners,
        object? selectedOwner)
    {
        if (!IsHitTestTraceEnabled())
        {
            return;
        }

        string ownerList = owners == null
            ? "<none>"
            : string.Join(", ", owners.Select(DescribeHitTestOwner));
        Console.Error.WriteLine(
            $"ProGPU WPF GPU hit-test ({rootX:0.###},{rootY:0.###}) owners=[{ownerList}] selected={DescribeHitTestOwner(selectedOwner)}");
    }

    private static bool IsHitTestTraceEnabled()
    {
        string? value = Environment.GetEnvironmentVariable("PROGPU_WPF_TRACE_HIT_TEST");
        return string.Equals(value, "1", StringComparison.Ordinal) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeHitTestOwner(object? owner)
    {
        if (owner == null)
        {
            return "<null>";
        }

        string typeName = owner.GetType().Name;
        string? name = owner.GetType()
            .GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(owner)
            ?.ToString();
        return string.IsNullOrWhiteSpace(name) ? typeName : $"{typeName}#{name}";
    }

    private object?[]? HitTestOwners(double rootX, double rootY)
    {
        if (_host.TryHitTestOwners(rootX, rootY, out object?[] owners))
        {
            return owners;
        }

        return _host.HasGpuHitTestCache ? Array.Empty<object>() : null;
    }

    private object?[]? HitTestBoundsOwners(double minX, double minY, double maxX, double maxY)
    {
        if (_host.TryQueryHitTestBoundsCandidates(
                minX,
                minY,
                maxX,
                maxY,
                out object?[] candidates))
        {
            return candidates;
        }

        return _host.HasGpuHitTestCache ? Array.Empty<object>() : null;
    }

    private object?[]? HitTestEllipseBoundsOwners(double minX, double minY, double maxX, double maxY)
    {
        if (_host.TryQueryHitTestEllipseCandidates(
                minX,
                minY,
                maxX,
                maxY,
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
