namespace System.Windows.Media.ProGPU;

public static class ProGpuWpfDiagnostics
{
    public static bool TryGetWindowHost(object? window, out ProGpuWpfWindowHost? host)
    {
        return WpfPortableWindowActivation.TryGetActiveHost(window, out host);
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
}
