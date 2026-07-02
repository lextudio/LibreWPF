using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.ProGPU.Platform;

namespace System.Windows.Media.ProGPU;

public sealed class WpfPortablePresentationSourceBridge : IDisposable
{
    private readonly ProGpuWpfWindowHost _host;
    private readonly IPortablePresentationSourceHost _source;
    private readonly bool _ownsSource;
    private Func<double, double, object?>? _hitTestOverrideHandler;
    private Func<double, double, object?[]?>? _hitTestAllOverrideHandler;
    private Func<double, double, double, double, object?[]?>? _hitTestBoundsOverrideHandler;
    private Func<double, double, double, double, object?[]?>? _hitTestEllipseBoundsOverrideHandler;
    private bool _isDisposed;

    private WpfPortablePresentationSourceBridge(
        ProGpuWpfWindowHost host,
        IPortablePresentationSourceHost source,
        bool ownsSource)
    {
        _host = host;
        _source = source;
        _ownsSource = ownsSource;
    }

    public object Source => _source;

    public object? CompositionTarget => _source.CompositionTarget;

    public IntPtr Handle => _source.Handle;

    public object? RootVisual
    {
        get => _source.RootVisual;
        set
        {
            ThrowIfDisposed();
            _source.RootVisual = value;
            SyncHostRootVisual();
        }
    }

    public static bool TryCreate(
        ProGpuWpfWindowHost host,
        out WpfPortablePresentationSourceBridge? bridge)
    {
        return TryCreate(host, 1.0, 1.0, out bridge);
    }

    public static bool TryCreate(
        ProGpuWpfWindowHost host,
        double dpiScaleX,
        double dpiScaleY,
        out WpfPortablePresentationSourceBridge? bridge)
    {
        ArgumentNullException.ThrowIfNull(host);

        IPortablePresentationSourceHost source;
        try
        {
            source = PortablePresentationSourceHost.Create(dpiScaleX, dpiScaleY);
        }
        catch (PlatformNotSupportedException)
        {
            bridge = null;
            return false;
        }

        return TryBind(host, source, ownsSource: true, out bridge);
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
        _source.SetDeviceScale(dpiScaleX, dpiScaleY);
        return true;
    }

    public bool TrySetClientSize(double width, double height)
    {
        ThrowIfDisposed();
        _source.SetClientSize(width, height);
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

        _source.RenderRequested -= OnSourceRenderRequested;
        _source.CursorRequested -= OnSourceCursorRequested;

        if (_hitTestOverrideHandler != null &&
            ReferenceEquals(_source.HitTestOverride, _hitTestOverrideHandler))
        {
            _source.HitTestOverride = null;
        }

        if (_hitTestAllOverrideHandler != null &&
            ReferenceEquals(_source.HitTestAllOverride, _hitTestAllOverrideHandler))
        {
            _source.HitTestAllOverride = null;
        }

        if (_hitTestBoundsOverrideHandler != null &&
            ReferenceEquals(_source.HitTestBoundsOverride, _hitTestBoundsOverrideHandler))
        {
            _source.HitTestBoundsOverride = null;
        }

        if (_hitTestEllipseBoundsOverrideHandler != null &&
            ReferenceEquals(_source.HitTestEllipseBoundsOverride, _hitTestEllipseBoundsOverrideHandler))
        {
            _source.HitTestEllipseBoundsOverride = null;
        }

        object? rootVisual = _source.RootVisual;
        if (ReferenceEquals(_host.WpfRootVisual, rootVisual))
        {
            _host.WpfRootVisual = null;
        }

        if (_ownsSource)
        {
            _source.Dispose();
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

        if (presentationSource is not IPortablePresentationSourceHost source)
        {
            bridge = null;
            return false;
        }

        bridge = new WpfPortablePresentationSourceBridge(host, source, ownsSource);
        bridge.SubscribeToSource();
        bridge.InstallHitTestOverrides();
        bridge.SyncHostRootVisual();
        return true;
    }

    private void SubscribeToSource()
    {
        _source.RenderRequested += OnSourceRenderRequested;
        _source.CursorRequested += OnSourceCursorRequested;
    }

    private void InstallHitTestOverrides()
    {
        _hitTestOverrideHandler = TryHitTestOwner;
        _hitTestAllOverrideHandler = HitTestOwners;
        _hitTestBoundsOverrideHandler = HitTestBoundsOwners;
        _hitTestEllipseBoundsOverrideHandler = HitTestEllipseBoundsOwners;

        _source.HitTestOverride = _hitTestOverrideHandler;
        _source.HitTestAllOverride = _hitTestAllOverrideHandler;
        _source.HitTestBoundsOverride = _hitTestBoundsOverrideHandler;
        _source.HitTestEllipseBoundsOverride = _hitTestEllipseBoundsOverrideHandler;
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

    private void OnSourceCursorRequested(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        _host.ApplyPortableCursor(ToWpfCursor(_source.RequestedCursorName ?? _source.RequestedCursor?.ToString()));
    }

    private object? TryHitTestOwner(double rootX, double rootY)
    {
        if (_host.TryHitTestOwners(rootX, rootY, out object?[] owners))
        {
            if (TrySelectPointerInputOwner(owners, out object? selectedOwner))
            {
                TraceHitTestOwners(rootX, rootY, owners, selectedOwner);
                return selectedOwner;
            }

            object? handledMiss = _host.HasGpuHitTestCache ? Source : null;
            TraceHitTestOwners(rootX, rootY, owners, handledMiss);
            return handledMiss;
        }

        object? fallbackOwner = _host.HasGpuHitTestCache ? Source : null;
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
            if (IsTransparentPointerOverlay(current))
            {
                current = TryGetVisualParent(current);
                continue;
            }

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
        return owner is not IPortableVisualOwnerHost host || host.IsPortableInputEnabled;
    }

    private static bool IsTransparentPointerOverlay(object owner)
    {
        return owner is IPortableVisualOwnerHost
        {
            PortableVisualOwnerKind: PortableVisualOwnerKind.TransparentPointerOverlay
        };
    }

    private static bool IsPointerInputInfrastructure(object owner)
    {
        return owner is IPortableVisualOwnerHost
        {
            PortableVisualOwnerKind: PortableVisualOwnerKind.PointerInfrastructure
        };
    }

    private static bool IsWindowOwner(object owner)
    {
        return owner is IPortableVisualOwnerHost
        {
            PortableVisualOwnerKind: PortableVisualOwnerKind.Window
        };
    }

    private static object? TryGetVisualParent(object current)
    {
        return current is IPortableVisualOwnerHost host ? host.PortableVisualParent : null;
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

        if (owner is IPortableVisualOwnerHost)
        {
            return "PortableVisualOwnerHost";
        }

        return owner is string label && !string.IsNullOrEmpty(label)
            ? label
            : "Owner";
    }

    private object?[]? HitTestOwners(double rootX, double rootY)
    {
        if (_host.TryHitTestOwners(rootX, rootY, out object?[] owners))
        {
            return FilterTransparentPointerOverlays(owners);
        }

        return _host.HasGpuHitTestCache ? Array.Empty<object>() : null;
    }

    private static object?[] FilterTransparentPointerOverlays(object?[] owners)
    {
        List<object?>? filteredOwners = null;
        for (int i = 0; i < owners.Length; i++)
        {
            object? owner = owners[i];
            if (owner != null && IsTransparentPointerOverlay(owner))
            {
                filteredOwners ??= new List<object?>(owners.Length);
                for (int j = 0; j < i; j++)
                {
                    filteredOwners.Add(owners[j]);
                }

                continue;
            }

            filteredOwners?.Add(owner);
        }

        return filteredOwners?.ToArray() ?? owners;
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

    private static WpfCursor ToWpfCursor(string? cursorName)
    {
        return cursorName switch
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
