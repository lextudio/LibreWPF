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
}
