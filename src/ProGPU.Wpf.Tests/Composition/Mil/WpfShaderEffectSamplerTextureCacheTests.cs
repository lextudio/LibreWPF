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

    private sealed class FakeVisual
    {
        public FakeVisual(Rect contentBounds)
        {
            ContentBounds = contentBounds;
        }

        public Rect ContentBounds { get; }
    }
}
