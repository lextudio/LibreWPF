using System;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaFormattedText = System.Windows.Media.FormattedText;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class WpfCompositionDrawingContext : IWpfGeneratedRenderDataDrawingContext, IDisposable
{
    private readonly IWpfCompositionCommandSink _sink;
    private int _stackDepth;
    private int _operationCount;
    private int _appliedCount;
    private int _unsupportedCount;
    private bool _isClosed;

    public WpfCompositionDrawingContext(IWpfCompositionCommandSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public MediaDrawingContext DrawingContext => _sink.DrawingContext;

    public int StackDepth => _stackDepth;

    public WpfCompositionDrawingContextResult Result => new(
        _operationCount,
        _appliedCount,
        _unsupportedCount);

    public void DrawLine(MediaPen? pen, Point point0, Point point1)
    {
        ThrowIfClosed();
        if (pen == null)
        {
            return;
        }

        _sink.DrawLine(pen, point0, point1);
        CountApplied();
    }

    public void DrawLine(
        MediaPen? pen,
        Point point0,
        object? point0Animations,
        Point point1,
        object? point1Animations)
    {
        ThrowIfClosed();
        if (pen == null)
        {
            return;
        }

        _sink.DrawLine(pen, point0, point1);
        CountApplied();
        CountUnsupportedStateIfAny(point0Animations, point1Animations);
    }

    public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
    {
        ThrowIfClosed();
        if (brush == null && pen == null)
        {
            return;
        }

        _sink.DrawRectangle(brush, pen, rectangle);
        CountApplied();
    }

    public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, object? rectangleAnimations)
    {
        ThrowIfClosed();
        if (brush == null && pen == null)
        {
            return;
        }

        _sink.DrawRectangle(brush, pen, rectangle);
        CountApplied();
        CountUnsupportedStateIfAny(rectangleAnimations);
    }

    public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (brush == null && pen == null)
        {
            return;
        }

        _sink.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        CountApplied();
    }

    public void DrawRoundedRectangle(
        MediaBrush? brush,
        MediaPen? pen,
        Rect rectangle,
        object? rectangleAnimations,
        double radiusX,
        object? radiusXAnimations,
        double radiusY,
        object? radiusYAnimations)
    {
        ThrowIfClosed();
        if (brush == null && pen == null)
        {
            return;
        }

        _sink.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        CountApplied();
        CountUnsupportedStateIfAny(rectangleAnimations, radiusXAnimations, radiusYAnimations);
    }

    public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (brush == null && pen == null)
        {
            return;
        }

        _sink.DrawEllipse(brush, pen, center, radiusX, radiusY);
        CountApplied();
    }

    public void DrawEllipse(
        MediaBrush? brush,
        MediaPen? pen,
        Point center,
        object? centerAnimations,
        double radiusX,
        object? radiusXAnimations,
        double radiusY,
        object? radiusYAnimations)
    {
        ThrowIfClosed();
        if (brush == null && pen == null)
        {
            return;
        }

        _sink.DrawEllipse(brush, pen, center, radiusX, radiusY);
        CountApplied();
        CountUnsupportedStateIfAny(centerAnimations, radiusXAnimations, radiusYAnimations);
    }

    public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry? geometry)
    {
        ThrowIfClosed();
        if ((brush == null && pen == null) || geometry == null)
        {
            return;
        }

        _sink.DrawGeometry(brush, pen, geometry);
        CountApplied();
    }

    public void DrawImage(MediaImageSource? imageSource, Rect rectangle)
    {
        ThrowIfClosed();
        if (imageSource == null)
        {
            return;
        }

        _sink.DrawImage(imageSource, rectangle);
        CountApplied();
    }

    public void DrawImage(MediaImageSource? imageSource, Rect rectangle, object? rectangleAnimations)
    {
        ThrowIfClosed();
        if (imageSource == null)
        {
            return;
        }

        _sink.DrawImage(imageSource, rectangle);
        CountApplied();
        CountUnsupportedStateIfAny(rectangleAnimations);
    }

    public void DrawText(MediaFormattedText formattedText, Point origin)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(formattedText);
        _sink.DrawText(formattedText, origin);
        CountApplied();
    }

    public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun? glyphRun)
    {
        ThrowIfClosed();
        if (foregroundBrush == null || glyphRun == null)
        {
            return;
        }

        _sink.DrawGlyphRun(foregroundBrush, glyphRun);
        CountApplied();
    }

    public WpfDrawingReplayStatus DrawDrawing(object? drawing, IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        Func<object?, MediaImageSource?>? adapter = imageSourceAdapter == null
            ? null
            : imageSourceAdapter.AdaptImageSource;

        return DrawDrawing(drawing, adapter);
    }

    public WpfDrawingReplayStatus DrawDrawing(
        object? drawing,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        ThrowIfClosed();

        var status = WpfReflectionDrawingReplay.Replay(drawing, _sink, imageSourceAdapter);
        CountDrawingReplayStatus(status);
        return status;
    }

    void IWpfGeneratedRenderDataDrawingContext.DrawDrawing(object? drawing)
    {
        DrawDrawing(drawing, (IWpfImageSourceAdapter?)null);
    }

    public void DrawVideo(object? player, Rect rectangle)
    {
        ThrowIfClosed();
        if (player == null)
        {
            return;
        }

        CountUnsupported();
    }

    public void DrawVideo(object? player, Rect rectangle, object? rectangleAnimations)
    {
        ThrowIfClosed();
        if (player == null)
        {
            return;
        }

        CountUnsupported();
    }

    public void PushClip(MediaGeometry? clipGeometry)
    {
        ThrowIfClosed();
        if (clipGeometry == null)
        {
            _sink.PushNoOpScope();
        }
        else
        {
            _sink.PushClip(clipGeometry);
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushOpacity(double opacity)
    {
        ThrowIfClosed();
        _sink.PushOpacity(opacity);
        _stackDepth++;
        CountApplied();
    }

    public void PushOpacity(double opacity, object? opacityAnimations)
    {
        ThrowIfClosed();
        _sink.PushOpacity(opacity);
        _stackDepth++;
        CountApplied();
        CountUnsupportedStateIfAny(opacityAnimations);
    }

    public void PushOpacityMask(MediaBrush? opacityMask)
    {
        PushOpacityMask(opacityMask, Rect.Empty);
    }

    public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
    {
        ThrowIfClosed();
        if (opacityMask == null)
        {
            _sink.PushNoOpScope();
        }
        else
        {
            _sink.PushOpacityMask(opacityMask, bounds);
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushTransform(MediaTransform? transform)
    {
        ThrowIfClosed();
        if (transform == null)
        {
            _sink.PushNoOpScope();
        }
        else
        {
            _sink.PushTransform(transform);
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushGuidelineSet()
    {
        PushGuidelineSet(guidelines: null);
    }

    public void PushGuidelineSet(object? guidelines)
    {
        ThrowIfClosed();

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

    public void PushGuidelineY1(double coordinate)
    {
        ThrowIfClosed();
        _sink.PushGuidelineY1(coordinate);
        _stackDepth++;
        CountApplied();
    }

    public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
    {
        ThrowIfClosed();
        _sink.PushGuidelineY2(leadingCoordinate, offsetToDrivenCoordinate);
        _stackDepth++;
        CountApplied();
    }

    public void PushEffect(object? effect, object? effectInput)
    {
        ThrowIfClosed();
        _stackDepth++;
        CountUnsupported();
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

    private void CountDrawingReplayStatus(WpfDrawingReplayStatus status)
    {
        switch (status)
        {
            case WpfDrawingReplayStatus.Applied:
                CountApplied();
                break;
            case WpfDrawingReplayStatus.PartiallyApplied:
                _operationCount++;
                _appliedCount++;
                _unsupportedCount++;
                break;
            case WpfDrawingReplayStatus.Unsupported:
                CountUnsupported();
                break;
        }
    }

    private void CountUnsupportedStateIfAny(params object?[] unsupportedState)
    {
        foreach (var state in unsupportedState)
        {
            if (state != null)
            {
                _unsupportedCount++;
            }
        }
    }

}

public readonly record struct WpfCompositionDrawingContextResult(
    int OperationCount,
    int AppliedCount,
    int UnsupportedCount);
