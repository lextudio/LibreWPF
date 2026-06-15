using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class SilkNetWpfMonitorService : IWpfMonitorService
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Func<IEnumerable<IMonitor>> _getMonitors;
    private readonly Func<IMonitor?> _getMainMonitor;

    public SilkNetWpfMonitorService()
        : this(GetDefaultMonitors, GetDefaultMainMonitor)
    {
    }

    public SilkNetWpfMonitorService(IWindowPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        _getMonitors = platform.GetMonitors;
        _getMainMonitor = platform.GetMainMonitor;
    }

    public SilkNetWpfMonitorService(
        Func<IEnumerable<IMonitor>> getMonitors,
        Func<IMonitor?> getMainMonitor)
    {
        _getMonitors = getMonitors ?? throw new ArgumentNullException(nameof(getMonitors));
        _getMainMonitor = getMainMonitor ?? throw new ArgumentNullException(nameof(getMainMonitor));
    }

    public IReadOnlyList<WpfMonitorInfo> GetMonitors()
    {
        var mainMonitor = _getMainMonitor();
        return _getMonitors()
            .Select(monitor => ToMonitorInfo(monitor, mainMonitor))
            .ToArray();
    }

    public static WpfMonitorInfo ToMonitorInfo(IMonitor monitor, IMonitor? mainMonitor)
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
            ResolveDpiScale(monitor, width, height),
            IsPrimary: ReferenceEquals(monitor, mainMonitor) || monitor.Index == mainMonitor?.Index);
    }

    internal static double ResolveDpiScale(IMonitor monitor, int boundsWidth, int boundsHeight)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (TryReadScalarScale(monitor, out var scalarScale))
        {
            return scalarScale;
        }

        if (TryReadDpiScale(monitor, out var dpiScale))
        {
            return dpiScale;
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

    private static bool TryReadScalarScale(IMonitor monitor, out double scale)
    {
        foreach (var propertyName in new[] { "DpiScale", "Scale", "ContentScale", "PixelScale", "BackingScaleFactor" })
        {
            if (TryGetPropertyValue(monitor, propertyName, out var value)
                && TryConvertToDouble(value, out var candidate)
                && IsUsableScale(candidate))
            {
                scale = NormalizeScale(candidate);
                return true;
            }
        }

        scale = 0;
        return false;
    }

    private static bool TryReadDpiScale(IMonitor monitor, out double scale)
    {
        if (TryReadDpiPair(monitor, "DpiX", "DpiY", out scale)
            || TryReadDpiPair(monitor, "HorizontalDpi", "VerticalDpi", out scale))
        {
            return true;
        }

        foreach (var propertyName in new[] { "Dpi", "PixelsPerInch" })
        {
            if (TryGetPropertyValue(monitor, propertyName, out var value)
                && TryConvertToDouble(value, out var dpi)
                && dpi > 0)
            {
                var candidate = dpi / 96.0;
                if (IsUsableScale(candidate))
                {
                    scale = NormalizeScale(candidate);
                    return true;
                }
            }
        }

        scale = 0;
        return false;
    }

    private static bool TryReadDpiPair(IMonitor monitor, string xPropertyName, string yPropertyName, out double scale)
    {
        scale = 0;
        if (!TryGetPropertyValue(monitor, xPropertyName, out var xValue)
            || !TryGetPropertyValue(monitor, yPropertyName, out var yValue)
            || !TryConvertToDouble(xValue, out var dpiX)
            || !TryConvertToDouble(yValue, out var dpiY)
            || dpiX <= 0
            || dpiY <= 0)
        {
            return false;
        }

        var candidate = ((dpiX / 96.0) + (dpiY / 96.0)) / 2.0;
        if (!IsUsableScale(candidate))
        {
            return false;
        }

        scale = NormalizeScale(candidate);
        return true;
    }

    private static bool TryGetPropertyValue(object instance, string propertyName, out object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, MemberFlags);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            value = null;
            return false;
        }

        value = property.GetValue(instance);
        return true;
    }

    private static bool TryConvertToDouble(object? value, out double result)
    {
        switch (value)
        {
            case double doubleValue:
                result = doubleValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case uint uintValue:
                result = uintValue;
                return true;
            default:
                result = 0;
                return false;
        }
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
}
