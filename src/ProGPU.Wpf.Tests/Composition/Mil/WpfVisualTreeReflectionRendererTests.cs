using System.Buffers.Binary;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using Xunit;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaBitmapSource = System.Windows.Media.Imaging.BitmapSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;
using WpfVector = System.Windows.Vector;
using ProGpuTexture = ProGPU.Backend.GpuTexture;
using ProGpuBlurEffect = ProGPU.Scene.BlurEffect;
using ProGpuDropShadowEffect = ProGPU.Scene.DropShadowEffect;
using ProGpuEffectBase = ProGPU.Scene.EffectBase;
using ProGpuWpfShaderEffect = ProGPU.Scene.WpfShaderEffect;
using ProGpuWpfShaderEffectSampler = ProGPU.Scene.WpfShaderEffectSampler;
using ProGpuTextureSamplingMode = ProGPU.Scene.TextureSamplingMode;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfVisualTreeReflectionRendererTests
{
    [Fact]
    public void ReplaySubtreeRecursesThroughChildren()
    {
        var parentBrush = Brushes.Red;
        var childBrush = Brushes.Blue;
        var parent = new FakeDrawingVisual(CreateRenderData(parentBrush));
        parent.Children.Add(new FakeDrawingVisual(CreateRenderData(childBrush)));
        var sink = new TestSink();

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(parent, sink);

        Assert.Equal(2, result.VisualCount);
        Assert.Equal(2, result.ContentCount);
        Assert.Equal(1, result.ChildEdgeCount);
        Assert.Equal(0, result.UnsupportedContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result.RenderData);
        Assert.Equal(2, sink.DrawRectangles.Count);
        Assert.Same(parentBrush, sink.DrawRectangles[0].Brush);
        Assert.Same(childBrush, sink.DrawRectangles[1].Brush);
    }

    [Fact]
    public void ReplaySubtreeReadsUiElementDrawingContent()
    {
        var brush = Brushes.Green;
        var visual = new FakeUiElementVisual(CreateRenderData(brush));
        var sink = new TestSink();

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(visual, sink);

        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Single(sink.DrawRectangles);
        Assert.Same(brush, sink.DrawRectangles[0].Brush);
    }

    [Fact]
    public void ReplaySubtreeRegistersSourceVisualOwnersWhenSinkSupportsBranchMap()
    {
        var parent = new FakeDrawingVisual(CreateRenderData(Brushes.Red));
        var child = new FakeDrawingVisual(CreateRenderData(Brushes.Blue));
        parent.Children.Add(child);
        var sink = new TestSink();

        _ = new WpfVisualTreeReflectionRenderer().ReplaySubtree(parent, sink);

        Assert.Equal(new object[] { parent, child }, sink.VisualOwners);
    }

    [Fact]
    public void ReplaySubtreeLowersNativeVisualStateIntoRetainedOwnerScopes()
    {
        var root = new FakeVisual
        {
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            Offset = new WpfVector(10, 20),
            Opacity = 0.5,
            Clip = new FakeRectangleGeometry(new FakeRect(0, 0, 100, 50))
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualOwner", "ApplyVisualState", "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner", "PopVisualOwner" }, sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Equal(2, sink.RetainedVisualStates.Count);
        var rootState = sink.RetainedVisualStates[0];
        Assert.Equal(new Vector2(10, 20), rootState.Offset);
        Assert.Equal(0.5f, rootState.Opacity);
        Assert.Equal(3, rootState.Transform.M41);
        Assert.Equal(4, rootState.Transform.M42);
        Assert.Equal(new Rect(0, 0, 100, 50), rootState.ClipBounds);
        var childState = sink.RetainedVisualStates[1];
        Assert.Equal(Vector2.Zero, childState.Offset);
        Assert.Equal(1f, childState.Opacity);
        Assert.Equal(Matrix4x4.Identity, childState.Transform);
        Assert.Null(childState.ClipBounds);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualUsesCurrentOwnerBranch()
    {
        var root = new FakeVisual
        {
            Offset = new WpfVector(10, 20),
            Opacity = 0.75
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var renderer = new WpfVisualTreeReflectionRenderer();

        Assert.True(renderer.CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.True(renderer.TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(new[] { "ApplyVisualState", "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner" }, sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Equal(2, sink.RetainedVisualStates.Count);
        Assert.Equal(new Vector2(10, 20), sink.RetainedVisualStates[0].Offset);
        Assert.Equal(0.75f, sink.RetainedVisualStates[0].Opacity);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(1, result.ChildEdgeCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeLowersNativeEffectCacheAndOpacityIntoRetainedOwnerScope()
    {
        var effect = new FakeBlurEffect(4);
        var root = new FakeDrawingVisual(CreateRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(10, 20, 30, 40),
            Effect = effect,
            CacheMode = new object(),
            Opacity = 0.6,
            Offset = new WpfVector(2, 3)
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawRectangle", "Pop", "PopVisualOwner" }, sink.Operations);
        Assert.Equal(new object[] { root }, sink.VisualOwners);
        var state = Assert.Single(sink.RetainedVisualStates);
        var blur = Assert.IsType<ProGpuBlurEffect>(state.Effect);
        Assert.Equal(4, blur.BlurRadius);
        Assert.True(state.CacheAsLayer);
        Assert.Equal(new Vector2(12, 23), state.Offset);
        Assert.Equal(new Vector2(30, 40), state.Size);
        Assert.Equal(0.6f, state.Opacity);
        Assert.Equal(new Rect(10, 20, 30, 40), state.ContentBounds);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(-10, transform.Matrix.OffsetX);
        Assert.Equal(-20, transform.Matrix.OffsetY);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualReappliesNativeCacheState()
    {
        var root = new FakeDrawingVisual(CreateRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(5, 6, 70, 80),
            CacheMode = new object(),
            Opacity = 0.35,
            Offset = new WpfVector(2, 3),
            Clip = new FakeRectangleGeometry(new FakeRect(10, 11, 20, 30))
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var renderer = new WpfVisualTreeReflectionRenderer();

        Assert.True(renderer.CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.True(renderer.TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(new[] { "ApplyVisualState", "PushTransform", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new object[] { root }, sink.VisualOwners);
        var state = Assert.Single(sink.RetainedVisualStates);
        Assert.Null(state.Effect);
        Assert.True(state.CacheAsLayer);
        Assert.Equal(new Vector2(7, 9), state.Offset);
        Assert.Equal(new Vector2(70, 80), state.Size);
        Assert.Equal(0.35f, state.Opacity);
        Assert.Equal(new Rect(5, 5, 20, 30), state.ClipBounds);
        Assert.Equal(new Rect(5, 6, 70, 80), state.ContentBounds);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(-5, transform.Matrix.OffsetX);
        Assert.Equal(-6, transform.Matrix.OffsetY);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualReappliesNativeEffectWithOuterTransform()
    {
        var root = new FakeDrawingVisual(CreateRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(5, 6, 70, 80),
            Effect = new FakeBlurEffect(4),
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            Offset = new WpfVector(11, 13),
            Clip = new FakeRectangleGeometry(new FakeRect(10, 12, 20, 25))
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var renderer = new WpfVisualTreeReflectionRenderer();

        Assert.True(renderer.CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.True(renderer.TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(new[] { "ApplyVisualState", "PushTransform", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new object[] { root }, sink.VisualOwners);
        var state = Assert.Single(sink.RetainedVisualStates);
        var blur = Assert.IsType<ProGpuBlurEffect>(state.Effect);
        Assert.Equal(4, blur.BlurRadius);
        Assert.False(state.CacheAsLayer);
        Assert.Equal(new Vector2(11, 13), state.Offset);
        Assert.Equal(8, state.Transform.M41);
        Assert.Equal(10, state.Transform.M42);
        Assert.Equal(new Rect(5, 6, 20, 25), state.ClipBounds);
        Assert.Equal(new Vector2(70, 80), state.Size);
        Assert.Equal(new Rect(5, 6, 70, 80), state.ContentBounds);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(-5, transform.Matrix.OffsetX);
        Assert.Equal(-6, transform.Matrix.OffsetY);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualRejectsMultipleNativeEffectSources()
    {
        var root = new FakeDrawingVisual(CreateRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(5, 6, 70, 80),
            Effect = new FakeBlurEffect(4),
            BitmapEffect = new FakeBlurBitmapEffect(6)
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var renderer = new WpfVisualTreeReflectionRenderer();

        Assert.False(renderer.CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.False(renderer.TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(default, result);
        Assert.Empty(sink.Operations);
        Assert.Empty(sink.VisualOwners);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualRejectsNonNativeRootState()
    {
        var root = new FakeVisual
        {
            Bounds = new FakeRect(1, 2, 100, 50),
            OpacityMask = Brushes.White
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));
        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var renderer = new WpfVisualTreeReflectionRenderer();

        Assert.False(renderer.CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.False(renderer.TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(default, result);
        Assert.Empty(sink.Operations);
        Assert.Empty(sink.VisualOwners);
    }

    [Fact]
    public void ReplaySubtreeRegistersRenderDataResourcesAsRetainedDependencies()
    {
        var brush = Brushes.Green;
        var renderData = CreateRenderData(brush);
        var root = new FakeDrawingVisual(renderData);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new object[] { root }, sink.VisualOwners);
        Assert.Contains(root.Children, sink.VisualDependencies);
        Assert.Contains(renderData, sink.VisualDependencies);
        Assert.Contains(brush, sink.VisualDependencies);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeRegistersUiElementDrawingContentResourcesAsRetainedDependencies()
    {
        var brush = Brushes.Green;
        var renderData = CreateRenderData(brush);
        var root = new FakeUiElementVisual(renderData);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new object[] { root }, sink.VisualOwners);
        Assert.Contains(root.Children, sink.VisualDependencies);
        Assert.Contains(renderData, sink.VisualDependencies);
        Assert.Contains(brush, sink.VisualDependencies);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeRegistersNestedRenderDataResourcesAsRetainedDependencies()
    {
        var brush = Brushes.Green;
        var nestedBrush = new FakeResource();
        var nestedDrawing = new FakeDrawingResource
        {
            Brush = nestedBrush
        };
        var renderData = CreateRenderData(brush, nestedDrawing);
        var root = new FakeDrawingVisual(renderData);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Contains(renderData, sink.VisualDependencies);
        Assert.Contains(nestedDrawing, sink.VisualDependencies);
        Assert.Contains(nestedBrush, sink.VisualDependencies);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeRegistersNestedVisualStateResourcesAsRetainedDependencies()
    {
        var shaderEffect = new FakeShaderEffect(new byte[] { 0, 3, 0, 0, 1, 2, 3, 4 });
        var root = new FakeVisual
        {
            Effect = shaderEffect
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Contains(shaderEffect, sink.VisualDependencies);
        Assert.Contains(shaderEffect.PixelShader, sink.VisualDependencies);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeRegistersVisualChildrenCollectionAsShallowRetainedDependency()
    {
        var root = new FakeVisual();
        var child = new FakeDrawingVisual(CreateRenderData(Brushes.Green));
        root.Children.Add(child);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new object[] { root, child }, sink.VisualOwners);
        Assert.Contains(root.Children, sink.VisualDependencies);
        Assert.DoesNotContain(child, sink.VisualDependencies);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ChildEdgeCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualRegistersRenderDataResourcesAsRetainedDependencies()
    {
        var brush = Brushes.Green;
        var renderData = CreateRenderData(brush);
        var root = new FakeDrawingVisual(renderData);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        Assert.True(new WpfVisualTreeReflectionRenderer().TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(new object[] { root }, sink.VisualOwners);
        Assert.Contains(root.Children, sink.VisualDependencies);
        Assert.Contains(renderData, sink.VisualDependencies);
        Assert.Contains(brush, sink.VisualDependencies);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeKeepsFallbackSubtreeInCommandScopeForNonNativeVisualState()
    {
        var root = new FakeVisual
        {
            Bounds = new FakeRect(1, 2, 100, 50),
            OpacityMask = Brushes.White
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushOpacityMask", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Empty(sink.RetainedVisualStates);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeKeepsRoundedClipInCommandScopeForNativeOwnerSink()
    {
        var root = new FakeVisual
        {
            Clip = new FakeRectangleGeometry(new FakeRect(0, 0, 100, 50))
            {
                RadiusX = 4,
                RadiusY = 4
            }
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushClip", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Empty(sink.RetainedVisualStates);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesOffsetAndOpacityAroundContentAndChildren()
    {
        var root = new FakeVisual
        {
            Offset = new WpfVector(10, 20),
            Opacity = 0.5
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTransform", "PushOpacity", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Transforms);
        Assert.Equal(10, sink.Transforms[0].Value.M41);
        Assert.Equal(20, sink.Transforms[0].Value.M42);
        Assert.Equal(new[] { 0.5 }, sink.Opacities);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeCountsUnsupportedContentWithoutThrowing()
    {
        var root = new FakeDrawingVisual(new object());
        var sink = new TestSink();

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(1, result.VisualCount);
        Assert.Equal(0, result.ContentCount);
        Assert.Equal(1, result.UnsupportedContentCount);
        Assert.Empty(sink.Operations);
    }

    [Fact]
    public void ReplaySubtreeAdaptsWpfShapedTransformAndClip()
    {
        var root = new FakeVisual
        {
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            Clip = new FakeRectangleGeometry(new FakeRect(0, 0, 100, 50))
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTransform", "PushClip", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Transforms);
        Assert.Equal(3, sink.Transforms[0].Value.M41);
        Assert.Equal(4, sink.Transforms[0].Value.M42);
        var clip = Assert.Single(sink.Clips);
        Assert.Equal(new Rect(0, 0, 100, 50), clip.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesOpacityMaskWhenBoundsAreAvailable()
    {
        var root = new FakeVisual
        {
            Bounds = new FakeRect(1, 2, 100, 50),
            OpacityMask = Brushes.White
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushOpacityMask", "DrawRectangle", "Pop" }, sink.Operations);
        var mask = Assert.Single(sink.OpacityMasks);
        Assert.Same(Brushes.White, mask.OpacityMask);
        Assert.Equal(new Rect(1, 2, 100, 50), mask.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeInfersOpacityMaskBoundsFromRenderDataContent()
    {
        var root = new FakeDrawingVisual(CreateRenderData(Brushes.Green))
        {
            OpacityMask = Brushes.White
        };

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushOpacityMask", "DrawRectangle", "Pop" }, sink.Operations);
        var mask = Assert.Single(sink.OpacityMasks);
        Assert.Same(Brushes.White, mask.OpacityMask);
        Assert.Equal(new Rect(1, 2, 30, 40), mask.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeInfersOpacityMaskBoundsFromTransformedRenderDataContent()
    {
        var root = new FakeDrawingVisual(CreateTransformedRenderData(
            new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9)),
            Brushes.Green))
        {
            OpacityMask = Brushes.White
        };

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushOpacityMask", "PushTransform", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        var mask = Assert.Single(sink.OpacityMasks);
        Assert.Same(Brushes.White, mask.OpacityMask);
        Assert.Equal(new Rect(8, 11, 30, 40), mask.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(3, 3, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeInfersOpacityMaskBoundsFromChildRenderDataContent()
    {
        var root = new FakeVisual
        {
            OpacityMask = Brushes.White
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushOpacityMask", "DrawRectangle", "Pop" }, sink.Operations);
        var mask = Assert.Single(sink.OpacityMasks);
        Assert.Same(Brushes.White, mask.OpacityMask);
        Assert.Equal(new Rect(1, 2, 30, 40), mask.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeProjectsChildVisualStateWhenInferringOpacityMaskBounds()
    {
        var root = new FakeVisual
        {
            OpacityMask = Brushes.White
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green))
        {
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 5, 7)),
            Offset = new WpfVector(10, 20),
            Clip = new FakeRectangleGeometry(new FakeRect(5, 6, 10, 12))
        });

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushOpacityMask", "PushTransform", "PushTransform", "PushClip", "DrawRectangle", "Pop", "Pop", "Pop", "Pop" },
            sink.Operations);
        var mask = Assert.Single(sink.OpacityMasks);
        Assert.Same(Brushes.White, mask.OpacityMask);
        Assert.Equal(new Rect(20, 33, 10, 12), mask.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeDoesNotInferOpacityMaskBoundsFromUnsupportedChildVisualState()
    {
        var root = new FakeVisual
        {
            OpacityMask = Brushes.White
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green))
        {
            Transform = new object()
        });

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.OpacityMasks);
        Assert.Equal(2, result.UnsupportedVisualStateCount);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesGuidelineCollectionsAsNoOpScope()
    {
        var root = new FakeVisual
        {
            XSnappingGuidelines = new FakeDoubleCollection(10)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushGuidelineSetObject", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesScrollableAreaClipAsRectangleClip()
    {
        var root = new FakeVisual
        {
            ScrollableAreaClip = new FakeRect(2, 3, 40, 50)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushClip", "DrawRectangle", "Pop" }, sink.Operations);
        var clip = Assert.Single(sink.Clips);
        Assert.Equal(new Rect(2, 3, 40, 50), clip.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeCountsUnsupportedVisualEffectAndRenderingHintState()
    {
        var root = new FakeVisual
        {
            Effect = new FakeBlurEffect(8),
            BitmapEffect = new object(),
            CacheMode = new object(),
            EdgeMode = new FakeRenderingHint("Aliased"),
            BitmapScalingMode = new FakeRenderingHint("NearestNeighbor"),
            ClearTypeHint = new FakeRenderingHint("Enabled"),
            TextRenderingMode = new FakeRenderingHint("Aliased"),
            TextHintingMode = new FakeRenderingHint("Fixed")
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushBitmapScalingMode", "PushEdgeMode", "PushTextRenderingMode", "PushTextHintingMode", "DrawRectangle", "Pop", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "NearestNeighbor" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Aliased" }, sink.EdgeModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Aliased" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Fixed" }, sink.TextHintingModes.Select(mode => mode?.ToString()));
        Assert.Equal(3, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePushesNativeBlurEffectWhenSinkSupportsVisualEffects()
    {
        var root = new FakeVisual
        {
            Effect = new FakeBlurEffect(12.5)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualEffects = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(12.5f, effect.BlurRadius);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePushesNativeCacheWhenSinkSupportsVisualCaches()
    {
        var root = new FakeVisual
        {
            Bounds = new FakeRect(10, 20, 30, 40),
            CacheMode = new object()
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualCaches = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualCache", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new Rect(10, 20, 30, 40), Assert.Single(sink.VisualCacheBounds));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePushesNativeDropShadowEffectWhenSinkSupportsVisualEffects()
    {
        var root = new FakeVisual
        {
            Effect = new FakeDropShadowEffect
            {
                BlurRadius = 7,
                ShadowDepth = 10,
                Direction = 315,
                Opacity = 0.5,
                Color = Color.FromArgb(128, 10, 20, 30)
            }
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualEffects = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuDropShadowEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(7f, effect.BlurRadius);
        Assert.InRange(effect.Offset.X, 7.06f, 7.08f);
        Assert.InRange(effect.Offset.Y, 7.06f, 7.08f);
        Assert.Equal(10f / 255f, effect.Color.X);
        Assert.Equal(20f / 255f, effect.Color.Y);
        Assert.Equal(30f / 255f, effect.Color.Z);
        Assert.InRange(effect.Color.W, 0.25f, 0.251f);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePushesNativeBitmapEffectWhenEmulationIsSupported()
    {
        var root = new FakeVisual
        {
            BitmapEffect = new FakeBlurBitmapEffect(6)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualEffects = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(6f, effect.BlurRadius);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeCountsSupportedEffectUnsupportedWhenSinkCannotApplyVisualEffects()
    {
        var root = new FakeVisual
        {
            Effect = new FakeBlurEffect(4)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.VisualEffects);
        Assert.Equal(1, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePushesNativeShaderEffectWhenReplacementIsRegistered()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 1, 2, 3, 4 };
        var shaderSource = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }";
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            shaderSource,
            shaderKey: "registered_fake_shader");

        try
        {
            var shaderEffect = new FakeShaderEffect(bytecode);
            shaderEffect.SetFloatConstant(2, 0.25f, 0.5f, 0.75f, 1f);
            shaderEffect.SetImplicitInputSampler(1, FakeSamplingMode.NearestNeighbor);

            var root = new FakeVisual { Effect = shaderEffect };
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

            Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
            var effect = Assert.IsType<ProGpuWpfShaderEffect>(Assert.Single(sink.VisualEffects));
            Assert.Equal(shaderSource, effect.Parameters.ShaderSource);
            Assert.Equal("registered_fake_shader", effect.Parameters.ShaderKey);
            Assert.Equal(1, effect.Parameters.SourceTextureRegisterIndex);
            Assert.Equal(ProGpuTextureSamplingMode.Nearest, effect.Parameters.SamplingMode);
            Assert.Equal(0.25f, effect.Parameters.Constants[8]);
            Assert.Equal(0.5f, effect.Parameters.Constants[9]);
            Assert.Equal(0.75f, effect.Parameters.Constants[10]);
            Assert.Equal(1f, effect.Parameters.Constants[11]);
            Assert.Equal(0, result.UnsupportedVisualStateCount);
            Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(replacementKey);
        }
    }

    [Fact]
    public void ReplaySubtreePushesNativeShaderEffectWithImageBrushSampler()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 2, 4, 6, 8 };
        var shaderSource = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }";
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            shaderSource,
            shaderKey: "registered_image_sampler_shader");
        var samplerTexture = (ProGpuTexture)RuntimeHelpers.GetUninitializedObject(typeof(ProGpuTexture));
        var rawSamplerSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter(new FakeSamplerBitmapSource(samplerTexture));

        try
        {
            var shaderEffect = new FakeShaderEffect(bytecode);
            shaderEffect.SetImplicitInputSampler(0, FakeSamplingMode.Bilinear);
            shaderEffect.SetSampler(2, new FakeShaderImageBrush(rawSamplerSource), FakeSamplingMode.NearestNeighbor);

            var root = new FakeVisual { Effect = shaderEffect };
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(
                root,
                sink,
                imageSourceAdapter: imageAdapter);

            Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
            var effect = Assert.IsType<ProGpuWpfShaderEffect>(Assert.Single(sink.VisualEffects));
            var sampler = Assert.Single(effect.Parameters.Samplers);
            Assert.Equal(2, sampler.RegisterIndex);
            Assert.Same(samplerTexture, sampler.Texture);
            Assert.Equal(ProGpuTextureSamplingMode.Nearest, sampler.SamplingMode);
            Assert.Equal(0, effect.Parameters.SourceTextureRegisterIndex);
            Assert.Equal(ProGpuTextureSamplingMode.Linear, effect.Parameters.SamplingMode);
            Assert.Same(rawSamplerSource, imageAdapter.LastImageSource);
            Assert.Equal(0, result.UnsupportedVisualStateCount);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(replacementKey);
        }
    }

    [Fact]
    public void ReplaySubtreePushesNativeShaderEffectWithAdapterRenderedBrushSampler()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 12, 14, 16, 18 };
        var shaderSource = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }";
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            shaderSource,
            shaderKey: "registered_rendered_brush_sampler_shader");
        var samplerTexture = (ProGpuTexture)RuntimeHelpers.GetUninitializedObject(typeof(ProGpuTexture));
        var samplerBrush = new FakeShaderDrawingBrush();
        var imageAdapter = new FakeShaderSamplerBrushAdapter(samplerTexture);

        try
        {
            var shaderEffect = new FakeShaderEffect(bytecode);
            shaderEffect.SetImplicitInputSampler(0, FakeSamplingMode.Bilinear);
            shaderEffect.SetSampler(3, samplerBrush, FakeSamplingMode.NearestNeighbor);

            var root = new FakeVisual { Effect = shaderEffect };
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(
                root,
                sink,
                imageSourceAdapter: imageAdapter);

            Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
            var effect = Assert.IsType<ProGpuWpfShaderEffect>(Assert.Single(sink.VisualEffects));
            var sampler = Assert.Single(effect.Parameters.Samplers);
            Assert.Equal(3, sampler.RegisterIndex);
            Assert.Same(samplerTexture, sampler.Texture);
            Assert.Equal(ProGpuTextureSamplingMode.Nearest, sampler.SamplingMode);
            Assert.Same(samplerBrush, imageAdapter.LastSamplerBrush);
            Assert.Equal(3, imageAdapter.LastSamplerRegisterIndex);
            Assert.Equal(ProGpuTextureSamplingMode.Nearest, imageAdapter.LastSamplerMode);
            Assert.Equal(0, result.UnsupportedVisualStateCount);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(replacementKey);
        }
    }

    [Fact]
    public void ReplaySubtreeCountsShaderEffectUnsupportedForUnsupportedSamplerBrush()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 5, 7, 9, 11 };
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }",
            shaderKey: "registered_unsupported_sampler_shader");

        try
        {
            var shaderEffect = new FakeShaderEffect(bytecode);
            shaderEffect.SetImplicitInputSampler(0, FakeSamplingMode.Bilinear);
            shaderEffect.SetSampler(2, new FakeUnsupportedSamplerBrush(), FakeSamplingMode.NearestNeighbor);

            var root = new FakeVisual { Effect = shaderEffect };
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

            Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
            Assert.Empty(sink.VisualEffects);
            Assert.Equal(1, result.UnsupportedVisualStateCount);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(replacementKey);
        }
    }

    [Fact]
    public void ReplaySubtreeCountsShaderEffectUnsupportedWhenReplacementIsMissing()
    {
        var root = new FakeVisual
        {
            Effect = new FakeShaderEffect(new byte[] { 0, 3, 0, 0, 9, 9, 9, 9 })
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualEffects = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.VisualEffects);
        Assert.Equal(1, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeCountsShaderEffectUnsupportedForOutOfRangeInputSampler()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 4, 4, 4, 4 };
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }",
            shaderKey: "registered_out_of_range_sampler_shader");

        try
        {
            var shaderEffect = new FakeShaderEffect(bytecode);
            shaderEffect.SetImplicitInputSampler(16, FakeSamplingMode.NearestNeighbor);

            var root = new FakeVisual { Effect = shaderEffect };
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

            Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
            Assert.Empty(sink.VisualEffects);
            Assert.Equal(1, result.UnsupportedVisualStateCount);
            Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(replacementKey);
        }
    }

    [Fact]
    public void ReplaySubtreeAppliesLowQualityBitmapScalingAsLinearState()
    {
        var root = new FakeVisual
        {
            BitmapScalingMode = new FakeRenderingHint("LowQuality")
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushBitmapScalingMode", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "LowQuality" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesHighQualityBitmapScalingAsCubicState()
    {
        var root = new FakeVisual
        {
            BitmapScalingMode = new FakeRenderingHint("HighQuality")
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushBitmapScalingMode", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "HighQuality" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesClearTypeTextRenderingMode()
    {
        var root = new FakeVisual
        {
            TextRenderingMode = new FakeRenderingHint("ClearType")
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTextRenderingMode", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesClearTypeHintAsTextRenderingMode()
    {
        var root = new FakeVisual
        {
            ClearTypeHint = new FakeRenderingHint("Enabled")
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTextRenderingMode", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesAnimatedTextHintingMode()
    {
        var root = new FakeVisual
        {
            TextHintingMode = new FakeRenderingHint("Animated")
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTextHintingMode", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "Animated" }, sink.TextHintingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePassesImageSourceAdapterToDefaultRenderDataResolver()
    {
        var source = new FakeBitmapSource();
        var adapter = new FakeImageSourceAdapter();
        var root = new FakeDrawingVisual(CreateImageRenderData(source));
        var sink = new TestSink();

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(
            root,
            sink,
            imageSourceAdapter: adapter);

        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Same(source, adapter.LastImageSource);
        var image = Assert.Single(sink.Images);
        Assert.Same(adapter.AdaptedImageSource, image.ImageSource);
        Assert.Equal(new Rect(1, 2, 30, 40), image.Rectangle);
    }

    private static FakeRenderData CreateRenderData(MediaBrush brush)
    {
        var record = CreateRectangleRecord(1, 0);
        return new FakeRenderData(record, record.Length, new FakeDependentResources(brush));
    }

    private static FakeRenderData CreateRenderData(MediaBrush brush, object extraResource)
    {
        var record = CreateRectangleRecord(1, 0);
        return new FakeRenderData(record, record.Length, new FakeDependentResources(brush, extraResource));
    }

    private static FakeRenderData CreateImageRenderData(object imageSource)
    {
        var record = CreateImageRecord(1);
        return new FakeRenderData(record, record.Length, new FakeDependentResources(imageSource));
    }

    private static FakeRenderData CreateTransformedRenderData(object transform, MediaBrush brush)
    {
        var pushTransformPayload = new byte[8];
        WriteUInt32(pushTransformPayload, 0, 1);

        var rectanglePayload = new byte[40];
        WriteRect(rectanglePayload, 0, 1, 2, 30, 40);
        WriteUInt32(rectanglePayload, 32, 2);

        var record = CreateRecord(WpfMilCommandId.PushTransform, pushTransformPayload)
            .Concat(CreateRecord(WpfMilCommandId.DrawRectangle, rectanglePayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        return new FakeRenderData(record, record.Length, new FakeDependentResources(transform, brush));
    }

    private static byte[] CreateRectangleRecord(uint brushToken, uint penToken)
    {
        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, brushToken);
        WriteUInt32(payload, 36, penToken);
        return CreateRecord(WpfMilCommandId.DrawRectangle, payload);
    }

    private static byte[] CreateImageRecord(uint imageSourceToken)
    {
        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, imageSourceToken);
        return CreateRecord(WpfMilCommandId.DrawImage, payload);
    }

    private static byte[] CreateRecord(WpfMilCommandId commandId, byte[] payload)
    {
        var record = new byte[payload.Length + 8];
        WriteInt32(record, 0, record.Length);
        WriteInt32(record, 4, (int)commandId);
        payload.CopyTo(record.AsSpan(8));
        return record;
    }

    private static void WriteRect(byte[] target, int offset, double x, double y, double width, double height)
    {
        WriteDouble(target, offset, x);
        WriteDouble(target, offset + 8, y);
        WriteDouble(target, offset + 16, width);
        WriteDouble(target, offset + 24, height);
    }

    private static void WriteInt32(byte[] target, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), value);
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset, 4), value);
    }

    private static void WriteDouble(byte[] target, int offset, double value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(target.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(value));
    }

    private class FakeVisual
    {
        public FakeVisualCollection Children { get; } = new();

        public WpfVector Offset { get; init; }

        public double Opacity { get; init; } = 1;

        public object? Transform { get; init; }

        public object? Clip { get; init; }

        public object? Bounds { get; init; }

        public MediaBrush? OpacityMask { get; init; }

        public object? XSnappingGuidelines { get; init; }

        public object? Effect { get; init; }

        public object? BitmapEffect { get; init; }

        public object? CacheMode { get; init; }

        public object? ScrollableAreaClip { get; init; }

        public object? EdgeMode { get; init; }

        public object? BitmapScalingMode { get; init; }

        public object? ClearTypeHint { get; init; }

        public object? TextRenderingMode { get; init; }

        public object? TextHintingMode { get; init; }
    }

    private sealed class FakeDrawingVisual : FakeVisual
    {
        private readonly object? _content;

        public FakeDrawingVisual(object? content)
        {
            _content = content;
        }
    }

    private sealed class FakeUiElementVisual : FakeVisual
    {
        private readonly object? _drawingContent;

        public FakeUiElementVisual(object? drawingContent)
        {
            _drawingContent = drawingContent;
        }
    }

    private sealed class FakeVisualCollection
    {
        private readonly List<object> _children = new();

        public int Count => _children.Count;

        public object this[int index] => _children[index];

        public void Add(object child)
        {
            _children.Add(child);
        }
    }

    private sealed class FakeDoubleCollection
    {
        private readonly double[] _values;

        public FakeDoubleCollection(params double[] values)
        {
            _values = values;
        }

        public int Count => _values.Length;

        public double this[int index] => _values[index];
    }

    private sealed class FakeDrawingResource
    {
        public object? Brush { get; init; }
    }

    private sealed class FakeResource
    {
    }

    private sealed class FakeRenderingHint
    {
        private readonly string _name;

        public FakeRenderingHint(string name)
        {
            _name = name;
        }

        public override string ToString()
        {
            return _name;
        }
    }

    private sealed class FakeBlurEffect
    {
        public FakeBlurEffect(double radius)
        {
            Radius = radius;
        }

        public double Radius { get; }
    }

    private sealed class FakeDropShadowEffect
    {
        public double BlurRadius { get; init; }

        public double ShadowDepth { get; init; }

        public double Direction { get; init; }

        public double Opacity { get; init; } = 1;

        public Color Color { get; init; } = Colors.Black;
    }

    private sealed class FakeBlurBitmapEffect
    {
        public FakeBlurBitmapEffect(double radius)
        {
            Radius = radius;
        }

        public double Radius { get; }

        private bool CanBeEmulatedUsingEffectPipeline()
        {
            return true;
        }

        private FakeBlurEffect GetEmulatingEffect()
        {
            return new FakeBlurEffect(Radius);
        }
    }

    private sealed class FakeShaderEffect
    {
        private readonly List<FakeFloatRegister?> _floatRegisters = new();
        private readonly List<FakeSamplerData?> _samplerData = new();

        public FakeShaderEffect(byte[] shaderBytecode)
        {
            PixelShader = new FakePixelShader(shaderBytecode);
        }

        public FakePixelShader PixelShader { get; }

        public void SetFloatConstant(int registerIndex, float r, float g, float b, float a)
        {
            while (_floatRegisters.Count <= registerIndex)
            {
                _floatRegisters.Add(null);
            }

            _floatRegisters[registerIndex] = new FakeFloatRegister(r, g, b, a);
        }

        public void SetImplicitInputSampler(int registerIndex, FakeSamplingMode samplingMode)
        {
            SetSampler(registerIndex, new FakeImplicitInputBrush(), samplingMode);
        }

        public void SetSampler(int registerIndex, object? brush, FakeSamplingMode samplingMode)
        {
            while (_samplerData.Count <= registerIndex)
            {
                _samplerData.Add(null);
            }

            _samplerData[registerIndex] = new FakeSamplerData(brush, samplingMode);
        }
    }

    private sealed class FakePixelShader
    {
        private readonly byte[] _shaderBytecode;

        public FakePixelShader(byte[] shaderBytecode)
        {
            _shaderBytecode = shaderBytecode;
        }

        public Uri? UriSource { get; init; }
    }

    private readonly record struct FakeFloatRegister(float r, float g, float b, float a);

    private readonly record struct FakeSamplerData(object? _brush, object? _samplingMode);

    private sealed class FakeImplicitInputBrush
    {
    }

    private sealed class FakeShaderImageBrush
    {
        public FakeShaderImageBrush(object? imageSource)
        {
            ImageSource = imageSource;
        }

        public object? ImageSource { get; }
    }

    private sealed class FakeShaderDrawingBrush
    {
    }

    private sealed class FakeUnsupportedSamplerBrush
    {
    }

    private sealed class FakeSamplerBitmapSource : MediaBitmapSource
    {
        private readonly ProGpuTexture _texture;

        public FakeSamplerBitmapSource(ProGpuTexture texture)
        {
            _texture = texture;
        }

        public override int PixelWidth => 1;

        public override int PixelHeight => 1;

        public override ProGpuTexture GpuTexture => _texture;
    }

    private enum FakeSamplingMode
    {
        NearestNeighbor = 0,
        Bilinear = 1,
        Auto = 2
    }

    private sealed class FakeRenderData
    {
        private readonly byte[] _buffer;
        private readonly int _curOffset;
        private readonly FakeDependentResources _dependentResources;

        public FakeRenderData(byte[] buffer, int curOffset, FakeDependentResources dependentResources)
        {
            _buffer = buffer;
            _curOffset = curOffset;
            _dependentResources = dependentResources;
        }
    }

    private sealed class FakeDependentResources
    {
        private readonly object?[] _items;

        public FakeDependentResources(params object?[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public object? this[int index] => _items[index];
    }

    private sealed class FakeMatrixTransform
    {
        public FakeMatrixTransform(FakeMatrix value)
        {
            Value = value;
        }

        public FakeMatrix Value { get; }
    }

    private readonly record struct FakeMatrix(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY);

    private sealed class FakeRectangleGeometry
    {
        public FakeRectangleGeometry(FakeRect rect)
        {
            Rect = rect;
        }

        public FakeRect Rect { get; }

        public double RadiusX { get; init; }

        public double RadiusY { get; init; }
    }

    private readonly record struct FakeRect(double X, double Y, double Width, double Height);

    private sealed class FakeBitmapSource
    {
    }

    private sealed class FakeImageSource : MediaImageSource
    {
    }

    private sealed class FakeImageSourceAdapter : IWpfImageSourceAdapter
    {
        public FakeImageSourceAdapter(MediaImageSource? adaptedImageSource = null)
        {
            AdaptedImageSource = adaptedImageSource ?? new FakeImageSource();
        }

        public MediaImageSource AdaptedImageSource { get; }

        public object? LastImageSource { get; private set; }

        public MediaImageSource? AdaptImageSource(object? imageSource)
        {
            LastImageSource = imageSource;
            return AdaptedImageSource;
        }
    }

    private sealed class FakeShaderSamplerBrushAdapter :
        IWpfImageSourceAdapter,
        IWpfShaderEffectSamplerBrushAdapter
    {
        private readonly ProGpuTexture _texture;

        public FakeShaderSamplerBrushAdapter(ProGpuTexture texture)
        {
            _texture = texture;
        }

        public object? LastSamplerBrush { get; private set; }

        public int LastSamplerRegisterIndex { get; private set; }

        public ProGpuTextureSamplingMode LastSamplerMode { get; private set; }

        public MediaImageSource? AdaptImageSource(object? imageSource)
        {
            return null;
        }

        public bool TryAdaptShaderEffectSamplerBrush(
            object? brush,
            int registerIndex,
            ProGpuTextureSamplingMode samplingMode,
            out ProGpuWpfShaderEffectSampler sampler)
        {
            LastSamplerBrush = brush;
            LastSamplerRegisterIndex = registerIndex;
            LastSamplerMode = samplingMode;
            sampler = new ProGpuWpfShaderEffectSampler(registerIndex, _texture, samplingMode);
            return true;
        }
    }

    private sealed class TestSink :
        IWpfCompositionCommandSink,
        IWpfVisualEffectCommandSink,
        IWpfVisualCacheCommandSink,
        IWpfRetainedVisualBranchSink,
        IWpfRetainedVisualStateSink
    {
        public List<string> Operations { get; } = new();

        public List<object> VisualOwners { get; } = new();

        public List<object> VisualDependencies { get; } = new();

        public List<WpfRetainedVisualState> RetainedVisualStates { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle)> DrawRectangles { get; } = new();

        public List<(MediaImageSource ImageSource, Rect Rectangle)> Images { get; } = new();

        public List<MediaTransform> Transforms { get; } = new();

        public List<MediaGeometry> Clips { get; } = new();

        public List<double> Opacities { get; } = new();

        public List<(MediaBrush? OpacityMask, Rect Bounds)> OpacityMasks { get; } = new();

        public List<object?> BitmapScalingModes { get; } = new();

        public List<object?> EdgeModes { get; } = new();

        public List<object?> TextRenderingModes { get; } = new();

        public List<object?> TextHintingModes { get; } = new();

        public List<ProGpuEffectBase> VisualEffects { get; } = new();

        public List<Rect?> VisualCacheBounds { get; } = new();

        public bool AcceptVisualEffects { get; init; }

        public bool AcceptVisualCaches { get; init; }

        public bool AcceptRetainedVisualOwners { get; init; }

        public MediaDrawingContext DrawingContext => null!;

        public void RegisterVisualOwner(object sourceVisual)
        {
            VisualOwners.Add(sourceVisual);
        }

        public void RegisterVisualDependency(object dependency)
        {
            VisualDependencies.Add(dependency);
        }

        public bool PushVisualOwner(object sourceVisual)
        {
            if (!AcceptRetainedVisualOwners)
            {
                return false;
            }

            Operations.Add("PushVisualOwner");
            VisualOwners.Add(sourceVisual);
            return true;
        }

        public void PopVisualOwner()
        {
            Operations.Add("PopVisualOwner");
        }

        public void ApplyVisualState(in WpfRetainedVisualState state)
        {
            Operations.Add("ApplyVisualState");
            RetainedVisualStates.Add(state);
        }

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
            Operations.Add("DrawRectangle");
            DrawRectangles.Add((brush, pen, rectangle));
        }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
        {
        }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
            Operations.Add("DrawImage");
            Images.Add((imageSource, rectangle));
        }

        public void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
            Operations.Add("PushClip");
            Clips.Add(clipGeometry);
        }

        public void PushOpacity(double opacity)
        {
            Operations.Add("PushOpacity");
            Opacities.Add(opacity);
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
            Operations.Add("PushOpacityMask");
            OpacityMasks.Add((opacityMask, bounds));
        }

        public void PushTransform(MediaTransform transform)
        {
            Operations.Add("PushTransform");
            Transforms.Add(transform);
        }

        public void PushGuidelineSet()
        {
            Operations.Add("PushGuidelineSet");
        }

        public void PushGuidelineSet(object? guidelines)
        {
            Operations.Add("PushGuidelineSetObject");
            Assert.NotNull(guidelines);
        }

        public void PushBitmapScalingMode(object? bitmapScalingMode)
        {
            Operations.Add("PushBitmapScalingMode");
            BitmapScalingModes.Add(bitmapScalingMode);
        }

        public void PushEdgeMode(object? edgeMode)
        {
            Operations.Add("PushEdgeMode");
            EdgeModes.Add(edgeMode);
        }

        public void PushTextRenderingMode(object? textRenderingMode)
        {
            Operations.Add("PushTextRenderingMode");
            TextRenderingModes.Add(textRenderingMode);
        }

        public void PushTextHintingMode(object? textHintingMode)
        {
            Operations.Add("PushTextHintingMode");
            TextHintingModes.Add(textHintingMode);
        }

        public bool PushVisualEffect(ProGpuEffectBase effect)
        {
            if (!AcceptVisualEffects)
            {
                return false;
            }

            Operations.Add("PushVisualEffect");
            VisualEffects.Add(effect);
            return true;
        }

        public bool PushVisualCache(Rect? bounds = null)
        {
            if (!AcceptVisualCaches)
            {
                return false;
            }

            Operations.Add("PushVisualCache");
            VisualCacheBounds.Add(bounds);
            return true;
        }

        public void Pop()
        {
            Operations.Add("Pop");
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }
}
