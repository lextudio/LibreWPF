using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using Xunit;
using ProGpuDrawingVisual = ProGPU.Scene.DrawingVisual;
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
