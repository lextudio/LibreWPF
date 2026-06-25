using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition.Mil;
using Xunit;

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
    public void EnumerateTrackedDependenciesIncludesPrivateDrawingContentGraph()
    {
        var brush = new FakeResource();
        var content = new FakeRenderContent
        {
            Brush = brush
        };
        var root = new FakeUiElementVisual(content);

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(root);

        Assert.Contains(root, dependencies);
        Assert.Contains(content, dependencies);
        Assert.Contains(brush, dependencies);
    }

    [Fact]
    public void PrivateDrawingContentChangeMarksTrackerDirty()
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

        public object? Effect { get; init; }

        public double Opacity { get; set; } = 1;

        public System.Windows.Vector VisualOffset { get; set; }

        public Rect? VisualScrollableAreaClip { get; set; }

        public void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

    private sealed class FakeUiElementVisual
    {
        private readonly object? _drawingContent;

        public FakeUiElementVisual(object? drawingContent)
        {
            _drawingContent = drawingContent;
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
