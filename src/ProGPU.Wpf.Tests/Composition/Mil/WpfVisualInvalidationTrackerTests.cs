using System.Collections.Specialized;
using System.ComponentModel;
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
        Assert.Equal(1, invalidationCount);
        Assert.True(tracker.ConsumeDirty());
        Assert.False(tracker.IsDirty);
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

        public double Opacity { get; set; } = 1;

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
