using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Media.ProGPU.Composition;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaFormattedText = System.Windows.Media.FormattedText;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfVisualTreeReflectionRenderer
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly WpfRenderDataReflectionBridge _renderDataBridge;

    public WpfVisualTreeReflectionRenderer()
        : this(new WpfRenderDataReflectionBridge())
    {
    }

    public WpfVisualTreeReflectionRenderer(WpfRenderDataReflectionBridge renderDataBridge)
    {
        _renderDataBridge = renderDataBridge ?? throw new ArgumentNullException(nameof(renderDataBridge));
    }

    public WpfVisualReplayResult ReplaySubtree(
        object rootVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(rootVisual);
        ArgumentNullException.ThrowIfNull(sink);

        var stats = new ReplayStats();
        ReplaySubtreeCore(rootVisual, sink, resources, imageSourceAdapter, stats);
        return stats.ToResult();
    }

    private void ReplaySubtreeCore(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats)
    {
        stats.VisualCount++;

        var popCount = PushVisualState(visual, sink, stats);

        if (!ReplayViewport3DVisual(visual, sink, stats))
        {
            ReplayVisualContent(visual, sink, resources, imageSourceAdapter, stats);

            foreach (var child in ExtractChildren(visual))
            {
                stats.ChildEdgeCount++;
                ReplaySubtreeCore(child, sink, resources, imageSourceAdapter, stats);
            }
        }

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }
    }

    private static bool ReplayViewport3DVisual(
        object visual,
        IWpfCompositionCommandSink sink,
        ReplayStats stats)
    {
        if (!TypeNameEndsWith(visual, "Viewport3DVisual"))
        {
            return false;
        }

        if (sink is IWpfViewport3DCommandSink viewport3DSink
            && viewport3DSink.DrawViewport3D(visual))
        {
            stats.ContentCount++;
        }
        else
        {
            stats.UnsupportedContentCount++;
        }

        return true;
    }

    private void ReplayVisualContent(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats)
    {
        if (!WpfVisualContentReflectionBridge.TryExtractContent(visual, out var content) || content == null)
        {
            return;
        }

        if (!HasRenderDataShape(content.GetType()))
        {
            stats.UnsupportedContentCount++;
            return;
        }

        stats.ContentCount++;
        stats.AddRenderData(_renderDataBridge.Replay(content, sink, resources, imageSourceAdapter));
    }

    private static int PushVisualState(object visual, IWpfCompositionCommandSink sink, ReplayStats stats)
    {
        var popCount = 0;

        if (TryGetPropertyValue(visual, "Transform", out var transform) && transform != null)
        {
            var mediaTransform = WpfReflectionResourceResolver.AdaptTransform(transform);
            if (mediaTransform != null)
            {
                sink.PushTransform(mediaTransform);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryReadOffset(visual, out var offsetX, out var offsetY) && (offsetX != 0 || offsetY != 0))
        {
            var matrix = Matrix.Identity;
            matrix.Translate(offsetX, offsetY);
            sink.PushTransform(new MatrixTransform(matrix));
            popCount++;
        }

        if (TryGetPropertyValue(visual, "Clip", out var clip) && clip != null)
        {
            var clipGeometry = WpfReflectionResourceResolver.AdaptGeometry(clip);
            if (clipGeometry != null)
            {
                sink.PushClip(clipGeometry);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetPropertyValue(visual, "ScrollableAreaClip", out var scrollableAreaClip) && scrollableAreaClip != null)
        {
            if (TryReadRect(scrollableAreaClip, out var scrollableClipBounds) && IsUsableBounds(scrollableClipBounds))
            {
                sink.PushClip(WpfReflectionResourceResolver.CreateRectanglePath(scrollableClipBounds));
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetPropertyValue(visual, "Opacity", out var opacityValue)
            && TryConvertToDouble(opacityValue, out var opacity)
            && opacity != 1)
        {
            sink.PushOpacity(opacity);
            popCount++;
        }

        if (TryGetPropertyValue(visual, "OpacityMask", out var opacityMask) && opacityMask != null)
        {
            var mediaOpacityMask = WpfReflectionResourceResolver.AdaptBrush(opacityMask);
            if (mediaOpacityMask != null && TryReadOpacityMaskBounds(visual, out var opacityMaskBounds))
            {
                sink.PushOpacityMask(mediaOpacityMask, opacityMaskBounds);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetPropertyValue(visual, "Effect", out var effect) && effect != null)
        {
            if (WpfEffectReflection.TryCreateProGpuEffect(effect, out var proGpuEffect)
                && sink is IWpfVisualEffectCommandSink effectSink
                && effectSink.PushVisualEffect(proGpuEffect))
            {
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryCreateVisualGuidelineSet(visual, out var guidelineSet))
        {
            sink.PushGuidelineSet(guidelineSet);
            popCount++;
        }
        else if (HasVisualGuidelines(visual))
        {
            sink.PushGuidelineSet();
            popCount++;
        }

        if (TryGetPropertyValue(visual, "BitmapScalingMode", out var bitmapScalingMode)
            && WpfBitmapScalingModeReflection.HasExplicitValue(bitmapScalingMode))
        {
            if (WpfBitmapScalingModeReflection.IsSupported(bitmapScalingMode))
            {
                sink.PushBitmapScalingMode(bitmapScalingMode);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetPropertyValue(visual, "EdgeMode", out var edgeMode)
            && WpfEdgeModeReflection.HasExplicitValue(edgeMode))
        {
            if (WpfEdgeModeReflection.IsSupported(edgeMode))
            {
                sink.PushEdgeMode(edgeMode);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetPropertyValue(visual, "TextRenderingMode", out var textRenderingMode)
            && WpfTextRenderingModeReflection.HasExplicitValue(textRenderingMode))
        {
            if (WpfTextRenderingModeReflection.IsSupported(textRenderingMode))
            {
                sink.PushTextRenderingMode(textRenderingMode);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        stats.UnsupportedVisualStateCount += CountUnsupportedVisualState(visual);

        return popCount;
    }

    private static int CountUnsupportedVisualState(object visual)
    {
        var count = 0;

        foreach (var propertyName in new[] { "BitmapEffect", "BitmapEffectInput", "CacheMode" })
        {
            if (HasNonNullProperty(visual, propertyName))
            {
                count++;
            }
        }

        foreach (var propertyName in new[]
        {
            "ClearTypeHint",
            "TextHintingMode"
        })
        {
            if (HasExplicitRenderingHint(visual, propertyName))
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasExplicitRenderingHint(object visual, string propertyName)
    {
        if (!TryGetPropertyValue(visual, propertyName, out var value) || value == null)
        {
            return false;
        }

        return HasExplicitRenderingHintValue(value);
    }

    private static bool HasExplicitRenderingHintValue(object? value)
    {
        var text = value?.ToString();
        return !string.IsNullOrEmpty(text)
            && !string.Equals(text, "Unspecified", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "Default", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasVisualGuidelines(object visual)
    {
        return HasNonNullProperty(visual, "XSnappingGuidelines")
            || HasNonNullProperty(visual, "YSnappingGuidelines")
            || HasNonNullProperty(visual, "VisualXSnappingGuidelines")
            || HasNonNullProperty(visual, "VisualYSnappingGuidelines");
    }

    private static bool TryCreateVisualGuidelineSet(object visual, out object guidelineSet)
    {
        var hasX = TryReadVisualGuidelines(
            visual,
            new[] { "XSnappingGuidelines", "VisualXSnappingGuidelines" },
            out var guidelinesX);
        var hasY = TryReadVisualGuidelines(
            visual,
            new[] { "YSnappingGuidelines", "VisualYSnappingGuidelines" },
            out var guidelinesY);

        if (!hasX && !hasY)
        {
            guidelineSet = null!;
            return false;
        }

        guidelineSet = new ReflectedGuidelineSet(guidelinesX, guidelinesY);
        return true;
    }

    private static bool TryReadVisualGuidelines(object visual, string[] propertyNames, out double[] guidelines)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetPropertyValue(visual, propertyName, out var collection)
                && collection != null
                && WpfGuidelineSetReflection.TryReadDoubleCollection(collection, out guidelines))
            {
                return true;
            }
        }

        guidelines = Array.Empty<double>();
        return false;
    }

    private static IReadOnlyList<object> ExtractChildren(object visual)
    {
        if (!TryGetPropertyValue(visual, "Children", out var children) || children == null)
        {
            return Array.Empty<object>();
        }

        if (!TryReadIntProperty(children, "Count", out var count) || count <= 0)
        {
            return Array.Empty<object>();
        }

        var getChild = FindIndexer(children.GetType());
        if (getChild == null)
        {
            return Array.Empty<object>();
        }

        var result = new List<object>(count);
        for (var i = 0; i < count; i++)
        {
            var child = getChild(children, i);
            if (child != null)
            {
                result.Add(child);
            }
        }

        return result;
    }

    private static bool TryReadOffset(object visual, out double x, out double y)
    {
        x = 0;
        y = 0;

        if (!TryGetPropertyValue(visual, "Offset", out var offset) || offset == null)
        {
            return false;
        }

        return TryReadDoubleProperty(offset, "X", out x)
            && TryReadDoubleProperty(offset, "Y", out y);
    }

    private static bool TryReadOpacityMaskBounds(object visual, out Rect bounds)
    {
        foreach (var propertyName in new[] { "Bounds", "DescendantBounds", "VisualContentBounds", "ContentBounds" })
        {
            if (TryGetPropertyValue(visual, propertyName, out var boundsValue)
                && boundsValue != null
                && TryReadRect(boundsValue, out bounds)
                && IsUsableBounds(bounds))
            {
                return true;
            }
        }

        if (TryGetPropertyValue(visual, "RenderSize", out var renderSize)
            && renderSize != null
            && TryReadSize(renderSize, out var width, out var height)
            && width > 0
            && height > 0)
        {
            bounds = new Rect(0, 0, width, height);
            return true;
        }

        if (TryReadDoubleProperty(visual, "ActualWidth", out width)
            && TryReadDoubleProperty(visual, "ActualHeight", out height)
            && width > 0
            && height > 0)
        {
            bounds = new Rect(0, 0, width, height);
            return true;
        }

        if (TryInferVisualContentBounds(visual, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryInferVisualContentBounds(object visual, out Rect bounds)
    {
        bounds = default;
        var hasBounds = false;

        if (WpfVisualContentReflectionBridge.TryExtractContent(visual, out var content)
            && content != null
            && HasRenderDataShape(content.GetType()))
        {
            var snapshot = WpfRenderDataReflectionBridge.Extract(content);
            var resolver = WpfReflectionResourceResolver.FromDependentResources(snapshot.DependentResources);
            var sink = new BoundsAccumulatingSink();
            _ = new WpfMilRenderDataDecoder().Decode(snapshot.RenderData, sink, resolver);
            if (sink.TryGetBounds(out var contentBounds))
            {
                bounds = contentBounds;
                hasBounds = true;
            }
        }

        foreach (var child in ExtractChildren(visual))
        {
            if (!TryReadOpacityMaskBounds(child, out var childBounds))
            {
                continue;
            }

            bounds = hasBounds ? UnionBounds(bounds, childBounds) : childBounds;
            hasBounds = true;
        }

        return hasBounds && IsUsableBounds(bounds);
    }

    private static bool TryReadRect(object rectValue, out Rect bounds)
    {
        if (rectValue is Rect mediaRect)
        {
            bounds = mediaRect;
            return true;
        }

        if (TryReadDoubleProperty(rectValue, "X", out var x)
            && TryReadDoubleProperty(rectValue, "Y", out var y)
            && TryReadDoubleProperty(rectValue, "Width", out var width)
            && TryReadDoubleProperty(rectValue, "Height", out var height))
        {
            bounds = new Rect(x, y, width, height);
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryReadSize(object sizeValue, out double width, out double height)
    {
        width = 0;
        height = 0;

        if (sizeValue is Size mediaSize)
        {
            width = mediaSize.Width;
            height = mediaSize.Height;
            return true;
        }

        var hasWidth = TryReadDoubleProperty(sizeValue, "Width", out width);
        var hasHeight = TryReadDoubleProperty(sizeValue, "Height", out height);
        return hasWidth && hasHeight;
    }

    private static bool IsUsableBounds(Rect bounds)
    {
        return !bounds.IsEmpty
            && double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height)
            && bounds.Width > 0
            && bounds.Height > 0;
    }

    private static Rect UnionBounds(Rect left, Rect right)
    {
        var x1 = Math.Min(left.X, right.X);
        var y1 = Math.Min(left.Y, right.Y);
        var x2 = Math.Max(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Max(left.Y + left.Height, right.Y + right.Height);

        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private static Rect IntersectBounds(Rect left, Rect right)
    {
        var x1 = Math.Max(left.X, right.X);
        var y1 = Math.Max(left.Y, right.Y);
        var x2 = Math.Min(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Min(left.Y + left.Height, right.Y + right.Height);

        return x2 <= x1 || y2 <= y1
            ? Rect.Empty
            : new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private static Rect TransformBounds(Rect bounds, System.Numerics.Matrix4x4 transform)
    {
        if (transform.IsIdentity)
        {
            return bounds;
        }

        var p1 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)bounds.X, (float)bounds.Y), transform);
        var p2 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)(bounds.X + bounds.Width), (float)bounds.Y), transform);
        var p3 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)bounds.X, (float)(bounds.Y + bounds.Height)), transform);
        var p4 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)(bounds.X + bounds.Width), (float)(bounds.Y + bounds.Height)), transform);

        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static Rect? ApplyClip(Rect bounds, Rect? clip)
    {
        if (!IsUsableBounds(bounds))
        {
            return null;
        }

        if (!clip.HasValue)
        {
            return bounds;
        }

        var clipped = IntersectBounds(bounds, clip.Value);
        return IsUsableBounds(clipped) ? clipped : null;
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

    private static bool HasNonNullProperty(object instance, string propertyName)
    {
        return TryGetPropertyValue(instance, propertyName, out var value) && value != null;
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
            default:
                result = 0;
                return false;
        }
    }

    private static bool TypeNameEndsWith(object instance, string suffix)
    {
        var type = instance.GetType();
        return type.Name.EndsWith(suffix, StringComparison.Ordinal)
            || (type.FullName?.EndsWith("." + suffix, StringComparison.Ordinal) ?? false);
    }

    private static bool HasRenderDataShape(Type contentType)
    {
        return FindField(contentType, "_buffer") != null
            && FindField(contentType, "_curOffset") != null
            && FindField(contentType, "_dependentResources") != null;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }

    private sealed class BoundsAccumulatingSink : IWpfCompositionCommandSink
    {
        private enum PushKind
        {
            NoOp,
            Clip,
            Transform
        }

        private readonly Stack<PushKind> _pushStack = new();
        private readonly Stack<System.Numerics.Matrix4x4> _transformStack = new();
        private readonly Stack<Rect?> _clipStack = new();
        private Rect _bounds;
        private bool _hasBounds;

        public BoundsAccumulatingSink()
        {
            _transformStack.Push(System.Numerics.Matrix4x4.Identity);
            _clipStack.Push(null);
        }

        public MediaDrawingContext DrawingContext => null!;

        public bool TryGetBounds(out Rect bounds)
        {
            bounds = _bounds;
            return _hasBounds && IsUsableBounds(_bounds);
        }

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
            var thickness = Math.Max(1, pen?.Thickness ?? 1);
            var minX = Math.Min(point0.X, point1.X) - thickness / 2;
            var minY = Math.Min(point0.Y, point1.Y) - thickness / 2;
            var maxX = Math.Max(point0.X, point1.X) + thickness / 2;
            var maxY = Math.Max(point0.Y, point1.Y) + thickness / 2;
            AddBounds(new Rect(minX, minY, maxX - minX, maxY - minY));
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
            AddBounds(InflateForPen(rectangle, pen));
        }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            AddBounds(InflateForPen(rectangle, pen));
        }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
        {
            AddBounds(InflateForPen(new Rect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2), pen));
        }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
            AddBounds(InflateForPen(geometry.Bounds, pen));
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
            AddBounds(rectangle);
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle, Rect sourceRectangle)
        {
            AddBounds(rectangle);
        }

        public void DrawText(MediaFormattedText formattedText, Point origin)
        {
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
            if (TryGetGlyphRunBounds(glyphRun, out var bounds))
            {
                AddBounds(bounds);
            }
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
            var clip = TransformBounds(clipGeometry.Bounds, _transformStack.Peek());
            var currentClip = _clipStack.Peek();
            _clipStack.Push(currentClip.HasValue ? IntersectBounds(currentClip.Value, clip) : clip);
            _pushStack.Push(PushKind.Clip);
        }

        public void PushOpacity(double opacity)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushTransform(MediaTransform transform)
        {
            _transformStack.Push(transform.Value * _transformStack.Peek());
            _pushStack.Push(PushKind.Transform);
        }

        public void PushNoOpScope()
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushGuidelineSet()
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushGuidelineY1(double coordinate)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void Pop()
        {
            if (_pushStack.Count == 0)
            {
                return;
            }

            var kind = _pushStack.Pop();
            if (kind == PushKind.Transform && _transformStack.Count > 1)
            {
                _transformStack.Pop();
            }
            else if (kind == PushKind.Clip && _clipStack.Count > 1)
            {
                _clipStack.Pop();
            }
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }

        private void AddBounds(Rect bounds)
        {
            var transformed = TransformBounds(bounds, _transformStack.Peek());
            var clipped = ApplyClip(transformed, _clipStack.Peek());
            if (!clipped.HasValue)
            {
                return;
            }

            _bounds = _hasBounds ? UnionBounds(_bounds, clipped.Value) : clipped.Value;
            _hasBounds = true;
        }

        private static Rect InflateForPen(Rect bounds, MediaPen? pen)
        {
            if (pen == null || !IsUsableBounds(bounds))
            {
                return bounds;
            }

            var halfThickness = Math.Max(0, pen.Thickness) / 2;
            return new Rect(
                bounds.X - halfThickness,
                bounds.Y - halfThickness,
                bounds.Width + halfThickness * 2,
                bounds.Height + halfThickness * 2);
        }

        private static bool TryGetGlyphRunBounds(MediaGlyphRun glyphRun, out Rect bounds)
        {
            bounds = default;

            if (glyphRun.FontSize <= 0)
            {
                return false;
            }

            var minX = glyphRun.Position.X;
            var minY = glyphRun.Position.Y - glyphRun.FontSize;
            var maxX = glyphRun.Position.X;
            var maxY = glyphRun.Position.Y;

            if (glyphRun.GlyphPositions.Length == 0)
            {
                maxX += glyphRun.FontSize;
            }
            else
            {
                foreach (var position in glyphRun.GlyphPositions)
                {
                    minX = Math.Min(minX, glyphRun.Position.X + position.X);
                    minY = Math.Min(minY, glyphRun.Position.Y + position.Y - glyphRun.FontSize);
                    maxX = Math.Max(maxX, glyphRun.Position.X + position.X + glyphRun.FontSize);
                    maxY = Math.Max(maxY, glyphRun.Position.Y + position.Y);
                }
            }

            bounds = TransformBounds(
                new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)),
                glyphRun.Transform);
            return IsUsableBounds(bounds);
        }
    }

    private sealed class ReflectedGuidelineSet
    {
        public ReflectedGuidelineSet(double[] guidelinesX, double[] guidelinesY)
        {
            GuidelinesX = new ReflectedGuidelineCollection(guidelinesX);
            GuidelinesY = new ReflectedGuidelineCollection(guidelinesY);
        }

        public bool IsFrozen => true;

        public bool IsDynamic => true;

        public ReflectedGuidelineCollection GuidelinesX { get; }

        public ReflectedGuidelineCollection GuidelinesY { get; }
    }

    private sealed class ReflectedGuidelineCollection
    {
        private readonly double[] _values;

        public ReflectedGuidelineCollection(double[] values)
        {
            _values = values;
        }

        public int Count => _values.Length;

        public double this[int index] => _values[index];
    }

    private sealed class ReplayStats
    {
        private int _renderDataRecordCount;
        private int _renderDataAppliedCount;
        private int _renderDataSkippedCount;
        private int _renderDataUnsupportedCount;

        public int VisualCount { get; set; }

        public int ContentCount { get; set; }

        public int ChildEdgeCount { get; set; }

        public int UnsupportedContentCount { get; set; }

        public int UnsupportedVisualStateCount { get; set; }

        public void AddRenderData(WpfMilDecodeResult result)
        {
            _renderDataRecordCount += result.RecordCount;
            _renderDataAppliedCount += result.AppliedCount;
            _renderDataSkippedCount += result.SkippedCount;
            _renderDataUnsupportedCount += result.UnsupportedCount;
        }

        public WpfVisualReplayResult ToResult()
        {
            return new WpfVisualReplayResult(
                VisualCount,
                ContentCount,
                ChildEdgeCount,
                UnsupportedContentCount,
                UnsupportedVisualStateCount,
                new WpfMilDecodeResult(
                    _renderDataRecordCount,
                    _renderDataAppliedCount,
                    _renderDataSkippedCount,
                    _renderDataUnsupportedCount));
        }
    }
}

public readonly record struct WpfVisualReplayResult(
    int VisualCount,
    int ContentCount,
    int ChildEdgeCount,
    int UnsupportedContentCount,
    int UnsupportedVisualStateCount,
    WpfMilDecodeResult RenderData);
