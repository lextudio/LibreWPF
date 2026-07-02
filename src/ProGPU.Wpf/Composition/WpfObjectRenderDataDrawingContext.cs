using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaBrush = System.Windows.Media.Brush;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPortableRenderDataSink = System.Windows.Media.IPortableRenderDataDrawingContextSink;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;
using PortablePoint = ProGPU.Wpf.Interop.PortablePoint;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class WpfObjectRenderDataDrawingContext : MediaPortableRenderDataSink, IDisposable
{
    private readonly IWpfCompositionCommandSink _sink;
    private readonly WpfResourceResolver _resources;
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
        _resources = new WpfResourceResolver(imageSourceAdapter);
    }

    public int StackDepth => _stackDepth;

    public WpfCompositionDrawingContextResult Result => new(
        _operationCount,
        _appliedCount,
        _unsupportedCount);

    public void DrawLine(object? pen, object? point0, object? point1)
    {
        ThrowIfClosed();
        MediaPen? mediaPen = WpfResourceResolver.AdaptPen(pen);
        if (mediaPen == null)
        {
            CountUnsupportedIfPresent(pen);
            return;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeLine(pen, point0, point1, mediaPen, nativeSink);
            return;
        }

        DrawLineTypedFallback(pen, point0, point1, mediaPen);
    }

    private void DrawNativeLine(
        object? pen,
        object? point0,
        object? point1,
        MediaPen mediaPen,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!TryReadReplayPoint(point0, out var replayPoint0) || !TryReadReplayPoint(point1, out var replayPoint1))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(pen);
        nativeSink.DrawNativeLine(mediaPen, replayPoint0, replayPoint1);
        CountApplied();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawLineTypedFallback(object? pen, object? point0, object? point1, MediaPen mediaPen)
    {
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
        MediaBrush? mediaBrush = WpfResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfResourceResolver.AdaptPen(pen);
        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeRectangle(brush, pen, rectangle, mediaBrush, mediaPen, nativeSink);
            return;
        }

        DrawRectangleTypedFallback(brush, pen, rectangle, mediaBrush, mediaPen);
    }

    private void DrawNativeRectangle(
        object? brush,
        object? pen,
        object? rectangle,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!TryReadReplayRect(rectangle, out var replayRectangle))
        {
            CountUnsupported();
            return;
        }

        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen);
            nativeSink.DrawNativeRectangle(mediaBrush, mediaPen, replayRectangle);
            CountApplied();
            return;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(brush, pen);
            nativeSink.DrawNativeRectangle(null, mediaPen, replayRectangle);
            if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
            {
                CountPartiallyApplied();
            }
            else
            {
                CountApplied();
            }

            return;
        }

        CountUnsupportedIfPresent(brush, pen);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawRectangleTypedFallback(
        object? brush,
        object? pen,
        object? rectangle,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen)
    {
        if (!TryReadRect(rectangle, out var mediaRectangle))
        {
            CountUnsupported();
            return;
        }

        if (brush != null
            && WpfDrawingReplay.IsTileBrush(brush)
            && WpfDrawingReplay.TryReplayTileBrushFill(
                brush,
                WpfResourceResolver.CreateRectanglePath(mediaRectangle),
                _sink,
                _resources.AdaptImageSource,
                out var brushReplayStatus))
        {
            RegisterRetainedDependencies(brush, pen);
            if (mediaPen != null)
            {
                _sink.DrawRectangle(null, mediaPen, mediaRectangle);
            }

            CountDrawingReplayStatus(brushReplayStatus);
            return;
        }

        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen);
            _sink.DrawRectangle(mediaBrush, mediaPen, mediaRectangle);
            CountApplied();
            return;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(brush, pen);
            _sink.DrawRectangle(null, mediaPen, mediaRectangle);
            if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
            {
                CountPartiallyApplied();
            }
            else
            {
                CountApplied();
            }

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
        MediaBrush? mediaBrush = WpfResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfResourceResolver.AdaptPen(pen);
        if (mediaBrush == null && mediaPen == null)
        {
            CountUnsupportedIfPresent(brush, pen);
            return;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeRoundedRectangle(brush, pen, rectangle, radiusX, radiusY, mediaBrush, mediaPen, nativeSink);
            return;
        }

        DrawRoundedRectangleTypedFallback(brush, pen, rectangle, radiusX, radiusY, mediaBrush, mediaPen);
    }

    private void DrawNativeRoundedRectangle(
        object? brush,
        object? pen,
        object? rectangle,
        object? radiusX,
        object? radiusY,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!TryReadReplayRect(rectangle, out var replayRectangle)
            || !TryReadDouble(radiusX, out var mediaRadiusX)
            || !TryReadDouble(radiusY, out var mediaRadiusY))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        nativeSink.DrawNativeRoundedRectangle(mediaBrush, mediaPen, replayRectangle, mediaRadiusX, mediaRadiusY);
        CountApplied();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawRoundedRectangleTypedFallback(
        object? brush,
        object? pen,
        object? rectangle,
        object? radiusX,
        object? radiusY,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen)
    {
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
        MediaBrush? mediaBrush = WpfResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfResourceResolver.AdaptPen(pen);
        if (mediaBrush == null && mediaPen == null)
        {
            CountUnsupportedIfPresent(brush, pen);
            return;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeEllipse(brush, pen, center, radiusX, radiusY, mediaBrush, mediaPen, nativeSink);
            return;
        }

        DrawEllipseTypedFallback(brush, pen, center, radiusX, radiusY, mediaBrush, mediaPen);
    }

    private void DrawNativeEllipse(
        object? brush,
        object? pen,
        object? center,
        object? radiusX,
        object? radiusY,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!TryReadReplayPoint(center, out var replayCenter)
            || !TryReadDouble(radiusX, out var mediaRadiusX)
            || !TryReadDouble(radiusY, out var mediaRadiusY))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        nativeSink.DrawNativeEllipse(mediaBrush, mediaPen, replayCenter, mediaRadiusX, mediaRadiusY);
        CountApplied();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawEllipseTypedFallback(
        object? brush,
        object? pen,
        object? center,
        object? radiusX,
        object? radiusY,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen)
    {
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
        MediaBrush? mediaBrush = WpfResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfResourceResolver.AdaptPen(pen);
        MediaGeometry? mediaGeometry = WpfResourceResolver.AdaptGeometry(geometry);
        if (mediaGeometry == null)
        {
            CountUnsupportedIfPresent(brush, pen, geometry);
            return;
        }

        if (brush != null
            && WpfDrawingReplay.IsTileBrush(brush)
            && WpfDrawingReplay.TryReplayTileBrushFill(
                brush,
                mediaGeometry,
                _sink,
                _resources.AdaptImageSource,
                out var brushReplayStatus))
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            if (mediaPen != null)
            {
                _sink.DrawGeometry(null, mediaPen, mediaGeometry);
            }

            CountDrawingReplayStatus(brushReplayStatus);
            return;
        }

        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            _sink.DrawGeometry(mediaBrush, mediaPen, mediaGeometry);
            CountApplied();
            return;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            _sink.DrawGeometry(null, mediaPen, mediaGeometry);
            if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
            {
                CountPartiallyApplied();
            }
            else
            {
                CountApplied();
            }

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

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeImage(imageSource, rectangle, mediaImageSource, nativeSink);
            return;
        }

        DrawImageTypedFallback(imageSource, rectangle, mediaImageSource);
    }

    private void DrawNativeImage(
        object? imageSource,
        object? rectangle,
        MediaImageSource mediaImageSource,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!TryReadReplayRect(rectangle, out var replayRectangle))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(imageSource);
        nativeSink.DrawNativeImage(mediaImageSource, replayRectangle);
        CountApplied();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawImageTypedFallback(object? imageSource, object? rectangle, MediaImageSource mediaImageSource)
    {
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
        MediaBrush? mediaBrush = WpfResourceResolver.AdaptBrush(foregroundBrush);
        if (mediaBrush == null || glyphRun == null)
        {
            CountUnsupportedIfPresent(foregroundBrush, glyphRun);
            return;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeGlyphRun(foregroundBrush, glyphRun, mediaBrush, nativeSink);
            return;
        }

        DrawGlyphRunTypedFallback(foregroundBrush, glyphRun, mediaBrush);
    }

    private void DrawNativeGlyphRun(
        object? foregroundBrush,
        object glyphRun,
        MediaBrush mediaBrush,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out _))
        {
            CountUnsupportedIfPresent(foregroundBrush, glyphRun);
            return;
        }

        RegisterRetainedDependencies(foregroundBrush, glyphRun);
        nativeSink.DrawNativeGlyphRun(mediaBrush, glyphRun);
        CountApplied();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawGlyphRunTypedFallback(object? foregroundBrush, object glyphRun, MediaBrush mediaBrush)
    {
        MediaGlyphRun? mediaGlyphRun = WpfResourceResolver.AdaptGlyphRun(glyphRun);
        if (mediaGlyphRun == null)
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
        var status = WpfDrawingReplay.Replay(drawing, _sink, _resources.AdaptImageSource);
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
        else if (WpfResourceResolver.AdaptGeometry(clipGeometry) is { } mediaGeometry)
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
        else if (WpfResourceResolver.AdaptBrush(opacityMask) is { } mediaOpacityMask)
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
        else if (_sink is IWpfNativeTransformCommandSink nativeTransformSink
            && WpfResourceResolver.TryAdaptTransformMatrix(transform, out var nativeTransform))
        {
            RegisterRetainedDependencies(transform);
            nativeTransformSink.PushNativeTransform(nativeTransform);
        }
        else if (WpfResourceResolver.AdaptTransform(transform) is { } mediaTransform)
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
        if (WpfGuidelineSetReader.TryReadDynamicGuidelineYPair(guidelines, out var leadingCoordinate, out var drivenCoordinate))
        {
            _sink.PushGuidelineY2(leadingCoordinate, drivenCoordinate - leadingCoordinate);
        }
        else if (WpfGuidelineSetReader.TryReadDynamicGuidelineY1(guidelines, out var coordinate))
        {
            _sink.PushGuidelineY1(coordinate);
        }
        else if (WpfGuidelineSetReader.TryReadDynamicGuidelineSet(guidelines, out _, out _))
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

        if (WpfEffectMapper.TryCreateProGpuPushEffect(effect, effectInput, out var proGpuEffect, _imageSourceAdapter)
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

        if (pointValue is PortablePoint portablePoint)
        {
            point = new Point(portablePoint.X, portablePoint.Y);
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryReadReplayPoint(object? pointValue, out WpfReplayPoint point)
    {
        if (pointValue is WpfReplayPoint replayPoint)
        {
            point = replayPoint;
            return true;
        }

        if (pointValue is PortablePoint portablePoint)
        {
            point = new WpfReplayPoint(portablePoint.X, portablePoint.Y);
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

        if (rectValue is PortableRect portableRect && !portableRect.IsEmpty)
        {
            rectangle = new Rect(portableRect.X, portableRect.Y, portableRect.Width, portableRect.Height);
            return true;
        }

        rectangle = default;
        return false;
    }

    private static bool TryReadReplayRect(object? rectValue, out WpfReplayRect rectangle)
    {
        if (rectValue is WpfReplayRect replayRect)
        {
            rectangle = replayRect;
            return true;
        }

        if (rectValue is PortableRect portableRect && !portableRect.IsEmpty)
        {
            rectangle = new WpfReplayRect(portableRect.X, portableRect.Y, portableRect.Width, portableRect.Height);
            return true;
        }

        rectangle = default;
        return false;
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
