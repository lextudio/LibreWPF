using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
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
using PortableBitmapEffectInput = ProGPU.Wpf.Interop.PortableBitmapEffectInput;
using PortableBitmapEffectInputSource = ProGPU.Wpf.Interop.IPortableBitmapEffectInputSource;
using PortableColor = ProGPU.Wpf.Interop.PortableColor;
using PortableEffect = ProGPU.Wpf.Interop.PortableEffect;
using PortableEffectSource = ProGPU.Wpf.Interop.IPortableEffectSource;
using PortableDrawingGroupState = ProGPU.Wpf.Interop.PortableDrawingGroupState;
using PortableDrawingGroupStateSource = ProGPU.Wpf.Interop.IPortableDrawingGroupStateSource;
using PortableGeometryDrawingState = ProGPU.Wpf.Interop.PortableGeometryDrawingState;
using PortableGeometryDrawingStateSource = ProGPU.Wpf.Interop.IPortableGeometryDrawingStateSource;
using PortableGlyphRunDrawingState = ProGPU.Wpf.Interop.PortableGlyphRunDrawingState;
using PortableGlyphRunDrawingStateSource = ProGPU.Wpf.Interop.IPortableGlyphRunDrawingStateSource;
using PortableImageDrawingState = ProGPU.Wpf.Interop.PortableImageDrawingState;
using PortableImageDrawingStateSource = ProGPU.Wpf.Interop.IPortableImageDrawingStateSource;
using PortablePixelShader = ProGPU.Wpf.Interop.PortablePixelShader;
using PortableShaderEffect = ProGPU.Wpf.Interop.PortableShaderEffect;
using PortableShaderEffectSource = ProGPU.Wpf.Interop.IPortableShaderEffectSource;
using PortableShaderSampler = ProGPU.Wpf.Interop.PortableShaderSampler;
using PortableShaderSamplingMode = ProGPU.Wpf.Interop.PortableShaderSamplingMode;
using PortablePoint = ProGPU.Wpf.Interop.PortablePoint;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;
using PortableVisualLayoutState = ProGPU.Wpf.Interop.PortableVisualLayoutState;
using PortableVisualLayoutStateSource = ProGPU.Wpf.Interop.IPortableVisualLayoutStateSource;
using PortableVisualChildrenSource = ProGPU.Wpf.Interop.IPortableVisualChildrenSource;
using PortableVisualState = ProGPU.Wpf.Interop.PortableVisualState;
using PortableVisualStateSource = ProGPU.Wpf.Interop.IPortableVisualStateSource;
using PortableDrawingContentSource = ProGPU.Wpf.Interop.IPortableDrawingContentSource;
using PortableRenderDataSnapshot = ProGPU.Wpf.Interop.PortableRenderDataSnapshot;
using PortableRenderDataSource = ProGPU.Wpf.Interop.IPortableRenderDataSource;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfVisualTreeReflectionRendererTests
{
    private static readonly Lazy<Type> s_xceedDataCellType = new(CreateXceedDataCellType);

    private static PortableVisualState CreatePortableScrollableAreaClipState(
        double x,
        double y,
        double width,
        double height)
    {
        return new PortableVisualState
        {
            HasScrollableAreaClip = true,
            ScrollableAreaClip = new PortableRect(x, y, width, height)
        };
    }

    private static PortableVisualState CreatePortableOpacityMaskState(object opacityMask)
    {
        return new PortableVisualState
        {
            HasOpacityMask = true,
            OpacityMask = opacityMask
        };
    }

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
    public void ReplaySubtreeRecursesThroughProtectedVisualChildren()
    {
        var root = new FakeVisualChildrenVisual();
        var childBrush = Brushes.Blue;
        root.AddChild(new FakeDrawingVisual(CreateRenderData(childBrush)));
        var sink = new TestSink();

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(1, result.ChildEdgeCount);
        Assert.Equal(0, result.UnsupportedContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        var rectangle = Assert.Single(sink.DrawRectangles);
        Assert.Same(childBrush, rectangle.Brush);
    }

    [Fact]
    public void ReplaySubtreeRecursesThroughPortableVisualChildren()
    {
        var root = new FakePortableVisualChildrenVisual();
        var childBrush = Brushes.Blue;
        root.AddChild(new FakeDrawingVisual(CreateRenderData(childBrush)));
        var sink = new TestSink();

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(1, result.ChildEdgeCount);
        Assert.Equal(0, result.UnsupportedContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        var rectangle = Assert.Single(sink.DrawRectangles);
        Assert.Same(childBrush, rectangle.Brush);
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
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasTransform = true,
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            HasOffset = true,
            Offset = new PortablePoint(10, 20),
            HasOpacity = true,
            Opacity = 0.5,
            HasClip = true,
            Clip = new FakeRectangleGeometry(new FakeRect(0, 0, 100, 50))
        });
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
        AssertReplayRect(0, 0, 100, 50, rootState.ClipBounds);
        Assert.Null(rootState.OuterClipBounds);
        var childState = sink.RetainedVisualStates[1];
        Assert.Equal(Vector2.Zero, childState.Offset);
        Assert.Equal(1f, childState.Opacity);
        Assert.Equal(Matrix4x4.Identity, childState.Transform);
        Assert.Null(childState.ClipBounds);
        Assert.Null(childState.OuterClipBounds);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeLowersPortableVisualClipIntoRetainedOwnerScopes()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasClip = true,
            Clip = new FakeRectangleGeometry(new FakeRect(5, 6, 70, 80))
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(5, 6, 70, 80, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeLowersLayoutClipIntoRetainedOwnerScopes()
    {
        var root = new FakeVisual
        {
            LayoutClip = new FakeRectangleGeometry(new FakeRect(4, 5, 60, 70))
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(4, 5, 60, 70, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeUsesPortableLayoutStateForLayoutClip()
    {
        var root = new FakePortableVisualLayoutVisual(new PortableVisualLayoutState
        {
            HasLayoutClip = true,
            LayoutClip = new FakeRectangleGeometry(new FakeRect(7, 8, 90, 20)),
            HasClipToBounds = true,
            ClipToBounds = false
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(7, 8, 90, 20, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void CanReplaySubtreeTreatsAbsentPortableVisualStateValuesAsAuthoritative()
    {
        var root = new ThrowingPortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1
            });

        Assert.True(new WpfVisualTreeReflectionRenderer().CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.Equal(0, root.ReflectedStateProbeCount);
    }

    [Fact]
    public void ReplaySubtreeRegistersPortableVisualStateResourcesAsRetainedDependencies()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4));
        var clip = new FakeRectangleGeometry(new FakeRect(0, 0, 100, 50));
        var opacityMask = Brushes.White;
        var effect = new FakeBlurEffect(3);
        var cacheMode = new object();
        var layoutClip = new FakeRectangleGeometry(new FakeRect(1, 2, 30, 40));
        var root = new FakePortableVisualStateAndLayoutDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasTransform = true,
                Transform = transform,
                HasClip = true,
                Clip = clip,
                HasOpacity = true,
                Opacity = 1,
                HasOpacityMask = true,
                OpacityMask = opacityMask,
                HasEffect = true,
                Effect = effect,
                HasCacheMode = true,
                CacheMode = cacheMode
            },
            new PortableVisualLayoutState
            {
                HasRenderSize = true,
                RenderSize = new ProGPU.Wpf.Interop.PortableSize(100, 50),
                HasLayoutClip = true,
                LayoutClip = layoutClip
            });
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Contains(transform, sink.VisualDependencies);
        Assert.Contains(clip, sink.VisualDependencies);
        Assert.Contains(opacityMask, sink.VisualDependencies);
        Assert.Contains(effect, sink.VisualDependencies);
        Assert.Contains(cacheMode, sink.VisualDependencies);
        Assert.Contains(layoutClip, sink.VisualDependencies);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeDoesNotReflectAbsentPortableVisualStateDependencies()
    {
        var root = new ThrowingPortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1
            });
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(0, root.ReflectedStateProbeCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeLowersClipToBoundsRenderSizeIntoRetainedOwnerScopes()
    {
        var root = new FakeVisual
        {
            ClipToBounds = true,
            RenderSize = new Size(80, 35)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(0, 0, 80, 35, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeUsesPortableLayoutStateForClipToBoundsAndOpacityMaskBounds()
    {
        var root = new FakePortableVisualStateAndLayoutVisual(
            CreatePortableOpacityMaskState(Brushes.White),
            new PortableVisualLayoutState
            {
                HasRenderSize = true,
                RenderSize = new ProGPU.Wpf.Interop.PortableSize(42, 24),
                HasClipToBounds = true,
                ClipToBounds = true
            });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        var state = sink.RetainedVisualStates[0];
        AssertReplayRect(0, 0, 42, 24, state.ClipBounds);
        AssertReplayRect(0, 0, 42, 24, state.OpacityMaskBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeSynthesizesXceedDataGridCellClipFromRenderSize()
    {
        var root = CreateXceedDataCellVisual(new Size(55, 18));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 55, 18, state.ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeIntersectsLayoutAndExplicitClipsForRetainedOwnerScopes()
    {
        var root = new FakePortableVisualStateAndLayoutVisual(
            new PortableVisualState
            {
                HasClip = true,
                Clip = new FakeRectangleGeometry(new FakeRect(0, 0, 50, 50))
            },
            new PortableVisualLayoutState
            {
                HasLayoutClip = true,
                LayoutClip = new FakeRectangleGeometry(new FakeRect(10, 12, 60, 70))
            });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(10, 12, 40, 38, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeLowersVisualScrollableAreaClipIntoRetainedOwnerScopes()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableScrollableAreaClipState(2, 3, 40, 50));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualOwner", "ApplyVisualState", "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner", "PopVisualOwner" }, sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Equal(2, sink.RetainedVisualStates.Count);
        Assert.Null(sink.RetainedVisualStates[0].ClipBounds);
        AssertReplayRect(2, 3, 40, 50, sink.RetainedVisualStates[0].OuterClipBounds);
        Assert.Null(sink.RetainedVisualStates[1].ClipBounds);
        Assert.Null(sink.RetainedVisualStates[1].OuterClipBounds);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeLowersLocalAndScrollableClipsIntoSeparateRetainedState()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(10, 20),
            HasClip = true,
            Clip = new FakeRectangleGeometry(new FakeRect(1, 2, 30, 40)),
            HasScrollableAreaClip = true,
            ScrollableAreaClip = new PortableRect(10, 20, 80, 90)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        var rootState = sink.RetainedVisualStates[0];
        Assert.Equal(new Vector2(10, 20), rootState.Offset);
        AssertReplayRect(1, 2, 30, 40, rootState.ClipBounds);
        AssertReplayRect(10, 20, 80, 90, rootState.OuterClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualUsesCurrentOwnerBranch()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(10, 20),
            HasOpacity = true,
            Opacity = 0.75
        });
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
        var cacheMode = new object();
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasEffect = true,
                Effect = effect,
                HasCacheMode = true,
                CacheMode = cacheMode,
                HasOpacity = true,
                Opacity = 0.6,
                HasOffset = true,
                Offset = new PortablePoint(2, 3)
            })
        {
            Bounds = new FakeRect(10, 20, 30, 40)
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
        AssertReplayRect(10, 20, 30, 40, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-10, transform.M41);
        Assert.Equal(-20, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeLowersOpacityMaskAndNativeEffectIntoRetainedOwnerScope()
    {
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            visualState)
        {
            Bounds = new FakeRect(10, 20, 30, 40)
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawRectangle", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        Assert.IsType<ProGpuBlurEffect>(state.Effect);
        Assert.Same(Brushes.White, state.OpacityMask);
        AssertReplayRect(0, 0, 30, 40, state.OpacityMaskBounds);
        AssertReplayRect(10, 20, 30, 40, state.ContentBounds);
        Assert.Empty(sink.OpacityMasks);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-10, transform.M41);
        Assert.Equal(-20, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualReappliesNativeCacheState()
    {
        var cacheMode = new object();
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(2, 3),
                HasOpacity = true,
                Opacity = 0.35,
                HasClip = true,
                Clip = new FakeRectangleGeometry(new FakeRect(10, 11, 20, 30)),
                HasCacheMode = true,
                CacheMode = cacheMode
            })
        {
            Bounds = new FakeRect(5, 6, 70, 80)
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
        AssertReplayRect(5, 5, 20, 30, state.ClipBounds);
        AssertReplayRect(5, 6, 70, 80, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-5, transform.M41);
        Assert.Equal(-6, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualReappliesNativeCacheVisualScrollableAreaClip()
    {
        var cacheMode = new object();
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(2, 3),
                HasCacheMode = true,
                CacheMode = cacheMode,
                HasScrollableAreaClip = true,
                ScrollableAreaClip = new PortableRect(10, 12, 20, 25)
            })
        {
            Bounds = new FakeRect(5, 6, 70, 80)
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
        Assert.True(state.CacheAsLayer);
        Assert.Equal(new Vector2(7, 9), state.Offset);
        Assert.Equal(new Vector2(70, 80), state.Size);
        Assert.Null(state.ClipBounds);
        AssertReplayRect(10, 12, 20, 25, state.OuterClipBounds);
        AssertReplayRect(5, 6, 70, 80, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-5, transform.M41);
        Assert.Equal(-6, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualReappliesNativeEffectWithOuterTransform()
    {
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasTransform = true,
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
                HasOffset = true,
                Offset = new PortablePoint(11, 13),
                HasClip = true,
                Clip = new FakeRectangleGeometry(new FakeRect(10, 12, 20, 25)),
                HasEffect = true,
                Effect = new FakeBlurEffect(4)
            })
        {
            Bounds = new FakeRect(5, 6, 70, 80)
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
        AssertReplayRect(5, 6, 20, 25, state.ClipBounds);
        Assert.Equal(new Vector2(70, 80), state.Size);
        AssertReplayRect(5, 6, 70, 80, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-5, transform.M41);
        Assert.Equal(-6, transform.M42);
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
    public void TryReplaySubtreeIntoCurrentRetainedVisualPreservesOpacityMaskNativeState()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White))
        {
            Bounds = new FakeRect(1, 2, 100, 50)
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
        var state = sink.RetainedVisualStates[0];
        Assert.Same(Brushes.White, state.OpacityMask);
        AssertReplayRect(1, 2, 100, 50, state.OpacityMaskBounds);
        Assert.Empty(sink.OpacityMasks);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
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
    public void ReplaySubtreeUsesPortableVisualScrollableAreaClipWithoutPropertyProbe()
    {
        var root = new ThrowingPortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            CreatePortableScrollableAreaClipState(2, 3, 40, 50));
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(0, root.ReflectedStateProbeCount);
        Assert.Equal(new[] { "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner" }, sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(2, 3, 40, 50, state.OuterClipBounds);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
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
    public void ReplaySubtreeKeepsOpacityMaskAsNativeRetainedOwnerState()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White))
        {
            Bounds = new FakeRect(1, 2, 100, 50)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner", "PopVisualOwner" },
            sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Equal(2, sink.RetainedVisualStates.Count);
        var state = sink.RetainedVisualStates[0];
        Assert.Same(Brushes.White, state.OpacityMask);
        AssertReplayRect(1, 2, 100, 50, state.OpacityMaskBounds);
        Assert.Empty(sink.OpacityMasks);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeFallsBackWhenRetainedOpacityMaskCannotBeAdapted()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(new object()))
        {
            Bounds = new FakeRect(1, 2, 100, 50)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Empty(sink.RetainedVisualStates);
        Assert.Empty(sink.OpacityMasks);
        Assert.Equal(1, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeKeepsRoundedClipInCommandScopeForNativeOwnerSink()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasClip = true,
            Clip = new FakeRectangleGeometry(new FakeRect(0, 0, 100, 50))
            {
                RadiusX = 4,
                RadiusY = 4
            }
        });
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
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(10, 20),
            HasOpacity = true,
            Opacity = 0.5
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTransform", "PushOpacity", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(10, transform.M41);
        Assert.Equal(20, transform.M42);
        Assert.Equal(new[] { 0.5 }, sink.Opacities);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesWpfVisualOffsetAroundContent()
    {
        var root = new FakeVisualOffsetDrawingVisual(
            CreateRenderData(Brushes.Green),
            new WpfVector(16, 24));
        var sink = new TestSink();

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTransform", "DrawRectangle", "Pop" }, sink.Operations);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(16, transform.M41);
        Assert.Equal(24, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeLowersWpfVisualOffsetIntoRetainedOwnerState()
    {
        var root = new FakeVisualOffsetDrawingVisual(
            CreateRenderData(Brushes.Green),
            new WpfVector(16, 24));
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner" }, sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        Assert.Equal(new Vector2(16, 24), state.Offset);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesWpfVisualTransformAroundContent()
    {
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasTransform = true,
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4))
            });
        var sink = new TestSink();

        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTransform", "DrawRectangle", "Pop" }, sink.Operations);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(3, transform.M41);
        Assert.Equal(4, transform.M42);
        Assert.Equal(1, result.VisualCount);
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
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasTransform = true,
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            HasClip = true,
            Clip = new FakeRectangleGeometry(new FakeRect(0, 0, 100, 50))
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTransform", "PushNativeClip", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        Assert.Empty(sink.Transforms);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(3, transform.M41);
        Assert.Equal(4, transform.M42);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(0, 0, 100, 50, clip);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesOpacityMaskWhenBoundsAreAvailable()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White))
        {
            Bounds = new FakeRect(1, 2, 100, 50)
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
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            CreatePortableOpacityMaskState(Brushes.White));

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
        var root = new FakePortableVisualStateDrawingVisual(
            CreateTransformedRenderData(
                new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9)),
                Brushes.Green),
            CreatePortableOpacityMaskState(Brushes.White));

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
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White));
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
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White));
        root.Children.Add(new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasTransform = true,
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 5, 7)),
                HasOffset = true,
                Offset = new PortablePoint(10, 20),
                HasClip = true,
                Clip = new FakeRectangleGeometry(new FakeRect(5, 6, 10, 12))
            }));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushOpacityMask", "PushTransform", "PushTransform", "PushNativeClip", "DrawRectangle", "Pop", "Pop", "Pop", "Pop" },
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
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White));
        root.Children.Add(new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasTransform = true,
                Transform = new object()
            }));

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
            XSnappingGuidelines = new[] { 10d }
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushGuidelineSetObject", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesPortableVisualGuidelinesWithoutReflection()
    {
        var root = new ThrowingPortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1,
                HasSnappingGuidelinesX = true,
                SnappingGuidelinesX = new[] { 10d },
                HasSnappingGuidelinesY = true,
                SnappingGuidelinesY = new[] { 20d, 21d }
            });

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushGuidelineSetObject", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(0, root.ReflectedStateProbeCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesScrollableAreaClipAsRectangleClip()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableScrollableAreaClipState(2, 3, 40, 50));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeClip", "DrawRectangle", "Pop" }, sink.Operations);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(2, 3, 40, 50, clip);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesVisualScrollableAreaClipAsRectangleClip()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableScrollableAreaClipState(2, 3, 40, 50));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeClip", "DrawRectangle", "Pop" }, sink.Operations);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(2, 3, 40, 50, clip);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesLayoutClipAsRectangleClip()
    {
        var root = new FakeVisual
        {
            LayoutClip = new FakeRectangleGeometry(new FakeRect(2, 3, 40, 50))
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeClip", "DrawRectangle", "Pop" }, sink.Operations);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(2, 3, 40, 50, clip);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesClipToBoundsRenderSizeAsRectangleClip()
    {
        var root = new FakeVisual
        {
            ClipToBounds = true,
            RenderSize = new Size(40, 50)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeClip", "DrawRectangle", "Pop" }, sink.Operations);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(0, 0, 40, 50, clip);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeProjectsScrollableAreaClipOutsideVisualOffsetForFallbackRendering()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(10, 20),
            HasScrollableAreaClip = true,
            ScrollableAreaClip = new PortableRect(2, 3, 40, 50)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushTransform", "PushTransform", "PushNativeClip", "PushTransform", "DrawRectangle", "Pop", "Pop", "Pop", "Pop" },
            sink.Operations);
        Assert.Equal(3, sink.NativeTransforms.Count);
        Assert.Equal(10, sink.NativeTransforms[0].M41);
        Assert.Equal(20, sink.NativeTransforms[0].M42);
        Assert.Equal(-10, sink.NativeTransforms[1].M41);
        Assert.Equal(-20, sink.NativeTransforms[1].M42);
        Assert.Equal(10, sink.NativeTransforms[2].M41);
        Assert.Equal(20, sink.NativeTransforms[2].M42);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(2, 3, 40, 50, clip);
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
    public void ReplaySubtreeAppliesPortableVisualRenderingHintsWithoutReflection()
    {
        var root = new ThrowingPortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1,
                HasBitmapScalingMode = true,
                BitmapScalingMode = new FakeRenderingHint("NearestNeighbor"),
                HasEdgeMode = true,
                EdgeMode = new FakeRenderingHint("Aliased"),
                HasTextRenderingMode = true,
                TextRenderingMode = new FakeRenderingHint("ClearType"),
                HasTextHintingMode = true,
                TextHintingMode = new FakeRenderingHint("Fixed")
            });

        var sink = new TestSink();
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushBitmapScalingMode", "PushEdgeMode", "PushTextRenderingMode", "PushTextHintingMode", "DrawRectangle", "Pop", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "NearestNeighbor" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Aliased" }, sink.EdgeModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Fixed" }, sink.TextHintingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, root.ReflectedStateProbeCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplayAppliesPortableGeometryDrawingStateWithoutReflection()
    {
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = new RectangleGeometry(new Rect(1, 2, 10, 12)),
            HasBrush = true,
            Brush = Brushes.Green
        });
        var sink = new TestSink();

        var status = WpfReflectionDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        var draw = Assert.Single(sink.DrawGeometries);
        Assert.Same(Brushes.Green, draw.Brush);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDoesNotReflectAbsentPortableGeometryDrawingState()
    {
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = new RectangleGeometry(new Rect(1, 2, 10, 12))
        });
        var sink = new TestSink();

        var status = WpfReflectionDrawingReplay.Replay(drawing, sink);

        Assert.Empty(sink.Operations);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Skipped, status);
    }

    [Fact]
    public void ReplayAppliesPortableImageDrawingStateWithoutReflection()
    {
        var source = new object();
        var drawing = new ThrowingPortableImageDrawing(new PortableImageDrawingState
        {
            HasImageSource = true,
            ImageSource = source,
            HasRect = true,
            Rect = new PortableRect(1, 2, 10, 12)
        });
        var sink = new TestSink();
        var adapter = new FakeImageSourceAdapter();

        var status = WpfReflectionDrawingReplay.Replay(drawing, sink, adapter.AdaptImageSource);

        Assert.Equal(new[] { "DrawImage" }, sink.Operations);
        var image = Assert.Single(sink.Images);
        Assert.Same(adapter.AdaptedImageSource, image.ImageSource);
        Assert.Equal(new Rect(1, 2, 10, 12), image.Rectangle);
        Assert.Same(source, adapter.LastImageSource);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDoesNotReflectAbsentPortableImageDrawingState()
    {
        var drawing = new ThrowingPortableImageDrawing(new PortableImageDrawingState());
        var sink = new TestSink();

        var status = WpfReflectionDrawingReplay.Replay(drawing, sink);

        Assert.Empty(sink.Operations);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Unsupported, status);
    }

    [Fact]
    public void ReplayDoesNotReflectAbsentPortableGlyphRunDrawingState()
    {
        var drawing = new ThrowingPortableGlyphRunDrawing(new PortableGlyphRunDrawingState
        {
            HasForegroundBrush = true,
            ForegroundBrush = Brushes.Green
        });
        var sink = new TestSink();

        var status = WpfReflectionDrawingReplay.Replay(drawing, sink);

        Assert.Empty(sink.Operations);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Unsupported, status);
    }

    [Fact]
    public void ReplaySubtreeAppliesPortableDrawingGroupStateWithoutReflection()
    {
        var group = new ThrowingPortableDrawingGroup(new PortableDrawingGroupState
        {
            HasBounds = true,
            Bounds = new PortableRect(0, 0, 40, 30),
            HasTransform = true,
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            HasClipGeometry = true,
            ClipGeometry = new RectangleGeometry(new Rect(0, 0, 20, 20)),
            HasOpacity = true,
            Opacity = 0.5,
            HasGuidelineSet = true,
            GuidelineSet = new object(),
            HasBitmapScalingMode = true,
            BitmapScalingMode = new FakeRenderingHint("LowQuality"),
            HasEdgeMode = true,
            EdgeMode = new FakeRenderingHint("Aliased"),
            HasTextRenderingMode = true,
            TextRenderingMode = new FakeRenderingHint("ClearType"),
            HasTextHintingMode = true,
            TextHintingMode = new FakeRenderingHint("Fixed"),
            Children =
            [
                new FakeGeometryDrawing(
                    new RectangleGeometry(new Rect(1, 2, 10, 12)),
                    Brushes.Green)
            ]
        });
        var sink = new TestSink();

        var status = WpfReflectionDrawingReplay.Replay(group, sink);

        Assert.Equal(
            new[]
            {
                "PushTransform",
                "PushClip",
                "PushOpacity",
                "PushGuidelineSetObject",
                "PushBitmapScalingMode",
                "PushEdgeMode",
                "PushTextRenderingMode",
                "PushTextHintingMode",
                "DrawGeometry",
                "Pop",
                "Pop",
                "Pop",
                "Pop",
                "Pop",
                "Pop",
                "Pop",
                "Pop"
            },
            sink.Operations);
        Assert.Equal(0, group.ReflectedStateProbeCount);
        Assert.Equal(new Vector2(3, 4), new Vector2(sink.NativeTransforms[0].M41, sink.NativeTransforms[0].M42));
        Assert.Equal(new[] { 0.5 }, sink.Opacities);
        Assert.Equal(new[] { "LowQuality" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Aliased" }, sink.EdgeModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Fixed" }, sink.TextHintingModes.Select(mode => mode?.ToString()));
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplaySubtreeDoesNotReflectAbsentPortableDrawingGroupState()
    {
        var group = new ThrowingPortableDrawingGroup(new PortableDrawingGroupState
        {
            HasOpacity = true,
            Opacity = 1,
            Children =
            [
                new FakeGeometryDrawing(
                    new RectangleGeometry(new Rect(1, 2, 10, 12)),
                    Brushes.Green)
            ]
        });
        var sink = new TestSink();

        var status = WpfReflectionDrawingReplay.Replay(group, sink);

        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Equal(0, group.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
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
    public void ReplaySubtreePushesPortableEffectWithoutReflectedTypeName()
    {
        var root = new FakeVisual
        {
            Effect = new FakePortableEffectSource(PortableEffect.Blur(9.5))
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualEffects = true };
        var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(9.5f, effect.BlurRadius);
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
    public void ReplaySubtreePushesPortableShaderEffectWithoutReflectedPixelShaderShape()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 21, 34, 55, 89 };
        var shaderSource = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }";
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            shaderSource,
            shaderKey: "registered_portable_shader");

        var constants = new float[12];
        constants[8] = 0.125f;
        constants[9] = 0.25f;
        constants[10] = 0.5f;
        constants[11] = 1f;

        try
        {
            var shaderEffect = new FakePortableShaderEffectSource(new PortableShaderEffect(
                effectTypeFullName: null,
                effectTypeName: null,
                pixelShader: new PortablePixelShader(
                    uriSource: null,
                    absoluteUri: null,
                    bytecode,
                    majorVersion: 3,
                    minorVersion: 0),
                floatConstants: constants,
                samplers: new[]
                {
                    PortableShaderSampler.ImplicitInput(
                        1,
                        PortableShaderSamplingMode.NearestNeighbor)
                },
                intConstantCount: 0,
                boolConstantCount: 0,
                paddingTop: 1,
                paddingBottom: 2,
                paddingLeft: 3,
                paddingRight: 4,
                ddxUvDdyUvRegisterIndex: -1));

            var root = new FakeVisual { Effect = shaderEffect };
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeReflectionRenderer().ReplaySubtree(root, sink);

            Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
            var effect = Assert.IsType<ProGpuWpfShaderEffect>(Assert.Single(sink.VisualEffects));
            Assert.Equal(shaderSource, effect.Parameters.ShaderSource);
            Assert.Equal("registered_portable_shader", effect.Parameters.ShaderKey);
            Assert.Equal(1, effect.Parameters.SourceTextureRegisterIndex);
            Assert.Equal(ProGpuTextureSamplingMode.Nearest, effect.Parameters.SamplingMode);
            Assert.Equal(4f, effect.Padding);
            Assert.Equal(0.125f, effect.Parameters.Constants[8]);
            Assert.Equal(0.25f, effect.Parameters.Constants[9]);
            Assert.Equal(0.5f, effect.Parameters.Constants[10]);
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

    private static void AssertReplayRect(double x, double y, double width, double height, WpfReplayRect? actual)
    {
        var bounds = Assert.NotNull(actual);
        Assert.Equal(x, bounds.X);
        Assert.Equal(y, bounds.Y);
        Assert.Equal(width, bounds.Width);
        Assert.Equal(height, bounds.Height);
    }

    private static object CreateXceedDataCellVisual(Size renderSize)
    {
        var visual = Activator.CreateInstance(s_xceedDataCellType.Value)!;
        s_xceedDataCellType.Value.GetProperty("RenderSize")!.SetValue(visual, renderSize);
        return visual;
    }

    private static Type CreateXceedDataCellType()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("ProGPU.Wpf.Tests.DynamicXceed"),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Main");
        var type = module.DefineType(
            "Xceed.Wpf.DataGrid.DataCell",
            TypeAttributes.Public | TypeAttributes.Class);

        var renderSizeField = type.DefineField("_renderSize", typeof(object), FieldAttributes.Private);
        var renderSizeProperty = type.DefineProperty("RenderSize", PropertyAttributes.None, typeof(object), Type.EmptyTypes);
        var getRenderSize = type.DefineMethod(
            "get_RenderSize",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(object),
            Type.EmptyTypes);
        var getRenderSizeIl = getRenderSize.GetILGenerator();
        getRenderSizeIl.Emit(OpCodes.Ldarg_0);
        getRenderSizeIl.Emit(OpCodes.Ldfld, renderSizeField);
        getRenderSizeIl.Emit(OpCodes.Ret);

        var setRenderSize = type.DefineMethod(
            "set_RenderSize",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            null,
            new[] { typeof(object) });
        var setRenderSizeIl = setRenderSize.GetILGenerator();
        setRenderSizeIl.Emit(OpCodes.Ldarg_0);
        setRenderSizeIl.Emit(OpCodes.Ldarg_1);
        setRenderSizeIl.Emit(OpCodes.Stfld, renderSizeField);
        setRenderSizeIl.Emit(OpCodes.Ret);

        renderSizeProperty.SetGetMethod(getRenderSize);
        renderSizeProperty.SetSetMethod(setRenderSize);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        return type.CreateType();
    }

    private class FakeVisual
    {
        public FakeVisualCollection Children { get; } = new();

        public WpfVector Offset { get; init; }

        public double Opacity { get; init; } = 1;

        public object? Transform { get; init; }

        public object? Clip { get; init; }

        public object? VisualClip { get; init; }

        public object? LayoutClip { get; init; }

        public bool ClipToBounds { get; init; }

        public object? RenderSize { get; init; }

        public object? Bounds { get; init; }

        public object? OpacityMask { get; init; }

        public object? XSnappingGuidelines { get; init; }

        public object? Effect { get; init; }

        public object? BitmapEffect { get; init; }

        public object? CacheMode { get; init; }

        public object? ScrollableAreaClip { get; init; }

        public object? VisualScrollableAreaClip { get; init; }

        public object? EdgeMode { get; init; }

        public object? BitmapScalingMode { get; init; }

        public object? ClearTypeHint { get; init; }

        public object? TextRenderingMode { get; init; }

        public object? TextHintingMode { get; init; }

        public object? GetLayoutClipInternal()
        {
            return LayoutClip;
        }
    }

    private sealed class FakeDrawingVisual : FakeVisual, PortableDrawingContentSource
    {
        private readonly object? _content;

        public FakeDrawingVisual(object? content)
        {
            _content = content;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }
    }

    private sealed class FakeUiElementVisual : FakeVisual, PortableDrawingContentSource
    {
        private readonly object? _drawingContent;

        public FakeUiElementVisual(object? drawingContent)
        {
            _drawingContent = drawingContent;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _drawingContent;
            return true;
        }
    }

    private sealed class FakePortableVisualLayoutVisual : FakeVisual, PortableVisualLayoutStateSource
    {
        private readonly PortableVisualLayoutState _state;

        public FakePortableVisualLayoutVisual(PortableVisualLayoutState state)
        {
            _state = state;
        }

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            state = _state;
            return true;
        }
    }

    private sealed class FakePortableVisualStateVisual : FakeVisual, PortableVisualStateSource
    {
        private readonly PortableVisualState _state;

        public FakePortableVisualStateVisual(PortableVisualState state)
        {
            _state = state;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }
    }

    private sealed class FakePortableVisualStateAndLayoutVisual :
        FakeVisual,
        PortableVisualStateSource,
        PortableVisualLayoutStateSource
    {
        private readonly PortableVisualState _visualState;
        private readonly PortableVisualLayoutState _layoutState;

        public FakePortableVisualStateAndLayoutVisual(
            PortableVisualState visualState,
            PortableVisualLayoutState layoutState)
        {
            _visualState = visualState;
            _layoutState = layoutState;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _visualState;
            return true;
        }

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            state = _layoutState;
            return true;
        }
    }

    private sealed class FakePortableVisualStateDrawingVisual :
        FakeVisual,
        PortableVisualStateSource,
        PortableDrawingContentSource
    {
        private readonly object? _content;
        private readonly PortableVisualState _state;

        public FakePortableVisualStateDrawingVisual(object? content, PortableVisualState state)
        {
            _content = content;
            _state = state;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }
    }

    private sealed class FakeVisualOffsetDrawingVisual : PortableDrawingContentSource
    {
        private readonly object? _content;

        public FakeVisualOffsetDrawingVisual(object? content, WpfVector visualOffset)
        {
            _content = content;
            VisualOffset = visualOffset;
        }

        private WpfVector VisualOffset { get; }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }
    }

    private sealed class ThrowingPortableVisualStateDrawingVisual :
        PortableVisualStateSource,
        PortableDrawingContentSource
    {
        private readonly object? _content;
        private readonly PortableVisualState _state;

        public ThrowingPortableVisualStateDrawingVisual(object? content, PortableVisualState state)
        {
            _content = content;
            _state = state;
        }

        public int ReflectedStateProbeCount { get; private set; }

        public object? Transform => ThrowReflectedStateProbe();

        public object? VisualTransform => ThrowReflectedStateProbe();

        public object? Clip => ThrowReflectedStateProbe();

        public object? VisualClip => ThrowReflectedStateProbe();

        public object? OpacityMask => ThrowReflectedStateProbe();

        public object? ScrollableAreaClip => ThrowReflectedStateProbe();

        public object? VisualScrollableAreaClip => ThrowReflectedStateProbe();

        public object? Effect => ThrowReflectedStateProbe();

        public object? BitmapEffect => ThrowReflectedStateProbe();

        public object? BitmapEffectInput => ThrowReflectedStateProbe();

        public object? CacheMode => ThrowReflectedStateProbe();

        public object? BitmapScalingMode => ThrowReflectedStateProbe();

        public object? EdgeMode => ThrowReflectedStateProbe();

        public object? ClearTypeHint => ThrowReflectedStateProbe();

        public object? TextRenderingMode => ThrowReflectedStateProbe();

        public object? TextHintingMode => ThrowReflectedStateProbe();

        public object? XSnappingGuidelines => ThrowReflectedStateProbe();

        public object? YSnappingGuidelines => ThrowReflectedStateProbe();

        public object? VisualXSnappingGuidelines => ThrowReflectedStateProbe();

        public object? VisualYSnappingGuidelines => ThrowReflectedStateProbe();

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected state property '{propertyName}' should not be read.");
        }
    }

    private sealed class ThrowingPortableDrawingGroup : PortableDrawingGroupStateSource
    {
        private readonly PortableDrawingGroupState _state;

        public ThrowingPortableDrawingGroup(PortableDrawingGroupState state)
        {
            _state = state;
        }

        public int ReflectedStateProbeCount { get; private set; }

        public object? Bounds => ThrowReflectedStateProbe();

        public object? Transform => ThrowReflectedStateProbe();

        public object? ClipGeometry => ThrowReflectedStateProbe();

        public object? Opacity => ThrowReflectedStateProbe();

        public object? OpacityMask => ThrowReflectedStateProbe();

        public object? GuidelineSet => ThrowReflectedStateProbe();

        public object? Effect => ThrowReflectedStateProbe();

        public object? BitmapEffect => ThrowReflectedStateProbe();

        public object? BitmapEffectInput => ThrowReflectedStateProbe();

        public object? CacheMode => ThrowReflectedStateProbe();

        public object? BitmapScalingMode => ThrowReflectedStateProbe();

        public object? EdgeMode => ThrowReflectedStateProbe();

        public object? ClearTypeHint => ThrowReflectedStateProbe();

        public object? TextRenderingMode => ThrowReflectedStateProbe();

        public object? TextHintingMode => ThrowReflectedStateProbe();

        public object? Children => ThrowReflectedStateProbe();

        public bool TryGetPortableDrawingGroupState(out PortableDrawingGroupState state)
        {
            state = _state;
            return true;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected drawing group property '{propertyName}' should not be read.");
        }
    }

    private sealed class ThrowingPortableGeometryDrawing : PortableGeometryDrawingStateSource
    {
        private readonly PortableGeometryDrawingState _state;

        public ThrowingPortableGeometryDrawing(PortableGeometryDrawingState state)
        {
            _state = state;
        }

        public int ReflectedStateProbeCount { get; private set; }

        public object? Geometry => ThrowReflectedStateProbe();

        public object? Brush => ThrowReflectedStateProbe();

        public object? Pen => ThrowReflectedStateProbe();

        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = _state;
            return true;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected geometry drawing property '{propertyName}' should not be read.");
        }
    }

    private sealed class ThrowingPortableImageDrawing : PortableImageDrawingStateSource
    {
        private readonly PortableImageDrawingState _state;

        public ThrowingPortableImageDrawing(PortableImageDrawingState state)
        {
            _state = state;
        }

        public int ReflectedStateProbeCount { get; private set; }

        public object? ImageSource => ThrowReflectedStateProbe();

        public object? Rect => ThrowReflectedStateProbe();

        public bool TryGetPortableImageDrawingState(out PortableImageDrawingState state)
        {
            state = _state;
            return true;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected image drawing property '{propertyName}' should not be read.");
        }
    }

    private sealed class ThrowingPortableGlyphRunDrawing : PortableGlyphRunDrawingStateSource
    {
        private readonly PortableGlyphRunDrawingState _state;

        public ThrowingPortableGlyphRunDrawing(PortableGlyphRunDrawingState state)
        {
            _state = state;
        }

        public int ReflectedStateProbeCount { get; private set; }

        public object? GlyphRun => ThrowReflectedStateProbe();

        public object? ForegroundBrush => ThrowReflectedStateProbe();

        public bool TryGetPortableGlyphRunDrawingState(out PortableGlyphRunDrawingState state)
        {
            state = _state;
            return true;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected glyph drawing property '{propertyName}' should not be read.");
        }
    }

    private sealed class FakePortableVisualStateAndLayoutDrawingVisual :
        PortableVisualStateSource,
        PortableVisualLayoutStateSource,
        PortableDrawingContentSource
    {
        private readonly object? _content;
        private readonly PortableVisualState _visualState;
        private readonly PortableVisualLayoutState _layoutState;

        public FakePortableVisualStateAndLayoutDrawingVisual(
            object? content,
            PortableVisualState visualState,
            PortableVisualLayoutState layoutState)
        {
            _content = content;
            _visualState = visualState;
            _layoutState = layoutState;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _visualState;
            return true;
        }

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            state = _layoutState;
            return true;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
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

    private abstract class FakeProtectedVisualChildrenBase
    {
        private readonly List<object> _children = new();

        protected int VisualChildrenCount => _children.Count;

        public void AddChild(object child)
        {
            _children.Add(child);
        }

        protected object GetVisualChild(int index)
        {
            return _children[index];
        }
    }

    private sealed class FakeVisualChildrenVisual : FakeProtectedVisualChildrenBase
    {
    }

    private sealed class FakePortableVisualChildrenVisual : PortableVisualChildrenSource
    {
        private readonly List<object> _children = new();

        public void AddChild(object child)
        {
            _children.Add(child);
        }

        public bool TryGetPortableVisualChildCount(out int count)
        {
            count = _children.Count;
            return true;
        }

        public bool TryGetPortableVisualChild(int index, out object? child)
        {
            child = index >= 0 && index < _children.Count
                ? _children[index]
                : null;
            return child != null;
        }
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

    private sealed class FakeBlurEffect : PortableEffectSource
    {
        public FakeBlurEffect(double radius)
        {
            Radius = radius;
        }

        public double Radius { get; }

        public bool TryGetPortableEffect(out PortableEffect effect)
        {
            effect = PortableEffect.Blur(Radius);
            return true;
        }
    }

    private sealed class FakeDropShadowEffect : PortableEffectSource
    {
        public double BlurRadius { get; init; }

        public double ShadowDepth { get; init; }

        public double Direction { get; init; }

        public double Opacity { get; init; } = 1;

        public Color Color { get; init; } = Colors.Black;

        public bool TryGetPortableEffect(out PortableEffect effect)
        {
            effect = PortableEffect.DropShadow(
                BlurRadius,
                ShadowDepth,
                Direction,
                Opacity,
                new PortableColor(Color.A, Color.R, Color.G, Color.B));
            return true;
        }
    }

    private sealed class FakePortableEffectSource : PortableEffectSource
    {
        private readonly PortableEffect _effect;

        public FakePortableEffectSource(PortableEffect effect)
        {
            _effect = effect;
        }

        public bool TryGetPortableEffect(out PortableEffect effect)
        {
            effect = _effect;
            return true;
        }
    }

    private sealed class FakePortableShaderEffectSource : PortableShaderEffectSource
    {
        private readonly PortableShaderEffect _effect;

        public FakePortableShaderEffectSource(PortableShaderEffect effect)
        {
            _effect = effect;
        }

        public bool TryGetPortableShaderEffect(out PortableShaderEffect effect)
        {
            effect = _effect;
            return true;
        }
    }

    private sealed class FakeBlurBitmapEffect : PortableEffectSource
    {
        public FakeBlurBitmapEffect(double radius)
        {
            Radius = radius;
        }

        public double Radius { get; }

        public bool TryGetPortableEffect(out PortableEffect effect)
        {
            effect = PortableEffect.Blur(Radius);
            return true;
        }
    }

    private sealed class FakeContextBitmapEffectInput : PortableBitmapEffectInputSource
    {
        public bool TryGetPortableBitmapEffectInput(out PortableBitmapEffectInput input)
        {
            input = new PortableBitmapEffectInput(
                usesContextInput: true,
                hasDefaultAreaToApplyEffect: true);
            return true;
        }
    }

    private sealed class FakeShaderEffect : PortableShaderEffectSource
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

        public bool TryGetPortableShaderEffect(out PortableShaderEffect effect)
        {
            effect = new PortableShaderEffect(
                GetType().FullName,
                GetType().Name,
                PixelShader.TryGetPortablePixelShader(),
                CreatePortableFloatConstants(),
                CreatePortableShaderSamplers(),
                intConstantCount: 0,
                boolConstantCount: 0,
                paddingTop: 0,
                paddingBottom: 0,
                paddingLeft: 0,
                paddingRight: 0,
                ddxUvDdyUvRegisterIndex: -1);
            return true;
        }

        private float[] CreatePortableFloatConstants()
        {
            if (_floatRegisters.Count == 0)
            {
                return Array.Empty<float>();
            }

            var constants = new float[_floatRegisters.Count * 4];
            var highestRegister = -1;

            for (var i = 0; i < _floatRegisters.Count; i++)
            {
                var register = _floatRegisters[i];
                if (!register.HasValue)
                {
                    continue;
                }

                var offset = i * 4;
                constants[offset] = register.Value.r;
                constants[offset + 1] = register.Value.g;
                constants[offset + 2] = register.Value.b;
                constants[offset + 3] = register.Value.a;
                highestRegister = i;
            }

            if (highestRegister < 0)
            {
                return Array.Empty<float>();
            }

            Array.Resize(ref constants, (highestRegister + 1) * 4);
            return constants;
        }

        private PortableShaderSampler[] CreatePortableShaderSamplers()
        {
            if (_samplerData.Count == 0)
            {
                return Array.Empty<PortableShaderSampler>();
            }

            var samplers = new List<PortableShaderSampler>(_samplerData.Count);
            for (var i = 0; i < _samplerData.Count; i++)
            {
                var sampler = _samplerData[i];
                if (!sampler.HasValue || sampler.Value._brush == null)
                {
                    continue;
                }

                var samplingMode = ConvertPortableSamplingMode(sampler.Value._samplingMode);
                if (sampler.Value._brush is FakeImplicitInputBrush)
                {
                    samplers.Add(PortableShaderSampler.ImplicitInput(i, samplingMode));
                }
                else if (sampler.Value._brush is FakeShaderImageBrush imageBrush)
                {
                    samplers.Add(PortableShaderSampler.Image(i, imageBrush.ImageSource, samplingMode));
                }
                else
                {
                    samplers.Add(new PortableShaderSampler(
                        i,
                        sampler.Value._brush,
                        samplingMode));
                }
            }

            return samplers.Count == 0 ? Array.Empty<PortableShaderSampler>() : samplers.ToArray();
        }

        private static PortableShaderSamplingMode ConvertPortableSamplingMode(object? samplingMode)
        {
            return samplingMode is FakeSamplingMode.NearestNeighbor
                ? PortableShaderSamplingMode.NearestNeighbor
                : samplingMode is FakeSamplingMode.Auto
                    ? PortableShaderSamplingMode.Auto
                    : PortableShaderSamplingMode.Bilinear;
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

        public PortablePixelShader TryGetPortablePixelShader()
        {
            return new PortablePixelShader(
                UriSource?.ToString(),
                UriSource != null && UriSource.IsAbsoluteUri ? UriSource.AbsoluteUri : null,
                _shaderBytecode,
                _shaderBytecode.Length > 1 ? (short)_shaderBytecode[1] : (short)0,
                _shaderBytecode.Length > 0 ? (short)_shaderBytecode[0] : (short)0);
        }
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

    private sealed class FakeRenderData : PortableRenderDataSource
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

        public bool TryGetPortableRenderDataSnapshot(out PortableRenderDataSnapshot snapshot)
        {
            snapshot = new PortableRenderDataSnapshot(
                _buffer.AsSpan(0, _curOffset).ToArray(),
                _dependentResources.Items);
            return true;
        }
    }

    private sealed class FakeDependentResources
    {
        private readonly object?[] _items;

        public FakeDependentResources(params object?[] items)
        {
            _items = items;
        }

        public IReadOnlyList<object?> Items => _items;

        public int Count => _items.Length;

        public object? this[int index] => _items[index];
    }

    private sealed class FakeGeometryDrawing
    {
        public FakeGeometryDrawing(object geometry, object? brush, object? pen = null)
        {
            Geometry = geometry;
            Brush = brush;
            Pen = pen;
        }

        public object Geometry { get; }

        public object? Brush { get; }

        public object? Pen { get; }
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
        IWpfRetainedVisualStateSink,
        IWpfNativeTransformCommandSink,
        IWpfNativeClipCommandSink
    {
        public List<string> Operations { get; } = new();

        public List<object> VisualOwners { get; } = new();

        public List<object> VisualDependencies { get; } = new();

        public List<WpfRetainedVisualState> RetainedVisualStates { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle)> DrawRectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, MediaGeometry Geometry)> DrawGeometries { get; } = new();

        public List<(MediaImageSource ImageSource, Rect Rectangle)> Images { get; } = new();

        public List<MediaTransform> Transforms { get; } = new();

        public List<Matrix4x4> NativeTransforms { get; } = new();

        public List<MediaGeometry> Clips { get; } = new();

        public List<WpfReplayRect> NativeClips { get; } = new();

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
            Operations.Add("DrawGeometry");
            DrawGeometries.Add((brush, pen, geometry));
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

        public void PushNativeTransform(Matrix4x4 transform)
        {
            Operations.Add("PushTransform");
            NativeTransforms.Add(transform);
        }

        public void PushNativeClip(WpfReplayRect bounds)
        {
            Operations.Add("PushNativeClip");
            NativeClips.Add(bounds);
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
