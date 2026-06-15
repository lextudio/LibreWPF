using System;
using System.Collections.Generic;
using MediaBrush = System.Windows.Media.Brush;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfMilResourceRegistry : IWpfMilResourceResolver, IWpfGuidelineSetResourceResolver
{
    private readonly Dictionary<uint, object> _resources = new();

    public static WpfMilResourceRegistry FromDependentResources(IEnumerable<object?> dependentResources)
    {
        ArgumentNullException.ThrowIfNull(dependentResources);

        var registry = new WpfMilResourceRegistry();
        uint token = 1;
        foreach (var resource in dependentResources)
        {
            if (resource != null)
            {
                registry.Register(token, resource);
            }

            token++;
        }

        return registry;
    }

    public void Register(uint resourceToken, object resource)
    {
        if (resourceToken == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(resourceToken), "WPF MIL dependent resource tokens are one-based.");
        }

        ArgumentNullException.ThrowIfNull(resource);
        _resources[resourceToken] = resource;
    }

    public MediaBrush? ResolveBrush(uint resourceToken)
    {
        return Resolve<MediaBrush>(resourceToken);
    }

    public MediaPen? ResolvePen(uint resourceToken)
    {
        return Resolve<MediaPen>(resourceToken);
    }

    public MediaGeometry? ResolveGeometry(uint resourceToken)
    {
        return Resolve<MediaGeometry>(resourceToken);
    }

    public MediaImageSource? ResolveImageSource(uint resourceToken)
    {
        return Resolve<MediaImageSource>(resourceToken);
    }

    public MediaGlyphRun? ResolveGlyphRun(uint resourceToken)
    {
        return Resolve<MediaGlyphRun>(resourceToken);
    }

    public MediaTransform? ResolveTransform(uint resourceToken)
    {
        return Resolve<MediaTransform>(resourceToken);
    }

    public object? ResolveGuidelineSet(uint resourceToken)
    {
        return resourceToken != 0 && _resources.TryGetValue(resourceToken, out var resource)
            ? resource
            : null;
    }

    private T? Resolve<T>(uint resourceToken) where T : class
    {
        if (resourceToken == 0)
        {
            return null;
        }

        return _resources.TryGetValue(resourceToken, out var resource)
            ? resource as T
            : null;
    }
}
