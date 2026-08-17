using System;
using System.Collections.Generic;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class SilkNetWpfMonitorService : IWpfMonitorService
{
    private readonly Func<IEnumerable<IMonitor>> _getMonitors;
    private readonly Func<IMonitor?> _getMainMonitor;
    private readonly Func<IMonitor, double?>? _getDpiScale;

    public SilkNetWpfMonitorService()
        : this(GetDefaultMonitors, GetDefaultMainMonitor, TryGetGlfwMonitorContentScale)
    {
    }

    public SilkNetWpfMonitorService(IWindowPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        _getMonitors = platform.GetMonitors;
        _getMainMonitor = platform.GetMainMonitor;
        _getDpiScale = TryGetGlfwMonitorContentScale;
    }

    public SilkNetWpfMonitorService(
        Func<IEnumerable<IMonitor>> getMonitors,
        Func<IMonitor?> getMainMonitor,
        Func<IMonitor, double?>? getDpiScale = null)
    {
        _getMonitors = getMonitors ?? throw new ArgumentNullException(nameof(getMonitors));
        _getMainMonitor = getMainMonitor ?? throw new ArgumentNullException(nameof(getMainMonitor));
        _getDpiScale = getDpiScale;
    }

    public IReadOnlyList<WpfMonitorInfo> GetMonitors()
    {
        var mainMonitor = _getMainMonitor();
        var monitors = _getMonitors();
        var mapped = monitors is ICollection<IMonitor> monitorCollection
            ? new List<WpfMonitorInfo>(monitorCollection.Count)
            : new List<WpfMonitorInfo>();

        foreach (var monitor in monitors)
        {
            mapped.Add(ToMonitorInfo(monitor, mainMonitor, _getDpiScale));
        }

        return mapped;
    }

    public static WpfMonitorInfo ToMonitorInfo(IMonitor monitor, IMonitor? mainMonitor)
    {
        return ToMonitorInfo(monitor, mainMonitor, getDpiScale: null);
    }

    public static WpfMonitorInfo ToMonitorInfo(
        IMonitor monitor,
        IMonitor? mainMonitor,
        Func<IMonitor, double?>? getDpiScale)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var bounds = monitor.Bounds;
        var width = bounds.Size.X;
        var height = bounds.Size.Y;

        if ((width <= 0 || height <= 0) && monitor.VideoMode.Resolution is Vector2D<int> resolution)
        {
            width = resolution.X;
            height = resolution.Y;
        }

        return new WpfMonitorInfo(
            monitor.Name,
            bounds.Origin.X,
            bounds.Origin.Y,
            Math.Max(0, width),
            Math.Max(0, height),
            ResolveDpiScale(monitor, width, height, getDpiScale?.Invoke(monitor)),
            IsPrimary: ReferenceEquals(monitor, mainMonitor) || monitor.Index == mainMonitor?.Index);
    }

    internal static double ResolveDpiScale(IMonitor monitor, int boundsWidth, int boundsHeight)
    {
        return ResolveDpiScale(monitor, boundsWidth, boundsHeight, explicitScale: null);
    }

    internal static double ResolveDpiScale(
        IMonitor monitor,
        int boundsWidth,
        int boundsHeight,
        double? explicitScale)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (explicitScale is double scale && IsUsableScale(scale))
        {
            return NormalizeScale(scale);
        }

        if (boundsWidth > 0
            && boundsHeight > 0
            && monitor.VideoMode.Resolution is Vector2D<int> resolution
            && resolution.X > 0
            && resolution.Y > 0)
        {
            var scaleX = resolution.X / (double)boundsWidth;
            var scaleY = resolution.Y / (double)boundsHeight;
            if (IsUsableScale(scaleX) && IsUsableScale(scaleY))
            {
                return NormalizeScale((scaleX + scaleY) / 2);
            }
        }

        return 1.0;
    }

    private static bool IsUsableScale(double scale)
    {
        return !double.IsNaN(scale)
            && !double.IsInfinity(scale)
            && scale > 0
            && scale <= 8;
    }

    private static double NormalizeScale(double scale)
    {
        return Math.Round(scale, 4, MidpointRounding.AwayFromZero);
    }

    private static IEnumerable<IMonitor> GetDefaultMonitors()
    {
        return GetDefaultPlatform().GetMonitors();
    }

    private static IMonitor? GetDefaultMainMonitor()
    {
        return GetDefaultPlatform().GetMainMonitor();
    }

    private static IWindowPlatform GetDefaultPlatform()
    {
        try
        {
            return Window.GetWindowPlatform(false)
                ?? throw new PlatformNotSupportedException("Silk.NET did not return a window platform for monitor enumeration.");
        }
        catch (Exception exception)
        {
            throw new PlatformNotSupportedException("Silk.NET monitor enumeration is not available on this platform.", exception);
        }
    }

    private static unsafe double? TryGetGlfwMonitorContentScale(IMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        try
        {
            Glfw glfw = GlfwProvider.GLFW.Value;
            Silk.NET.GLFW.Monitor** nativeMonitors = glfw.GetMonitors(out int monitorCount);
            if (nativeMonitors == null || monitor.Index < 0 || monitor.Index >= monitorCount)
            {
                return null;
            }

            glfw.GetMonitorContentScale(nativeMonitors[monitor.Index], out float scaleX, out float scaleY);
            return SilkNetGlfwDpiService.TryNormalizeContentScale(scaleX, scaleY, out WpfDeviceScale scale)
                ? scale.Average
                : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (GlfwException)
        {
            return null;
        }
    }
}
