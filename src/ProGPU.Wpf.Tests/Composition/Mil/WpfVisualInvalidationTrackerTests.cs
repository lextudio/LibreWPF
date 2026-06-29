using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition.Mil;
using Xunit;
using PortablePoint = ProGPU.Wpf.Interop.PortablePoint;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;
using PortableSize = ProGPU.Wpf.Interop.PortableSize;
using PortableRenderDataSnapshot = ProGPU.Wpf.Interop.PortableRenderDataSnapshot;
using PortableRenderDataSource = ProGPU.Wpf.Interop.IPortableRenderDataSource;
using PortableDrawingContentSource = ProGPU.Wpf.Interop.IPortableDrawingContentSource;
using PortableInvalidationSource = ProGPU.Wpf.Interop.IPortableInvalidationSource;
using PortableVisualChildrenSource = ProGPU.Wpf.Interop.IPortableVisualChildrenSource;
using PortableVisualLayoutState = ProGPU.Wpf.Interop.PortableVisualLayoutState;
using PortableVisualLayoutStateSource = ProGPU.Wpf.Interop.IPortableVisualLayoutStateSource;
using PortableVisualState = ProGPU.Wpf.Interop.PortableVisualState;
using PortableVisualStateSource = ProGPU.Wpf.Interop.IPortableVisualStateSource;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfVisualInvalidationTrackerTests
{
    [Fact]
    public void AttachMarksRootDirtyAndConsumeClearsDirtyState()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        var invalidationCount = 0;
        tracker.Invalidated += (_, _) => invalidationCount++;

        tracker.Attach(root);

        Assert.Same(root, tracker.Root);
        Assert.True(tracker.IsDirty);
        Assert.True(tracker.SubscriptionCount > 0);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Equal(1, tracker.DirtySourceCount);
        Assert.Contains(root, tracker.DirtySources);
        Assert.Equal(1, invalidationCount);
        Assert.True(tracker.ConsumeDirty());
        Assert.False(tracker.IsDirty);
        Assert.Null(tracker.LastDirtySource);
        Assert.Equal(0, tracker.DirtySourceCount);
    }

    [Fact]
    public void ReflectedChangedEventMarksTrackerDirty()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.RaiseChanged();

        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);
    }

    [Fact]
    public void PortableInvalidationSourceMarksTrackerDirtyWithoutReflectedEvent()
    {
        var root = new FakePortableInvalidationResource();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.RaisePortableInvalidated();

        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);
        Assert.True(root.PortableSubscriptionCount > 0);
        Assert.Equal(0, root.ReflectedChangedSubscriptionCount);
    }

    [Fact]
    public void PropertyChangedMarksTrackerDirty()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.RaisePropertyChanged(nameof(FakeVisual.Opacity));

        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
    }

    [Fact]
    public void AttachCapturesNestedVersionSnapshots()
    {
        var root = new FakeVisual
        {
            Brush = new FakePublicVersionResource()
        };
        using var tracker = new WpfVisualInvalidationTracker();

        tracker.Attach(root);

        Assert.Equal(1, tracker.VersionSnapshotCount);
    }

    [Fact]
    public void DetectVersionChangesReturnsFalseWhenVersionsAreUnchanged()
    {
        var root = new FakeVisual
        {
            Brush = new FakePublicVersionResource()
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        Assert.False(tracker.DetectVersionChanges());
        Assert.False(tracker.IsDirty);
    }

    [Fact]
    public void PublicVersionChangeMarksTrackerDirtyWithoutEvent()
    {
        var brush = new FakePublicVersionResource();
        var root = new FakeVisual
        {
            Brush = brush
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brush.IncrementVersion();

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(brush, tracker.LastDirtySource);
        Assert.Contains(brush, tracker.DirtySources);
    }

    [Fact]
    public void VisualOffsetChangeMarksTrackerDirtyWithoutEvent()
    {
        var root = new FakeVisual
        {
            VisualOffset = new System.Windows.Vector(0, 0)
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.VisualOffset = new System.Windows.Vector(0, -120);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);
    }

    [Fact]
    public void VisualScrollableAreaClipChangeMarksTrackerDirtyWithoutEvent()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.VisualScrollableAreaClip = new Rect(0, 0, 100, 40);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);

        tracker.ConsumeDirty();
        root.VisualScrollableAreaClip = new Rect(0, 0, 100, 56);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

    }

    [Fact]
    public void VisualClipChangeMarksTrackerDirtyWithoutEvent()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.VisualClip = new Rect(0, 0, 100, 40);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);

        tracker.ConsumeDirty();
        root.VisualClip = new Rect(0, 0, 100, 56);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

    }

    [Fact]
    public void LayoutClipChangeMarksTrackerDirtyWithoutEvent()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.LayoutClip = new RectangleGeometry(new Rect(0, 0, 100, 40));

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);

        tracker.ConsumeDirty();
        root.LayoutClip = new RectangleGeometry(new Rect(0, 0, 100, 40));

        Assert.False(tracker.DetectVersionChanges());
        Assert.False(tracker.IsDirty);

        root.LayoutClip = new RectangleGeometry(new Rect(0, 0, 100, 56));

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

    }

    [Fact]
    public void PortableLayoutStateChangeMarksTrackerDirtyWithoutEvent()
    {
        var state = new PortableVisualLayoutState
        {
            HasRenderSize = true,
            RenderSize = new PortableSize(40, 20),
            HasClipToBounds = true,
            ClipToBounds = false
        };
        var root = new FakePortableLayoutVisual(state);
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        state.RenderSize = new PortableSize(41, 20);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);

        tracker.ConsumeDirty();
        state.HasLayoutClip = true;
        state.LayoutClip = new RectangleGeometry(new Rect(0, 0, 100, 40));

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.ClipToBounds = true;

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
    }

    [Fact]
    public void PortableVisualStateChangeMarksTrackerDirtyWithoutEvent()
    {
        var state = new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(0, 0),
            HasOpacity = true,
            Opacity = 1.0
        };
        var root = new FakePortableStateVisual(state);
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        state.Offset = new PortablePoint(0, -120);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);

        tracker.ConsumeDirty();
        state.HasScrollableAreaClip = true;
        state.ScrollableAreaClip = new PortableRect(0, 0, 100, 40);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasOpacityMask = true;
        state.OpacityMask = new FakeResource();

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasEffect = true;
        state.Effect = new FakeResource();

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasCacheMode = true;
        state.CacheMode = new FakeResource();

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasBitmapScalingMode = true;
        state.BitmapScalingMode = "NearestNeighbor";

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasTextRenderingMode = true;
        state.TextRenderingMode = "ClearType";

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasSnappingGuidelinesX = true;
        state.SnappingGuidelinesX = new[] { 10d, 20d };

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
    }

    [Fact]
    public void ClipToBoundsChangeMarksTrackerDirtyWithoutEvent()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.ClipToBounds = true;

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);
    }

    [Fact]
    public void PrivateVersionFieldChangeMarksTrackerDirtyWithoutEvent()
    {
        var brush = new FakePrivateVersionResource();
        var root = new FakeVisual
        {
            Brush = brush
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brush.IncrementVersion();

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(brush, tracker.LastDirtySource);
    }

    [Fact]
    public void CollectionChangeRefreshesSubscriptionsForNewChildren()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        var child = new FakeVisual();
        root.Children.Add(child);

        Assert.True(tracker.IsDirty);
        Assert.Same(root.Children, tracker.LastDirtySource);
        tracker.ConsumeDirty();

        child.RaiseChanged();

        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void PortableVisualChildrenChangeMarksTrackerDirtyWithoutReflectedChildrenCollection()
    {
        var root = new FakePortableVisualChildrenOnly();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        var child = new FakeVisual();
        root.AddChild(child);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);
        tracker.ConsumeDirty();

        child.RaiseChanged();

        Assert.True(tracker.IsDirty);
        Assert.Same(child, tracker.LastDirtySource);
    }

    [Fact]
    public void DrawingForegroundBrushChangeMarksTrackerDirty()
    {
        var brush = new FakeResource();
        var root = new FakeVisual
        {
            Drawing = new FakeGlyphRunDrawing
            {
                ForegroundBrush = brush
            }
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brush.RaiseChanged();

        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void VisualBrushVisualChangeMarksTrackerDirty()
    {
        var brushVisual = new FakeVisual();
        var root = new FakeVisual
        {
            Brush = new FakeVisualBrush
            {
                Visual = brushVisual
            }
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brushVisual.RaiseChanged();

        Assert.True(tracker.IsDirty);
        Assert.Same(brushVisual, tracker.LastDirtySource);
        Assert.Contains(brushVisual, tracker.DirtySources);
    }

    [Fact]
    public void VisualEffectChangeMarksTrackerDirty()
    {
        var effect = new FakeResource();
        var root = new FakeVisual
        {
            Effect = effect
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        effect.RaiseChanged();

        Assert.True(tracker.IsDirty);
        Assert.Same(effect, tracker.LastDirtySource);
        Assert.Contains(effect, tracker.DirtySources);
    }

    [Fact]
    public void EnumerateTrackedDependenciesIncludesNestedResourceGraph()
    {
        var brush = new FakeResource();
        var drawing = new FakeGlyphRunDrawing
        {
            ForegroundBrush = brush
        };

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(drawing);

        Assert.Equal(new object[] { drawing, brush }, dependencies);
    }

    [Fact]
    public void EnumerateTrackedDependenciesIgnoresNonPortablePrivateDrawingContentGraph()
    {
        var brush = new FakeResource();
        var content = new FakeRenderContent
        {
            Brush = brush
        };
        var root = new FakeUiElementVisual(content);

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(root);

        Assert.Contains(root, dependencies);
        Assert.DoesNotContain(content, dependencies);
        Assert.DoesNotContain(brush, dependencies);
    }

    [Fact]
    public void EnumerateTrackedDependenciesUsesPortableDrawingAndRenderDataSources()
    {
        var brush = new FakeResource();
        var renderData = new FakePortableRenderDataSource(new object?[] { brush });
        var root = new FakePortableDrawingVisual(renderData);

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(root);

        Assert.Contains(root, dependencies);
        Assert.Contains(renderData, dependencies);
        Assert.Contains(brush, dependencies);
        Assert.Equal(1, root.ContentReadCount);
        Assert.Equal(1, renderData.SnapshotReadCount);
    }

    [Fact]
    public void EnumerateTrackedDependenciesUsesPortableVisualStateResources()
    {
        var transform = new FakeResource();
        var clip = new FakeResource();
        var effect = new FakeResource();
        var cacheMode = new FakeResource();
        var root = new FakePortableStateVisual(new PortableVisualState
        {
            HasTransform = true,
            Transform = transform,
            HasClip = true,
            Clip = clip,
            HasEffect = true,
            Effect = effect,
            HasCacheMode = true,
            CacheMode = cacheMode
        });

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(root);

        Assert.Contains(root, dependencies);
        Assert.Contains(transform, dependencies);
        Assert.Contains(clip, dependencies);
        Assert.Contains(effect, dependencies);
        Assert.Contains(cacheMode, dependencies);
    }

    [Fact]
    public void NonPortablePrivateDrawingContentChangeDoesNotMarkTrackerDirty()
    {
        var brush = new FakeResource();
        var root = new FakeUiElementVisual(new FakeRenderContent
        {
            Brush = brush
        });
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brush.RaiseChanged();

        Assert.False(tracker.IsDirty);
        Assert.Null(tracker.LastDirtySource);
        Assert.DoesNotContain(brush, tracker.DirtySources);
    }

    [Fact]
    public void PortableDrawingRenderDataDependencyChangeMarksTrackerDirty()
    {
        var brush = new FakeResource();
        var renderData = new FakePortableRenderDataSource(new object?[] { brush });
        var root = new FakePortableDrawingVisual(renderData);
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brush.RaiseChanged();

        Assert.True(tracker.IsDirty);
        Assert.Same(brush, tracker.LastDirtySource);
        Assert.Contains(brush, tracker.DirtySources);
    }

    [Fact]
    public void EnumerateTrackedDependenciesIncludesGradientStopGraph()
    {
        var firstStop = new GradientStop(Colors.Red, 0);
        var secondStop = new GradientStop(Colors.Blue, 1);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                firstStop,
                secondStop
            }
        };

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(brush);

        Assert.Contains(brush, dependencies);
        Assert.Contains(brush.GradientStops, dependencies);
        Assert.Contains(firstStop, dependencies);
        Assert.Contains(secondStop, dependencies);
    }

    [Fact]
    public void GradientStopChangeMarksTrackerDirty()
    {
        var stop = new GradientStop(Colors.Red, 0);
        var brush = new LinearGradientBrush
        {
            GradientStops = new GradientStopCollection
            {
                stop,
                new GradientStop(Colors.Blue, 1)
            }
        };
        var root = new FakeVisual
        {
            Brush = brush
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        stop.Offset = 0.25;

        Assert.True(tracker.IsDirty);
        Assert.Contains(stop, tracker.DirtySources);
    }

    [Fact]
    public void AttachSkipsFrozenFreezableChangedSubscription()
    {
        var transform = new FakeFrozenFreezableLikeResource();
        using var tracker = new WpfVisualInvalidationTracker();

        var exception = Record.Exception(() => tracker.Attach(transform));

        Assert.Null(exception);
        Assert.Same(transform, tracker.Root);
        Assert.True(tracker.IsDirty);
        Assert.Equal(0, tracker.SubscriptionCount);
    }

    [Fact]
    public void GradientStopCollectionChangeRefreshesSubscriptionsForNewStops()
    {
        var brush = new LinearGradientBrush
        {
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Colors.Red, 0)
            }
        };
        var root = new FakeVisual
        {
            Brush = brush
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        var addedStop = new GradientStop(Colors.Green, 0.5);
        brush.GradientStops.Add(addedStop);

        Assert.True(tracker.IsDirty);
        Assert.Contains(brush.GradientStops, tracker.DirtySources);
        tracker.ConsumeDirty();

        addedStop.Color = Colors.Yellow;

        Assert.True(tracker.IsDirty);
        Assert.Contains(addedStop, tracker.DirtySources);
    }

    [Fact]
    public void PathGeometryCollectionChangesRefreshNestedSegmentSubscriptions()
    {
        var geometry = new FakePathGeometry();
        var root = new FakeVisual
        {
            Clip = geometry
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        var figure = new FakePathFigure();
        geometry.Figures.Add(figure);

        Assert.True(tracker.IsDirty);
        tracker.ConsumeDirty();

        var segment = new FakeResource();
        figure.Segments.Add(segment);

        Assert.True(tracker.IsDirty);
        tracker.ConsumeDirty();

        segment.RaiseChanged();

        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void DetachUnsubscribesTrackedSources()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        tracker.Detach();
        root.RaiseChanged();

        Assert.Null(tracker.Root);
        Assert.False(tracker.IsDirty);
        Assert.Equal(0, tracker.SubscriptionCount);
    }

    private sealed class FakeVisual : INotifyPropertyChanged
    {
        public event EventHandler? Changed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public FakeVisualCollection Children { get; } = new();

        public object? Drawing { get; init; }

        public object? Brush { get; init; }

        public object? Clip { get; init; }

        public object? VisualClip { get; set; }

        public object? LayoutClip { get; set; }

        public bool ClipToBounds { get; set; }

        public object? Effect { get; init; }

        public double Opacity { get; set; } = 1;

        public System.Windows.Vector VisualOffset { get; set; }

        public Rect? VisualScrollableAreaClip { get; set; }

        private object? GetLayoutClipInternal()
        {
            return LayoutClip;
        }

        public void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class FakePortableLayoutVisual : PortableVisualLayoutStateSource
    {
        private readonly PortableVisualLayoutState _state;

        public FakePortableLayoutVisual(PortableVisualLayoutState state)
        {
            _state = state;
        }

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            state = _state;
            return true;
        }
    }

    private sealed class FakePortableStateVisual : PortableVisualStateSource
    {
        private readonly PortableVisualState _state;

        public FakePortableStateVisual(PortableVisualState state)
        {
            _state = state;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }
    }

    private sealed class FakeGlyphRunDrawing
    {
        public object? ForegroundBrush { get; init; }
    }

    private sealed class FakeVisualBrush
    {
        public object? Visual { get; init; }
    }

    private sealed class FakePortableVisualChildrenOnly : PortableVisualChildrenSource
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
            child = _children[index];
            return true;
        }
    }

    private sealed class FakeUiElementVisual
    {
        private readonly object? _drawingContent;

        public FakeUiElementVisual(object? drawingContent)
        {
            _drawingContent = drawingContent;
        }
    }

    private sealed class FakePortableDrawingVisual : PortableDrawingContentSource
    {
        private readonly object? _drawingContent;

        public FakePortableDrawingVisual(object? drawingContent)
        {
            _drawingContent = drawingContent;
        }

        public int ContentReadCount { get; private set; }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            ContentReadCount++;
            content = _drawingContent;
            return true;
        }
    }

    private sealed class FakePortableRenderDataSource : PortableRenderDataSource
    {
        private readonly IReadOnlyList<object?> _dependentResources;

        public FakePortableRenderDataSource(IReadOnlyList<object?> dependentResources)
        {
            _dependentResources = dependentResources;
        }

        public int SnapshotReadCount { get; private set; }

        public bool TryGetPortableRenderDataSnapshot(out PortableRenderDataSnapshot snapshot)
        {
            SnapshotReadCount++;
            snapshot = new PortableRenderDataSnapshot(Array.Empty<byte>(), _dependentResources);
            return true;
        }
    }

    private sealed class FakeRenderContent
    {
        public object? Brush { get; init; }
    }

    private sealed class FakePathGeometry
    {
        public FakeVisualCollection Figures { get; } = new();
    }

    private sealed class FakePathFigure
    {
        public FakeVisualCollection Segments { get; } = new();
    }

    private sealed class FakeResource
    {
        public event EventHandler? Changed;

        public void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakePortableInvalidationResource : PortableInvalidationSource
    {
        private EventHandler? _portableInvalidated;

        public event EventHandler? Changed
        {
            add => ReflectedChangedSubscriptionCount++;
            remove => ReflectedChangedUnsubscriptionCount++;
        }

        public int PortableSubscriptionCount { get; private set; }

        public int ReflectedChangedSubscriptionCount { get; private set; }

        public int ReflectedChangedUnsubscriptionCount { get; private set; }

        public bool TrySubscribeInvalidated(EventHandler handler, out IDisposable subscription)
        {
            PortableSubscriptionCount++;
            _portableInvalidated += handler;
            subscription = new Subscription(() => _portableInvalidated -= handler);
            return true;
        }

        public void RaisePortableInvalidated()
        {
            _portableInvalidated?.Invoke(this, EventArgs.Empty);
        }

        private sealed class Subscription : IDisposable
        {
            private Action? _unsubscribe;

            public Subscription(Action unsubscribe)
            {
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                var unsubscribe = _unsubscribe;
                _unsubscribe = null;
                unsubscribe?.Invoke();
            }
        }
    }

    private sealed class FakeFrozenFreezableLikeResource
    {
        public event EventHandler? Changed
        {
            add => throw new InvalidOperationException("Specified value must have IsFrozen set to false to modify.");
            remove => throw new InvalidOperationException("Specified value must have IsFrozen set to false to modify.");
        }
    }

    private sealed class FakePublicVersionResource
    {
        public int Version { get; private set; }

        public void IncrementVersion()
        {
            Version++;
        }
    }

    private sealed class FakePrivateVersionResource
    {
        private uint _version;

        public void IncrementVersion()
        {
            _version++;
        }
    }

    private sealed class FakeVisualCollection : INotifyCollectionChanged
    {
        private readonly List<object> _items = new();

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public int Count => _items.Count;

        public object this[int index] => _items[index];

        public void Add(object item)
        {
            _items.Add(item);
            CollectionChanged?.Invoke(
                this,
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
        }
    }
}
