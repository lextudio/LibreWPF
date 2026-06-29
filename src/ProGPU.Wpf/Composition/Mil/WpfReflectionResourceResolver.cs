using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Text;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaMatrix = System.Windows.Media.Matrix;
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

namespace System.Windows.Media.ProGPU.Composition.Mil;

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

public sealed class WpfReflectionResourceResolver :
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

    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int UnionPathOperation = 2;
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

    public WpfReflectionResourceResolver()
    {
    }

    public WpfReflectionResourceResolver(IWpfImageSourceAdapter? imageSourceAdapter)
    {
        _imageSourceAdapter = imageSourceAdapter;
    }

    public static WpfReflectionResourceResolver FromDependentResources(
        IEnumerable<object?> dependentResources,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(dependentResources);

        var resolver = new WpfReflectionResourceResolver(imageSourceAdapter);
        uint token = 1;
        foreach (var resource in dependentResources)
        {
            if (resource != null)
            {
                resolver.Register(token, resource);
            }

            token++;
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

        return WpfReflectionDrawingReplay.Replay(drawing, sink, AdaptImageSource);
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

        if (resource is ProGpuNativeBrush nativeProGpuBrush)
        {
            unsupportedStateCount += nativeProGpuBrush.CountUnsupportedStateForBounds(bounds);
            return nativeProGpuBrush.ToNative(bounds);
        }

        if (resource is MediaBrush mediaBrush)
        {
            var mediaBounds = ToMediaRect(bounds);
            return mediaBrush.ToNative(mediaBounds);
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

        if (resource is MediaPen mediaPen)
        {
            var mediaBounds = ToMediaRect(bounds);
            return mediaPen.ToNative(mediaBounds);
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

    private static Rect ToMediaRect(WpfReplayRect bounds)
    {
        return new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
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
                if (!TryCreatePortableLinearGradientBrush(brush, mapRelativeToBounds: false, default, out var linearBrush, out var linearStopsTruncated))
                {
                    return null;
                }

                return new ProGpuNativeBrush(
                    linearBrush,
                    ToProGpuBrushMappingMode(brush.MappingMode),
                    ToOptionalMatrix4x4(brush.HasTransform, brush.Transform),
                    ToOptionalMatrix4x4(brush.HasRelativeTransform, brush.RelativeTransform),
                    CountUnsupportedGradientState(linearStopsTruncated, unsupportedColorInterpolationMode: false));

            case PortableBrushKind.RadialGradient:
                if (!TryCreatePortableRadialGradientBrush(brush, mapRelativeToBounds: false, default, out var radialBrush, out var radialStopsTruncated))
                {
                    return null;
                }

                return new ProGpuNativeBrush(
                    radialBrush,
                    ToProGpuBrushMappingMode(brush.MappingMode),
                    ToOptionalMatrix4x4(brush.HasTransform, brush.Transform),
                    ToOptionalMatrix4x4(brush.HasRelativeTransform, brush.RelativeTransform),
                    CountUnsupportedGradientState(radialStopsTruncated, unsupportedColorInterpolationMode: false));

            default:
                return null;
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

                var wrappedLinearBrush = new ProGpuNativeBrush(
                    linearBrush,
                    ToProGpuBrushMappingMode(brush.MappingMode),
                    ToOptionalMatrix4x4(brush.HasTransform, brush.Transform),
                    ToOptionalMatrix4x4(brush.HasRelativeTransform, brush.RelativeTransform),
                    CountUnsupportedGradientState(linearStopsTruncated, unsupportedColorInterpolationMode: false));
                unsupportedStateCount += wrappedLinearBrush.CountUnsupportedStateForBounds(bounds);
                return wrappedLinearBrush.ToNative(bounds);

            case PortableBrushKind.RadialGradient:
                if (!TryCreatePortableRadialGradientBrush(brush, mapRelativeToBounds: false, default, out var radialBrush, out var radialStopsTruncated))
                {
                    return null;
                }

                var wrappedRadialBrush = new ProGpuNativeBrush(
                    radialBrush,
                    ToProGpuBrushMappingMode(brush.MappingMode),
                    ToOptionalMatrix4x4(brush.HasTransform, brush.Transform),
                    ToOptionalMatrix4x4(brush.HasRelativeTransform, brush.RelativeTransform),
                    CountUnsupportedGradientState(radialStopsTruncated, unsupportedColorInterpolationMode: false));
                unsupportedStateCount += wrappedRadialBrush.CountUnsupportedStateForBounds(bounds);
                return wrappedRadialBrush.ToNative(bounds);

            default:
                unsupportedStateCount = 1;
                return null;
        }
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

        if (resource is PortableGlyphRunSource portableGlyphRunSource)
        {
            return portableGlyphRunSource.TryGetPortableGlyphRun(out var portableGlyphRun)
                && TryAdaptPortableNativeGlyphRun(portableGlyphRun, out glyphRun);
        }

        if (resource is MediaGlyphRun mediaGlyphRun)
        {
            glyphRun = new WpfNativeGlyphRun(
                mediaGlyphRun.GlyphIndices,
                mediaGlyphRun.GlyphPositions,
                mediaGlyphRun.Font,
                mediaGlyphRun.FontSize,
                mediaGlyphRun.Position,
                mediaGlyphRun.Transform,
                mediaGlyphRun.IsBold,
                mediaGlyphRun.IsItalic);
            return mediaGlyphRun.GlyphIndices.Length > 0
                && mediaGlyphRun.FontSize > 0
                && mediaGlyphRun.Font != null;
        }

        return false;
    }

    public static MediaGlyphRun? AdaptGlyphRun(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is PortableGlyphRunSource portableGlyphRunSource)
        {
            return portableGlyphRunSource.TryGetPortableGlyphRun(out var portableGlyphRun)
                ? AdaptPortableGlyphRun(portableGlyphRun)
                : null;
        }

        if (resource is MediaGlyphRun glyphRun)
        {
            return glyphRun;
        }

        return null;
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
        if (glyphRun.NativeFont is TtfFont font)
        {
            return font;
        }

        if (!string.IsNullOrWhiteSpace(glyphRun.FontUri)
            && TryResolveFontFileValue(glyphRun.FontUri) is { } fontFromUri)
        {
            return fontFromUri;
        }

        foreach (var familyName in glyphRun.FontFamilyNames)
        {
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

        if (TypeNameEndsWith(resource, "LineGeometry")
            && TryGetPropertyValue(resource, "StartPoint", out var startPointValue)
            && TryGetPropertyValue(resource, "EndPoint", out var endPointValue)
            && startPointValue != null
            && endPointValue != null
            && TryReadReplayPoint(startPointValue, out var startPoint)
            && TryReadReplayPoint(endPointValue, out var endPoint))
        {
            return ApplyGeometryTransform(resource, CreateLinePath(startPoint, endPoint));
        }

        if (TypeNameEndsWith(resource, "RectangleGeometry")
            && TryGetPropertyValue(resource, "Rect", out var rectValue)
            && rectValue != null
            && TryReadReplayRect(rectValue, out var rectangle))
        {
            return ApplyGeometryTransform(resource, CreateRectanglePath(rectangle));
        }

        if (TypeNameEndsWith(resource, "EllipseGeometry")
            && TryGetPropertyValue(resource, "Center", out var centerValue)
            && centerValue != null
            && TryReadReplayPoint(centerValue, out var center)
            && TryReadDoubleProperty(resource, "RadiusX", out var radiusX)
            && TryReadDoubleProperty(resource, "RadiusY", out var radiusY))
        {
            return ApplyGeometryTransform(resource, CreateEllipsePath(center, radiusX, radiusY));
        }

        if (TypeNameEndsWith(resource, "CombinedGeometry")
            && TryGetPropertyValue(resource, "Geometry1", out var geometry1Value)
            && TryGetPropertyValue(resource, "Geometry2", out var geometry2Value)
            && TryGetPropertyValue(resource, "GeometryCombineMode", out var combineModeValue)
            && TryReadGeometryCombineMode(combineModeValue, out var pathOperation))
        {
            var geometry1 = geometry1Value == null ? new PathGeometry() : AdaptGeometry(geometry1Value);
            var geometry2 = geometry2Value == null ? new PathGeometry() : AdaptGeometry(geometry2Value);
            if (geometry1 == null || geometry2 == null)
            {
                return null;
            }

            return ApplyGeometryTransform(resource, CreateCombinedGeometry(geometry1, geometry2, pathOperation));
        }

        if (TypeNameEndsWith(resource, "GeometryGroup")
            && TryGetPropertyValue(resource, "Children", out var children)
            && children != null)
        {
            var groupGeometry = CreateGeometryGroupGeometry(resource, children);
            return groupGeometry == null ? null : ApplyGeometryTransform(resource, groupGeometry);
        }

        if (TypeNameEndsWith(resource, "PathGeometry")
            && TryCreatePathGeometry(resource, out var reflectedPathGeometry))
        {
            return ApplyGeometryTransform(resource, reflectedPathGeometry);
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
            var pathGeometry = new PathGeometry
            {
                FillRule = ToMediaFillRule(portablePath.FillRule)
            };

            foreach (var portableFigure in portablePath.Figures)
            {
                var figure = new PathFigure
                {
                    StartPoint = ToPoint(portableFigure.StartPoint),
                    IsClosed = portableFigure.IsClosed,
                    IsFilled = portableFigure.IsFilled
                };

                foreach (var segment in portableFigure.Segments)
                {
                    AppendPortablePathSegment(figure, segment);
                }

                pathGeometry.Figures.Add(figure);
            }

            geometry = pathGeometry;
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

    private static void AppendPortablePathSegment(PathFigure figure, PortablePathSegment segment)
    {
        switch (segment.Kind)
        {
            case PortablePathSegmentKind.Line:
                figure.Segments.Add(new LineSegment(ToVector2(segment.Point1), segment.IsSmoothJoin, segment.IsStroked));
                break;
            case PortablePathSegmentKind.QuadraticBezier:
                figure.Segments.Add(new QuadraticBezierSegment(
                    ToVector2(segment.Point1),
                    ToVector2(segment.Point2),
                    segment.IsSmoothJoin,
                    segment.IsStroked));
                break;
            case PortablePathSegmentKind.CubicBezier:
                figure.Segments.Add(new BezierSegment(
                    ToVector2(segment.Point1),
                    ToVector2(segment.Point2),
                    ToVector2(segment.Point3),
                    segment.IsSmoothJoin,
                    segment.IsStroked));
                break;
            case PortablePathSegmentKind.Arc:
                figure.Segments.Add(new ArcSegment
                {
                    Point = ToPoint(segment.Point1),
                    Size = ToSize(segment.Size),
                    RotationAngle = segment.RotationAngle,
                    IsLargeArc = segment.IsLargeArc,
                    SweepDirection = ToMediaSweepDirection(segment.SweepDirection),
                    IsSmoothJoin = segment.IsSmoothJoin,
                    IsStroked = segment.IsStroked
                });
                break;
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

    private static T ApplyGeometryTransform<T>(object resource, T geometry)
        where T : MediaGeometry
    {
        if (TryGetPropertyValue(resource, "Transform", out var transformValue) && transformValue != null)
        {
            geometry.Transform = AdaptTransform(transformValue);
        }

        return geometry;
    }

    private static PathGeometry CreateLinePath(WpfReplayPoint startPoint, WpfReplayPoint endPoint)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(startPoint.X, startPoint.Y),
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new LineSegment(new Vector2((float)endPoint.X, (float)endPoint.Y)));
        geometry.Figures.Add(figure);

        return geometry;
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
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(x, y),
            IsClosed = true,
            IsFilled = true
        };

        figure.Segments.Add(new LineSegment(new Vector2((float)(x + width), (float)y)));
        figure.Segments.Add(new LineSegment(new Vector2((float)(x + width), (float)(y + height))));
        figure.Segments.Add(new LineSegment(new Vector2((float)x, (float)(y + height))));
        geometry.Figures.Add(figure);

        return geometry;
    }

    private static PathGeometry CreateEllipsePath(WpfReplayPoint center, double radiusX, double radiusY)
    {
        var geometry = new PathGeometry();
        if (radiusX <= 0 || radiusY <= 0)
        {
            return geometry;
        }

        const double kappa = 0.5522847498307936;
        var cx = center.X;
        var cy = center.Y;
        var rx = radiusX;
        var ry = radiusY;
        var ox = rx * kappa;
        var oy = ry * kappa;

        var figure = new PathFigure
        {
            StartPoint = new Point(cx + rx, cy),
            IsClosed = true,
            IsFilled = true
        };

        figure.Segments.Add(new BezierSegment(
            new Vector2((float)(cx + rx), (float)(cy + oy)),
            new Vector2((float)(cx + ox), (float)(cy + ry)),
            new Vector2((float)cx, (float)(cy + ry))));
        figure.Segments.Add(new BezierSegment(
            new Vector2((float)(cx - ox), (float)(cy + ry)),
            new Vector2((float)(cx - rx), (float)(cy + oy)),
            new Vector2((float)(cx - rx), (float)cy)));
        figure.Segments.Add(new BezierSegment(
            new Vector2((float)(cx - rx), (float)(cy - oy)),
            new Vector2((float)(cx - ox), (float)(cy - ry)),
            new Vector2((float)cx, (float)(cy - ry))));
        figure.Segments.Add(new BezierSegment(
            new Vector2((float)(cx + ox), (float)(cy - ry)),
            new Vector2((float)(cx + rx), (float)(cy - oy)),
            new Vector2((float)(cx + rx), (float)cy)));

        geometry.Figures.Add(figure);
        return geometry;
    }

    private static MediaGeometry? CreateGeometryGroupGeometry(object group, object children)
    {
        var flattenedPath = new PathGeometry();
        if (TryGetPropertyValue(group, "FillRule", out var fillRuleValue)
            && TryReadFillRule(fillRuleValue, out var fillRule))
        {
            flattenedPath.FillRule = fillRule;
        }

        if (!TryReadIntProperty(children, "Count", out var count) || count <= 0)
        {
            return null;
        }

        var getChild = FindIndexer(children.GetType());
        if (getChild == null)
        {
            return null;
        }

        var adaptedChildren = new List<MediaGeometry>(count);
        var canFlattenAllChildren = true;
        for (var i = 0; i < count; i++)
        {
            var child = getChild(children, i);
            var childGeometry = AdaptGeometry(child);
            if (childGeometry == null)
            {
                continue;
            }

            adaptedChildren.Add(childGeometry);
            if (childGeometry is PathGeometry childPathGeometry)
            {
                AppendFigures(flattenedPath, childPathGeometry);
            }
            else
            {
                canFlattenAllChildren = false;
            }
        }

        if (adaptedChildren.Count == 0)
        {
            return null;
        }

        if (canFlattenAllChildren)
        {
            return flattenedPath.Figures.Count == 0 ? null : flattenedPath;
        }

        return FoldGeometryGroupChildrenAsUnion(adaptedChildren);
    }

    private static MediaGeometry FoldGeometryGroupChildrenAsUnion(IReadOnlyList<MediaGeometry> children)
    {
        var combined = children[0];
        for (var i = 1; i < children.Count; i++)
        {
            combined = CreateCombinedGeometry(combined, children[i], UnionPathOperation);
        }

        return combined;
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

    private static void AppendFigures(PathGeometry target, PathGeometry source)
    {
        var transform = source.Transform?.Value ?? Matrix4x4.Identity;
        foreach (var figure in source.Figures)
        {
            target.Figures.Add(CloneFigure(figure, transform));
        }
    }

    private static bool TryCreatePathGeometry(object resource, out PathGeometry geometry)
    {
        geometry = new PathGeometry();

        if (TryGetPropertyValue(resource, "FillRule", out var fillRuleValue)
            && TryReadFillRule(fillRuleValue, out var fillRule))
        {
            geometry.FillRule = fillRule;
        }

        if (!TryGetPropertyValue(resource, "Figures", out var figuresValue)
            || figuresValue == null
            || !TryReadIntProperty(figuresValue, "Count", out var count)
            || count <= 0)
        {
            return false;
        }

        var getFigure = FindIndexer(figuresValue.GetType());
        if (getFigure == null)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var figureValue = getFigure(figuresValue, i);
            if (figureValue != null && TryCreatePathFigure(figureValue, out var figure))
            {
                geometry.Figures.Add(figure);
            }
        }

        return geometry.Figures.Count > 0;
    }

    private static bool TryCreatePathFigure(object figureValue, out PathFigure figure)
    {
        figure = new PathFigure();

        if (!TryGetPropertyValue(figureValue, "StartPoint", out var startPointValue)
            || startPointValue == null
            || !TryReadPoint(startPointValue, out var startPoint))
        {
            return false;
        }

        figure.StartPoint = startPoint;

        if (TryReadBoolProperty(figureValue, "IsClosed", out var isClosed))
        {
            figure.IsClosed = isClosed;
        }

        if (TryReadBoolProperty(figureValue, "IsFilled", out var isFilled))
        {
            figure.IsFilled = isFilled;
        }

        if (!TryGetPropertyValue(figureValue, "Segments", out var segmentsValue) || segmentsValue == null)
        {
            return true;
        }

        if (!TryReadIntProperty(segmentsValue, "Count", out var count) || count <= 0)
        {
            return true;
        }

        var getSegment = FindIndexer(segmentsValue.GetType());
        if (getSegment == null)
        {
            return true;
        }

        for (var i = 0; i < count; i++)
        {
            var segmentValue = getSegment(segmentsValue, i);
            if (segmentValue != null)
            {
                AppendPathSegment(figure, segmentValue);
            }
        }

        return true;
    }

    private static void AppendPathSegment(PathFigure figure, object segmentValue)
    {
        var isSmoothJoin = ReadIsSmoothJoin(segmentValue);

        if (TypeNameEndsWith(segmentValue, "PolyLineSegment"))
        {
            foreach (var point in ReadPointCollection(segmentValue))
            {
                figure.Segments.Add(new LineSegment(ToVector2(point), isSmoothJoin));
            }

            return;
        }

        if (TypeNameEndsWith(segmentValue, "PolyQuadraticBezierSegment"))
        {
            var points = ReadPointCollection(segmentValue);
            for (var i = 0; i + 1 < points.Count; i += 2)
            {
                figure.Segments.Add(new QuadraticBezierSegment(ToVector2(points[i]), ToVector2(points[i + 1]), isSmoothJoin));
            }

            return;
        }

        if (TypeNameEndsWith(segmentValue, "PolyBezierSegment"))
        {
            var points = ReadPointCollection(segmentValue);
            for (var i = 0; i + 2 < points.Count; i += 3)
            {
                figure.Segments.Add(new BezierSegment(ToVector2(points[i]), ToVector2(points[i + 1]), ToVector2(points[i + 2]), isSmoothJoin));
            }

            return;
        }

        if (TypeNameEndsWith(segmentValue, "LineSegment")
            && TryGetPropertyValue(segmentValue, "Point", out var linePointValue)
            && linePointValue != null
            && TryReadPoint(linePointValue, out var linePoint))
        {
            figure.Segments.Add(new LineSegment(ToVector2(linePoint), isSmoothJoin));
            return;
        }

        if (TypeNameEndsWith(segmentValue, "QuadraticBezierSegment")
            && TryGetPropertyValue(segmentValue, "Point1", out var quadraticPoint1Value)
            && TryGetPropertyValue(segmentValue, "Point2", out var quadraticPoint2Value)
            && quadraticPoint1Value != null
            && quadraticPoint2Value != null
            && TryReadPoint(quadraticPoint1Value, out var quadraticPoint1)
            && TryReadPoint(quadraticPoint2Value, out var quadraticPoint2))
        {
            figure.Segments.Add(new QuadraticBezierSegment(ToVector2(quadraticPoint1), ToVector2(quadraticPoint2), isSmoothJoin));
            return;
        }

        if (TypeNameEndsWith(segmentValue, "BezierSegment")
            && TryGetPropertyValue(segmentValue, "Point1", out var bezierPoint1Value)
            && TryGetPropertyValue(segmentValue, "Point2", out var bezierPoint2Value)
            && TryGetPropertyValue(segmentValue, "Point3", out var bezierPoint3Value)
            && bezierPoint1Value != null
            && bezierPoint2Value != null
            && bezierPoint3Value != null
            && TryReadPoint(bezierPoint1Value, out var bezierPoint1)
            && TryReadPoint(bezierPoint2Value, out var bezierPoint2)
            && TryReadPoint(bezierPoint3Value, out var bezierPoint3))
        {
            figure.Segments.Add(new BezierSegment(ToVector2(bezierPoint1), ToVector2(bezierPoint2), ToVector2(bezierPoint3), isSmoothJoin));
            return;
        }

        if (TypeNameEndsWith(segmentValue, "ArcSegment")
            && TryGetPropertyValue(segmentValue, "Point", out var arcPointValue)
            && TryGetPropertyValue(segmentValue, "Size", out var arcSizeValue)
            && arcPointValue != null
            && arcSizeValue != null
            && TryReadPoint(arcPointValue, out var arcPoint)
            && TryReadSize(arcSizeValue, out var arcSize))
        {
            var arc = new ArcSegment
            {
                Point = arcPoint,
                Size = ToSize(arcSize),
                IsSmoothJoin = isSmoothJoin
            };

            if (TryReadDoubleProperty(segmentValue, "RotationAngle", out var rotationAngle))
            {
                arc.RotationAngle = rotationAngle;
            }

            if (TryReadBoolProperty(segmentValue, "IsLargeArc", out var isLargeArc))
            {
                arc.IsLargeArc = isLargeArc;
            }

            if (TryGetPropertyValue(segmentValue, "SweepDirection", out var sweepDirectionValue)
                && TryReadSweepDirection(sweepDirectionValue, out var sweepDirection))
            {
                arc.SweepDirection = sweepDirection;
            }

            figure.Segments.Add(arc);
        }
    }

    private static IReadOnlyList<Point> ReadPointCollection(object segmentValue)
    {
        if (!TryGetPropertyValue(segmentValue, "Points", out var pointsValue)
            || !TryReadPointList(pointsValue, out var points))
        {
            return Array.Empty<Point>();
        }

        return points;
    }

    private static bool ReadIsSmoothJoin(object segmentValue)
    {
        return TryReadBoolProperty(segmentValue, "IsSmoothJoin", out var isSmoothJoin) && isSmoothJoin;
    }

    private static bool TryReadFillRule(object? value, out FillRule fillRule)
    {
        if (value is FillRule mediaFillRule)
        {
            fillRule = mediaFillRule;
            return true;
        }

        if (value != null && Enum.TryParse(value.ToString(), ignoreCase: false, out FillRule parsedFillRule))
        {
            fillRule = parsedFillRule;
            return true;
        }

        if (TryConvertToInt32(value, out var intValue) && Enum.IsDefined(typeof(FillRule), intValue))
        {
            fillRule = (FillRule)intValue;
            return true;
        }

        fillRule = FillRule.EvenOdd;
        return false;
    }

    private static bool TryReadGeometryCombineMode(object? value, out int pathOperation)
    {
        if (value != null)
        {
            switch (value.ToString())
            {
                case "Union":
                    pathOperation = 2;
                    return true;
                case "Intersect":
                    pathOperation = 1;
                    return true;
                case "Xor":
                    pathOperation = 3;
                    return true;
                case "Exclude":
                    pathOperation = 0;
                    return true;
            }
        }

        if (TryConvertToInt32(value, out var intValue))
        {
            pathOperation = intValue switch
            {
                0 => 2,
                1 => 1,
                2 => 3,
                3 => 0,
                _ => -1
            };
            return pathOperation >= 0;
        }

        pathOperation = -1;
        return false;
    }

    private static bool TryReadSweepDirection(object? value, out SweepDirection sweepDirection)
    {
        if (value is SweepDirection mediaSweepDirection)
        {
            sweepDirection = mediaSweepDirection;
            return true;
        }

        if (value != null && Enum.TryParse(value.ToString(), ignoreCase: false, out SweepDirection parsedSweepDirection))
        {
            sweepDirection = parsedSweepDirection;
            return true;
        }

        if (TryConvertToInt32(value, out var intValue) && Enum.IsDefined(typeof(SweepDirection), intValue))
        {
            sweepDirection = (SweepDirection)intValue;
            return true;
        }

        sweepDirection = SweepDirection.Counterclockwise;
        return false;
    }

    private static bool TryReadSize(object sizeValue, out Vector2 size)
    {
        if (TryReadDoubleProperty(sizeValue, "Width", out var width)
            && TryReadDoubleProperty(sizeValue, "Height", out var height))
        {
            size = new Vector2((float)width, (float)height);
            return true;
        }

        size = default;
        return false;
    }

    private static Vector2 ToVector2(Point point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static Vector2 ToVector2(PortablePoint point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static Vector2 ToVector2(Size size)
    {
        return new Vector2((float)size.Width, (float)size.Height);
    }

    private static Point ToPoint(Vector2 point)
    {
        return new Point(point.X, point.Y);
    }

    private static Point ToPoint(PortablePoint point)
    {
        return new Point(point.X, point.Y);
    }

    private static Size ToSize(Vector2 size)
    {
        return new Size(Math.Abs(size.X), Math.Abs(size.Y));
    }

    private static Size ToSize(PortableSize size)
    {
        return new Size(Math.Abs(size.Width), Math.Abs(size.Height));
    }

    private static PathFigure CloneFigure(PathFigure source)
    {
        return CloneFigure(source, Matrix4x4.Identity);
    }

    private static PathFigure CloneFigure(PathFigure source, Matrix4x4 transform)
    {
        var sourceCurrentPoint = ToVector2(source.StartPoint);
        var target = new PathFigure
        {
            StartPoint = ToPoint(Vector2.Transform(ToVector2(source.StartPoint), transform)),
            IsClosed = source.IsClosed,
            IsFilled = source.IsFilled
        };

        foreach (var segment in source.Segments)
        {
            switch (segment)
            {
                case LineSegment line:
                    target.Segments.Add(new LineSegment(Vector2.Transform(ToVector2(line.Point), transform), line.IsSmoothJoin));
                    sourceCurrentPoint = ToVector2(line.Point);
                    break;
                case QuadraticBezierSegment quadratic:
                    target.Segments.Add(new QuadraticBezierSegment(
                        Vector2.Transform(ToVector2(quadratic.Point1), transform),
                        Vector2.Transform(ToVector2(quadratic.Point2), transform),
                        quadratic.IsSmoothJoin));
                    sourceCurrentPoint = ToVector2(quadratic.Point2);
                    break;
                case BezierSegment bezier:
                    target.Segments.Add(new BezierSegment(
                        Vector2.Transform(ToVector2(bezier.Point1), transform),
                        Vector2.Transform(ToVector2(bezier.Point2), transform),
                        Vector2.Transform(ToVector2(bezier.Point3), transform),
                        bezier.IsSmoothJoin));
                    sourceCurrentPoint = ToVector2(bezier.Point3);
                    break;
                case ArcSegment arc:
                    if (TryTransformArcSegment(sourceCurrentPoint, arc, transform, out var transformedArc))
                    {
                        target.Segments.Add(transformedArc);
                    }
                    else
                    {
                        target.Segments.Add(new LineSegment(Vector2.Transform(ToVector2(arc.Point), transform), arc.IsSmoothJoin));
                    }

                    sourceCurrentPoint = ToVector2(arc.Point);
                    break;
            }
        }

        return target;
    }

    private static bool TryTransformArcSegment(
        Vector2 startPoint,
        ArcSegment arc,
        Matrix4x4 transform,
        out ArcSegment transformedArc)
    {
        transformedArc = null!;

        if (!global::ProGPU.Vector.ArcSegmentGeometry.TryTransformArcSegment(
                startPoint,
                new global::ProGPU.Vector.ArcSegment(
                    ToVector2(arc.Point),
                    ToVector2(arc.Size),
                    (float)arc.RotationAngle,
                    arc.IsLargeArc,
                    (global::ProGPU.Vector.SweepDirection)(int)arc.SweepDirection,
                    arc.IsSmoothJoin),
                transform,
                out _,
                out var vectorArc))
        {
            return false;
        }

        transformedArc = new ArcSegment
        {
            Point = ToPoint(vectorArc.Point),
            Size = ToSize(vectorArc.Size),
            RotationAngle = vectorArc.RotationAngle,
            IsLargeArc = vectorArc.IsLargeArc,
            SweepDirection = (SweepDirection)(int)vectorArc.SweepDirection,
            IsSmoothJoin = vectorArc.IsSmoothJoin
        };
        return true;
    }

    private static bool TryReadColor(object colorValue, out MediaColor color)
    {
        if (colorValue is MediaColor mediaColor)
        {
            color = mediaColor;
            return true;
        }

        if (TryReadByteProperty(colorValue, "A", out var a)
            && TryReadByteProperty(colorValue, "R", out var r)
            && TryReadByteProperty(colorValue, "G", out var g)
            && TryReadByteProperty(colorValue, "B", out var b))
        {
            color = MediaColor.FromArgb(a, r, g, b);
            return true;
        }

        color = default;
        return false;
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
            new MediaMatrix
            {
                M11 = matrix.M11,
                M12 = matrix.M12,
                M21 = matrix.M21,
                M22 = matrix.M22,
                OffsetX = matrix.OffsetX,
                OffsetY = matrix.OffsetY
            });
        return true;
    }

    private static bool NearlyEqual(float left, float right)
    {
        return Math.Abs(left - right) < 0.0001f;
    }

    private static bool TryReadRect(object rectValue, out Rect rectangle)
    {
        if (rectValue is Rect mediaRect)
        {
            rectangle = mediaRect;
            return true;
        }

        if (TryReadDoubleProperty(rectValue, "X", out var x)
            && TryReadDoubleProperty(rectValue, "Y", out var y)
            && TryReadDoubleProperty(rectValue, "Width", out var width)
            && TryReadDoubleProperty(rectValue, "Height", out var height))
        {
            rectangle = new Rect(x, y, width, height);
            return true;
        }

        rectangle = default;
        return false;
    }

    private static bool TryReadReplayRect(object rectValue, out WpfReplayRect rectangle)
    {
        if (rectValue is WpfReplayRect replayRect)
        {
            rectangle = replayRect;
            return true;
        }

        if (TryReadDoubleProperty(rectValue, "X", out var x)
            && TryReadDoubleProperty(rectValue, "Y", out var y)
            && TryReadDoubleProperty(rectValue, "Width", out var width)
            && TryReadDoubleProperty(rectValue, "Height", out var height))
        {
            rectangle = new WpfReplayRect(x, y, width, height);
            return true;
        }

        rectangle = default;
        return false;
    }

    private static bool TryReadPoint(object pointValue, out Point point)
    {
        if (pointValue is Point mediaPoint)
        {
            point = mediaPoint;
            return true;
        }

        if (TryReadDoubleProperty(pointValue, "X", out var x)
            && TryReadDoubleProperty(pointValue, "Y", out var y))
        {
            point = new Point(x, y);
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryReadReplayPoint(object pointValue, out WpfReplayPoint point)
    {
        if (pointValue is WpfReplayPoint replayPoint)
        {
            point = replayPoint;
            return true;
        }

        if (TryReadDoubleProperty(pointValue, "X", out var x)
            && TryReadDoubleProperty(pointValue, "Y", out var y))
        {
            point = new WpfReplayPoint(x, y);
            return true;
        }

        point = default;
        return false;
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

    private static bool TryReadDoubleList(object? listValue, out double[] values)
    {
        return TryReadList(listValue, TryConvertToDouble, out values);
    }

    private static bool TryReadPointList(object? listValue, out Point[] values)
    {
        return TryReadList(listValue, TryReadPoint, out values);
    }

    private static bool TryReadList<T>(
        object? listValue,
        TryConvertValue<T> convert,
        out T[] values)
    {
        values = Array.Empty<T>();

        if (listValue == null)
        {
            return false;
        }

        if (listValue is IEnumerable<T> typedValues)
        {
            values = typedValues.ToArray();
            return true;
        }

        if (!TryReadIntProperty(listValue, "Count", out var count) || count < 0)
        {
            return false;
        }

        var getItem = FindIndexer(listValue.GetType());
        if (getItem == null)
        {
            return false;
        }

        var result = new T[count];
        for (var i = 0; i < count; i++)
        {
            var item = getItem(listValue, i);
            if (item == null || !convert(item, out result[i]))
            {
                values = Array.Empty<T>();
                return false;
            }
        }

        values = result;
        return true;
    }

    private delegate bool TryConvertValue<T>(object value, out T result);

    private static bool TryReadByteProperty(object instance, string propertyName, out byte value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        switch (propertyValue)
        {
            case byte byteValue:
                value = byteValue;
                return true;
            case int intValue:
                value = ClampToByte(intValue);
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadBoolProperty(object instance, string propertyName, out bool value)
    {
        value = false;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        if (propertyValue is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        return false;
    }

    private static bool TryReadDoubleProperty(object instance, string propertyName, out double value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        return TryConvertToDouble(propertyValue, out value);
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
            case byte byteValue:
                result = byteValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryConvertToInt32(object? value, out int result)
    {
        switch (value)
        {
            case int intValue:
                result = intValue;
                return true;
            case uint uintValue when uintValue <= int.MaxValue:
                result = (int)uintValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case ushort ushortValue:
                result = ushortValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            case Enum enumValue:
                try
                {
                    result = Convert.ToInt32(enumValue, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception ex) when (ex is InvalidCastException or OverflowException)
                {
                    result = 0;
                    return false;
                }
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryReadIntProperty(object instance, string propertyName, out int value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        if (propertyValue is int intValue)
        {
            value = intValue;
            return true;
        }

        return false;
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

    private static Func<object, int, object?>? FindIndexer(Type type)
    {
        var indexer = type.GetProperty("Item", MemberFlags, binder: null, returnType: null, types: new[] { typeof(int) }, modifiers: null);
        if (indexer != null)
        {
            return (instance, index) => indexer.GetValue(instance, new object[] { index });
        }

        var getter = type.GetMethod("get_Item", MemberFlags, binder: null, types: new[] { typeof(int) }, modifiers: null);
        if (getter != null)
        {
            return (instance, index) => getter.Invoke(instance, new object[] { index });
        }

        return null;
    }

    private static bool TypeNameEndsWith(object resource, string typeName)
    {
        return resource.GetType().Name.EndsWith(typeName, StringComparison.Ordinal);
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
