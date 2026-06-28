using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaFormattedText = System.Windows.Media.FormattedText;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaBitmapSource = System.Windows.Media.Imaging.BitmapSource;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPathGeometry = System.Windows.Media.PathGeometry;
using MediaPen = System.Windows.Media.Pen;
using MediaPenLineCap = System.Windows.Media.PenLineCap;
using MediaTransform = System.Windows.Media.Transform;
using VectorArcSegment = ProGPU.Vector.ArcSegment;
using VectorCubicBezierSegment = ProGPU.Vector.CubicBezierSegment;
using VectorLineSegment = ProGPU.Vector.LineSegment;
using VectorPen = ProGPU.Vector.Pen;
using VectorPathFigure = ProGPU.Vector.PathFigure;
using VectorPathGeometry = ProGPU.Vector.PathGeometry;
using VectorQuadraticBezierSegment = ProGPU.Vector.QuadraticBezierSegment;
using VectorBrush = ProGPU.Vector.Brush;
using VectorFillRule = ProGPU.Vector.FillRule;
using VectorPenLineCap = ProGPU.Vector.PenLineCap;
using VectorPenLineJoin = ProGPU.Vector.PenLineJoin;
using VectorSolidColorBrush = ProGPU.Vector.SolidColorBrush;
using VectorSweepDirection = ProGPU.Vector.SweepDirection;
using NativePathGeometrySource = ProGPU.Scene.INativePathGeometrySource;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class ProGpuCompositionCommandSink :
    IWpfCompositionCommandSink,
    IWpfViewport3DCommandSink,
    IWpfCompositionCommandSinkDiagnostics,
    IWpfNativeTransformCommandSink,
    IWpfNativePrimitiveCommandSink,
    IWpfNativeClipCommandSink
{
    private const float TransformEpsilon = 0.0001f;

    private enum PushKind
    {
        DrawingContext,
        Clip,
        GeometryClip,
        Guideline,
        NoOp,
        Opacity,
        OpacityMask,
        Transform,
        BitmapScalingMode,
        EdgeMode,
        TextRenderingMode,
        TextHintingMode
    }

    private readonly Stack<PushKind> _pushStack = new();
    private readonly Stack<GuidelineState> _guidelineStack = new();
    private readonly Stack<Matrix4x4> _transformStack = new();
    private readonly Stack<global::ProGPU.Scene.TextureSamplingMode> _bitmapScalingModeStack = new();
    private readonly Stack<bool> _edgeModeStack = new();
    private readonly Stack<global::ProGPU.Scene.TextRenderingMode> _textRenderingModeStack = new();
    private readonly Stack<global::ProGPU.Scene.TextHintingMode> _textHintingModeStack = new();
    private readonly global::ProGPU.Backend.WgpuContext? _context;
    private readonly WpfViewport3DTextureCache? _viewport3DTextureCache;
    private readonly Func<VectorPathGeometry, VectorPathGeometry?>? _pathOperationResolver;
    private readonly MediaDrawingContext? _drawingContext;
    private readonly int _hitTestId;
    private bool _isClosed;

    public ProGpuCompositionCommandSink(MediaDrawingContext drawingContext)
        : this(drawingContext, context: null, viewport3DTextureCache: null)
    {
    }

    internal ProGpuCompositionCommandSink(
        MediaDrawingContext drawingContext,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache,
        Func<VectorPathGeometry, VectorPathGeometry?>? pathOperationResolver = null,
        int hitTestId = 0)
        : this(
            drawingContext?.NativeContext ?? throw new ArgumentNullException(nameof(drawingContext)),
            context,
            viewport3DTextureCache,
            pathOperationResolver,
            drawingContext,
            hitTestId)
    {
    }

    public ProGpuCompositionCommandSink(global::ProGPU.Scene.DrawingContext nativeContext)
        : this(nativeContext, context: null, viewport3DTextureCache: null)
    {
    }

    internal ProGpuCompositionCommandSink(
        global::ProGPU.Scene.DrawingContext nativeContext,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache,
        Func<VectorPathGeometry, VectorPathGeometry?>? pathOperationResolver = null,
        int hitTestId = 0)
        : this(nativeContext, context, viewport3DTextureCache, pathOperationResolver, drawingContext: null, hitTestId)
    {
    }

    private ProGpuCompositionCommandSink(
        global::ProGPU.Scene.DrawingContext nativeContext,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache,
        Func<VectorPathGeometry, VectorPathGeometry?>? pathOperationResolver,
        MediaDrawingContext? drawingContext,
        int hitTestId)
    {
        NativeContext = nativeContext ?? throw new ArgumentNullException(nameof(nativeContext));
        _drawingContext = drawingContext;
        _context = context;
        _viewport3DTextureCache = viewport3DTextureCache;
        _pathOperationResolver = pathOperationResolver;
        _hitTestId = hitTestId;
        _transformStack.Push(Matrix4x4.Identity);
        _bitmapScalingModeStack.Push(global::ProGPU.Scene.TextureSamplingMode.Linear);
        _edgeModeStack.Push(false);
        _textRenderingModeStack.Push(global::ProGPU.Scene.TextRenderingMode.Grayscale);
        _textHintingModeStack.Push(global::ProGPU.Scene.TextHintingMode.Auto);
    }

    public MediaDrawingContext? DrawingContext => _drawingContext;

    internal global::ProGPU.Scene.DrawingContext NativeContext { get; }

    private void AddNativeCommand(global::ProGPU.Scene.RenderCommand command)
    {
        command.HitTestId = _hitTestId;
        NativeContext.Commands.Add(command);
    }

    public int UnsupportedStateCount { get; private set; }

    public bool DrawViewport3D(object viewportVisual)
    {
        ThrowIfClosed();

        if (_context == null || _viewport3DTextureCache == null)
        {
            return false;
        }

        if (!WpfViewport3DReflectionBridge.TryCreateReplayData(
                viewportVisual,
                _viewport3DTextureCache,
                out var replayData)
            || replayData.Payload.ColorTexture == null
            || replayData.Payload.MsaaColorTexture == null
            || replayData.Payload.DepthTexture == null)
        {
            return false;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawExtension,
            ExtensionId = global::ProGPU.Scene.CompositorBuiltInExtensions.Mesh3D,
            UseGpuTransforms = true,
            CameraView = replayData.View,
            Transform = replayData.Projection,
            DataParam = replayData.Payload
        });

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
            Texture = replayData.Payload.ColorTexture,
            Rect = replayData.Viewport,
            Transform = _transformStack.Peek(),
            TextureSamplingMode = _bitmapScalingModeStack.Peek()
        });

        return true;
    }

    public void DrawLine(MediaPen? pen, Point point0, Point point1)
    {
        ThrowIfClosed();

        point0 = SnapGuideline(point0);
        point1 = SnapGuideline(point1);
        var bounds = new Rect(point0, point1);
        if (pen == null || ToNativePen(pen, bounds) is not { } nativePen)
        {
            return;
        }

        AddNativeLine(nativePen, point0, point1, pen.StartLineCap, pen.EndLineCap);
    }

    private void AddNativeLine(
        VectorPen pen,
        Point point0,
        Point point1,
        MediaPenLineCap startLineCap = MediaPenLineCap.Flat,
        MediaPenLineCap endLineCap = MediaPenLineCap.Flat)
    {
        var originalPoint0 = point0;
        var originalPoint1 = point1;
        ApplySquareLineCaps(pen, ref point0, ref point1, startLineCap, endLineCap);

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawLine,
            Pen = pen,
            Position = new Vector2((float)point0.X, (float)point0.Y),
            Position2 = new Vector2((float)point1.X, (float)point1.Y),
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });

        AddRoundLineCap(pen, originalPoint0, startLineCap);
        AddRoundLineCap(pen, originalPoint1, endLineCap);
        AddTriangleLineCap(pen, originalPoint0, originalPoint1, startLineCap, isStart: true);
        AddTriangleLineCap(pen, originalPoint0, originalPoint1, endLineCap, isStart: false);
    }

    private static void ApplySquareLineCaps(
        VectorPen pen,
        ref Point point0,
        ref Point point1,
        MediaPenLineCap startLineCap,
        MediaPenLineCap endLineCap)
    {
        if (startLineCap != MediaPenLineCap.Square && endLineCap != MediaPenLineCap.Square)
        {
            return;
        }

        var start = new Vector2((float)point0.X, (float)point0.Y);
        var end = new Vector2((float)point1.X, (float)point1.Y);
        var delta = end - start;
        var length = delta.Length();
        if (length <= TransformEpsilon)
        {
            return;
        }

        var extension = delta / length * (pen.Thickness / 2);
        if (startLineCap == MediaPenLineCap.Square)
        {
            start -= extension;
            point0 = new Point(start.X, start.Y);
        }

        if (endLineCap == MediaPenLineCap.Square)
        {
            end += extension;
            point1 = new Point(end.X, end.Y);
        }
    }

    private void AddRoundLineCap(VectorPen pen, Point point, MediaPenLineCap lineCap)
    {
        if (lineCap != MediaPenLineCap.Round || pen.Thickness <= TransformEpsilon)
        {
            return;
        }

        var radius = pen.Thickness / 2;
        AddNativeEllipse(pen.Brush, null, point, radius, radius);
    }

    private void AddTriangleLineCap(
        VectorPen pen,
        Point point0,
        Point point1,
        MediaPenLineCap lineCap,
        bool isStart)
    {
        if (lineCap != MediaPenLineCap.Triangle || pen.Thickness <= TransformEpsilon)
        {
            return;
        }

        var start = new Vector2((float)point0.X, (float)point0.Y);
        var end = new Vector2((float)point1.X, (float)point1.Y);
        var delta = end - start;
        var length = delta.Length();
        if (length <= TransformEpsilon)
        {
            return;
        }

        var direction = delta / length;
        var radius = pen.Thickness / 2;
        var perpendicular = new Vector2(-direction.Y, direction.X) * radius;
        var center = isStart ? start : end;
        var outward = isStart ? -direction : direction;

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.FillTriangle,
            Brush = pen.Brush,
            Position = center - perpendicular,
            Position2 = center + outward * radius,
            Position3 = center + perpendicular,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
    {
        ThrowIfClosed();
        rectangle = SnapGuidelines(rectangle);
        var nativeBrush = ToNativeBrush(brush, rectangle);
        var nativePen = ToNativePen(pen, rectangle);

        AddNativeRect(nativeBrush, nativePen, rectangle);
    }

    private void AddNativeRect(VectorBrush? brush, VectorPen? pen, Rect rectangle)
    {
        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawRect,
            Brush = brush,
            Pen = pen,
            Rect = ToNativeRect(rectangle),
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        rectangle = SnapGuidelines(rectangle);
        var nativeBrush = ToNativeBrush(brush, rectangle);
        var nativePen = ToNativePen(pen, rectangle);

        AddNativeRoundedRect(nativeBrush, nativePen, rectangle, radiusX, radiusY);
    }

    private void AddNativeRoundedRect(VectorBrush? brush, VectorPen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawRoundedRect,
            Brush = brush,
            Pen = pen,
            Rect = ToNativeRect(rectangle),
            RadiusX = (float)radiusX,
            RadiusY = (float)radiusY,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        var bounds = new Rect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2);
        bounds = SnapGuidelines(bounds);
        center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        radiusX = bounds.Width / 2;
        radiusY = bounds.Height / 2;
        var nativeBrush = ToNativeBrush(brush, bounds);
        var nativePen = ToNativePen(pen, bounds);

        AddNativeEllipse(nativeBrush, nativePen, center, radiusX, radiusY);
    }

    private void AddNativeEllipse(VectorBrush? brush, VectorPen? pen, Point center, double radiusX, double radiusY)
    {
        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawEllipse,
            Brush = brush,
            Pen = pen,
            Position2 = new Vector2((float)center.X, (float)center.Y),
            RadiusX = (float)radiusX,
            RadiusY = (float)radiusY,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        ThrowIfClosed();

        if ((brush != null || pen != null)
            && TryConvertGeometryToNativePath(geometry, Matrix4x4.Identity, out var path))
        {
            var bounds = TryReadGeometryBounds(geometry, out var geometryBounds)
                ? geometryBounds
                : WpfReplayRect.Empty;
            var nativeBrush = ToNativeBrush(brush, bounds);
            var nativePen = ToNativePen(pen, bounds);

            AddNativePath(nativeBrush, nativePen, path);
            return;
        }

        if (_drawingContext != null)
        {
            _drawingContext.DrawGeometry(brush, pen, geometry);
        }
        else
        {
            UnsupportedStateCount++;
        }
    }

    private void AddNativePath(VectorBrush? brush, VectorPen? pen, VectorPathGeometry path)
    {
        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawPath,
            Brush = brush,
            Pen = pen,
            Path = path,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    public void DrawImage(MediaImageSource imageSource, Rect rectangle)
    {
        ThrowIfClosed();

        if (imageSource is MediaBitmapSource bitmapSource
            && WpfBitmapSourceImageAdapter.TryGetGpuTexture(bitmapSource, out var texture))
        {
            AddNativeCommand(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = texture,
                Rect = ToNativeRect(rectangle),
                Transform = _transformStack.Peek(),
                TextureSamplingMode = _bitmapScalingModeStack.Peek()
            });
            return;
        }

        if (_drawingContext != null)
        {
            _drawingContext.DrawImage(imageSource, rectangle);
        }
        else
        {
            UnsupportedStateCount++;
        }
    }

    public void DrawImage(MediaImageSource imageSource, Rect rectangle, Rect sourceRectangle)
    {
        ThrowIfClosed();

        if (imageSource is MediaBitmapSource bitmapSource
            && WpfBitmapSourceImageAdapter.TryGetGpuTexture(bitmapSource, out var texture))
        {
            AddNativeCommand(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = texture,
                Rect = ToNativeRect(rectangle),
                SrcRect = ToNativeRect(sourceRectangle),
                Transform = _transformStack.Peek(),
                TextureSamplingMode = _bitmapScalingModeStack.Peek()
            });
        }
    }

    public void DrawText(MediaFormattedText formattedText, Point origin)
    {
        ThrowIfClosed();

        if (formattedText == null || formattedText.Font == null)
        {
            return;
        }

        var nativeBrush = formattedText.Foreground?.ToNative() ?? new VectorSolidColorBrush(Vector4.One);
        var position = new Vector2(
            (float)origin.X,
            (float)(origin.Y + formattedText.Height * 0.8));

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawText,
            Text = formattedText.Text,
            Font = formattedText.Font,
            FontSize = (float)formattedText.FontSize,
            Brush = nativeBrush,
            Position = position,
            Transform = _transformStack.Peek(),
            TextRenderingMode = _textRenderingModeStack.Peek(),
            TextHintingMode = _textHintingModeStack.Peek()
        });
    }

    public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
    {
        ThrowIfClosed();

        if (foregroundBrush == null || glyphRun == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawGlyphRun,
            GlyphIndices = glyphRun.GlyphIndices,
            GlyphPositions = glyphRun.GlyphPositions,
            Font = glyphRun.Font,
            FontSize = glyphRun.FontSize,
            Brush = foregroundBrush.ToNative(),
            Position = glyphRun.Position,
            Transform = glyphRun.Transform * _transformStack.Peek(),
            IsBold = glyphRun.IsBold,
            IsItalic = glyphRun.IsItalic,
            TextRenderingMode = _textRenderingModeStack.Peek(),
            TextHintingMode = _textHintingModeStack.Peek()
        });
    }

    public void PushClip(MediaGeometry clipGeometry)
    {
        ThrowIfClosed();

        if (TryConvertGeometryToNativePath(clipGeometry, _transformStack.Peek(), out var path))
        {
            NativeContext.PushGeometryClip(path);
            _pushStack.Push(PushKind.GeometryClip);
            return;
        }

        if (_drawingContext != null)
        {
            _drawingContext.PushClip(clipGeometry);
            _pushStack.Push(PushKind.DrawingContext);
        }
        else
        {
            UnsupportedStateCount++;
            _pushStack.Push(PushKind.NoOp);
        }
    }

    void IWpfNativeClipCommandSink.PushNativeClip(WpfReplayRect bounds)
    {
        ThrowIfClosed();
        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.PushClip,
            Rect = ToNativeRect(bounds),
            Transform = _transformStack.Peek()
        });
        _pushStack.Push(PushKind.Clip);
    }

    public void PushOpacity(double opacity)
    {
        ThrowIfClosed();
        NativeContext.PushOpacity((float)opacity);
        _pushStack.Push(PushKind.Opacity);
    }

    public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
    {
        ThrowIfClosed();

        if (opacityMask == null)
        {
            PushNoOpScope();
            return;
        }

        var nativeBounds = new global::ProGPU.Scene.Rect(
            (float)bounds.X,
            (float)bounds.Y,
            (float)bounds.Width,
            (float)bounds.Height);

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.PushOpacityMask,
            Brush = AdaptNativeBrush(opacityMask, bounds, count => UnsupportedStateCount += count),
            Rect = nativeBounds,
            Transform = _transformStack.Peek()
        });
        _pushStack.Push(PushKind.OpacityMask);
    }

    public void PushTransform(MediaTransform transform)
    {
        ThrowIfClosed();
        var nativeTransform = WpfReflectionResourceResolver.TryAdaptTransformMatrix(transform, out var adaptedTransform)
            ? adaptedTransform
            : Matrix4x4.Identity;
        _transformStack.Push(nativeTransform * _transformStack.Peek());
        _drawingContext?.PushTransform(transform);
        _pushStack.Push(PushKind.Transform);
    }

    public void PushNativeTransform(Matrix4x4 transform)
    {
        ThrowIfClosed();
        _transformStack.Push(transform * _transformStack.Peek());
        _pushStack.Push(PushKind.Transform);
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeLine(MediaPen? pen, WpfReplayPoint point0, WpfReplayPoint point1)
    {
        ThrowIfClosed();

        var nativePen = ToNativePen(pen, CreateLineBounds(point0, point1));
        if (nativePen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawLine,
            Pen = nativePen,
            Position = new Vector2((float)point0.X, (float)point0.Y),
            Position2 = new Vector2((float)point1.X, (float)point1.Y),
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle)
    {
        ThrowIfClosed();

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawRect,
            Brush = ToNativeBrush(brush, rectangle),
            Pen = ToNativePen(pen, rectangle),
            Rect = ToNativeRect(rectangle),
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeRoundedRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle, double radiusX, double radiusY)
    {
        ThrowIfClosed();

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawRoundedRect,
            Brush = ToNativeBrush(brush, rectangle),
            Pen = ToNativePen(pen, rectangle),
            Rect = ToNativeRect(rectangle),
            RadiusX = (float)radiusX,
            RadiusY = (float)radiusY,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeEllipse(MediaBrush? brush, MediaPen? pen, WpfReplayPoint center, double radiusX, double radiusY)
    {
        ThrowIfClosed();

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawEllipse,
            Brush = ToNativeBrush(brush, new WpfReplayRect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2)),
            Pen = ToNativePen(pen, new WpfReplayRect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2)),
            Position2 = new Vector2((float)center.X, (float)center.Y),
            RadiusX = (float)radiusX,
            RadiusY = (float)radiusY,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle)
    {
        ThrowIfClosed();

        if (imageSource is MediaBitmapSource bitmapSource
            && WpfBitmapSourceImageAdapter.TryGetGpuTexture(bitmapSource, out var texture))
        {
            AddNativeCommand(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = texture,
                Rect = ToNativeRect(rectangle),
                Transform = _transformStack.Peek(),
                TextureSamplingMode = _bitmapScalingModeStack.Peek()
            });
            return;
        }

        UnsupportedStateCount++;
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle, WpfReplayRect sourceRectangle)
    {
        ThrowIfClosed();

        if (imageSource is MediaBitmapSource bitmapSource
            && WpfBitmapSourceImageAdapter.TryGetGpuTexture(bitmapSource, out var texture))
        {
            AddNativeCommand(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = texture,
                Rect = ToNativeRect(rectangle),
                SrcRect = ToNativeRect(sourceRectangle),
                Transform = _transformStack.Peek(),
                TextureSamplingMode = _bitmapScalingModeStack.Peek()
            });
            return;
        }

        UnsupportedStateCount++;
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeGlyphRun(MediaBrush? foregroundBrush, object glyphRunResource)
    {
        ThrowIfClosed();

        if (foregroundBrush == null
            || !WpfReflectionResourceResolver.TryAdaptNativeGlyphRun(glyphRunResource, out var glyphRun))
        {
            return;
        }

        var nativeBrush = ToNativeBrush(foregroundBrush, CreateGlyphRunBounds(glyphRun));
        if (nativeBrush == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawGlyphRun,
            GlyphIndices = glyphRun.GlyphIndices,
            GlyphPositions = glyphRun.GlyphPositions,
            Font = glyphRun.Font,
            FontSize = glyphRun.FontSize,
            Brush = nativeBrush,
            Position = glyphRun.Position,
            Transform = glyphRun.Transform * _transformStack.Peek(),
            IsBold = glyphRun.IsBold,
            IsItalic = glyphRun.IsItalic,
            TextRenderingMode = _textRenderingModeStack.Peek(),
            TextHintingMode = _textHintingModeStack.Peek()
        });
    }

    void IWpfNativePrimitiveCommandSink.PushNativeOpacityMask(MediaBrush? opacityMask, WpfReplayRect bounds)
    {
        ThrowIfClosed();

        if (opacityMask == null)
        {
            PushNoOpScope();
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.PushOpacityMask,
            Brush = ToNativeBrush(opacityMask, bounds),
            Rect = ToNativeRect(bounds),
            Transform = _transformStack.Peek()
        });
        _pushStack.Push(PushKind.OpacityMask);
    }

    public void PushNoOpScope()
    {
        ThrowIfClosed();
        _pushStack.Push(PushKind.NoOp);
    }

    public void PushGuidelineSet()
    {
        PushNoOpScope();
    }

    public void PushGuidelineSet(object? guidelines)
    {
        ThrowIfClosed();

        if (WpfGuidelineSetReflection.TryReadDynamicGuidelineSet(guidelines, out var guidelinesX, out var guidelinesY))
        {
            _guidelineStack.Push(GuidelineState.FromGuidelineSet(guidelinesX, guidelinesY));
            _pushStack.Push(PushKind.Guideline);
            return;
        }

        _pushStack.Push(PushKind.NoOp);
    }

    public void PushGuidelineY1(double coordinate)
    {
        ThrowIfClosed();
        _guidelineStack.Push(GuidelineState.FromGuidelineY1(coordinate));
        _pushStack.Push(PushKind.Guideline);
    }

    public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
    {
        ThrowIfClosed();
        _guidelineStack.Push(GuidelineState.FromGuidelineY2(leadingCoordinate, offsetToDrivenCoordinate));
        _pushStack.Push(PushKind.Guideline);
    }

    public void PushBitmapScalingMode(object? bitmapScalingMode)
    {
        ThrowIfClosed();

        if (WpfBitmapScalingModeReflection.TryMapToTextureSamplingMode(bitmapScalingMode, out var samplingMode))
        {
            _bitmapScalingModeStack.Push(samplingMode);
            _pushStack.Push(PushKind.BitmapScalingMode);
            return;
        }

        if (bitmapScalingMode != null)
        {
            UnsupportedStateCount++;
        }

        PushNoOpScope();
    }

    public void PushEdgeMode(object? edgeMode)
    {
        ThrowIfClosed();

        if (WpfEdgeModeReflection.TryMapToAliased(edgeMode, out var isAliased))
        {
            _edgeModeStack.Push(isAliased);
            _pushStack.Push(PushKind.EdgeMode);
            return;
        }

        if (edgeMode != null)
        {
            UnsupportedStateCount++;
        }

        PushNoOpScope();
    }

    public void PushTextRenderingMode(object? textRenderingMode)
    {
        ThrowIfClosed();

        if (WpfTextRenderingModeReflection.TryMapToTextRenderingMode(textRenderingMode, out var mode))
        {
            _textRenderingModeStack.Push(mode);
            _pushStack.Push(PushKind.TextRenderingMode);
            return;
        }

        if (textRenderingMode != null)
        {
            UnsupportedStateCount++;
        }

        PushNoOpScope();
    }

    public void PushTextHintingMode(object? textHintingMode)
    {
        ThrowIfClosed();

        if (WpfTextRenderingModeReflection.TryMapToTextHintingMode(textHintingMode, out var mode))
        {
            _textHintingModeStack.Push(mode);
            _pushStack.Push(PushKind.TextHintingMode);
            return;
        }

        if (textHintingMode != null)
        {
            UnsupportedStateCount++;
        }

        PushNoOpScope();
    }

    public void Pop()
    {
        ThrowIfClosed();

        if (_pushStack.Count == 0)
        {
            if (_drawingContext != null)
            {
                PopDrawingContext(_drawingContext);
            }

            return;
        }

        var pushKind = _pushStack.Pop();
        if (pushKind == PushKind.Clip)
        {
            NativeContext.PopClip();
            return;
        }

        if (pushKind == PushKind.GeometryClip)
        {
            NativeContext.PopGeometryClip();
            return;
        }

        if (pushKind == PushKind.OpacityMask)
        {
            NativeContext.PopOpacityMask();
            return;
        }

        if (pushKind == PushKind.Opacity)
        {
            NativeContext.PopOpacity();
            return;
        }

        if (pushKind == PushKind.NoOp)
        {
            return;
        }

        if (pushKind == PushKind.Guideline)
        {
            if (_guidelineStack.Count > 0)
            {
                _guidelineStack.Pop();
            }

            return;
        }

        if (pushKind == PushKind.BitmapScalingMode)
        {
            if (_bitmapScalingModeStack.Count > 1)
            {
                _bitmapScalingModeStack.Pop();
            }

            return;
        }

        if (pushKind == PushKind.EdgeMode)
        {
            if (_edgeModeStack.Count > 1)
            {
                _edgeModeStack.Pop();
            }

            return;
        }

        if (pushKind == PushKind.TextRenderingMode)
        {
            if (_textRenderingModeStack.Count > 1)
            {
                _textRenderingModeStack.Pop();
            }

            return;
        }

        if (pushKind == PushKind.TextHintingMode)
        {
            if (_textHintingModeStack.Count > 1)
            {
                _textHintingModeStack.Pop();
            }

            return;
        }

        if (pushKind == PushKind.Transform && _transformStack.Count > 1)
        {
            _transformStack.Pop();
        }

        if (_drawingContext != null)
        {
            PopDrawingContext(_drawingContext);
        }
    }

    public void Close()
    {
        if (_isClosed)
        {
            return;
        }

        if (_drawingContext != null)
        {
            CloseDrawingContext(_drawingContext);
        }

        _isClosed = true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PopDrawingContext(MediaDrawingContext drawingContext)
    {
        drawingContext.Pop();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CloseDrawingContext(MediaDrawingContext drawingContext)
    {
        drawingContext.Close();
    }

    public void Dispose()
    {
        Close();
    }

    private void ThrowIfClosed()
    {
        if (_isClosed)
        {
            throw new ObjectDisposedException(nameof(ProGpuCompositionCommandSink));
        }
    }

    private static global::ProGPU.Scene.Rect ToNativeRect(Rect rectangle)
    {
        return new global::ProGPU.Scene.Rect(
            (float)rectangle.X,
            (float)rectangle.Y,
            (float)rectangle.Width,
            (float)rectangle.Height);
    }

    private static global::ProGPU.Scene.Rect ToNativeRect(WpfReplayRect rectangle)
    {
        return new global::ProGPU.Scene.Rect(
            (float)rectangle.X,
            (float)rectangle.Y,
            (float)rectangle.Width,
            (float)rectangle.Height);
    }

    private VectorBrush? ToNativeBrush(MediaBrush? brush, WpfReplayRect bounds)
    {
        var nativeBrush = WpfReflectionResourceResolver.AdaptNativeBrush(brush, bounds, out var unsupportedStateCount);
        UnsupportedStateCount += unsupportedStateCount;
        return nativeBrush;
    }

    private VectorPen? ToNativePen(MediaPen? pen, WpfReplayRect bounds)
    {
        var nativePen = WpfReflectionResourceResolver.AdaptNativePen(pen, bounds, out var unsupportedStateCount);
        UnsupportedStateCount += unsupportedStateCount;
        return nativePen;
    }

    private static WpfReplayRect CreateLineBounds(WpfReplayPoint point0, WpfReplayPoint point1)
    {
        var x1 = Math.Min(point0.X, point1.X);
        var y1 = Math.Min(point0.Y, point1.Y);
        var x2 = Math.Max(point0.X, point1.X);
        var y2 = Math.Max(point0.Y, point1.Y);
        return new WpfReplayRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static WpfReplayRect CreateGlyphRunBounds(WpfNativeGlyphRun glyphRun)
    {
        if (glyphRun.GlyphPositions.Length == 0)
        {
            return new WpfReplayRect(glyphRun.Position.X, glyphRun.Position.Y - glyphRun.FontSize, glyphRun.FontSize, glyphRun.FontSize);
        }

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var position in glyphRun.GlyphPositions)
        {
            var x = glyphRun.Position.X + position.X;
            var y = glyphRun.Position.Y + position.Y;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y - glyphRun.FontSize);
            maxX = Math.Max(maxX, x + glyphRun.FontSize);
            maxY = Math.Max(maxY, y);
        }

        return new WpfReplayRect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    private Point SnapGuideline(Point point)
    {
        var x = TrySnapGuidelineX(point.X, out var snappedX) ? snappedX : point.X;
        var y = TrySnapGuidelineY(point.Y, out var snappedY) ? snappedY : point.Y;
        return x == point.X && y == point.Y ? point : new Point(x, y);
    }

    private Rect SnapGuidelines(Rect rectangle)
    {
        var left = rectangle.X;
        var right = rectangle.X + rectangle.Width;
        var top = rectangle.Y;
        var bottom = rectangle.Y + rectangle.Height;
        var snappedLeft = TrySnapGuidelineX(left, out var newLeft) ? newLeft : left;
        var snappedRight = TrySnapGuidelineX(right, out var newRight) ? newRight : right;
        var snappedTop = TrySnapGuidelineY(top, out var newTop) ? newTop : top;
        var snappedBottom = TrySnapGuidelineY(bottom, out var newBottom) ? newBottom : bottom;

        if (snappedLeft == left && snappedRight == right && snappedTop == top && snappedBottom == bottom)
        {
            return rectangle;
        }

        return new Rect(
            snappedLeft,
            snappedTop,
            Math.Max(0, snappedRight - snappedLeft),
            Math.Max(0, snappedBottom - snappedTop));
    }

    private bool TrySnapGuidelineX(double x, out double snappedX)
    {
        snappedX = x;
        if (_guidelineStack.Count == 0
            || !TryGetAxisAlignedMapping(
                _transformStack.Peek(),
                out var scaleX,
                out var translateX,
                out _,
                out _))
        {
            return false;
        }

        foreach (var guideline in _guidelineStack)
        {
            if (guideline.TrySnapX(x, scaleX, translateX, out snappedX))
            {
                return true;
            }
        }

        return false;
    }

    private bool TrySnapGuidelineY(double y, out double snappedY)
    {
        snappedY = y;
        if (_guidelineStack.Count == 0
            || !TryGetAxisAlignedMapping(
                _transformStack.Peek(),
                out _,
                out _,
                out var scaleY,
                out var translateY))
        {
            return false;
        }

        foreach (var guideline in _guidelineStack)
        {
            if (guideline.TrySnapY(y, scaleY, translateY, out snappedY))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetAxisAlignedMapping(
        Matrix4x4 transform,
        out double scaleX,
        out double translateX,
        out double scaleY,
        out double translateY)
    {
        scaleX = transform.M11;
        translateX = transform.M41;
        scaleY = transform.M22;
        translateY = transform.M42;

        return !AreClose(scaleX, 0)
            && !AreClose(scaleY, 0)
            && double.IsFinite(scaleX)
            && double.IsFinite(translateX)
            && double.IsFinite(scaleY)
            && double.IsFinite(translateY)
            && AreClose(transform.M12, 0)
            && AreClose(transform.M21, 0)
            && AreClose(transform.M13, 0)
            && AreClose(transform.M14, 0)
            && AreClose(transform.M23, 0)
            && AreClose(transform.M24, 0)
            && AreClose(transform.M31, 0)
            && AreClose(transform.M32, 0)
            && AreClose(transform.M34, 0)
            && AreClose(transform.M43, 0)
            && AreClose(transform.M33, 1)
            && AreClose(transform.M44, 1);
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= TransformEpsilon;
    }

    private static bool AreClose(double left, double right, double epsilon)
    {
        return Math.Abs(left - right) <= epsilon;
    }

    internal static VectorBrush? AdaptNativeBrush(
        MediaBrush? brush,
        Rect bounds,
        Action<int>? reportUnsupportedState = null)
    {
        return brush switch
        {
            null => null,
            ProGpuNativeBrush nativeBrush => AdaptNativeBrush(nativeBrush, bounds, reportUnsupportedState),
            _ => brush.ToNative()
        };
    }

    private VectorBrush? ToNativeBrush(MediaBrush? brush, Rect bounds)
    {
        return AdaptNativeBrush(brush, bounds, count => UnsupportedStateCount += count);
    }

    private static VectorBrush AdaptNativeBrush(
        ProGpuNativeBrush brush,
        Rect bounds,
        Action<int>? reportUnsupportedState)
    {
        reportUnsupportedState?.Invoke(brush.CountUnsupportedStateForBounds(bounds));
        return brush.ToNative(bounds);
    }

    private readonly struct GuidelineState
    {
        private readonly double[] _guidelinesX;
        private readonly double[] _guidelinesY;
        private readonly bool _preserveDrivenYOffset;
        private readonly double _leadingY;
        private readonly double _offsetToDrivenY;

        private GuidelineState(
            double[] guidelinesX,
            double[] guidelinesY,
            bool preserveDrivenYOffset,
            double leadingY,
            double offsetToDrivenY)
        {
            _guidelinesX = guidelinesX;
            _guidelinesY = guidelinesY;
            _preserveDrivenYOffset = preserveDrivenYOffset;
            _leadingY = leadingY;
            _offsetToDrivenY = offsetToDrivenY;
        }

        public static GuidelineState FromGuidelineSet(double[] guidelinesX, double[] guidelinesY)
        {
            return new GuidelineState(guidelinesX, guidelinesY, preserveDrivenYOffset: false, leadingY: 0, offsetToDrivenY: 0);
        }

        public static GuidelineState FromGuidelineY1(double coordinate)
        {
            return new GuidelineState(Array.Empty<double>(), new[] { coordinate }, preserveDrivenYOffset: false, leadingY: 0, offsetToDrivenY: 0);
        }

        public static GuidelineState FromGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            return new GuidelineState(
                Array.Empty<double>(),
                new[] { leadingCoordinate, leadingCoordinate + offsetToDrivenCoordinate },
                preserveDrivenYOffset: true,
                leadingCoordinate,
                offsetToDrivenCoordinate);
        }

        public bool TrySnapX(double x, double scaleX, double translateX, out double snappedX)
        {
            return TrySnapCoordinate(_guidelinesX, x, scaleX, translateX, out snappedX);
        }

        public bool TrySnapY(double y, double scaleY, double translateY, out double snappedY)
        {
            if (_preserveDrivenYOffset)
            {
                if (AreClose(y, _leadingY))
                {
                    snappedY = SnapCoordinate(_leadingY, scaleY, translateY);
                    return true;
                }

                var drivenCoordinate = _leadingY + _offsetToDrivenY;
                if (AreClose(y, drivenCoordinate))
                {
                    var snappedLeading = SnapCoordinate(_leadingY, scaleY, translateY);
                    snappedY = drivenCoordinate + snappedLeading - _leadingY;
                    return true;
                }

                snappedY = y;
                return false;
            }

            return TrySnapCoordinate(_guidelinesY, y, scaleY, translateY, out snappedY);
        }

        private static bool TrySnapCoordinate(
            double[] guidelines,
            double coordinate,
            double scale,
            double translate,
            out double snappedCoordinate)
        {
            foreach (var guideline in guidelines)
            {
                if (AreClose(coordinate, guideline))
                {
                    snappedCoordinate = SnapCoordinate(guideline, scale, translate);
                    return true;
                }
            }

            snappedCoordinate = coordinate;
            return false;
        }

        private static double SnapCoordinate(double coordinate, double scale, double translate)
        {
            var deviceCoordinate = coordinate * scale + translate;
            var snappedDeviceCoordinate = Math.Round(deviceCoordinate, MidpointRounding.AwayFromZero);
            return (snappedDeviceCoordinate - translate) / scale;
        }
    }

    private VectorPen? ToNativePen(MediaPen? pen, Rect bounds)
    {
        if (pen?.Brush == null)
        {
            return null;
        }

        var brush = ToNativeBrush(pen.Brush, bounds);
        if (brush == null)
        {
            return null;
        }

        var nativeDashArray = TryReadDashStyle(pen, out var dashArray, out var dashOffset)
            ? dashArray
            : null;
        return new VectorPen(
            brush,
            (float)pen.Thickness,
            ToNativeLineJoin(pen.LineJoin),
            (float)Math.Max(1.0, pen.MiterLimit),
            ToNativeLineCap(pen.StartLineCap),
            ToNativeLineCap(pen.EndLineCap),
            ToNativeLineCap(pen.DashCap),
            nativeDashArray,
            dashOffset);
    }

    private static bool TryReadDashStyle(MediaPen pen, out double[] dashArray, out double dashOffset)
    {
        dashArray = Array.Empty<double>();
        dashOffset = 0.0;

        if (pen.DashStyle?.Dashes is not { Length: > 0 } dashes)
        {
            return false;
        }

        var dashScale = pen.Thickness;
        if (!double.IsFinite(dashScale) || dashScale < 0.0)
        {
            dashScale = 0.0;
        }

        var scaledDashes = new double[dashes.Length];
        for (var i = 0; i < dashes.Length; i++)
        {
            var dash = dashes[i];
            if (!double.IsFinite(dash) || dash < 0.0)
            {
                return false;
            }

            scaledDashes[i] = dash * dashScale;
        }

        dashArray = scaledDashes;
        dashOffset = double.IsFinite(pen.DashStyle.Offset) ? pen.DashStyle.Offset : 0.0;
        return true;
    }

    private static VectorPenLineJoin ToNativeLineJoin(PenLineJoin lineJoin)
    {
        return lineJoin switch
        {
            PenLineJoin.Bevel => VectorPenLineJoin.Bevel,
            PenLineJoin.Round => VectorPenLineJoin.Round,
            _ => VectorPenLineJoin.Miter
        };
    }

    private static VectorPenLineCap ToNativeLineCap(MediaPenLineCap lineCap)
    {
        return lineCap switch
        {
            MediaPenLineCap.Square => VectorPenLineCap.Square,
            MediaPenLineCap.Round => VectorPenLineCap.Round,
            MediaPenLineCap.Triangle => VectorPenLineCap.Triangle,
            _ => VectorPenLineCap.Flat
        };
    }

    private static VectorPen WithLineCaps(VectorPen pen, MediaPenLineCap startLineCap, MediaPenLineCap endLineCap)
    {
        return new VectorPen(
            pen.Brush,
            pen.Thickness,
            pen.LineJoin,
            pen.MiterLimit,
            ToNativeLineCap(startLineCap),
            ToNativeLineCap(endLineCap),
            pen.DashCap);
    }

    private static bool TryConvertGeometryToNativePath(
        MediaGeometry geometry,
        Matrix4x4 transform,
        out VectorPathGeometry path,
        bool allowEmpty = false)
    {
        if (TryConvertCombinedGeometryToNativePath(geometry, transform, out path))
        {
            return true;
        }

        if (geometry is MediaPathGeometry pathGeometry)
        {
            path = ConvertPathGeometry(pathGeometry, transform);
            return allowEmpty || path.Figures.Count > 0;
        }

        if (geometry is NativePathGeometrySource nativePathSource
            && nativePathSource.TryGetPathGeometry(out path, out var nativeTransform))
        {
            var combinedTransform = nativeTransform * transform;
            if (!combinedTransform.IsIdentity)
            {
                path = path.CreateTransformed(combinedTransform);
            }

            return true;
        }

        return false;
    }

    private static bool TryConvertCombinedGeometryToNativePath(
        MediaGeometry geometry,
        Matrix4x4 transform,
        out VectorPathGeometry path)
    {
        path = new VectorPathGeometry();
        if (!TypeNameEndsWith(geometry, "CombinedGeometry")
            || !TryGetPropertyValue(geometry, "Geometry1", out var geometry1Value)
            || !TryGetPropertyValue(geometry, "Geometry2", out var geometry2Value)
            || !TryGetPropertyValue(geometry, "GeometryCombineMode", out var combineModeValue)
            || !TryReadGeometryCombinePathOperation(combineModeValue, out var pathOperation))
        {
            return false;
        }

        var geometry1 = geometry1Value as MediaGeometry;
        var geometry2 = geometry2Value as MediaGeometry;
        var pathA = new VectorPathGeometry();
        var pathB = new VectorPathGeometry();
        if ((geometry1 != null && !TryConvertGeometryToNativePath(geometry1, Matrix4x4.Identity, out pathA, allowEmpty: true))
            || (geometry2 != null && !TryConvertGeometryToNativePath(geometry2, Matrix4x4.Identity, out pathB, allowEmpty: true)))
        {
            return false;
        }

        path = new VectorPathGeometry
        {
            IsCombined = true,
            PathA = pathA,
            PathB = pathB,
            Op = pathOperation
        };

        var geometryTransform = ReadGeometryTransform(geometry);
        var combinedTransform = geometryTransform * transform;
        if (!combinedTransform.IsIdentity)
        {
            path = path.CreateTransformed(combinedTransform);
        }

        return true;
    }

    private static bool TryReadGeometryBounds(MediaGeometry geometry, out WpfReplayRect bounds)
    {
        if (TryGetPropertyValue(geometry, "Bounds", out var boundsValue)
            && boundsValue != null
            && TryReadReplayRect(boundsValue, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static Matrix4x4 ReadGeometryTransform(MediaGeometry geometry)
    {
        if (TryGetPropertyValue(geometry, "Transform", out var transformValue)
            && transformValue != null
            && TryReadTransformValue(transformValue, out var transform))
        {
            return transform;
        }

        return Matrix4x4.Identity;
    }

    private static bool TryReadTransformValue(object transformValue, out Matrix4x4 transform)
    {
        if (TryGetPropertyValue(transformValue, "Value", out var matrixValue)
            && matrixValue != null
            && TryReadMatrix4x4(matrixValue, out transform))
        {
            return true;
        }

        transform = Matrix4x4.Identity;
        return false;
    }

    private static bool TryReadMatrix4x4(object matrixValue, out Matrix4x4 transform)
    {
        if (matrixValue is Matrix4x4 matrix)
        {
            transform = matrix;
            return true;
        }

        if (TryReadDoubleProperty(matrixValue, "M11", out var m11)
            && TryReadDoubleProperty(matrixValue, "M12", out var m12)
            && TryReadDoubleProperty(matrixValue, "M21", out var m21)
            && TryReadDoubleProperty(matrixValue, "M22", out var m22)
            && TryReadDoubleProperty(matrixValue, "OffsetX", out var offsetX)
            && TryReadDoubleProperty(matrixValue, "OffsetY", out var offsetY))
        {
            transform = new Matrix4x4(
                (float)m11,
                (float)m12,
                0,
                0,
                (float)m21,
                (float)m22,
                0,
                0,
                0,
                0,
                1,
                0,
                (float)offsetX,
                (float)offsetY,
                0,
                1);
            return true;
        }

        transform = Matrix4x4.Identity;
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

    private static bool TryReadDoubleProperty(object value, string propertyName, out double result)
    {
        if (TryGetPropertyValue(value, propertyName, out var propertyValue)
            && propertyValue is IConvertible convertible)
        {
            try
            {
                result = convertible.ToDouble(CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException)
            {
            }
            catch (InvalidCastException)
            {
            }
            catch (OverflowException)
            {
            }
        }

        result = 0;
        return false;
    }

    private static bool TryReadGeometryCombinePathOperation(object? value, out int pathOperation)
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

    private static bool TryConvertToInt32(object? value, out int result)
    {
        switch (value)
        {
            case int intValue:
                result = intValue;
                return true;
            case Enum enumValue:
                result = Convert.ToInt32(enumValue, CultureInfo.InvariantCulture);
                return true;
            case IConvertible convertible:
                try
                {
                    result = convertible.ToInt32(CultureInfo.InvariantCulture);
                    return true;
                }
                catch (FormatException)
                {
                }
                catch (InvalidCastException)
                {
                }
                catch (OverflowException)
                {
                }

                break;
        }

        result = 0;
        return false;
    }

    private static bool TryGetPropertyValue(object instance, string propertyName, out object? value)
    {
        var property = instance.GetType().GetProperty(propertyName);
        if (property == null)
        {
            value = null;
            return false;
        }

        value = property.GetValue(instance);
        return true;
    }

    private static bool TypeNameEndsWith(object value, string typeName)
    {
        var type = value.GetType();
        return type.Name.EndsWith(typeName, StringComparison.Ordinal)
            || (type.FullName?.EndsWith("." + typeName, StringComparison.Ordinal) ?? false);
    }

    private static VectorPathGeometry ConvertPathGeometry(MediaPathGeometry geometry, Matrix4x4 transform)
    {
        var path = new VectorPathGeometry
        {
            FillRule = ReadPathFillRule(geometry)
        };
        var geometryTransform = ReadGeometryTransform(geometry);
        var combinedTransform = geometryTransform * transform;

        if (!TryGetPropertyValue(geometry, "Figures", out var figuresValue)
            || figuresValue == null)
        {
            return path;
        }

        foreach (var figure in EnumerateObjects(figuresValue))
        {
            if (!TryReadVector2Property(figure, "StartPoint", out var sourceCurrentPoint))
            {
                continue;
            }

            var nativeFigure = new VectorPathFigure
            {
                StartPoint = Vector2.Transform(sourceCurrentPoint, combinedTransform),
                IsClosed = ReadBooleanProperty(figure, "IsClosed", defaultValue: false),
                IsFilled = ReadBooleanProperty(figure, "IsFilled", defaultValue: true)
            };

            if (TryGetPropertyValue(figure, "Segments", out var segmentsValue)
                && segmentsValue != null)
            {
                foreach (var segment in EnumerateObjects(segmentsValue))
                {
                    TryAppendPathSegment(segment, ref sourceCurrentPoint, combinedTransform, nativeFigure);
                }
            }

            path.Figures.Add(nativeFigure);
        }

        return path;
    }

    private static VectorFillRule ReadPathFillRule(object geometry)
    {
        if (TryGetPropertyValue(geometry, "FillRule", out var fillRuleValue))
        {
            if (string.Equals(fillRuleValue?.ToString(), "Nonzero", StringComparison.Ordinal)
                || (TryConvertToInt32(fillRuleValue, out var intValue) && intValue == 1))
            {
                return VectorFillRule.Nonzero;
            }
        }

        return VectorFillRule.EvenOdd;
    }

    private static void TryAppendPathSegment(
        object segment,
        ref Vector2 sourceCurrentPoint,
        Matrix4x4 transform,
        VectorPathFigure nativeFigure)
    {
        var isSmoothJoin = ReadBooleanProperty(segment, "IsSmoothJoin", defaultValue: false);
        var isStroked = ReadBooleanProperty(segment, "IsStroked", defaultValue: true);

        if (TypeNameEndsWith(segment, "LineSegment")
            && TryReadVector2Property(segment, "Point", out var linePoint))
        {
            nativeFigure.Segments.Add(new VectorLineSegment(
                Vector2.Transform(linePoint, transform),
                isSmoothJoin,
                isStroked));
            sourceCurrentPoint = linePoint;
            return;
        }

        if (TypeNameEndsWith(segment, "QuadraticBezierSegment")
            && TryReadVector2Property(segment, "Point1", out var quadraticPoint1)
            && TryReadVector2Property(segment, "Point2", out var quadraticPoint2))
        {
            nativeFigure.Segments.Add(new VectorQuadraticBezierSegment(
                Vector2.Transform(quadraticPoint1, transform),
                Vector2.Transform(quadraticPoint2, transform),
                isSmoothJoin,
                isStroked));
            sourceCurrentPoint = quadraticPoint2;
            return;
        }

        if (TypeNameEndsWith(segment, "BezierSegment")
            && TryReadVector2Property(segment, "Point1", out var cubicPoint1)
            && TryReadVector2Property(segment, "Point2", out var cubicPoint2)
            && TryReadVector2Property(segment, "Point3", out var cubicPoint3))
        {
            nativeFigure.Segments.Add(new VectorCubicBezierSegment(
                Vector2.Transform(cubicPoint1, transform),
                Vector2.Transform(cubicPoint2, transform),
                Vector2.Transform(cubicPoint3, transform),
                isSmoothJoin,
                isStroked));
            sourceCurrentPoint = cubicPoint3;
            return;
        }

        if (TypeNameEndsWith(segment, "ArcSegment")
            && TryReadVector2Property(segment, "Point", out var arcPoint)
            && TryReadSizeVector2Property(segment, "Size", out var arcSize))
        {
            var rotationAngle = TryReadDoubleProperty(segment, "RotationAngle", out var angle)
                ? (float)angle
                : 0f;
            var isLargeArc = ReadBooleanProperty(segment, "IsLargeArc", defaultValue: false);
            var sweepDirection = ReadSweepDirection(segment);

            if (TryTransformArcSegment(
                sourceCurrentPoint,
                arcPoint,
                arcSize,
                rotationAngle,
                isLargeArc,
                sweepDirection,
                transform,
                out var transformedArc))
            {
                transformedArc.IsSmoothJoin = isSmoothJoin;
                transformedArc.IsStroked = isStroked;
                nativeFigure.Segments.Add(transformedArc);
            }
            else
            {
                nativeFigure.Segments.Add(new VectorLineSegment(
                    Vector2.Transform(arcPoint, transform),
                    isSmoothJoin,
                    isStroked));
            }

            sourceCurrentPoint = arcPoint;
        }
    }

    private static VectorSweepDirection ReadSweepDirection(object segment)
    {
        if (TryGetPropertyValue(segment, "SweepDirection", out var sweepDirectionValue))
        {
            if (string.Equals(sweepDirectionValue?.ToString(), "Clockwise", StringComparison.Ordinal)
                || (TryConvertToInt32(sweepDirectionValue, out var intValue) && intValue == 1))
            {
                return VectorSweepDirection.Clockwise;
            }
        }

        return VectorSweepDirection.Counterclockwise;
    }

    private static bool TryReadVector2Property(object instance, string propertyName, out Vector2 point)
    {
        if (TryGetPropertyValue(instance, propertyName, out var pointValue)
            && pointValue != null
            && TryReadPointVector2(pointValue, out point))
        {
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryReadPointVector2(object pointValue, out Vector2 point)
    {
        if (TryReadDoubleProperty(pointValue, "X", out var x)
            && TryReadDoubleProperty(pointValue, "Y", out var y))
        {
            point = new Vector2((float)x, (float)y);
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryReadSizeVector2Property(object instance, string propertyName, out Vector2 size)
    {
        if (TryGetPropertyValue(instance, propertyName, out var sizeValue)
            && sizeValue != null
            && TryReadDoubleProperty(sizeValue, "Width", out var width)
            && TryReadDoubleProperty(sizeValue, "Height", out var height))
        {
            size = new Vector2((float)width, (float)height);
            return true;
        }

        size = default;
        return false;
    }

    private static bool ReadBooleanProperty(object instance, string propertyName, bool defaultValue)
    {
        if (TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            if (propertyValue is bool boolValue)
            {
                return boolValue;
            }

            if (propertyValue is IConvertible convertible)
            {
                try
                {
                    return convertible.ToBoolean(CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                }
                catch (InvalidCastException)
                {
                }
            }
        }

        return defaultValue;
    }

    private static IEnumerable<object> EnumerateObjects(object collection)
    {
        if (collection is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }
    }

    private static Vector2 ToVector2(Point point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static Vector2 ToVector2(Size size)
    {
        return new Vector2((float)size.Width, (float)size.Height);
    }

    private static bool TryTransformArcSegment(
        Vector2 startPoint,
        Vector2 point,
        Vector2 size,
        float rotationAngle,
        bool isLargeArc,
        VectorSweepDirection sweepDirection,
        Matrix4x4 transform,
        out VectorArcSegment arc)
    {
        return global::ProGPU.Vector.ArcSegmentGeometry.TryTransformArcSegment(
            startPoint,
            new VectorArcSegment(point, size, rotationAngle, isLargeArc, sweepDirection),
            transform,
            out _,
            out arc);
    }

}
