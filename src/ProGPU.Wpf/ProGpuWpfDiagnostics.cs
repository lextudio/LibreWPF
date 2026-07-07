namespace System.Windows.Media.ProGPU;

public static class ProGpuWpfDiagnostics
{
    public readonly record struct RenderSurfaceGeometrySnapshot(
        uint LogicalWidth,
        uint LogicalHeight,
        uint PixelWidth,
        uint PixelHeight,
        double DpiScaleX,
        double DpiScaleY,
        double DpiScale,
        uint ViewportX,
        uint ViewportY,
        uint ViewportWidth,
        uint ViewportHeight);

    public readonly record struct GpuHitTestCacheSnapshot(
        bool HasIndex,
        bool HasDeviceIndex,
        int PrimitiveCount,
        int NodeCount,
        int PrimitiveIndexCount,
        int PathSegmentCount,
        int OwnerCount);

    public readonly record struct CompositionLayerSnapshot(
        bool HasCompositionTarget,
        int SceneRootChildCount,
        int RetainedLayerIndex,
        int FlatLayerIndex,
        int PopupLayerIndex,
        int RetainedLayerChildCount,
        int PopupLayerChildCount);

    public static bool TryGetWindowHost(object? window, out ProGpuWpfWindowHost? host)
    {
        if (window is ProGpuWpfWindowHost directHost)
        {
            host = directHost;
            return true;
        }

        return WpfPortableWindowActivation.TryGetActiveHost(window, out host);
    }

    public static bool TryRequestRender(object? window)
    {
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        host.RequestRenderAndWakeNativeLoop();
        return true;
    }

    public static bool TryWakeNativeLoop(object? window)
    {
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryRequestNativeLoopWakeup();
    }

    public static bool TryGetRenderSchedulerWakeupCount(object? window, out long wakeupCount)
    {
        wakeupCount = 0;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        wakeupCount = host.RenderSchedulerWakeupCount;
        return true;
    }

    public static bool TryGetRenderSurfaceGeometry(object? window, out RenderSurfaceGeometrySnapshot geometry)
    {
        geometry = default;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        geometry = CreateSnapshot(host.ResolveCurrentRenderSurfaceGeometryForDiagnostics());
        return true;
    }

    public static bool TryGetCompositionLayerSnapshot(object? window, out CompositionLayerSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        var target = host.CompositionTarget;
        if (target == null)
        {
            return false;
        }

        var sceneChildren = target.SceneRootVisual.Children;
        snapshot = new CompositionLayerSnapshot(
            HasCompositionTarget: true,
            SceneRootChildCount: sceneChildren.Count,
            RetainedLayerIndex: IndexOfChild(sceneChildren, target.RetainedWpfVisualRoot),
            FlatLayerIndex: IndexOfChild(sceneChildren, target.RootVisual),
            PopupLayerIndex: IndexOfChild(sceneChildren, target.PopupRetainedWpfVisualRoot),
            RetainedLayerChildCount: target.RetainedWpfVisualRoot.Children.Count,
            PopupLayerChildCount: target.PopupRetainedWpfVisualRoot.Children.Count);
        return true;
    }

    public static bool TryRaiseInput(
        object? window,
        Platform.WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        host.RaiseInputForDiagnostics(input);
        return true;
    }

    public static bool HasGpuHitTestCache(object? window)
    {
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.HasGpuHitTestCache;
    }

    public static bool TryGetGpuHitTestCacheSnapshot(object? window, out GpuHitTestCacheSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryGetGpuHitTestCacheSnapshot(out snapshot);
    }

    public static bool TryHitTestOwner(object? window, double x, double y, out object? owner)
    {
        owner = null;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryHitTestOwner(x, y, out owner);
    }

    public static bool TryHitTestOwners(object? window, double x, double y, out object?[] owners)
    {
        owners = Array.Empty<object?>();
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryHitTestOwners(x, y, out owners);
    }

    public static bool TryHitTestOwners(object? window, double x, double y, Span<object?> owners, out int ownerCount)
    {
        ownerCount = 0;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryHitTestOwners(x, y, owners, out ownerCount);
    }

    public static bool TryQueryHitTestBoundsOwners(
        object? window,
        double minX,
        double minY,
        double maxX,
        double maxY,
        out object?[] owners)
    {
        owners = Array.Empty<object?>();
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryQueryHitTestBoundsOwners(minX, minY, maxX, maxY, out owners);
    }

    public static bool TryQueryHitTestBoundsOwners(
        object? window,
        double minX,
        double minY,
        double maxX,
        double maxY,
        Span<object?> owners,
        out int ownerCount)
    {
        ownerCount = 0;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryQueryHitTestBoundsOwners(minX, minY, maxX, maxY, owners, out ownerCount);
    }

    private static RenderSurfaceGeometrySnapshot CreateSnapshot(
        ProGpuWpfWindowHost.RenderSurfaceGeometry geometry)
    {
        return new RenderSurfaceGeometrySnapshot(
            geometry.LogicalWidth,
            geometry.LogicalHeight,
            geometry.PixelWidth,
            geometry.PixelHeight,
            geometry.DpiScaleX,
            geometry.DpiScaleY,
            geometry.DpiScale,
            geometry.ViewportX,
            geometry.ViewportY,
            geometry.ViewportWidth,
            geometry.ViewportHeight);
    }

    private static int IndexOfChild(
        System.Collections.Generic.IReadOnlyList<global::ProGPU.Scene.Visual> children,
        global::ProGPU.Scene.Visual target)
    {
        for (int i = 0; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], target))
            {
                return i;
            }
        }

        return -1;
    }
}
