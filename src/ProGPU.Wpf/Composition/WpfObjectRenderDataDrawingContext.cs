using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaBrush = System.Windows.Media.Brush;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class WpfObjectRenderDataDrawingContext : IDisposable
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly IWpfCompositionCommandSink _sink;
    private readonly WpfReflectionResourceResolver _resources;
    private readonly IWpfImageSourceAdapter? _imageSourceAdapter;
    private int _stackDepth;
    private int _operationCount;
    private int _appliedCount;
    private int _unsupportedCount;
    private bool _isClosed;

    public WpfObjectRenderDataDrawingContext(
        IWpfCompositionCommandSink sink,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _imageSourceAdapter = imageSourceAdapter;
        _resources = new WpfReflectionResourceResolver(imageSourceAdapter);
    }

    public int StackDepth => _stackDepth;

    public WpfCompositionDrawingContextResult Result => new(
        _operationCount,
        _appliedCount,
        _unsupportedCount);

    public void DrawLine(object? pen, object? point0, object? point1)
    {
        ThrowIfClosed();
        MediaPen? mediaPen = WpfReflectionResourceResolver.AdaptPen(pen);
        if (mediaPen == null)
        {
            CountUnsupportedIfPresent(pen);
            return;
        }

        if (!TryReadPoint(point0, out var mediaPoint0) || !TryReadPoint(point1, out var mediaPoint1))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(pen);
        _sink.DrawLine(mediaPen, mediaPoint0, mediaPoint1);
        CountApplied();
    }

    public void DrawLine(object? pen, object? point0, object? point0Animations, object? point1, object? point1Animations)
    {
        DrawLine(pen, point0, point1);
        CountUnsupportedStateIfAny(point0Animations, point1Animations);
    }

    public void DrawRectangle(object? brush, object? pen, object? rectangle)
    {
        ThrowIfClosed();
        MediaBrush? mediaBrush = WpfReflectionResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfReflectionResourceResolver.AdaptPen(pen);
        if (!TryReadRect(rectangle, out var mediaRectangle))
        {
            CountUnsupported();
            return;
        }

        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen);
            _sink.DrawRectangle(mediaBrush, mediaPen, mediaRectangle);
            CountApplied();
            return;
        }

        if (brush != null
            && WpfReflectionDrawingReplay.TryReplayImageBrushFill(
                brush,
                WpfReflectionResourceResolver.CreateRectanglePath(mediaRectangle),
                _sink,
                _resources.AdaptImageSource))
        {
            RegisterRetainedDependencies(brush, pen);
            if (mediaPen != null)
            {
                _sink.DrawRectangle(null, mediaPen, mediaRectangle);
            }

            CountApplied();
            return;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(pen);
            _sink.DrawRectangle(null, mediaPen, mediaRectangle);
            CountApplied();
            return;
        }

        CountUnsupportedIfPresent(brush, pen);
    }

    public void DrawRectangle(object? brush, object? pen, object? rectangle, object? rectangleAnimations)
    {
        DrawRectangle(brush, pen, rectangle);
        CountUnsupportedStateIfAny(rectangleAnimations);
    }

    public void DrawRoundedRectangle(object? brush, object? pen, object? rectangle, object? radiusX, object? radiusY)
    {
        ThrowIfClosed();
        MediaBrush? mediaBrush = WpfReflectionResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfReflectionResourceResolver.AdaptPen(pen);
        if (mediaBrush == null && mediaPen == null)
        {
            CountUnsupportedIfPresent(brush, pen);
            return;
        }

        if (!TryReadRect(rectangle, out var mediaRectangle)
            || !TryReadDouble(radiusX, out var mediaRadiusX)
            || !TryReadDouble(radiusY, out var mediaRadiusY))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        _sink.DrawRoundedRectangle(mediaBrush, mediaPen, mediaRectangle, mediaRadiusX, mediaRadiusY);
        CountApplied();
    }

    public void DrawRoundedRectangle(
        object? brush,
        object? pen,
        object? rectangle,
        object? rectangleAnimations,
        object? radiusX,
        object? radiusXAnimations,
        object? radiusY,
        object? radiusYAnimations)
    {
        DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        CountUnsupportedStateIfAny(rectangleAnimations, radiusXAnimations, radiusYAnimations);
    }

    public void DrawEllipse(object? brush, object? pen, object? center, object? radiusX, object? radiusY)
    {
        ThrowIfClosed();
        MediaBrush? mediaBrush = WpfReflectionResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfReflectionResourceResolver.AdaptPen(pen);
        if (mediaBrush == null && mediaPen == null)
        {
            CountUnsupportedIfPresent(brush, pen);
            return;
        }

        if (!TryReadPoint(center, out var mediaCenter)
            || !TryReadDouble(radiusX, out var mediaRadiusX)
            || !TryReadDouble(radiusY, out var mediaRadiusY))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        _sink.DrawEllipse(mediaBrush, mediaPen, mediaCenter, mediaRadiusX, mediaRadiusY);
        CountApplied();
    }

    public void DrawEllipse(
        object? brush,
        object? pen,
        object? center,
        object? centerAnimations,
        object? radiusX,
        object? radiusXAnimations,
        object? radiusY,
        object? radiusYAnimations)
    {
        DrawEllipse(brush, pen, center, radiusX, radiusY);
        CountUnsupportedStateIfAny(centerAnimations, radiusXAnimations, radiusYAnimations);
    }

    public void DrawGeometry(object? brush, object? pen, object? geometry)
    {
        ThrowIfClosed();
        MediaBrush? mediaBrush = WpfReflectionResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfReflectionResourceResolver.AdaptPen(pen);
        MediaGeometry? mediaGeometry = WpfReflectionResourceResolver.AdaptGeometry(geometry);
        if (mediaGeometry == null)
        {
            CountUnsupportedIfPresent(brush, pen, geometry);
            return;
        }

        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            _sink.DrawGeometry(mediaBrush, mediaPen, mediaGeometry);
            CountApplied();
            return;
        }

        if (brush != null
            && WpfReflectionDrawingReplay.TryReplayImageBrushFill(
                brush,
                mediaGeometry,
                _sink,
                _resources.AdaptImageSource))
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            if (mediaPen != null)
            {
                _sink.DrawGeometry(null, mediaPen, mediaGeometry);
            }

            CountApplied();
            return;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(pen, geometry);
            _sink.DrawGeometry(null, mediaPen, mediaGeometry);
            CountApplied();
            return;
        }

        CountUnsupportedIfPresent(brush, pen, geometry);
    }

    public void DrawImage(object? imageSource, object? rectangle)
    {
        ThrowIfClosed();
        MediaImageSource? mediaImageSource = _resources.AdaptImageSource(imageSource);
        if (mediaImageSource == null)
        {
            CountUnsupportedIfPresent(imageSource);
            return;
        }

        if (!TryReadRect(rectangle, out var mediaRectangle))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(imageSource);
        _sink.DrawImage(mediaImageSource, mediaRectangle);
        CountApplied();
    }

    public void DrawImage(object? imageSource, object? rectangle, object? rectangleAnimations)
    {
        DrawImage(imageSource, rectangle);
        CountUnsupportedStateIfAny(rectangleAnimations);
    }

    public void DrawGlyphRun(object? foregroundBrush, object? glyphRun)
    {
        ThrowIfClosed();
        MediaBrush? mediaBrush = WpfReflectionResourceResolver.AdaptBrush(foregroundBrush);
        MediaGlyphRun? mediaGlyphRun = WpfReflectionResourceResolver.AdaptGlyphRun(glyphRun);
        if (mediaBrush == null || mediaGlyphRun == null)
        {
            CountUnsupportedIfPresent(foregroundBrush, glyphRun);
            return;
        }

        RegisterRetainedDependencies(foregroundBrush, glyphRun);
        _sink.DrawGlyphRun(mediaBrush, mediaGlyphRun);
        CountApplied();
    }

    public void DrawDrawing(object? drawing)
    {
        ThrowIfClosed();
        var status = WpfReflectionDrawingReplay.Replay(drawing, _sink, _resources.AdaptImageSource);
        if (status is WpfDrawingReplayStatus.Applied or WpfDrawingReplayStatus.PartiallyApplied)
        {
            RegisterRetainedDependencies(drawing);
        }

        CountDrawingReplayStatus(status);
    }

    public void DrawVideo(object? player, object? rectangle)
    {
        ThrowIfClosed();
        CountUnsupportedIfPresent(player);
    }

    public void DrawVideo(object? player, object? rectangle, object? rectangleAnimations)
    {
        DrawVideo(player, rectangle);
    }

    public void PushClip(object? clipGeometry)
    {
        ThrowIfClosed();
        if (clipGeometry == null)
        {
            _sink.PushNoOpScope();
        }
        else if (WpfReflectionResourceResolver.AdaptGeometry(clipGeometry) is { } mediaGeometry)
        {
            RegisterRetainedDependencies(clipGeometry);
            _sink.PushClip(mediaGeometry);
        }
        else
        {
            _sink.PushNoOpScope();
            CountUnsupported();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushOpacityMask(object? opacityMask)
    {
        ThrowIfClosed();
        if (opacityMask == null)
        {
            _sink.PushNoOpScope();
        }
        else if (WpfReflectionResourceResolver.AdaptBrush(opacityMask) is { } mediaOpacityMask)
        {
            RegisterRetainedDependencies(opacityMask);
            _sink.PushOpacityMask(mediaOpacityMask, Rect.Empty);
        }
        else
        {
            _sink.PushNoOpScope();
            CountUnsupported();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushOpacity(object? opacity)
    {
        ThrowIfClosed();
        if (TryReadDouble(opacity, out var mediaOpacity))
        {
            _sink.PushOpacity(mediaOpacity);
        }
        else
        {
            _sink.PushNoOpScope();
            CountUnsupported();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushOpacity(object? opacity, object? opacityAnimations)
    {
        PushOpacity(opacity);
        CountUnsupportedStateIfAny(opacityAnimations);
    }

    public void PushTransform(object? transform)
    {
        ThrowIfClosed();
        if (transform == null)
        {
            _sink.PushNoOpScope();
        }
        else if (WpfReflectionResourceResolver.AdaptTransform(transform) is { } mediaTransform)
        {
            RegisterRetainedDependencies(transform);
            _sink.PushTransform(mediaTransform);
        }
        else
        {
            _sink.PushNoOpScope();
            CountUnsupported();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushGuidelineSet(object? guidelines)
    {
        ThrowIfClosed();

        RegisterRetainedDependencies(guidelines);
        if (WpfGuidelineSetReflection.TryReadDynamicGuidelineYPair(guidelines, out var leadingCoordinate, out var drivenCoordinate))
        {
            _sink.PushGuidelineY2(leadingCoordinate, drivenCoordinate - leadingCoordinate);
        }
        else if (WpfGuidelineSetReflection.TryReadDynamicGuidelineY1(guidelines, out var coordinate))
        {
            _sink.PushGuidelineY1(coordinate);
        }
        else if (WpfGuidelineSetReflection.TryReadDynamicGuidelineSet(guidelines, out _, out _))
        {
            _sink.PushGuidelineSet(guidelines);
        }
        else
        {
            _sink.PushGuidelineSet();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushGuidelineY1(object? coordinate)
    {
        ThrowIfClosed();
        if (TryReadDouble(coordinate, out var mediaCoordinate))
        {
            _sink.PushGuidelineY1(mediaCoordinate);
        }
        else
        {
            _sink.PushNoOpScope();
            CountUnsupported();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushGuidelineY2(object? leadingCoordinate, object? offsetToDrivenCoordinate)
    {
        ThrowIfClosed();
        if (TryReadDouble(leadingCoordinate, out var mediaLeadingCoordinate)
            && TryReadDouble(offsetToDrivenCoordinate, out var mediaOffsetToDrivenCoordinate))
        {
            _sink.PushGuidelineY2(mediaLeadingCoordinate, mediaOffsetToDrivenCoordinate);
        }
        else
        {
            _sink.PushNoOpScope();
            CountUnsupported();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushEffect(object? effect, object? effectInput)
    {
        ThrowIfClosed();

        if (WpfEffectReflection.TryCreateProGpuPushEffect(effect, effectInput, out var proGpuEffect, _imageSourceAdapter)
            && _sink is IWpfVisualEffectCommandSink effectSink
            && effectSink.PushVisualEffect(proGpuEffect))
        {
            RegisterRetainedDependencies(effect, effectInput);
            _stackDepth++;
            CountApplied();
            return;
        }

        _sink.PushNoOpScope();
        _stackDepth++;
        CountPartiallyApplied();
    }

    public void Pop()
    {
        ThrowIfClosed();

        if (_stackDepth <= 0)
        {
            throw new InvalidOperationException("Cannot pop more drawing-context scopes than were pushed.");
        }

        _sink.Pop();
        _stackDepth--;
        CountApplied();
    }

    public void Close()
    {
        if (_isClosed)
        {
            return;
        }

        while (_stackDepth > 0)
        {
            _sink.Pop();
            _stackDepth--;
            CountApplied();
        }

        _sink.Close();
        _isClosed = true;
    }

    public void Dispose()
    {
        Close();
    }

    private void ThrowIfClosed()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
    }

    private void CountApplied()
    {
        _operationCount++;
        _appliedCount++;
    }

    private void CountUnsupported()
    {
        _operationCount++;
        _unsupportedCount++;
    }

    private void CountPartiallyApplied()
    {
        _operationCount++;
        _appliedCount++;
        _unsupportedCount++;
    }

    private void CountUnsupportedIfPresent(params object?[] unsupportedState)
    {
        foreach (object? state in unsupportedState)
        {
            if (state != null)
            {
                CountUnsupported();
                return;
            }
        }
    }

    private void CountDrawingReplayStatus(WpfDrawingReplayStatus status)
    {
        switch (status)
        {
            case WpfDrawingReplayStatus.Applied:
                CountApplied();
                break;
            case WpfDrawingReplayStatus.PartiallyApplied:
                CountPartiallyApplied();
                break;
            case WpfDrawingReplayStatus.Unsupported:
                CountUnsupported();
                break;
        }
    }

    private void CountUnsupportedStateIfAny(params object?[] unsupportedState)
    {
        foreach (object? state in unsupportedState)
        {
            if (state != null)
            {
                _unsupportedCount++;
            }
        }
    }

    private void RegisterRetainedDependencies(params object?[] dependencies)
    {
        WpfRetainedVisualDependencyRegistrar.Register(_sink, dependencies);
    }

    private static bool TryReadPoint(object? pointValue, out Point point)
    {
        if (pointValue is Point mediaPoint)
        {
            point = mediaPoint;
            return true;
        }

        if (pointValue != null
            && TryReadDoubleProperty(pointValue, "X", out var x)
            && TryReadDoubleProperty(pointValue, "Y", out var y))
        {
            point = new Point(x, y);
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryReadRect(object? rectValue, out Rect rectangle)
    {
        if (rectValue is Rect mediaRect)
        {
            rectangle = mediaRect;
            return true;
        }

        if (rectValue != null
            && TryReadDoubleProperty(rectValue, "X", out var x)
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

    private static bool TryReadDoubleProperty(object instance, string propertyName, out double value)
    {
        value = 0;
        PropertyInfo? property = instance.GetType().GetProperty(propertyName, MemberFlags);
        if (property == null)
        {
            return false;
        }

        return TryReadDouble(property.GetValue(instance), out value);
    }

    private static bool TryReadDouble(object? value, out double result)
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
            case short shortValue:
                result = shortValue;
                return true;
            case ushort ushortValue:
                result = ushortValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            case sbyte sbyteValue:
                result = sbyteValue;
                return true;
            case long longValue when longValue >= -9007199254740992L && longValue <= 9007199254740992L:
                result = longValue;
                return true;
            case ulong ulongValue when ulongValue <= 9007199254740992UL:
                result = ulongValue;
                return true;
            case decimal decimalValue:
                result = (double)decimalValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
