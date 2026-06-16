using System;
using System.Collections.Generic;
using System.Numerics;
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
using ProGpuContainerVisual = global::ProGPU.Scene.ContainerVisual;
using ProGpuDrawingContext = global::ProGPU.Scene.DrawingContext;
using ProGpuEffectBase = global::ProGPU.Scene.EffectBase;

namespace System.Windows.Media.ProGPU.Composition;

internal sealed class ProGpuRetainedCompositionCommandSink :
    IWpfCompositionCommandSink,
    IWpfViewport3DCommandSink,
    IWpfVisualEffectCommandSink,
    IWpfVisualCacheCommandSink,
    IWpfDrawingCacheCommandSink,
    IWpfRetainedVisualBranchSink,
    IWpfRetainedVisualStateSink
{
    private enum ScopeKind
    {
        Delegate,
        VisualEffect,
        VisualCache,
        DrawingCache
    }

    private enum VisualScopeKind
    {
        Root,
        SourceOwner,
        Effect,
        Cache
    }

    private readonly Stack<ScopeKind> _scopeStack = new();
    private readonly Stack<VisualScope> _visualScopes = new();
    private readonly ProGpuWpfDrawingFrame _drawingFrame;
    private bool _isClosed;

    public ProGpuRetainedCompositionCommandSink(
        ProGpuWpfDrawingFrame drawingFrame,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache)
    {
        ArgumentNullException.ThrowIfNull(drawingFrame);
        _drawingFrame = drawingFrame;

        var rootVisual = new ProGpuRetainedDrawingVisual
        {
            Size = new Vector2(drawingFrame.PixelWidth, drawingFrame.PixelHeight)
        };

        if (!drawingFrame.AddRetainedWpfVisual(rootVisual))
        {
            throw new InvalidOperationException("The drawing frame does not expose a retained WPF visual root.");
        }

        _visualScopes.Push(new VisualScope(rootVisual, context, viewport3DTextureCache, VisualScopeKind.Root, 0));
    }

    internal ProGpuRetainedCompositionCommandSink(
        ProGpuWpfDrawingFrame drawingFrame,
        ProGpuRetainedDrawingVisual rootVisual,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache)
    {
        ArgumentNullException.ThrowIfNull(drawingFrame);
        ArgumentNullException.ThrowIfNull(rootVisual);

        _drawingFrame = drawingFrame;
        _visualScopes.Push(new VisualScope(rootVisual, context, viewport3DTextureCache, VisualScopeKind.Root, 0));
    }

    public MediaDrawingContext DrawingContext => Current.DrawingContext;

    public void RegisterVisualOwner(object sourceVisual)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(sourceVisual);

        _drawingFrame.RegisterRetainedWpfVisualOwner(sourceVisual, Current.Visual);
    }

    public bool PushVisualOwner(object sourceVisual)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(sourceVisual);

        var ownerVisual = new ProGpuRetainedDrawingVisual
        {
            Size = Current.Visual.Size
        };

        Current.Visual.AddChild(ownerVisual);
        _visualScopes.Push(new VisualScope(
            ownerVisual,
            Current.Context,
            Current.Viewport3DTextureCache,
            VisualScopeKind.SourceOwner,
            _scopeStack.Count));
        _drawingFrame.RegisterRetainedWpfVisualOwner(sourceVisual, ownerVisual);
        return true;
    }

    public void PopVisualOwner()
    {
        ThrowIfClosed();

        if (_visualScopes.Count <= 1)
        {
            throw new InvalidOperationException("There is no retained source owner visual scope to pop.");
        }

        var current = Current;
        if (current.ScopeKind != VisualScopeKind.SourceOwner)
        {
            throw new InvalidOperationException("The current retained visual scope is not a source owner scope.");
        }

        if (_scopeStack.Count != current.ScopeStackDepth)
        {
            throw new InvalidOperationException("Cannot pop a retained source owner visual scope while drawing scopes are still open.");
        }

        PopVisualScope();
    }

    public void ApplyVisualState(in WpfRetainedVisualState state)
    {
        ThrowIfClosed();

        var visual = Current.Visual;
        visual.Offset = state.Offset;
        visual.Transform = state.Transform;
        visual.Opacity = state.Opacity;
        visual.ClipBounds = state.ClipBounds.HasValue
            ? new global::ProGPU.Scene.Rect(
                (float)state.ClipBounds.Value.X,
                (float)state.ClipBounds.Value.Y,
                (float)state.ClipBounds.Value.Width,
                (float)state.ClipBounds.Value.Height)
            : null;
    }

    private VisualScope Current
    {
        get
        {
            ThrowIfClosed();
            return _visualScopes.Peek();
        }
    }

    public void DrawLine(MediaPen? pen, Point point0, Point point1)
    {
        Current.Sink.DrawLine(pen, point0, point1);
    }

    public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
    {
        Current.Sink.DrawRectangle(brush, pen, rectangle);
    }

    public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        Current.Sink.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
    }

    public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
    {
        Current.Sink.DrawEllipse(brush, pen, center, radiusX, radiusY);
    }

    public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        Current.Sink.DrawGeometry(brush, pen, geometry);
    }

    public void DrawImage(MediaImageSource imageSource, Rect rectangle)
    {
        Current.Sink.DrawImage(imageSource, rectangle);
    }

    public void DrawImage(MediaImageSource imageSource, Rect rectangle, Rect sourceRectangle)
    {
        Current.Sink.DrawImage(imageSource, rectangle, sourceRectangle);
    }

    public void DrawText(MediaFormattedText formattedText, Point origin)
    {
        Current.Sink.DrawText(formattedText, origin);
    }

    public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
    {
        Current.Sink.DrawGlyphRun(foregroundBrush, glyphRun);
    }

    public bool DrawViewport3D(object viewportVisual)
    {
        return Current.Sink.DrawViewport3D(viewportVisual);
    }

    public void PushClip(MediaGeometry clipGeometry)
    {
        Current.Sink.PushClip(clipGeometry);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushOpacity(double opacity)
    {
        Current.Sink.PushOpacity(opacity);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
    {
        Current.Sink.PushOpacityMask(opacityMask, bounds);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushTransform(MediaTransform transform)
    {
        Current.Sink.PushTransform(transform);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushNoOpScope()
    {
        Current.Sink.PushNoOpScope();
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushGuidelineSet()
    {
        Current.Sink.PushGuidelineSet();
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushGuidelineSet(object? guidelines)
    {
        Current.Sink.PushGuidelineSet(guidelines);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushGuidelineY1(double coordinate)
    {
        Current.Sink.PushGuidelineY1(coordinate);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
    {
        Current.Sink.PushGuidelineY2(leadingCoordinate, offsetToDrivenCoordinate);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushBitmapScalingMode(object? bitmapScalingMode)
    {
        Current.Sink.PushBitmapScalingMode(bitmapScalingMode);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushEdgeMode(object? edgeMode)
    {
        Current.Sink.PushEdgeMode(edgeMode);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushTextRenderingMode(object? textRenderingMode)
    {
        Current.Sink.PushTextRenderingMode(textRenderingMode);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushTextHintingMode(object? textHintingMode)
    {
        Current.Sink.PushTextHintingMode(textHintingMode);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public bool PushVisualEffect(ProGpuEffectBase effect)
    {
        return PushVisualEffect(effect, bounds: null);
    }

    public bool PushVisualEffect(ProGpuEffectBase effect, Rect? bounds)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(effect);

        var effectBounds = NormalizeBounds(bounds);
        var effectVisual = new ProGpuRetainedDrawingVisual
        {
            Effect = effect,
            Offset = new Vector2((float)effectBounds.X, (float)effectBounds.Y),
            Size = new Vector2((float)effectBounds.Width, (float)effectBounds.Height)
        };

        PushVisualScope(effectVisual, effectBounds, ScopeKind.VisualEffect);
        return true;
    }

    public bool PushVisualCache(Rect? bounds = null)
    {
        return PushCacheVisual(bounds, ScopeKind.VisualCache);
    }

    public bool PushDrawingCache(Rect? bounds = null)
    {
        return PushCacheVisual(bounds, ScopeKind.DrawingCache);
    }

    private bool PushCacheVisual(Rect? bounds, ScopeKind scopeKind)
    {
        ThrowIfClosed();
        var cacheBounds = NormalizeBounds(bounds);
        var cacheVisual = new ProGpuRetainedDrawingVisual
        {
            CacheAsLayer = true,
            Offset = new Vector2((float)cacheBounds.X, (float)cacheBounds.Y),
            Size = new Vector2((float)cacheBounds.Width, (float)cacheBounds.Height)
        };

        PushVisualScope(cacheVisual, cacheBounds, scopeKind);
        return true;
    }

    public void Pop()
    {
        ThrowIfClosed();

        if (_scopeStack.Count == 0)
        {
            Current.Sink.Pop();
            return;
        }

        var scopeKind = _scopeStack.Pop();
        if (scopeKind == ScopeKind.Delegate)
        {
            Current.Sink.Pop();
            return;
        }

        PopVisualScope();
    }

    public void Close()
    {
        if (_isClosed)
        {
            return;
        }

        while (_scopeStack.Count > 0)
        {
            Pop();
        }

        while (_visualScopes.Count > 0)
        {
            _visualScopes.Pop().Dispose();
        }

        _isClosed = true;
    }

    public void Dispose()
    {
        Close();
    }

    private Rect NormalizeBounds(Rect? bounds)
    {
        if (bounds.HasValue
            && !bounds.Value.IsEmpty
            && double.IsFinite(bounds.Value.X)
            && double.IsFinite(bounds.Value.Y)
            && double.IsFinite(bounds.Value.Width)
            && double.IsFinite(bounds.Value.Height)
            && bounds.Value.Width > 0
            && bounds.Value.Height > 0)
        {
            return bounds.Value;
        }

        var rootSize = _visualScopes.Count > 0
            ? _visualScopes.Peek().Visual.Size
            : Vector2.One;
        return new Rect(0, 0, Math.Max(1, rootSize.X), Math.Max(1, rootSize.Y));
    }

    private void PushVisualScope(ProGpuRetainedDrawingVisual visual, Rect bounds, ScopeKind scopeKind)
    {
        Current.Visual.AddChild(visual);

        var visualScopeKind = scopeKind == ScopeKind.VisualEffect ? VisualScopeKind.Effect : VisualScopeKind.Cache;
        var scope = new VisualScope(visual, Current.Context, Current.Viewport3DTextureCache, visualScopeKind, _scopeStack.Count);
        if (bounds.X != 0 || bounds.Y != 0)
        {
            var matrix = Matrix.Identity;
            matrix.Translate(-bounds.X, -bounds.Y);
            scope.Sink.PushTransform(new MatrixTransform(matrix));
            scope.HasBoundsTransform = true;
        }

        _visualScopes.Push(scope);
        _scopeStack.Push(scopeKind);
    }

    private void PopVisualScope()
    {
        if (_visualScopes.Count <= 1)
        {
            return;
        }

        var scope = _visualScopes.Pop();
        if (scope.HasBoundsTransform)
        {
            scope.Sink.Pop();
        }

        scope.Dispose();
    }

    private void ThrowIfClosed()
    {
        if (_isClosed)
        {
            throw new ObjectDisposedException(nameof(ProGpuRetainedCompositionCommandSink));
        }
    }

    private sealed class VisualScope : IDisposable
    {
        public VisualScope(
            ProGpuRetainedDrawingVisual visual,
            global::ProGPU.Backend.WgpuContext? context,
            WpfViewport3DTextureCache? viewport3DTextureCache,
            VisualScopeKind scopeKind,
            int scopeStackDepth)
        {
            Visual = visual;
            Context = context;
            Viewport3DTextureCache = viewport3DTextureCache;
            ScopeKind = scopeKind;
            ScopeStackDepth = scopeStackDepth;
            DrawingContext = new MediaDrawingContext(visual.Context);
            Sink = new ProGpuCompositionCommandSink(DrawingContext, context, viewport3DTextureCache);
        }

        public ProGpuRetainedDrawingVisual Visual { get; }

        public global::ProGPU.Backend.WgpuContext? Context { get; }

        public WpfViewport3DTextureCache? Viewport3DTextureCache { get; }

        public VisualScopeKind ScopeKind { get; }

        public int ScopeStackDepth { get; }

        public MediaDrawingContext DrawingContext { get; }

        public ProGpuCompositionCommandSink Sink { get; }

        public bool HasBoundsTransform { get; set; }

        public void Dispose()
        {
            Sink.Dispose();
            DrawingContext.Dispose();
        }
    }
}

internal sealed class ProGpuRetainedDrawingVisual : ProGpuContainerVisual
{
    public ProGpuDrawingContext Context { get; } = new();

    public override void OnRender(ProGpuDrawingContext context)
    {
        context.Append(Context);
    }
}
