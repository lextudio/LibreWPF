using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using System.Numerics;
using Xunit;
using ProGpuBlurEffect = ProGPU.Scene.BlurEffect;
using ProGpuContainerVisual = ProGPU.Scene.ContainerVisual;
using ProGpuDrawingVisual = ProGPU.Scene.DrawingVisual;
using ProGpuRetainedDrawingVisual = System.Windows.Media.ProGPU.Composition.ProGpuRetainedDrawingVisual;
using ProGpuRenderCommandType = ProGPU.Scene.RenderCommandType;

namespace ProGPU.Wpf.Tests;

public sealed class ProGpuWpfDrawingFrameTests
{
    [Fact]
    public void ConstructorClearsRootOnceAndSetsClampedPixelSize()
    {
        var root = new ProGpuDrawingVisual();
        root.Context.DrawRectangle(null, null, new ProGPU.Scene.Rect(1, 2, 3, 4));

        var frame = new ProGpuWpfDrawingFrame(root, 0, 0);

        Assert.Equal(1u, frame.PixelWidth);
        Assert.Equal(1u, frame.PixelHeight);
        Assert.Equal(new System.Numerics.Vector2(1, 1), root.Size);
        Assert.Empty(root.Context.Commands);
    }

    [Fact]
    public void ConstructorResetsSceneRootWithRetainedWpfLayerBeforeFlatLayer()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        retainedRoot.AddChild(new ProGpuDrawingVisual());
        flatRoot.Context.DrawRectangle(null, null, new ProGPU.Scene.Rect(1, 2, 3, 4));

        var frame = new ProGpuWpfDrawingFrame(sceneRoot, retainedRoot, flatRoot, 200, 100);

        Assert.Equal(200u, frame.PixelWidth);
        Assert.Equal(100u, frame.PixelHeight);
        Assert.Empty(retainedRoot.Children);
        Assert.Empty(flatRoot.Context.Commands);
        Assert.Equal(new Vector2(200, 100), sceneRoot.Size);
        Assert.Equal(new Vector2(200, 100), retainedRoot.Size);
        Assert.Equal(new Vector2(200, 100), flatRoot.Size);
        Assert.Equal(new ProGPU.Scene.Visual[] { retainedRoot, flatRoot }, sceneRoot.Children.ToArray());
    }

    [Fact]
    public void RetainedSinkCreatesBoundedNativeEffectVisual()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(sceneRoot, retainedRoot, flatRoot, 200, 100);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var blur = new ProGpuBlurEffect(6);

        Assert.True(sink.PushVisualEffect(blur, new Rect(10, 20, 30, 40)));
        sink.DrawRectangle(Brushes.Red, null, new Rect(10, 20, 5, 6));
        sink.Pop();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var effectVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        Assert.Same(blur, effectVisual.Effect);
        Assert.Equal(new Vector2(10, 20), effectVisual.Offset);
        Assert.Equal(new Vector2(30, 40), effectVisual.Size);
        var command = Assert.Single(effectVisual.Context.Commands);
        Assert.Equal(ProGpuRenderCommandType.DrawRect, command.Type);
        Assert.Equal(-10, command.Transform.M41);
        Assert.Equal(-20, command.Transform.M42);
    }

    [Fact]
    public void RetainedSinkCreatesBoundedNativeCacheVisual()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(sceneRoot, retainedRoot, flatRoot, 200, 100);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);

        Assert.True(sink.PushVisualCache(new Rect(5, 6, 70, 80)));
        sink.DrawRectangle(Brushes.Red, null, new Rect(5, 6, 10, 11));
        sink.Pop();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var cacheVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        Assert.True(cacheVisual.CacheAsLayer);
        Assert.Null(cacheVisual.Effect);
        Assert.Equal(new Vector2(5, 6), cacheVisual.Offset);
        Assert.Equal(new Vector2(70, 80), cacheVisual.Size);
        var command = Assert.Single(cacheVisual.Context.Commands);
        Assert.Equal(ProGpuRenderCommandType.DrawRect, command.Type);
        Assert.Equal(-5, command.Transform.M41);
        Assert.Equal(-6, command.Transform.M42);
    }

    [Fact]
    public void RetainedSinkCreatesBoundedNativeDrawingCacheVisual()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(sceneRoot, retainedRoot, flatRoot, 200, 100);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var drawingCacheSink = (IWpfDrawingCacheCommandSink)sink;

        Assert.True(drawingCacheSink.PushDrawingCache(new Rect(12, 13, 40, 50)));
        sink.DrawRectangle(Brushes.Red, null, new Rect(12, 13, 14, 15));
        sink.Pop();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var cacheVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        Assert.True(cacheVisual.CacheAsLayer);
        Assert.Null(cacheVisual.Effect);
        Assert.Equal(new Vector2(12, 13), cacheVisual.Offset);
        Assert.Equal(new Vector2(40, 50), cacheVisual.Size);
        var command = Assert.Single(cacheVisual.Context.Commands);
        Assert.Equal(ProGpuRenderCommandType.DrawRect, command.Type);
        Assert.Equal(-12, command.Transform.M41);
        Assert.Equal(-13, command.Transform.M42);
    }

    [Fact]
    public void DrawingContextFactoryAppendsMultipleWrappersToSameFrameBuffer()
    {
        var root = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(root, 200, 100);
        var factory = frame.CreateDrawingContextFactory();
        var ownerVisual = new object();

        using (var first = factory(null))
        {
            first.DrawRectangle(Brushes.Red, null, new Rect(1, 2, 3, 4));
        }

        using (var second = factory(ownerVisual))
        {
            second.DrawLine(new Pen(Brushes.Black, 1), new Point(5, 6), new Point(7, 8));
        }

        Assert.Equal(2, frame.DrawingContextCount);
        Assert.Equal(0, frame.CompositionDrawingContextCount);
        Assert.Same(ownerVisual, frame.LastOwnerVisual);
        Assert.Equal(new[]
        {
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.DrawLine
        }, root.Context.Commands.Select(command => command.Type).ToArray());
    }

    [Fact]
    public void CompositionDrawingContextFactoryAppendsToSameFrameBuffer()
    {
        var root = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(root, 200, 100);
        var factory = frame.CreateCompositionDrawingContextFactory();

        using (var first = factory(null))
        {
            first.DrawRectangle(Brushes.Red, null, new Rect(1, 2, 3, 4));
        }

        using (var second = factory(new object()))
        {
            second.DrawLine(new Pen(Brushes.Black, 1), new Point(5, 6), new Point(7, 8));
        }

        Assert.Equal(2, frame.DrawingContextCount);
        Assert.Equal(2, frame.CompositionDrawingContextCount);
        Assert.Equal(new[]
        {
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.DrawLine
        }, root.Context.Commands.Select(command => command.Type).ToArray());
    }

    [Fact]
    public void TryRegisterRenderDataSinkProviderReturnsFalseWhenProviderIsAbsent()
    {
        var frame = new ProGpuWpfDrawingFrame(new ProGpuDrawingVisual(), 200, 100);

        var registered = frame.TryRegisterRenderDataSinkProvider(out var registration);

        Assert.False(registered);
        Assert.Null(registration);
    }
}
