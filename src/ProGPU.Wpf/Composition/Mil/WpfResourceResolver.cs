using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Text;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaMatrixTransform = System.Windows.Media.MatrixTransform;
using MediaPen = System.Windows.Media.Pen;
using MediaPenLineCap = System.Windows.Media.PenLineCap;
using MediaTransform = System.Windows.Media.Transform;
using PortableBrush = ProGPU.Wpf.Interop.PortableBrush;
using PortableBrushMappingMode = ProGPU.Wpf.Interop.PortableBrushMappingMode;
using PortableBrushKind = ProGPU.Wpf.Interop.PortableBrushKind;
using PortableColor = ProGPU.Wpf.Interop.PortableColor;
using PortableGradientColorInterpolationMode = ProGPU.Wpf.Interop.PortableGradientColorInterpolationMode;
using PortableGradientSpreadMethod = ProGPU.Wpf.Interop.PortableGradientSpreadMethod;
using PortableGradientStop = ProGPU.Wpf.Interop.PortableGradientStop;
using PortableFillRule = ProGPU.Wpf.Interop.PortableFillRule;
using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableGeometryPathKind = ProGPU.Wpf.Interop.PortableGeometryPathKind;
using PortableGeometryPathSource = ProGPU.Wpf.Interop.IPortableGeometryPathSource;
using PortableGlyphRun = ProGPU.Wpf.Interop.PortableGlyphRun;
using PortableGlyphRunSource = ProGPU.Wpf.Interop.IPortableGlyphRunSource;
using PortableNativeGlyphRun = ProGPU.Wpf.Interop.PortableNativeGlyphRun;
using PortableNativeGlyphRunSource = ProGPU.Wpf.Interop.IPortableNativeGlyphRunSource;
using PortablePathSegment = ProGPU.Wpf.Interop.PortablePathSegment;
using PortablePathSegmentKind = ProGPU.Wpf.Interop.PortablePathSegmentKind;
using PortablePoint = ProGPU.Wpf.Interop.PortablePoint;
using PortableSize = ProGPU.Wpf.Interop.PortableSize;
using PortableSweepDirection = ProGPU.Wpf.Interop.PortableSweepDirection;
using PortableBrushSource = ProGPU.Wpf.Interop.IPortableBrushSource;
using PortablePen = ProGPU.Wpf.Interop.PortablePen;
using PortablePenLineCap = ProGPU.Wpf.Interop.PortablePenLineCap;
using PortablePenLineJoin = ProGPU.Wpf.Interop.PortablePenLineJoin;
using PortablePenSource = ProGPU.Wpf.Interop.IPortablePenSource;
using PortableMatrix3x2 = ProGPU.Wpf.Interop.PortableMatrix3x2;
using PortableTransformMatrixSource = ProGPU.Wpf.Interop.IPortableTransformMatrixSource;
using MediaBrushMappingMode = System.Windows.Media.BrushMappingMode;
using MediaColorInterpolationMode = System.Windows.Media.ColorInterpolationMode;
using MediaGradientSpreadMethod = System.Windows.Media.GradientSpreadMethod;
using MediaGradientStop = System.Windows.Media.GradientStop;
using MediaGradientStopCollection = System.Windows.Media.GradientStopCollection;
using MediaLinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using MediaRadialGradientBrush = System.Windows.Media.RadialGradientBrush;
using WpfPoint = System.Windows.Point;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal enum ProGpuBrushMappingMode
{
    RelativeToBoundingBox,
    Absolute
}

internal readonly struct WpfNativeGlyphRun
{
    public WpfNativeGlyphRun(
        ushort[] glyphIndices,
        Vector2[] glyphPositions,
        TtfFont font,
        float fontSize,
        Vector2 position,
        Matrix4x4 transform,
        bool isBold,
        bool isItalic)
    {
        GlyphIndices = glyphIndices;
        GlyphPositions = glyphPositions;
        Font = font;
        FontSize = fontSize;
        Position = position;
        Transform = transform;
        IsBold = isBold;
        IsItalic = isItalic;
    }

    public ushort[] GlyphIndices { get; }

    public Vector2[] GlyphPositions { get; }

    public TtfFont Font { get; }

    public float FontSize { get; }

    public Vector2 Position { get; }

    public Matrix4x4 Transform { get; }

    public bool IsBold { get; }

    public bool IsItalic { get; }
}

public sealed class WpfResourceResolver :
    IWpfMilResourceResolver,
    IWpfDrawingResourceResolver,
    IWpfGuidelineSetResourceResolver,
    IWpfRawMilResourceResolver
{
    private readonly struct WpfMatrix2D
    {
        public WpfMatrix2D(
            double m11,
            double m12,
            double m21,
            double m22,
            double offsetX,
            double offsetY)
        {
            M11 = m11;
            M12 = m12;
            M21 = m21;
            M22 = m22;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public static WpfMatrix2D Identity { get; } = new(1, 0, 0, 1, 0, 0);

        public double M11 { get; }

        public double M12 { get; }

        public double M21 { get; }

        public double M22 { get; }

        public double OffsetX { get; }

        public double OffsetY { get; }
    }

    private const int MaxSupportedGradientStops = 65536;
    private static readonly ConcurrentDictionary<string, TtfFont> s_fontFileCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<uint, object> _resources = new();
    private readonly Dictionary<uint, MediaBrush?> _brushes = new();
    private readonly Dictionary<uint, MediaPen?> _pens = new();
    private readonly Dictionary<uint, MediaGeometry?> _geometries = new();
    private readonly Dictionary<uint, MediaImageSource?> _imageSources = new();
    private readonly Dictionary<uint, MediaGlyphRun?> _glyphRuns = new();
    private readonly Dictionary<uint, MediaTransform?> _transforms = new();
    private readonly IWpfImageSourceAdapter? _imageSourceAdapter;

    public WpfResourceResolver()
    {
    }

    public WpfResourceResolver(IWpfImageSourceAdapter? imageSourceAdapter)
    {
        _imageSourceAdapter = imageSourceAdapter;
    }

    public static WpfResourceResolver FromDependentResources(
        IReadOnlyList<object?> dependentResources,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(dependentResources);

        var resolver = new WpfResourceResolver(imageSourceAdapter);
        for (var i = 0; i < dependentResources.Count; i++)
        {
            var resource = dependentResources[i];
            if (resource != null)
            {
                resolver.Register((uint)i + 1, resource);
            }
        }

        return resolver;
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
        return Resolve(resourceToken, _brushes, AdaptBrush);
    }

    public MediaPen? ResolvePen(uint resourceToken)
    {
        return Resolve(resourceToken, _pens, AdaptPen);
    }

    public MediaGeometry? ResolveGeometry(uint resourceToken)
    {
        return Resolve(resourceToken, _geometries, AdaptGeometry);
    }

    public MediaImageSource? ResolveImageSource(uint resourceToken)
    {
        return Resolve(resourceToken, _imageSources, AdaptImageSource);
    }

    public MediaGlyphRun? ResolveGlyphRun(uint resourceToken)
    {
        return Resolve(resourceToken, _glyphRuns, AdaptGlyphRun);
    }

    public MediaTransform? ResolveTransform(uint resourceToken)
    {
        return Resolve(resourceToken, _transforms, AdaptTransform);
    }

    public object? ResolveGuidelineSet(uint resourceToken)
    {
        return resourceToken != 0 && _resources.TryGetValue(resourceToken, out var resource)
            ? resource
            : null;
    }

    bool IWpfRawMilResourceResolver.TryResolveRawResource(uint resourceToken, out object resource)
    {
        if (resourceToken != 0 && _resources.TryGetValue(resourceToken, out resource!))
        {
            return true;
        }

        resource = null!;
        return false;
    }

    public bool TryReplayDrawing(uint resourceToken, IWpfCompositionCommandSink sink)
    {
        var status = ReplayDrawing(resourceToken, sink);
        return status == WpfDrawingReplayStatus.Applied
            || status == WpfDrawingReplayStatus.PartiallyApplied;
    }

    public WpfDrawingReplayStatus ReplayDrawing(uint resourceToken, IWpfCompositionCommandSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (resourceToken == 0 || !_resources.TryGetValue(resourceToken, out var drawing))
        {
            return WpfDrawingReplayStatus.Skipped;
        }

        return WpfDrawingReplay.Replay(drawing, sink, AdaptImageSource);
    }

    private T? Resolve<T>(uint resourceToken, Dictionary<uint, T?> cache, Func<object, T?> adapter)
        where T : class
    {
        if (resourceToken == 0)
        {
            return null;
        }

        if (cache.TryGetValue(resourceToken, out var cached))
        {
            return cached;
        }

        var resolved = _resources.TryGetValue(resourceToken, out var resource)
            ? adapter(resource)
            : null;

        cache[resourceToken] = resolved;
        return resolved;
    }

    public static MediaBrush? AdaptBrush(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is MediaBrush brush)
        {
            return brush;
        }

        if (resource is PortableBrushSource portableBrushSource)
        {
            return portableBrushSource.TryGetPortableBrush(out var portableBrush)
                ? AdaptPortableBrush(portableBrush)
                : null;
        }

        return null;
    }

    internal static global::ProGPU.Vector.Brush? AdaptNativeBrush(
        object? resource,
        WpfReplayRect bounds,
        out int unsupportedStateCount)
    {
        unsupportedStateCount = 0;
        if (resource == null)
        {
            return null;
        }

        if (resource is PortableBrushSource portableBrushSource)
        {
            return portableBrushSource.TryGetPortableBrush(out var portableBrush)
                ? AdaptNativePortableBrush(portableBrush, bounds, out unsupportedStateCount)
                : null;
        }

        return null;
    }

    internal static global::ProGPU.Vector.Pen? AdaptNativePen(
        object? resource,
        WpfReplayRect bounds,
        out int unsupportedStateCount)
    {
        unsupportedStateCount = 0;
        if (resource == null)
        {
            return null;
        }

        if (resource is PortablePenSource portablePenSource)
        {
            return portablePenSource.TryGetPortablePen(out var portablePen)
                ? AdaptNativePortablePen(portablePen, bounds, out unsupportedStateCount)
                : null;
        }

        return null;
    }

    private static bool IsUsable(WpfReplayRect bounds)
    {
        return bounds.Width > 0
            && bounds.Height > 0
            && double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height);
    }

    private static int CountUnsupportedGradientState(bool stopsTruncated, bool unsupportedColorInterpolationMode)
    {
        var count = stopsTruncated ? 1 : 0;
        if (unsupportedColorInterpolationMode)
        {
            count++;
        }

        return count;
    }

    public static MediaPen? AdaptPen(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is MediaPen pen)
        {
            return pen;
        }

        if (resource is PortablePenSource portablePenSource)
        {
            return portablePenSource.TryGetPortablePen(out var portablePen)
                ? AdaptPortablePen(portablePen)
                : null;
        }

        return null;
    }

    private static MediaBrush? AdaptPortableBrush(PortableBrush brush)
    {
        switch (brush.Kind)
        {
            case PortableBrushKind.SolidColor:
                return new SolidColorBrush(ToMediaColor(brush));

            case PortableBrushKind.LinearGradient:
                if (!TryCreatePortableLinearGradientMediaBrush(brush, out var linearBrush))
                {
                    return null;
                }

                return linearBrush;

            case PortableBrushKind.RadialGradient:
                if (!TryCreatePortableRadialGradientMediaBrush(brush, out var radialBrush))
                {
                    return null;
                }

                return radialBrush;

            default:
                return null;
        }
    }

    private static bool TryCreatePortableLinearGradientMediaBrush(
        PortableBrush brush,
        out MediaLinearGradientBrush mediaBrush)
    {
        mediaBrush = null!;
        if (!TryCreateMediaGradientStops(brush.GradientStops, out var stops))
        {
            return false;
        }

        mediaBrush = new MediaLinearGradientBrush(
            stops,
            new WpfPoint(brush.StartPoint.X, brush.StartPoint.Y),
            new WpfPoint(brush.EndPoint.X, brush.EndPoint.Y))
        {
            Opacity = ClampOpacity(brush.Opacity),
            MappingMode = ToMediaBrushMappingMode(brush.MappingMode),
            SpreadMethod = ToMediaGradientSpreadMethod(brush.SpreadMethod),
            ColorInterpolationMode = ToMediaColorInterpolationMode(brush.ColorInterpolationMode)
        };
        ApplyPortableBrushTransforms(brush, mediaBrush);
        return true;
    }

    private static bool TryCreatePortableRadialGradientMediaBrush(
        PortableBrush brush,
        out MediaRadialGradientBrush mediaBrush)
    {
        mediaBrush = null!;
        if (!TryCreateMediaGradientStops(brush.GradientStops, out var stops))
        {
            return false;
        }

        mediaBrush = new MediaRadialGradientBrush(stops)
        {
            Center = new WpfPoint(brush.Center.X, brush.Center.Y),
            GradientOrigin = new WpfPoint(brush.GradientOrigin.X, brush.GradientOrigin.Y),
            RadiusX = brush.RadiusX,
            RadiusY = brush.RadiusY,
            Opacity = ClampOpacity(brush.Opacity),
            MappingMode = ToMediaBrushMappingMode(brush.MappingMode),
            SpreadMethod = ToMediaGradientSpreadMethod(brush.SpreadMethod),
            ColorInterpolationMode = ToMediaColorInterpolationMode(brush.ColorInterpolationMode)
        };
        ApplyPortableBrushTransforms(brush, mediaBrush);
        return true;
    }

    private static bool TryCreateMediaGradientStops(
        PortableGradientStop[] portableStops,
        out MediaGradientStopCollection stops)
    {
        stops = null!;
        if (portableStops.Length == 0)
        {
            return false;
        }

        stops = new MediaGradientStopCollection(portableStops.Length);
        for (var i = 0; i < portableStops.Length; i++)
        {
            var stop = portableStops[i];
            stops.Add(new MediaGradientStop(
                ToMediaColor(stop.Color),
                stop.Offset));
        }

        return true;
    }

    private static void ApplyPortableBrushTransforms(PortableBrush source, MediaBrush target)
    {
        if (source.HasTransform
            && TryCreateMatrixTransform(ToWpfMatrix2D(source.Transform), out var transform)
            && transform != null)
        {
            target.Transform = transform;
        }

        if (source.HasRelativeTransform
            && TryCreateMatrixTransform(ToWpfMatrix2D(source.RelativeTransform), out var relativeTransform)
            && relativeTransform != null)
        {
            target.RelativeTransform = relativeTransform;
        }
    }

    private static global::ProGPU.Vector.Brush? AdaptNativePortableBrush(
        PortableBrush brush,
        WpfReplayRect bounds,
        out int unsupportedStateCount)
    {
        unsupportedStateCount = 0;
        switch (brush.Kind)
        {
            case PortableBrushKind.SolidColor:
                var color = ToMediaColor(brush);
                return new global::ProGPU.Vector.SolidColorBrush(
                    new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));

            case PortableBrushKind.LinearGradient:
                if (!TryCreatePortableLinearGradientBrush(brush, mapRelativeToBounds: false, default, out var linearBrush, out var linearStopsTruncated))
                {
                    return null;
                }

                return AdaptMappedNativeBrush(
                    linearBrush,
                    ToProGpuBrushMappingMode(brush.MappingMode),
                    ToOptionalMatrix4x4(brush.HasTransform, brush.Transform),
                    ToOptionalMatrix4x4(brush.HasRelativeTransform, brush.RelativeTransform),
                    CountUnsupportedGradientState(linearStopsTruncated, unsupportedColorInterpolationMode: false),
                    bounds,
                    out unsupportedStateCount);

            case PortableBrushKind.RadialGradient:
                if (!TryCreatePortableRadialGradientBrush(brush, mapRelativeToBounds: false, default, out var radialBrush, out var radialStopsTruncated))
                {
                    return null;
                }

                return AdaptMappedNativeBrush(
                    radialBrush,
                    ToProGpuBrushMappingMode(brush.MappingMode),
                    ToOptionalMatrix4x4(brush.HasTransform, brush.Transform),
                    ToOptionalMatrix4x4(brush.HasRelativeTransform, brush.RelativeTransform),
                    CountUnsupportedGradientState(radialStopsTruncated, unsupportedColorInterpolationMode: false),
                    bounds,
                    out unsupportedStateCount);

            default:
                unsupportedStateCount = 1;
                return null;
        }
    }

    private static global::ProGPU.Vector.Brush AdaptMappedNativeBrush(
        global::ProGPU.Vector.Brush brush,
        ProGpuBrushMappingMode mappingMode,
        Matrix4x4? transform,
        Matrix4x4? relativeTransform,
        int unsupportedGradientStateCount,
        WpfReplayRect bounds,
        out int unsupportedStateCount)
    {
        unsupportedStateCount = unsupportedGradientStateCount;
        if (HasUnsupportedBrushTransformForBounds(brush, transform, relativeTransform, bounds))
        {
            unsupportedStateCount++;
        }

        return ToMappedNativeBrush(brush, mappingMode, transform, relativeTransform, bounds);
    }

    private static global::ProGPU.Vector.Brush ToMappedNativeBrush(
        global::ProGPU.Vector.Brush brush,
        ProGpuBrushMappingMode mappingMode,
        Matrix4x4? transform,
        Matrix4x4? relativeTransform,
        WpfReplayRect bounds)
    {
        bool hasUsableBounds = IsUsable(bounds);
        double x = bounds.X;
        double y = bounds.Y;
        double width = bounds.Width;
        double height = bounds.Height;

        if (mappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && !hasUsableBounds)
        {
            return brush;
        }

        bool hasTransform = TryGetEffectiveBrushTransform(
            transform,
            relativeTransform,
            x,
            y,
            width,
            height,
            hasUsableBounds,
            out Matrix4x4 effectiveTransform);
        bool hasCoordinateTransform = TryGetCoordinateBrushTransform(
            effectiveTransform,
            hasTransform,
            out Matrix4x4 coordinateTransform);

        return brush switch
        {
            global::ProGPU.Vector.LinearGradientBrush linear => new global::ProGPU.Vector.LinearGradientBrush(
                MapBrushPoint(linear.StartPoint, mappingMode, x, y, width, height, hasUsableBounds),
                MapBrushPoint(linear.EndPoint, mappingMode, x, y, width, height, hasUsableBounds),
                linear.Stops ?? Array.Empty<global::ProGPU.Vector.GradientStop>())
            {
                Opacity = linear.Opacity,
                SpreadMethod = linear.SpreadMethod,
                ColorInterpolationMode = linear.ColorInterpolationMode,
                CoordinateTransform = hasCoordinateTransform ? coordinateTransform : Matrix4x4.Identity
            },
            global::ProGPU.Vector.RadialGradientBrush radial => CreateMappedRadialGradientBrush(
                radial,
                mappingMode,
                x,
                y,
                width,
                height,
                hasUsableBounds,
                coordinateTransform,
                hasCoordinateTransform),
            _ => brush
        };
    }

    private static bool HasUnsupportedBrushTransformForBounds(
        global::ProGPU.Vector.Brush brush,
        Matrix4x4? transform,
        Matrix4x4? relativeTransform,
        WpfReplayRect bounds)
    {
        return (brush is global::ProGPU.Vector.LinearGradientBrush || brush is global::ProGPU.Vector.RadialGradientBrush)
            && TryGetEffectiveBrushTransform(
                transform,
                relativeTransform,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                IsUsable(bounds),
                out Matrix4x4 effectiveTransform)
            && !TryCreateCoordinateBrushTransform(effectiveTransform, out _);
    }

    private static global::ProGPU.Vector.RadialGradientBrush CreateMappedRadialGradientBrush(
        global::ProGPU.Vector.RadialGradientBrush radial,
        ProGpuBrushMappingMode mappingMode,
        double x,
        double y,
        double width,
        double height,
        bool hasUsableBounds,
        Matrix4x4 coordinateTransform,
        bool hasCoordinateTransform)
    {
        var center = MapBrushPoint(radial.Center, mappingMode, x, y, width, height, hasUsableBounds);
        var gradientOrigin = MapBrushPoint(radial.GradientOrigin, mappingMode, x, y, width, height, hasUsableBounds);
        var radiusX = mappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && hasUsableBounds
            ? (float)(radial.RadiusX * width)
            : radial.RadiusX;
        var radiusY = mappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && hasUsableBounds
            ? (float)(radial.RadiusY * height)
            : radial.RadiusY;

        return new global::ProGPU.Vector.RadialGradientBrush(
            center,
            gradientOrigin,
            radiusX,
            radiusY,
            radial.Stops ?? Array.Empty<global::ProGPU.Vector.GradientStop>())
        {
            Opacity = radial.Opacity,
            SpreadMethod = radial.SpreadMethod,
            ColorInterpolationMode = radial.ColorInterpolationMode,
            CoordinateTransform = hasCoordinateTransform ? coordinateTransform : Matrix4x4.Identity
        };
    }

    private static Vector2 MapBrushPoint(
        Vector2 point,
        ProGpuBrushMappingMode mappingMode,
        double x,
        double y,
        double width,
        double height,
        bool hasUsableBounds)
    {
        if (mappingMode != ProGpuBrushMappingMode.RelativeToBoundingBox || !hasUsableBounds)
        {
            return point;
        }

        return new Vector2(
            (float)(x + point.X * width),
            (float)(y + point.Y * height));
    }

    private static bool TryGetEffectiveBrushTransform(
        Matrix4x4? transform,
        Matrix4x4? relativeTransform,
        double x,
        double y,
        double width,
        double height,
        bool hasUsableBounds,
        out Matrix4x4 effectiveTransform)
    {
        effectiveTransform = Matrix4x4.Identity;
        bool hasTransform = false;

        if (relativeTransform.HasValue && hasUsableBounds)
        {
            effectiveTransform *= CreateRelativeBoundsBrushTransform(relativeTransform.Value, x, y, width, height);
            hasTransform = true;
        }

        if (transform.HasValue)
        {
            effectiveTransform *= transform.Value;
            hasTransform = true;
        }

        return hasTransform;
    }

    private static bool TryGetCoordinateBrushTransform(
        Matrix4x4 transform,
        bool hasTransform,
        out Matrix4x4 coordinateTransform)
    {
        coordinateTransform = Matrix4x4.Identity;
        return !hasTransform || TryCreateCoordinateBrushTransform(transform, out coordinateTransform);
    }

    private static bool TryCreateCoordinateBrushTransform(Matrix4x4 transform, out Matrix4x4 coordinateTransform)
    {
        coordinateTransform = Matrix4x4.Identity;
        return Is2DAffineBrushTransform(transform)
            && Matrix4x4.Invert(transform, out coordinateTransform)
            && Is2DAffineBrushTransform(coordinateTransform);
    }

    private static Matrix4x4 CreateRelativeBoundsBrushTransform(
        Matrix4x4 relativeTransform,
        double x,
        double y,
        double width,
        double height)
    {
        return Matrix4x4.CreateTranslation((float)-x, (float)-y, 0)
            * Matrix4x4.CreateScale((float)(1 / width), (float)(1 / height), 1)
            * relativeTransform
            * Matrix4x4.CreateScale((float)width, (float)height, 1)
            * Matrix4x4.CreateTranslation((float)x, (float)y, 0);
    }

    private static bool Is2DAffineBrushTransform(Matrix4x4 transform)
    {
        return NearlyZero(transform.M13)
            && NearlyZero(transform.M14)
            && NearlyZero(transform.M23)
            && NearlyZero(transform.M24)
            && NearlyZero(transform.M31)
            && NearlyZero(transform.M32)
            && NearlyEqual(transform.M33, 1)
            && NearlyZero(transform.M34)
            && NearlyZero(transform.M43)
            && NearlyEqual(transform.M44, 1);
    }

    private static bool NearlyZero(float value)
    {
        return MathF.Abs(value) <= 0.0001f;
    }

    private static bool TryCreatePortableLinearGradientBrush(
        PortableBrush brush,
        bool mapRelativeToBounds,
        WpfReplayRect bounds,
        out global::ProGPU.Vector.LinearGradientBrush nativeBrush,
        out bool stopsTruncated)
    {
        nativeBrush = null!;
        if (!TryConvertPortableGradientStops(brush.GradientStops, out var stops, out stopsTruncated))
        {
            return false;
        }

        nativeBrush = new global::ProGPU.Vector.LinearGradientBrush(
            MapBrushPoint(brush.StartPoint, brush.MappingMode, bounds, mapRelativeToBounds),
            MapBrushPoint(brush.EndPoint, brush.MappingMode, bounds, mapRelativeToBounds),
            stops)
        {
            Opacity = (float)ClampOpacity(brush.Opacity),
            SpreadMethod = ToVectorGradientSpreadMethod(brush.SpreadMethod),
            ColorInterpolationMode = ToVectorGradientColorInterpolationMode(brush.ColorInterpolationMode)
        };
        return true;
    }

    private static bool TryCreatePortableRadialGradientBrush(
        PortableBrush brush,
        bool mapRelativeToBounds,
        WpfReplayRect bounds,
        out global::ProGPU.Vector.RadialGradientBrush nativeBrush,
        out bool stopsTruncated)
    {
        nativeBrush = null!;
        if (!TryConvertPortableGradientStops(brush.GradientStops, out var stops, out stopsTruncated))
        {
            return false;
        }

        var hasUsableRelativeBounds = mapRelativeToBounds
            && brush.MappingMode == PortableBrushMappingMode.RelativeToBoundingBox
            && IsUsable(bounds);

        nativeBrush = new global::ProGPU.Vector.RadialGradientBrush(
            MapBrushPoint(brush.Center, brush.MappingMode, bounds, mapRelativeToBounds),
            MapBrushPoint(brush.GradientOrigin, brush.MappingMode, bounds, mapRelativeToBounds),
            hasUsableRelativeBounds ? (float)(brush.RadiusX * bounds.Width) : (float)brush.RadiusX,
            hasUsableRelativeBounds ? (float)(brush.RadiusY * bounds.Height) : (float)brush.RadiusY,
            stops)
        {
            Opacity = (float)ClampOpacity(brush.Opacity),
            SpreadMethod = ToVectorGradientSpreadMethod(brush.SpreadMethod),
            ColorInterpolationMode = ToVectorGradientColorInterpolationMode(brush.ColorInterpolationMode)
        };
        return true;
    }

    private static bool TryConvertPortableGradientStops(
        PortableGradientStop[] portableStops,
        out global::ProGPU.Vector.GradientStop[] stops,
        out bool truncated)
    {
        stops = Array.Empty<global::ProGPU.Vector.GradientStop>();
        truncated = false;
        if (portableStops.Length == 0)
        {
            return false;
        }

        truncated = portableStops.Length > MaxSupportedGradientStops;
        var count = truncated ? MaxSupportedGradientStops : portableStops.Length;
        stops = new global::ProGPU.Vector.GradientStop[count];
        for (var i = 0; i < count; i++)
        {
            var stop = portableStops[i];
            stops[i] = new global::ProGPU.Vector.GradientStop(
                ToVectorColor(stop.Color),
                (float)stop.Offset);
        }

        return true;
    }

    private static Vector2 MapBrushPoint(
        PortablePoint point,
        PortableBrushMappingMode mappingMode,
        WpfReplayRect bounds,
        bool mapRelativeToBounds)
    {
        if (!mapRelativeToBounds
            || mappingMode != PortableBrushMappingMode.RelativeToBoundingBox
            || !IsUsable(bounds))
        {
            return new Vector2((float)point.X, (float)point.Y);
        }

        return new Vector2(
            (float)(bounds.X + point.X * bounds.Width),
            (float)(bounds.Y + point.Y * bounds.Height));
    }

    private static ProGpuBrushMappingMode ToProGpuBrushMappingMode(PortableBrushMappingMode mappingMode)
    {
        return mappingMode == PortableBrushMappingMode.Absolute
            ? ProGpuBrushMappingMode.Absolute
            : ProGpuBrushMappingMode.RelativeToBoundingBox;
    }

    private static global::ProGPU.Vector.GradientSpreadMethod ToVectorGradientSpreadMethod(
        PortableGradientSpreadMethod spreadMethod)
    {
        return spreadMethod switch
        {
            PortableGradientSpreadMethod.Reflect => global::ProGPU.Vector.GradientSpreadMethod.Reflect,
            PortableGradientSpreadMethod.Repeat => global::ProGPU.Vector.GradientSpreadMethod.Repeat,
            _ => global::ProGPU.Vector.GradientSpreadMethod.Pad
        };
    }

    private static global::ProGPU.Vector.GradientColorInterpolationMode ToVectorGradientColorInterpolationMode(
        PortableGradientColorInterpolationMode colorInterpolationMode)
    {
        return colorInterpolationMode == PortableGradientColorInterpolationMode.ScRgbLinearInterpolation
            ? global::ProGPU.Vector.GradientColorInterpolationMode.ScRgbLinearInterpolation
            : global::ProGPU.Vector.GradientColorInterpolationMode.SRgbLinearInterpolation;
    }

    private static Matrix4x4? ToOptionalMatrix4x4(bool hasMatrix, PortableMatrix3x2 matrix)
    {
        return hasMatrix ? ToMatrix4x4(ToWpfMatrix2D(matrix)) : null;
    }

    private static Vector4 ToVectorColor(PortableColor color)
    {
        return new Vector4(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f);
    }

    private static MediaPen? AdaptPortablePen(PortablePen pen)
    {
        var brush = AdaptPortableBrush(pen.Brush);
        if (brush == null)
        {
            return null;
        }

        var adaptedPen = new MediaPen(brush, pen.Thickness)
        {
            StartLineCap = ToMediaPenLineCap(pen.StartLineCap),
            EndLineCap = ToMediaPenLineCap(pen.EndLineCap),
            DashCap = ToMediaPenLineCap(pen.DashCap),
            LineJoin = ToMediaPenLineJoin(pen.LineJoin),
            MiterLimit = ReadMiterLimit(pen.MiterLimit)
        };

        if (TryUseSupportedDashArray(pen.DashArray, pen.Thickness, pen.DashOffset, out var dashArray, out var dashOffset))
        {
            adaptedPen.DashStyle = new DashStyle(dashArray, dashOffset);
        }

        return adaptedPen;
    }

    private static global::ProGPU.Vector.Pen? AdaptNativePortablePen(
        PortablePen pen,
        WpfReplayRect bounds,
        out int unsupportedStateCount)
    {
        var nativeBrush = AdaptNativePortableBrush(pen.Brush, bounds, out unsupportedStateCount);
        if (nativeBrush == null)
        {
            return null;
        }

        var dashArray = Array.Empty<double>();
        var dashOffset = 0.0;
        if (TryUseSupportedDashArray(pen.DashArray, pen.Thickness, pen.DashOffset, out var portableDashArray, out var portableDashOffset))
        {
            dashArray = portableDashArray;
            dashOffset = portableDashOffset;
        }

        return new global::ProGPU.Vector.Pen(
            nativeBrush,
            (float)Math.Max(0, pen.Thickness),
            ToVectorLineJoin(pen.LineJoin),
            (float)ReadMiterLimit(pen.MiterLimit),
            ToVectorLineCap(pen.StartLineCap),
            ToVectorLineCap(pen.EndLineCap),
            ToVectorLineCap(pen.DashCap),
            dashArray,
            dashOffset);
    }

    private static Color ToMediaColor(PortableBrush brush)
    {
        var color = brush.Color;
        return Color.FromArgb(
            ClampToByte(color.A * ClampOpacity(brush.Opacity)),
            color.R,
            color.G,
            color.B);
    }

    private static Color ToMediaColor(PortableColor color)
    {
        return Color.FromArgb(
            color.A,
            color.R,
            color.G,
            color.B);
    }

    private static MediaBrushMappingMode ToMediaBrushMappingMode(PortableBrushMappingMode mappingMode)
    {
        return mappingMode == PortableBrushMappingMode.Absolute
            ? MediaBrushMappingMode.Absolute
            : MediaBrushMappingMode.RelativeToBoundingBox;
    }

    private static MediaGradientSpreadMethod ToMediaGradientSpreadMethod(PortableGradientSpreadMethod spreadMethod)
    {
        return spreadMethod switch
        {
            PortableGradientSpreadMethod.Reflect => MediaGradientSpreadMethod.Reflect,
            PortableGradientSpreadMethod.Repeat => MediaGradientSpreadMethod.Repeat,
            _ => MediaGradientSpreadMethod.Pad
        };
    }

    private static MediaColorInterpolationMode ToMediaColorInterpolationMode(
        PortableGradientColorInterpolationMode colorInterpolationMode)
    {
        return colorInterpolationMode == PortableGradientColorInterpolationMode.ScRgbLinearInterpolation
            ? MediaColorInterpolationMode.ScRgbLinearInterpolation
            : MediaColorInterpolationMode.SRgbLinearInterpolation;
    }

    private static double ClampOpacity(double opacity)
    {
        return double.IsFinite(opacity) ? Math.Clamp(opacity, 0.0, 1.0) : 1.0;
    }

    private static MediaPenLineCap ToMediaPenLineCap(PortablePenLineCap lineCap)
    {
        return lineCap switch
        {
            PortablePenLineCap.Square => MediaPenLineCap.Square,
            PortablePenLineCap.Round => MediaPenLineCap.Round,
            PortablePenLineCap.Triangle => MediaPenLineCap.Triangle,
            _ => MediaPenLineCap.Flat
        };
    }

    private static PenLineJoin ToMediaPenLineJoin(PortablePenLineJoin lineJoin)
    {
        return lineJoin switch
        {
            PortablePenLineJoin.Bevel => PenLineJoin.Bevel,
            PortablePenLineJoin.Round => PenLineJoin.Round,
            _ => PenLineJoin.Miter
        };
    }

    private static global::ProGPU.Vector.PenLineCap ToVectorLineCap(PortablePenLineCap lineCap)
    {
        return lineCap switch
        {
            PortablePenLineCap.Square => global::ProGPU.Vector.PenLineCap.Square,
            PortablePenLineCap.Round => global::ProGPU.Vector.PenLineCap.Round,
            PortablePenLineCap.Triangle => global::ProGPU.Vector.PenLineCap.Triangle,
            _ => global::ProGPU.Vector.PenLineCap.Flat
        };
    }

    private static global::ProGPU.Vector.PenLineJoin ToVectorLineJoin(PortablePenLineJoin lineJoin)
    {
        return lineJoin switch
        {
            PortablePenLineJoin.Bevel => global::ProGPU.Vector.PenLineJoin.Bevel,
            PortablePenLineJoin.Round => global::ProGPU.Vector.PenLineJoin.Round,
            _ => global::ProGPU.Vector.PenLineJoin.Miter
        };
    }

    private static double ReadMiterLimit(double miterLimit)
    {
        if (!double.IsFinite(miterLimit))
        {
            return 10.0;
        }

        return Math.Max(1.0, miterLimit);
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= 0.0001;
    }

    private static bool TryUseSupportedDashArray(
        double[]? values,
        double thickness,
        double offset,
        out double[] dashArray,
        out double dashOffset)
    {
        dashArray = Array.Empty<double>();
        dashOffset = 0;

        if (thickness <= 0 || values == null || values.Length == 0)
        {
            return false;
        }

        var hasPositiveEntry = false;
        foreach (var value in values)
        {
            if (!double.IsFinite(value) || value < 0)
            {
                return false;
            }

            hasPositiveEntry |= value > 0;
        }

        if (!hasPositiveEntry)
        {
            return false;
        }

        dashArray = values;
        dashOffset = double.IsFinite(offset) ? offset : 0.0;
        return true;
    }

    public MediaImageSource? AdaptImageSource(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is MediaImageSource imageSource)
        {
            return WpfBitmapSourceImageAdapter.CanProvideGpuTexture(imageSource)
                ? imageSource
                : _imageSourceAdapter?.AdaptImageSource(resource) ?? imageSource;
        }

        return _imageSourceAdapter?.AdaptImageSource(resource);
    }

    public static MediaTransform? AdaptTransform(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (!TryAdaptTransformMatrix2D(resource, out var matrix))
        {
            return null;
        }

        return TryCreateMatrixTransform(matrix, out var transform)
            ? transform
            : null;
    }

    internal static bool TryAdaptTransformMatrix(object? resource, out Matrix4x4 transform)
    {
        if (TryAdaptTransformMatrix2D(resource, out var matrix))
        {
            transform = ToMatrix4x4(matrix);
            return true;
        }

        transform = Matrix4x4.Identity;
        return false;
    }

    internal static bool TryCreateManagedMatrixTransform(
        Matrix4x4 matrix,
        out MediaTransform transform)
    {
        transform = null!;
        if (!TryReadMatrix4x4(matrix, out var matrix2D)
            || !TryCreateMatrixTransform(matrix2D, out var mediaTransform)
            || mediaTransform == null)
        {
            return false;
        }

        transform = mediaTransform;
        return true;
    }

    internal static bool IsIdentityMatrix(Matrix4x4 matrix)
    {
        return NearlyEqual(matrix.M11, 1)
            && NearlyEqual(matrix.M12, 0)
            && NearlyEqual(matrix.M13, 0)
            && NearlyEqual(matrix.M14, 0)
            && NearlyEqual(matrix.M21, 0)
            && NearlyEqual(matrix.M22, 1)
            && NearlyEqual(matrix.M23, 0)
            && NearlyEqual(matrix.M24, 0)
            && NearlyEqual(matrix.M31, 0)
            && NearlyEqual(matrix.M32, 0)
            && NearlyEqual(matrix.M33, 1)
            && NearlyEqual(matrix.M34, 0)
            && NearlyEqual(matrix.M41, 0)
            && NearlyEqual(matrix.M42, 0)
            && NearlyEqual(matrix.M43, 0)
            && NearlyEqual(matrix.M44, 1);
    }

    internal static bool TryAdaptNativeGlyphRun(object? resource, out WpfNativeGlyphRun glyphRun)
    {
        glyphRun = default;
        if (resource == null)
        {
            return false;
        }

        if (resource is PortableNativeGlyphRunSource nativeGlyphRunSource)
        {
            return nativeGlyphRunSource.TryGetPortableNativeGlyphRun(out var nativeGlyphRun)
                && TryAdaptPortableNativeGlyphRun(nativeGlyphRun, out glyphRun);
        }

        if (resource is PortableNativeGlyphRun nativeGlyphRunDto)
        {
            return TryAdaptPortableNativeGlyphRun(nativeGlyphRunDto, out glyphRun);
        }

        if (resource is PortableGlyphRunSource portableGlyphRunSource)
        {
            return portableGlyphRunSource.TryGetPortableGlyphRun(out var portableGlyphRun)
                && TryAdaptPortableNativeGlyphRun(portableGlyphRun, out glyphRun);
        }

        if (resource is PortableGlyphRun portableGlyphRunDto)
        {
            return TryAdaptPortableNativeGlyphRun(portableGlyphRunDto, out glyphRun);
        }

        return false;
    }

    public static MediaGlyphRun? AdaptGlyphRun(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is PortableNativeGlyphRunSource nativeGlyphRunSource)
        {
            return nativeGlyphRunSource.TryGetPortableNativeGlyphRun(out var nativeGlyphRun)
                ? AdaptPortableNativeGlyphRun(nativeGlyphRun)
                : null;
        }

        if (resource is PortableNativeGlyphRun nativeGlyphRunDto)
        {
            return AdaptPortableNativeGlyphRun(nativeGlyphRunDto);
        }

        if (resource is PortableGlyphRunSource portableGlyphRunSource)
        {
            return portableGlyphRunSource.TryGetPortableGlyphRun(out var portableGlyphRun)
                ? AdaptPortableGlyphRun(portableGlyphRun)
                : null;
        }

        if (resource is PortableGlyphRun portableGlyphRunDto)
        {
            return AdaptPortableGlyphRun(portableGlyphRunDto);
        }

        if (resource is MediaGlyphRun glyphRun)
        {
            return glyphRun;
        }

        return null;
    }

    private static bool TryAdaptPortableNativeGlyphRun(PortableNativeGlyphRun portableGlyphRun, out WpfNativeGlyphRun glyphRun)
    {
        glyphRun = default;
        if (!TryValidatePortableNativeGlyphRun(portableGlyphRun, out var font))
        {
            return false;
        }

        var transform = Matrix4x4.Identity;
        if (portableGlyphRun.HasTransform)
        {
            if (!TryReadMatrix4x4(portableGlyphRun.Transform, out var matrix))
            {
                return false;
            }

            transform = ToMatrix4x4(matrix);
        }

        glyphRun = new WpfNativeGlyphRun(
            portableGlyphRun.GlyphIndices,
            CreatePortableNativeGlyphPositions(portableGlyphRun),
            font,
            (float)portableGlyphRun.FontRenderingEmSize,
            portableGlyphRun.BaselineOrigin,
            transform,
            portableGlyphRun.IsBold,
            portableGlyphRun.IsItalic);
        return true;
    }

    private static bool TryAdaptPortableNativeGlyphRun(PortableGlyphRun portableGlyphRun, out WpfNativeGlyphRun glyphRun)
    {
        glyphRun = default;
        if (!TryValidatePortableGlyphRun(portableGlyphRun, out var font))
        {
            return false;
        }

        var transform = Matrix4x4.Identity;
        if (portableGlyphRun.HasTransform)
        {
            var matrix = ToWpfMatrix2D(portableGlyphRun.Transform);
            if (!TryUseFiniteMatrix(matrix, out matrix))
            {
                return false;
            }

            transform = ToMatrix4x4(matrix);
        }

        glyphRun = new WpfNativeGlyphRun(
            portableGlyphRun.GlyphIndices,
            CreatePortableGlyphPositions(portableGlyphRun),
            font,
            (float)portableGlyphRun.FontRenderingEmSize,
            ToVector2(portableGlyphRun.BaselineOrigin),
            transform,
            portableGlyphRun.IsBold,
            portableGlyphRun.IsItalic);
        return true;
    }

    private static MediaGlyphRun? AdaptPortableNativeGlyphRun(PortableNativeGlyphRun portableGlyphRun)
    {
        if (!TryValidatePortableNativeGlyphRun(portableGlyphRun, out var font))
        {
            return null;
        }

        var transform = Matrix4x4.Identity;
        if (portableGlyphRun.HasTransform)
        {
            if (!TryReadMatrix4x4(portableGlyphRun.Transform, out var matrix))
            {
                return null;
            }

            transform = ToMatrix4x4(matrix);
        }

        return new MediaGlyphRun(
            font,
            (float)portableGlyphRun.FontRenderingEmSize,
            portableGlyphRun.GlyphIndices,
            CreatePortableNativeGlyphPositions(portableGlyphRun))
        {
            Position = portableGlyphRun.BaselineOrigin,
            Transform = transform,
            IsBold = portableGlyphRun.IsBold,
            IsItalic = portableGlyphRun.IsItalic
        };
    }

    private static MediaGlyphRun? AdaptPortableGlyphRun(PortableGlyphRun portableGlyphRun)
    {
        if (!TryValidatePortableGlyphRun(portableGlyphRun, out var font))
        {
            return null;
        }

        var transform = Matrix4x4.Identity;
        if (portableGlyphRun.HasTransform)
        {
            var matrix = ToWpfMatrix2D(portableGlyphRun.Transform);
            if (!TryUseFiniteMatrix(matrix, out matrix))
            {
                return null;
            }

            transform = ToMatrix4x4(matrix);
        }

        return new MediaGlyphRun(
            font,
            (float)portableGlyphRun.FontRenderingEmSize,
            portableGlyphRun.GlyphIndices,
            CreatePortableGlyphPositions(portableGlyphRun))
        {
            Position = ToVector2(portableGlyphRun.BaselineOrigin),
            Transform = transform,
            IsBold = portableGlyphRun.IsBold,
            IsItalic = portableGlyphRun.IsItalic
        };
    }

    private static bool TryValidatePortableNativeGlyphRun(PortableNativeGlyphRun portableGlyphRun, out TtfFont font)
    {
        font = null!;
        if (portableGlyphRun.GlyphIndices.Length == 0
            || portableGlyphRun.GlyphPositions.Length < portableGlyphRun.GlyphIndices.Length
            || portableGlyphRun.FontRenderingEmSize <= 0
            || TryResolvePortableGlyphRunFont(portableGlyphRun) is not { } resolvedFont)
        {
            return false;
        }

        font = resolvedFont;
        return true;
    }

    private static bool TryValidatePortableGlyphRun(PortableGlyphRun portableGlyphRun, out TtfFont font)
    {
        font = null!;
        if (portableGlyphRun.GlyphIndices.Length == 0
            || portableGlyphRun.FontRenderingEmSize <= 0
            || TryResolvePortableGlyphRunFont(portableGlyphRun) is not { } resolvedFont)
        {
            return false;
        }

        font = resolvedFont;
        return true;
    }

    private static Vector2[] CreatePortableNativeGlyphPositions(PortableNativeGlyphRun portableGlyphRun)
    {
        var glyphCount = portableGlyphRun.GlyphIndices.Length;
        if (portableGlyphRun.GlyphPositions.Length == glyphCount)
        {
            return portableGlyphRun.GlyphPositions;
        }

        var positions = new Vector2[glyphCount];
        Array.Copy(portableGlyphRun.GlyphPositions, positions, glyphCount);
        return positions;
    }

    private static Vector2[] CreatePortableGlyphPositions(PortableGlyphRun portableGlyphRun)
    {
        var glyphCount = portableGlyphRun.GlyphIndices.Length;
        if (portableGlyphRun.GlyphPositions.Length >= glyphCount)
        {
            var positions = new Vector2[glyphCount];
            for (var i = 0; i < positions.Length; i++)
            {
                positions[i] = ToVector2(portableGlyphRun.GlyphPositions[i]);
            }

            return positions;
        }

        var computedPositions = new Vector2[glyphCount];
        double x = 0;
        for (var i = 0; i < computedPositions.Length; i++)
        {
            var offset = i < portableGlyphRun.GlyphOffsets.Length
                ? portableGlyphRun.GlyphOffsets[i]
                : new PortablePoint(0, 0);
            computedPositions[i] = new Vector2((float)(x + offset.X), (float)offset.Y);

            if (i < portableGlyphRun.AdvanceWidths.Length)
            {
                x += portableGlyphRun.AdvanceWidths[i];
            }
        }

        return computedPositions;
    }

    private static TtfFont? TryResolvePortableGlyphRunFont(PortableGlyphRun glyphRun)
    {
        return TryResolvePortableGlyphRunFont(
            glyphRun.NativeFont,
            glyphRun.FontUri,
            glyphRun.FontFamilyNames);
    }

    private static TtfFont? TryResolvePortableGlyphRunFont(PortableNativeGlyphRun glyphRun)
    {
        return TryResolvePortableGlyphRunFont(
            glyphRun.NativeFont,
            glyphRun.FontUri,
            glyphRun.FontFamilyNames);
    }

    private static TtfFont? TryResolvePortableGlyphRunFont(object? nativeFont, string? fontUri, string[] fontFamilyNames)
    {
        if (nativeFont is TtfFont font)
        {
            return font;
        }

        if (!string.IsNullOrWhiteSpace(fontUri)
            && TryResolveFontFileValue(fontUri) is { } fontFromUri)
        {
            return fontFromUri;
        }

        for (var i = 0; i < fontFamilyNames.Length; i++)
        {
            var familyName = fontFamilyNames[i];
            if (string.IsNullOrWhiteSpace(familyName))
            {
                continue;
            }

            var resolved = TryResolveFontFamily(familyName);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return TryResolveFontFamily("Arial");
    }

    public static MediaGeometry? AdaptGeometry(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is PortableGeometryPathSource portableGeometry)
        {
            return portableGeometry.TryGetPortableGeometryPath(out var portablePath)
                ? AdaptPortableGeometryPath(portablePath)
                : null;
        }

        if (resource is MediaGeometry geometry)
        {
            return geometry;
        }

        return null;
    }

    private static MediaGeometry? AdaptPortableGeometryPath(PortableGeometryPath portablePath)
    {
        MediaGeometry geometry;
        if (portablePath.Kind == PortableGeometryPathKind.Combined)
        {
            var geometryA = portablePath.PathA == null
                ? new PathGeometry()
                : AdaptPortableGeometryPath(portablePath.PathA);
            var geometryB = portablePath.PathB == null
                ? new PathGeometry()
                : AdaptPortableGeometryPath(portablePath.PathB);
            if (geometryA == null || geometryB == null)
            {
                return null;
            }

            geometry = CreateCombinedGeometry(geometryA, geometryB, portablePath.CombineOperation);
        }
        else
        {
            var figures = new List<PathFigure>(portablePath.Figures.Length);

            foreach (var portableFigure in portablePath.Figures)
            {
                var segments = new List<PathSegment>(portableFigure.Segments.Length);
                foreach (var segment in portableFigure.Segments)
                {
                    segments.Add(CreatePortablePathSegment(segment));
                }

                var figure = new PathFigure
                {
                    Segments = new PathSegmentCollection(segments),
                    StartPoint = ToPoint(portableFigure.StartPoint),
                    IsClosed = portableFigure.IsClosed,
                    IsFilled = portableFigure.IsFilled
                };

                figures.Add(figure);
            }

            geometry = new PathGeometry
            {
                Figures = new PathFigureCollection(figures),
                FillRule = ToMediaFillRule(portablePath.FillRule)
            };
        }

        return ApplyPortableGeometryTransform(portablePath, geometry);
    }

    private static MediaGeometry? ApplyPortableGeometryTransform(PortableGeometryPath portablePath, MediaGeometry geometry)
    {
        if (portablePath.Transform.IsIdentity)
        {
            return geometry;
        }

        var matrix = ToWpfMatrix2D(portablePath.Transform);
        if (!TryUseFiniteMatrix(matrix, out matrix)
            || !TryCreateMatrixTransform(matrix, out var transform)
            || transform == null)
        {
            return null;
        }

        geometry.Transform = transform;
        return geometry;
    }

    private static PathSegment CreatePortablePathSegment(PortablePathSegment segment)
    {
        switch (segment.Kind)
        {
            case PortablePathSegmentKind.Line:
                return new LineSegment(ToPoint(segment.Point1), segment.IsStroked)
                {
                    IsSmoothJoin = segment.IsSmoothJoin
                };
            case PortablePathSegmentKind.QuadraticBezier:
                return new QuadraticBezierSegment(ToPoint(segment.Point1), ToPoint(segment.Point2), segment.IsStroked)
                {
                    IsSmoothJoin = segment.IsSmoothJoin
                };
            case PortablePathSegmentKind.CubicBezier:
                return new BezierSegment(
                    ToPoint(segment.Point1),
                    ToPoint(segment.Point2),
                    ToPoint(segment.Point3),
                    segment.IsStroked)
                {
                    IsSmoothJoin = segment.IsSmoothJoin
                };
            case PortablePathSegmentKind.Arc:
                return new ArcSegment
                {
                    Point = ToPoint(segment.Point1),
                    Size = ToSize(segment.Size),
                    RotationAngle = segment.RotationAngle,
                    IsLargeArc = segment.IsLargeArc,
                    SweepDirection = ToMediaSweepDirection(segment.SweepDirection),
                    IsSmoothJoin = segment.IsSmoothJoin,
                    IsStroked = segment.IsStroked
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(segment));
        }
    }

    private static FillRule ToMediaFillRule(PortableFillRule fillRule)
    {
        return fillRule == PortableFillRule.EvenOdd
            ? FillRule.EvenOdd
            : FillRule.Nonzero;
    }

    private static SweepDirection ToMediaSweepDirection(PortableSweepDirection sweepDirection)
    {
        return sweepDirection == PortableSweepDirection.Clockwise
            ? SweepDirection.Clockwise
            : SweepDirection.Counterclockwise;
    }

    internal static PathGeometry CreateRectanglePath(Rect rectangle)
    {
        return CreateRectanglePath(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    internal static PathGeometry CreateRectanglePath(WpfReplayRect rectangle)
    {
        return CreateRectanglePath(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    private static PathGeometry CreateRectanglePath(double x, double y, double width, double height)
    {
        var segments = new[]
        {
            new LineSegment(new Point(x + width, y), isStroked: true),
            new LineSegment(new Point(x + width, y + height), isStroked: true),
            new LineSegment(new Point(x, y + height), isStroked: true)
        };

        var geometry = new PathGeometry
        {
            Figures = new PathFigureCollection(new[]
            {
                new PathFigure
                {
                    Segments = new PathSegmentCollection(segments),
                    StartPoint = new Point(x, y),
                    IsClosed = true,
                    IsFilled = true
                }
            })
        };

        return geometry;
    }

    private static MediaGeometry CreateCombinedGeometry(MediaGeometry geometry1, MediaGeometry geometry2, int pathOperation)
    {
        return new CombinedGeometry(ToGeometryCombineMode(pathOperation), geometry1, geometry2);
    }

    private static GeometryCombineMode ToGeometryCombineMode(int pathOperation)
    {
        return pathOperation switch
        {
            0 => GeometryCombineMode.Exclude,
            1 => GeometryCombineMode.Intersect,
            3 => GeometryCombineMode.Xor,
            _ => GeometryCombineMode.Union
        };
    }

    private static Vector2 ToVector2(PortablePoint point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static Point ToPoint(PortablePoint point)
    {
        return new Point(point.X, point.Y);
    }

    private static Size ToSize(PortableSize size)
    {
        return new Size(Math.Abs(size.Width), Math.Abs(size.Height));
    }

    private static bool TryAdaptTransformMatrix2D(object? resource, out WpfMatrix2D matrix)
    {
        if (resource == null)
        {
            matrix = default;
            return false;
        }

        if (resource is Matrix4x4 nativeMatrix)
        {
            return TryReadMatrix4x4(nativeMatrix, out matrix);
        }

        if (resource is PortableTransformMatrixSource portableTransform)
        {
            if (portableTransform.TryGetPortableTransformMatrix(out var portableMatrix))
            {
                return TryUseFiniteMatrix(ToWpfMatrix2D(portableMatrix), out matrix);
            }

            matrix = default;
            return false;
        }

        matrix = default;
        return false;
    }

    private static bool TryReadMatrix4x4(Matrix4x4 value, out WpfMatrix2D matrix)
    {
        if (!NearlyEqual(value.M13, 0)
            || !NearlyEqual(value.M14, 0)
            || !NearlyEqual(value.M23, 0)
            || !NearlyEqual(value.M24, 0)
            || !NearlyEqual(value.M31, 0)
            || !NearlyEqual(value.M32, 0)
            || !NearlyEqual(value.M33, 1)
            || !NearlyEqual(value.M34, 0)
            || !NearlyEqual(value.M43, 0)
            || !NearlyEqual(value.M44, 1))
        {
            matrix = default;
            return false;
        }

        return TryUseFiniteMatrix(
            new WpfMatrix2D(value.M11, value.M12, value.M21, value.M22, value.M41, value.M42),
            out matrix);
    }

    private static bool TryUseFiniteMatrix(WpfMatrix2D value, out WpfMatrix2D matrix)
    {
        matrix = value;
        return double.IsFinite(value.M11)
            && double.IsFinite(value.M12)
            && double.IsFinite(value.M21)
            && double.IsFinite(value.M22)
            && double.IsFinite(value.OffsetX)
            && double.IsFinite(value.OffsetY);
    }

    private static WpfMatrix2D ToWpfMatrix2D(PortableMatrix3x2 matrix)
    {
        return new WpfMatrix2D(
            matrix.M11,
            matrix.M12,
            matrix.M21,
            matrix.M22,
            matrix.OffsetX,
            matrix.OffsetY);
    }

    private static Matrix4x4 ToMatrix4x4(WpfMatrix2D matrix)
    {
        return new Matrix4x4(
            (float)matrix.M11,
            (float)matrix.M12,
            0,
            0,
            (float)matrix.M21,
            (float)matrix.M22,
            0,
            0,
            0,
            0,
            1,
            0,
            (float)matrix.OffsetX,
            (float)matrix.OffsetY,
            0,
            1);
    }

    private static bool TryCreateMatrixTransform(WpfMatrix2D matrix, out MediaTransform? transform)
    {
        transform = new MediaMatrixTransform(
            matrix.M11,
            matrix.M12,
            matrix.M21,
            matrix.M22,
            matrix.OffsetX,
            matrix.OffsetY);
        return true;
    }

    private static bool NearlyEqual(float left, float right)
    {
        return Math.Abs(left - right) < 0.0001f;
    }

    private static TtfFont? TryResolveFontFileValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && TryGetLocalFontPath(value, out var path)
            ? TryLoadFontFile(path)
            : null;
    }

    private static bool TryGetLocalFontPath(string value, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return TryGetLocalFontPath(uri, out path);
        }

        path = value;
        return File.Exists(path);
    }

    private static bool TryGetLocalFontPath(Uri uri, out string path)
    {
        path = string.Empty;
        if (uri.IsAbsoluteUri)
        {
            if (!uri.IsFile)
            {
                return false;
            }

            path = uri.LocalPath;
            return File.Exists(path);
        }

        path = uri.OriginalString;
        return File.Exists(path);
    }

    private static TtfFont? TryLoadFontFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath)
                ? s_fontFileCache.GetOrAdd(fullPath, static filePath => new TtfFont(filePath))
                : null;
        }
        catch (Exception ex) when (IsRecoverableFontLoadException(ex))
        {
            return null;
        }
    }

    private static bool IsRecoverableFontLoadException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or FormatException
            or InvalidDataException
            or KeyNotFoundException
            or IndexOutOfRangeException
            or OverflowException
            or NotSupportedException;
    }

    private static TtfFont? TryResolveFontFamily(string familyName)
    {
        try
        {
            return new FontFamily(familyName).NativeFont;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte ClampToByte(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value >= 255)
        {
            return 255;
        }

        return (byte)Math.Round(value);
    }
}
