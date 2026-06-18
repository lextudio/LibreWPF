using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaFormattedText = System.Windows.Media.FormattedText;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaBitmapSource = System.Windows.Media.Imaging.BitmapSource;
using MediaFillRule = System.Windows.Media.FillRule;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPathGeometry = System.Windows.Media.PathGeometry;
using MediaPen = System.Windows.Media.Pen;
using MediaPenLineCap = System.Windows.Media.PenLineCap;
using MediaSweepDirection = System.Windows.Media.SweepDirection;
using MediaTransform = System.Windows.Media.Transform;
using VectorArcSegment = ProGPU.Vector.ArcSegment;
using VectorCubicBezierSegment = ProGPU.Vector.CubicBezierSegment;
using VectorDashPattern = ProGPU.Vector.DashPattern;
using VectorLineSegment = ProGPU.Vector.LineSegment;
using VectorPen = ProGPU.Vector.Pen;
using VectorPathFigure = ProGPU.Vector.PathFigure;
using VectorPathGeometry = ProGPU.Vector.PathGeometry;
using VectorPrimitivePathGeometry = ProGPU.Vector.PrimitivePathGeometry;
using VectorQuadraticBezierSegment = ProGPU.Vector.QuadraticBezierSegment;
using VectorBrush = ProGPU.Vector.Brush;
using VectorFillRule = ProGPU.Vector.FillRule;
using VectorPenLineCap = ProGPU.Vector.PenLineCap;
using VectorPenLineJoin = ProGPU.Vector.PenLineJoin;
using VectorSolidColorBrush = ProGPU.Vector.SolidColorBrush;
using VectorSweepDirection = ProGPU.Vector.SweepDirection;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class ProGpuCompositionCommandSink :
    IWpfCompositionCommandSink,
    IWpfViewport3DCommandSink,
    IWpfCompositionCommandSinkDiagnostics,
    IWpfNativeTransformCommandSink,
    IWpfNativePrimitiveCommandSink
{
    private const float TransformEpsilon = 0.0001f;

    private enum PushKind
    {
        DrawingContext,
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
    private bool _isClosed;

    public ProGpuCompositionCommandSink(MediaDrawingContext drawingContext)
        : this(drawingContext, context: null, viewport3DTextureCache: null)
    {
    }

    internal ProGpuCompositionCommandSink(
        MediaDrawingContext drawingContext,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache,
        Func<VectorPathGeometry, VectorPathGeometry?>? pathOperationResolver = null)
        : this(
            drawingContext?.NativeContext ?? throw new ArgumentNullException(nameof(drawingContext)),
            context,
            viewport3DTextureCache,
            pathOperationResolver,
            drawingContext)
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
        Func<VectorPathGeometry, VectorPathGeometry?>? pathOperationResolver = null)
        : this(nativeContext, context, viewport3DTextureCache, pathOperationResolver, drawingContext: null)
    {
    }

    private ProGpuCompositionCommandSink(
        global::ProGPU.Scene.DrawingContext nativeContext,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache,
        Func<VectorPathGeometry, VectorPathGeometry?>? pathOperationResolver,
        MediaDrawingContext? drawingContext)
    {
        NativeContext = nativeContext ?? throw new ArgumentNullException(nameof(nativeContext));
        _drawingContext = drawingContext;
        _context = context;
        _viewport3DTextureCache = viewport3DTextureCache;
        _pathOperationResolver = pathOperationResolver;
        _transformStack.Push(Matrix4x4.Identity);
        _bitmapScalingModeStack.Push(global::ProGPU.Scene.TextureSamplingMode.Linear);
        _edgeModeStack.Push(false);
        _textRenderingModeStack.Push(global::ProGPU.Scene.TextRenderingMode.Grayscale);
        _textHintingModeStack.Push(global::ProGPU.Scene.TextHintingMode.Auto);
    }

    public MediaDrawingContext? DrawingContext => _drawingContext;

    internal global::ProGPU.Scene.DrawingContext NativeContext { get; }

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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawExtension,
            ExtensionId = global::ProGPU.Scene.CompositorBuiltInExtensions.Mesh3D,
            UseGpuTransforms = true,
            CameraView = replayData.View,
            Transform = replayData.Projection,
            DataParam = replayData.Payload
        });

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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
        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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
        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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
        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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
            var bounds = geometry.Bounds;
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
        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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

        if (imageSource is MediaBitmapSource bitmapSource)
        {
            NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = bitmapSource.GpuTexture,
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

        if (imageSource is MediaBitmapSource bitmapSource)
        {
            NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = bitmapSource.GpuTexture,
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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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
        _transformStack.Push(transform.Value * _transformStack.Peek());
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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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

        if (imageSource is MediaBitmapSource bitmapSource)
        {
            NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = bitmapSource.GpuTexture,
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

        if (imageSource is MediaBitmapSource bitmapSource)
        {
            NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = bitmapSource.GpuTexture,
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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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

        NativeContext.Commands.Add(new global::ProGPU.Scene.RenderCommand
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

    private bool TryDrawDashedRectangle(VectorPen nativePen, ProGpuWpfPen pen, Rect rectangle)
    {
        if (rectangle.Width <= TransformEpsilon || rectangle.Height <= TransformEpsilon)
        {
            return false;
        }

        return TryDrawDashedPolyline(
            nativePen,
            pen,
            new[]
            {
                new Point(rectangle.X, rectangle.Y),
                new Point(rectangle.X + rectangle.Width, rectangle.Y),
                new Point(rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height),
                new Point(rectangle.X, rectangle.Y + rectangle.Height),
                new Point(rectangle.X, rectangle.Y)
            });
    }

    private bool TryDrawDashedRoundedRectangle(
        VectorPen nativePen,
        ProGpuWpfPen pen,
        Rect rectangle,
        double radiusX,
        double radiusY)
    {
        if (rectangle.Width <= TransformEpsilon || rectangle.Height <= TransformEpsilon)
        {
            return false;
        }

        radiusX = Math.Min(Math.Abs(radiusX), rectangle.Width / 2);
        radiusY = Math.Min(Math.Abs(radiusY), rectangle.Height / 2);
        if (radiusX <= TransformEpsilon || radiusY <= TransformEpsilon)
        {
            return TryDrawDashedRectangle(nativePen, pen, rectangle);
        }

        return TryDrawDashedPath(
            nativePen,
            pen,
            VectorPrimitivePathGeometry.CreateRoundedRectangle(
                (float)rectangle.X,
                (float)rectangle.Y,
                (float)rectangle.Width,
                (float)rectangle.Height,
                (float)radiusX,
                (float)radiusY));
    }

    private bool TryDrawDashedEllipse(
        VectorPen nativePen,
        ProGpuWpfPen pen,
        Point center,
        double radiusX,
        double radiusY)
    {
        if (radiusX <= TransformEpsilon || radiusY <= TransformEpsilon)
        {
            return false;
        }

        return TryDrawDashedPath(
            nativePen,
            pen,
            VectorPrimitivePathGeometry.CreateEllipse(
                new Vector2((float)center.X, (float)center.Y),
                (float)radiusX,
                (float)radiusY));
    }

    private bool TryDrawDashedPath(VectorPen nativePen, ProGpuWpfPen pen, VectorPathGeometry path)
    {
        return TryDrawDashedPath(nativePen, pen, path, depth: 0);
    }

    private bool TryDrawDashedPath(VectorPen nativePen, ProGpuWpfPen pen, VectorPathGeometry path, int depth)
    {
        if (path.IsCombined)
        {
            if (depth > 32)
            {
                return false;
            }

            if (TryResolveCombinedPathForDashing(path, out var resolvedPath)
                && !resolvedPath.IsCombined)
            {
                if (resolvedPath.Figures.Count == 0)
                {
                    return true;
                }

                return TryDrawDashedPath(nativePen, pen, resolvedPath, depth + 1);
            }

            UnsupportedStateCount++;
            var combinedEmitted = false;
            if (path.PathA != null)
            {
                combinedEmitted |= TryDrawDashedPath(nativePen, pen, path.PathA, depth + 1);
            }

            if (path.PathB != null)
            {
                combinedEmitted |= TryDrawDashedPath(nativePen, pen, path.PathB, depth + 1);
            }

            return combinedEmitted;
        }

        var handled = false;
        foreach (var figure in path.Figures)
        {
            handled |= TryDrawDashedPathFigure(nativePen, pen, figure);
        }

        return handled;
    }

    private bool TryResolveCombinedPathForDashing(
        VectorPathGeometry path,
        out VectorPathGeometry resolvedPath)
    {
        if (!path.IsCombined)
        {
            resolvedPath = path;
            return true;
        }

        if (_pathOperationResolver != null)
        {
            var resolved = _pathOperationResolver(path);
            if (resolved == null)
            {
                resolvedPath = new VectorPathGeometry();
                return false;
            }

            resolvedPath = resolved;
            return true;
        }

        if (_context == null)
        {
            resolvedPath = new VectorPathGeometry();
            return false;
        }

        try
        {
            return TryResolveCombinedPathWithProGpuSolver(path, out resolvedPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            resolvedPath = new VectorPathGeometry();
            return false;
        }
    }

    private static bool TryResolveCombinedPathWithProGpuSolver(
        VectorPathGeometry path,
        out VectorPathGeometry resolvedPath,
        int depth = 0)
    {
        if (depth > 32)
        {
            resolvedPath = new VectorPathGeometry();
            return false;
        }

        if (!path.IsCombined)
        {
            resolvedPath = path;
            return true;
        }

        if (path.PathA == null || path.PathB == null)
        {
            resolvedPath = new VectorPathGeometry();
            return false;
        }

        if (!TryResolveCombinedPathWithProGpuSolver(path.PathA, out var pathA, depth + 1)
            || !TryResolveCombinedPathWithProGpuSolver(path.PathB, out var pathB, depth + 1))
        {
            resolvedPath = new VectorPathGeometry();
            return false;
        }

        resolvedPath = global::ProGPU.Vector.PathOpGeometrySolver.Combine(pathA, pathB, path.Op);
        return true;
    }

    private bool TryDrawDashedPathFigure(VectorPen nativePen, ProGpuWpfPen pen, VectorPathFigure figure)
    {
        if (!TryInitializeDashPattern(pen, out var pattern, out var patternIndex, out var distanceInPattern))
        {
            return false;
        }

        var handled = false;
        var current = figure.StartPoint;

        foreach (var segment in figure.Segments)
        {
            switch (segment)
            {
                case VectorLineSegment line:
                    handled |= TryDrawDashedLineSegment(
                        nativePen,
                        pen,
                        current,
                        line.Point,
                        pattern,
                        ref patternIndex,
                        ref distanceInPattern);
                    current = line.Point;
                    break;

                case VectorQuadraticBezierSegment quadratic:
                    handled |= TryDrawDashedQuadraticBezierSegment(
                        nativePen,
                        pen,
                        current,
                        quadratic,
                        pattern,
                        ref patternIndex,
                        ref distanceInPattern);
                    current = quadratic.Point;
                    break;

                case VectorCubicBezierSegment cubic:
                    handled |= TryDrawDashedCubicBezierSegment(
                        nativePen,
                        pen,
                        current,
                        cubic,
                        pattern,
                        ref patternIndex,
                        ref distanceInPattern);
                    current = cubic.Point;
                    break;

                case VectorArcSegment arc:
                    handled |= TryDrawDashedArcSegment(
                        nativePen,
                        pen,
                        current,
                        arc,
                        pattern,
                        ref patternIndex,
                        ref distanceInPattern);
                    current = arc.Point;
                    break;
            }
        }

        if (figure.IsClosed && Vector2.DistanceSquared(current, figure.StartPoint) > TransformEpsilon * TransformEpsilon)
        {
            handled |= TryDrawDashedLineSegment(
                nativePen,
                pen,
                current,
                figure.StartPoint,
                pattern,
                ref patternIndex,
                ref distanceInPattern);
        }

        return handled;
    }

    private static Point ToPoint(Vector2 point)
    {
        return new Point(point.X, point.Y);
    }

    private bool TryDrawDashedPolyline(VectorPen nativePen, ProGpuWpfPen pen, IReadOnlyList<Point> points)
    {
        if (!TryInitializeDashPattern(pen, out var pattern, out var patternIndex, out var distanceInPattern))
        {
            return false;
        }

        if (points.Count < 2)
        {
            return false;
        }

        return TryDrawDashedPolylineSegments(
            nativePen,
            pen,
            points,
            pattern,
            ref patternIndex,
            ref distanceInPattern);
    }

    private bool TryDrawDashedPolylineSegments(
        VectorPen nativePen,
        ProGpuWpfPen pen,
        IReadOnlyList<Point> points,
        VectorDashPattern pattern,
        ref int patternIndex,
        ref float distanceInPattern)
    {
        if (points.Count < 2)
        {
            return true;
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            TryDrawDashedLineSegment(
                nativePen,
                pen,
                new Vector2((float)points[i].X, (float)points[i].Y),
                new Vector2((float)points[i + 1].X, (float)points[i + 1].Y),
                pattern,
                ref patternIndex,
                ref distanceInPattern);
        }

        return true;
    }

    private bool TryDrawDashedLineSegment(
        VectorPen nativePen,
        ProGpuWpfPen pen,
        Vector2 start,
        Vector2 end,
        VectorDashPattern pattern,
        ref int patternIndex,
        ref float distanceInPattern)
    {
        if (!pattern.TryCreateLineSegments(
                start,
                end,
                patternIndex,
                distanceInPattern,
                out var dashSegments,
                out var finalPatternIndex,
                out var finalDistanceInPattern))
        {
            return Vector2.DistanceSquared(start, end) <= TransformEpsilon * TransformEpsilon;
        }

        foreach (var dashSegment in dashSegments)
        {
            AddNativeLine(
                nativePen,
                new Point(dashSegment.Start.X, dashSegment.Start.Y),
                new Point(dashSegment.End.X, dashSegment.End.Y),
                pen.DashCap,
                pen.DashCap);
        }

        patternIndex = finalPatternIndex;
        distanceInPattern = finalDistanceInPattern;
        return true;
    }

    private bool TryDrawDashedQuadraticBezierSegment(
        VectorPen nativePen,
        ProGpuWpfPen pen,
        Vector2 start,
        VectorQuadraticBezierSegment segment,
        VectorDashPattern pattern,
        ref int patternIndex,
        ref float distanceInPattern)
    {
        if (!global::ProGPU.Vector.BezierSegmentGeometry.TryCreateDashedQuadraticBezierSegments(
                start,
                segment,
                pattern.Intervals,
                patternIndex,
                distanceInPattern,
                out var dashSegments,
                out var finalPatternIndex,
                out var finalDistanceInPattern))
        {
            return TryDrawDashedLineSegment(
                nativePen,
                pen,
                start,
                segment.Point,
                pattern,
                ref patternIndex,
                ref distanceInPattern);
        }

        foreach (var dashSegment in dashSegments)
        {
            AddNativeQuadraticBezierDash(nativePen, pen.DashCap, dashSegment.Start, dashSegment.Segment);
        }

        patternIndex = finalPatternIndex;
        distanceInPattern = finalDistanceInPattern;
        return true;
    }

    private bool TryDrawDashedCubicBezierSegment(
        VectorPen nativePen,
        ProGpuWpfPen pen,
        Vector2 start,
        VectorCubicBezierSegment segment,
        VectorDashPattern pattern,
        ref int patternIndex,
        ref float distanceInPattern)
    {
        if (!global::ProGPU.Vector.BezierSegmentGeometry.TryCreateDashedCubicBezierSegments(
                start,
                segment,
                pattern.Intervals,
                patternIndex,
                distanceInPattern,
                out var dashSegments,
                out var finalPatternIndex,
                out var finalDistanceInPattern))
        {
            return TryDrawDashedLineSegment(
                nativePen,
                pen,
                start,
                segment.Point,
                pattern,
                ref patternIndex,
                ref distanceInPattern);
        }

        foreach (var dashSegment in dashSegments)
        {
            AddNativeCubicBezierDash(nativePen, pen.DashCap, dashSegment.Start, dashSegment.Segment);
        }

        patternIndex = finalPatternIndex;
        distanceInPattern = finalDistanceInPattern;
        return true;
    }

    private bool TryDrawDashedArcSegment(
        VectorPen nativePen,
        ProGpuWpfPen pen,
        Vector2 start,
        VectorArcSegment arc,
        VectorDashPattern pattern,
        ref int patternIndex,
        ref float distanceInPattern)
    {
        if (!global::ProGPU.Vector.ArcSegmentGeometry.TryCreateDashedArcSegments(
                start,
                arc,
                pattern.Intervals,
                patternIndex,
                distanceInPattern,
                out var dashSegments,
                out var finalPatternIndex,
                out var finalDistanceInPattern))
        {
            return TryDrawDashedLineSegment(
                nativePen,
                pen,
                start,
                arc.Point,
                pattern,
                ref patternIndex,
                ref distanceInPattern);
        }

        foreach (var dashSegment in dashSegments)
        {
            AddNativeArcDash(nativePen, pen.DashCap, dashSegment.Start, dashSegment.Arc);
        }

        patternIndex = finalPatternIndex;
        distanceInPattern = finalDistanceInPattern;
        return true;
    }

    private static bool TryInitializeDashPattern(
        ProGpuWpfPen pen,
        out VectorDashPattern pattern,
        out int patternIndex,
        out float distanceInPattern)
    {
        patternIndex = 0;
        distanceInPattern = 0;
        if (!VectorDashPattern.TryCreate(pen.DashArray, pen.DashOffset, pen.Thickness, out pattern))
        {
            return false;
        }

        patternIndex = pattern.InitialIndex;
        distanceInPattern = pattern.InitialDistance;
        return true;
    }

    private void AddNativeArcDash(
        VectorPen nativePen,
        MediaPenLineCap dashCap,
        Vector2 start,
        VectorArcSegment arc)
    {
        var path = new VectorPathGeometry();
        var figure = new VectorPathFigure(start)
        {
            IsFilled = false
        };
        figure.Segments.Add(arc);
        path.Figures.Add(figure);

        AddNativePath(null, WithLineCaps(nativePen, dashCap, dashCap), path);
    }

    private void AddNativeQuadraticBezierDash(
        VectorPen nativePen,
        MediaPenLineCap dashCap,
        Vector2 start,
        VectorQuadraticBezierSegment segment)
    {
        var path = new VectorPathGeometry();
        var figure = new VectorPathFigure(start)
        {
            IsFilled = false
        };
        figure.Segments.Add(segment);
        path.Figures.Add(figure);

        AddNativePath(null, WithLineCaps(nativePen, dashCap, dashCap), path);
    }

    private void AddNativeCubicBezierDash(
        VectorPen nativePen,
        MediaPenLineCap dashCap,
        Vector2 start,
        VectorCubicBezierSegment segment)
    {
        var path = new VectorPathGeometry();
        var figure = new VectorPathFigure(start)
        {
            IsFilled = false
        };
        figure.Segments.Add(segment);
        path.Figures.Add(figure);

        AddNativePath(null, WithLineCaps(nativePen, dashCap, dashCap), path);
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
        return brush == null
            ? null
            : new VectorPen(
                brush,
                (float)pen.Thickness,
                ToNativeLineJoin(pen.LineJoin),
                (float)Math.Max(1.0, pen.MiterLimit),
                ToNativeLineCap(pen.StartLineCap),
                ToNativeLineCap(pen.EndLineCap),
                ToNativeLineCap(pen.DashCap));
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
        if (geometry is ProGpuCombinedGeometry combinedGeometry)
        {
            return TryConvertCombinedGeometryToNativePath(combinedGeometry, transform, out path);
        }

        if (geometry is MediaPathGeometry pathGeometry)
        {
            path = ConvertPathGeometry(pathGeometry, transform);
            return allowEmpty || path.Figures.Count > 0;
        }

        if (TryRecordGeometryPath(geometry, out path))
        {
            if (!transform.IsIdentity)
            {
                path = path.CreateTransformed(transform);
            }

            return true;
        }

        return false;
    }

    private static bool TryConvertCombinedGeometryToNativePath(
        ProGpuCombinedGeometry geometry,
        Matrix4x4 transform,
        out VectorPathGeometry path)
    {
        if (!TryConvertGeometryToNativePath(geometry.Geometry1, Matrix4x4.Identity, out var pathA, allowEmpty: true)
            || !TryConvertGeometryToNativePath(geometry.Geometry2, Matrix4x4.Identity, out var pathB, allowEmpty: true))
        {
            path = new VectorPathGeometry();
            return false;
        }

        path = new VectorPathGeometry
        {
            IsCombined = true,
            PathA = pathA,
            PathB = pathB,
            Op = geometry.PathOperation
        };

        var geometryTransform = geometry.Transform?.Value ?? Matrix4x4.Identity;
        var combinedTransform = geometryTransform * transform;
        if (!combinedTransform.IsIdentity)
        {
            path = path.CreateTransformed(combinedTransform);
        }

        return true;
    }

    private static VectorPathGeometry ConvertPathGeometry(MediaPathGeometry geometry, Matrix4x4 transform)
    {
        var path = new VectorPathGeometry
        {
            FillRule = geometry.FillRule == MediaFillRule.Nonzero
                ? VectorFillRule.Nonzero
                : VectorFillRule.EvenOdd
        };
        var geometryTransform = geometry.Transform?.Value ?? Matrix4x4.Identity;
        var combinedTransform = geometryTransform * transform;

        foreach (var figure in geometry.Figures)
        {
            var sourceCurrentPoint = ToVector2(figure.StartPoint);
            var nativeFigure = new VectorPathFigure
            {
                StartPoint = Vector2.Transform(ToVector2(figure.StartPoint), combinedTransform),
                IsClosed = figure.IsClosed,
                IsFilled = figure.IsFilled
            };

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        nativeFigure.Segments.Add(new VectorLineSegment(
                            Vector2.Transform(ToVector2(line.Point), combinedTransform),
                            line.IsSmoothJoin,
                            line.IsStroked));
                        sourceCurrentPoint = ToVector2(line.Point);
                        break;

                    case QuadraticBezierSegment quadratic:
                        nativeFigure.Segments.Add(new VectorQuadraticBezierSegment(
                            Vector2.Transform(ToVector2(quadratic.Point1), combinedTransform),
                            Vector2.Transform(ToVector2(quadratic.Point2), combinedTransform),
                            quadratic.IsSmoothJoin,
                            quadratic.IsStroked));
                        sourceCurrentPoint = ToVector2(quadratic.Point2);
                        break;

                    case BezierSegment cubic:
                        nativeFigure.Segments.Add(new VectorCubicBezierSegment(
                            Vector2.Transform(ToVector2(cubic.Point1), combinedTransform),
                            Vector2.Transform(ToVector2(cubic.Point2), combinedTransform),
                            Vector2.Transform(ToVector2(cubic.Point3), combinedTransform),
                            cubic.IsSmoothJoin,
                            cubic.IsStroked));
                        sourceCurrentPoint = ToVector2(cubic.Point3);
                        break;

                    case ArcSegment arc:
                        if (TryTransformArcSegment(
                            sourceCurrentPoint,
                            ToVector2(arc.Point),
                            ToVector2(arc.Size),
                            (float)arc.RotationAngle,
                            arc.IsLargeArc,
                            arc.SweepDirection == MediaSweepDirection.Clockwise
                                ? VectorSweepDirection.Clockwise
                                : VectorSweepDirection.Counterclockwise,
                            combinedTransform,
                            out var transformedArc))
                        {
                            transformedArc.IsSmoothJoin = arc.IsSmoothJoin;
                            transformedArc.IsStroked = arc.IsStroked;
                            nativeFigure.Segments.Add(transformedArc);
                        }
                        else
                        {
                            nativeFigure.Segments.Add(new VectorLineSegment(
                                Vector2.Transform(ToVector2(arc.Point), combinedTransform),
                                arc.IsSmoothJoin,
                                arc.IsStroked));
                        }

                        sourceCurrentPoint = ToVector2(arc.Point);
                        break;
                }
            }

            path.Figures.Add(nativeFigure);
        }

        return path;
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

    private static bool TryRecordGeometryPath(MediaGeometry geometry, out VectorPathGeometry path)
    {
        var recordingContext = new global::ProGPU.Scene.DrawingContext();
        geometry.Draw(recordingContext, new VectorSolidColorBrush(new Vector4(1f, 1f, 1f, 1f)), null);

        foreach (var command in recordingContext.Commands)
        {
            if (command.Type == global::ProGPU.Scene.RenderCommandType.DrawPath && command.Path != null)
            {
                path = command.Path;
                return true;
            }
        }

        path = new VectorPathGeometry();
        return false;
    }
}
