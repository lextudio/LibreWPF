using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfShaderEffectSamplerTextureCacheTests
{
    [Fact]
    public void TryGetBrushSourceBoundsResolvesDrawingBrushRelativeViewbox()
    {
        var brush = new FakeDrawingBrush(new FakeDrawing(new Rect(10, 20, 200, 100)))
        {
            Viewbox = new Rect(0.25, 0.2, 0.5, 0.4),
            ViewboxUnits = "RelativeToBoundingBox"
        };

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(60, 40, 100, 40), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsResolvesVisualBrushRelativeViewbox()
    {
        var brush = new FakeVisualBrush(new FakeVisual(new Rect(4, 8, 80, 40)))
        {
            Viewbox = new Rect(0.5, 0.25, 0.25, 0.5),
            ViewboxUnits = "RelativeToBoundingBox"
        };

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(44, 18, 20, 20), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsKeepsAbsoluteViewboxPrecedence()
    {
        var brush = new FakeDrawingBrush(new FakeDrawing(new Rect(10, 20, 200, 100)))
        {
            Viewbox = new Rect(2, 3, 4, 5),
            ViewboxUnits = "Absolute"
        };

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(2, 3, 4, 5), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsUsesGeometryDrawingBoundsWhenDrawingBoundsAreAbsent()
    {
        var brush = new FakeDrawingBrush(
            new FakeGeometryDrawing(
                new FakeRectangleGeometry(new FakeRect(5, 6, 70, 80))));

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(5, 6, 70, 80), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsInfersDrawingGroupBoundsFromChildren()
    {
        var brush = new FakeDrawingBrush(
            new FakeDrawingGroup(
                new FakeGeometryDrawing(
                    new FakeRectangleGeometry(new FakeRect(2, 3, 10, 20))),
                new FakeGeometryDrawing(
                    new FakeRectangleGeometry(new FakeRect(20, 7, 5, 8)))));

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(2, 3, 23, 20), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsUsesVisualDescendantBoundsBeforeRenderSizeFallback()
    {
        var visual = new FakeVisualWithDescendantBounds(
            new Rect(3, 4, 30, 40),
            new FakeSize(300, 200));
        var brush = new FakeVisualBrush(visual);

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(3, 4, 30, 40), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsKeepsDesiredSizeVisualFallback()
    {
        var brush = new FakeVisualBrush(new FakeDesiredSizeVisual(new FakeSize(64, 32)));

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(0, 0, 64, 32), bounds);
    }

    private sealed class FakeDrawingBrush
    {
        public FakeDrawingBrush(object? drawing)
        {
            Drawing = drawing;
        }

        public object? Drawing { get; }

        public Rect Viewbox { get; init; }

        public string ViewboxUnits { get; init; } = "RelativeToBoundingBox";
    }

    private sealed class FakeVisualBrush
    {
        public FakeVisualBrush(object? visual)
        {
            Visual = visual;
        }

        public object? Visual { get; }

        public Rect Viewbox { get; init; }

        public string ViewboxUnits { get; init; } = "RelativeToBoundingBox";
    }

    private sealed class FakeDrawing
    {
        public FakeDrawing(Rect bounds)
        {
            Bounds = bounds;
        }

        public Rect Bounds { get; }
    }

    private sealed class FakeGeometryDrawing
    {
        public FakeGeometryDrawing(object? geometry)
        {
            Geometry = geometry;
        }

        public object? Geometry { get; }
    }

    private sealed class FakeDrawingGroup
    {
        public FakeDrawingGroup(params object[] children)
        {
            Children = new FakeDrawingCollection(children);
        }

        public FakeDrawingCollection Children { get; }
    }

    private sealed class FakeDrawingCollection
    {
        private readonly object[] _items;

        public FakeDrawingCollection(object[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public object this[int index] => _items[index];
    }

    private sealed class FakeRectangleGeometry
    {
        public FakeRectangleGeometry(FakeRect rect)
        {
            Rect = rect;
        }

        public FakeRect Rect { get; }
    }

    private readonly record struct FakeRect(double X, double Y, double Width, double Height);

    private sealed class FakeVisual
    {
        public FakeVisual(Rect contentBounds)
        {
            ContentBounds = contentBounds;
        }

        public Rect ContentBounds { get; }
    }

    private sealed class FakeVisualWithDescendantBounds
    {
        public FakeVisualWithDescendantBounds(Rect descendantBounds, FakeSize renderSize)
        {
            DescendantBounds = descendantBounds;
            RenderSize = renderSize;
        }

        public Rect DescendantBounds { get; }

        public FakeSize RenderSize { get; }
    }

    private sealed class FakeDesiredSizeVisual
    {
        public FakeDesiredSizeVisual(FakeSize desiredSize)
        {
            DesiredSize = desiredSize;
        }

        public FakeSize DesiredSize { get; }
    }

    private readonly record struct FakeSize(double Width, double Height);
}
