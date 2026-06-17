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
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;
using ProGpuBlurEffect = ProGPU.Scene.BlurEffect;
using ProGpuEffectBase = ProGPU.Scene.EffectBase;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfCompositionDrawingContextTests
{
    [Fact]
    public void DrawCallsForwardDirectlyToCompositionSink()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);

        context.DrawLine(pen, new Point(1, 2), new Point(3, 4));
        context.DrawRectangle(Brushes.Red, pen, new Rect(5, 6, 7, 8));
        context.DrawEllipse(Brushes.Blue, null, new Point(9, 10), 11, 12);

        Assert.Equal(new[] { "DrawLine", "DrawRectangle", "DrawEllipse" }, sink.Operations);
        Assert.Equal((pen, new Point(1, 2), new Point(3, 4)), sink.Lines.Single());
        Assert.Equal((Brushes.Red, pen, new Rect(5, 6, 7, 8)), sink.Rectangles.Single());
        Assert.Equal((Brushes.Blue, null, new Point(9, 10), 11d, 12d), sink.Ellipses.Single());
        Assert.Equal(new WpfCompositionDrawingContextResult(3, 3, 0), context.Result);
    }

    [Fact]
    public void AnimatedDrawOverloadsForwardBaseValuesAndCountUnsupportedAnimationState()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var image = new FakeImageSource();
        var animation = new object();

        context.DrawLine(pen, new Point(1, 2), animation, new Point(3, 4), null);
        context.DrawRectangle(Brushes.Red, pen, new Rect(5, 6, 7, 8), animation);
        context.DrawRoundedRectangle(Brushes.Green, pen, new Rect(9, 10, 11, 12), null, 2, animation, 3, animation);
        context.DrawEllipse(Brushes.Blue, null, new Point(13, 14), animation, 15, null, 16, animation);
        context.DrawImage(image, new Rect(17, 18, 19, 20), animation);

        Assert.Equal(new[]
        {
            "DrawLine",
            "DrawRectangle",
            "DrawRoundedRectangle",
            "DrawEllipse",
            "DrawImage"
        }, sink.Operations);
        Assert.Equal((pen, new Point(1, 2), new Point(3, 4)), sink.Lines.Single());
        Assert.Equal((Brushes.Red, pen, new Rect(5, 6, 7, 8)), sink.Rectangles.Single());
        Assert.Equal((Brushes.Green, pen, new Rect(9, 10, 11, 12), 2d, 3d), sink.RoundedRectangles.Single());
        Assert.Equal((Brushes.Blue, null, new Point(13, 14), 15d, 16d), sink.Ellipses.Single());
        Assert.Equal((image, new Rect(17, 18, 19, 20)), sink.Images.Single());
        Assert.Equal(new WpfCompositionDrawingContextResult(5, 5, 7), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextAdaptsReflectedPrimitiveValues()
    {
        var sink = new RecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);

        context.DrawLine(pen, new FakePoint(1, 2), new FakePoint(3, 4));
        context.DrawRectangle(Brushes.Red, pen, new FakeRect(5, 6, 7, 8));
        context.DrawRoundedRectangle(Brushes.Green, pen, new FakeRect(9, 10, 11, 12), 2, 3);
        context.DrawEllipse(Brushes.Blue, null, new FakePoint(13, 14), 15, 16);

        Assert.Equal(new[]
        {
            "DrawLine",
            "DrawRectangle",
            "DrawRoundedRectangle",
            "DrawEllipse"
        }, sink.Operations);
        Assert.Equal((pen, new Point(1, 2), new Point(3, 4)), sink.Lines.Single());
        Assert.Equal((Brushes.Red, pen, new Rect(5, 6, 7, 8)), sink.Rectangles.Single());
        Assert.Equal((Brushes.Green, pen, new Rect(9, 10, 11, 12), 2d, 3d), sink.RoundedRectangles.Single());
        Assert.Equal((Brushes.Blue, null, new Point(13, 14), 15d, 16d), sink.Ellipses.Single());
        Assert.Equal(new WpfCompositionDrawingContextResult(4, 4, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextUsesImageSourceAdapter()
    {
        var sink = new RecordingSink();
        var imageSource = new FakeBitmapSource();
        var adapter = new FakeImageSourceAdapter();
        using var context = new WpfObjectRenderDataDrawingContext(sink, adapter);

        context.DrawImage(imageSource, new FakeRect(17, 18, 19, 20));

        Assert.Same(imageSource, adapter.LastImageSource);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(adapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(17, 18, 19, 20), replayed.Rectangle);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextReplaysImageBrushRectangleThroughImageSourceAdapter()
    {
        var sink = new RecordingSink();
        var imageSource = new FakeBitmapSource();
        var imageBrush = new FakeImageBrush(imageSource);
        var adapter = new FakeImageSourceAdapter();
        using var context = new WpfObjectRenderDataDrawingContext(sink, adapter);

        context.DrawRectangle(imageBrush, null, new FakeRect(1, 2, 30, 40));

        Assert.Equal(new[] { "PushClip", "DrawImage", "Pop" }, sink.Operations);
        Assert.Same(imageSource, adapter.LastImageSource);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(adapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(1, 2, 30, 40), replayed.Rectangle);
        Assert.Contains(imageBrush, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextReplaysDrawingBrushRectangleThroughSharedTileBrushReplay()
    {
        var sink = new RecordingSink();
        var nestedDrawing = new FakeGeometryDrawing(
            Brushes.Red,
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawingBrush = new FakeDrawingBrush(nestedDrawing);
        using var context = new WpfObjectRenderDataDrawingContext(sink);

        context.DrawRectangle(drawingBrush, null, new FakeRect(1, 2, 30, 40));

        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawGeometry", "Pop", "Pop" }, sink.Operations);
        var replayed = Assert.Single(sink.Geometries);
        Assert.Same(Brushes.Red, replayed.Brush);
        Assert.IsType<PathGeometry>(replayed.Geometry);
        Assert.Contains(drawingBrush, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextCountsPartialDrawingBrushReplayAsUnsupported()
    {
        var sink = new RecordingSink();
        var nestedGroup = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                Brushes.Red,
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10))),
            new object());
        var drawingBrush = new FakeDrawingBrush(nestedGroup);
        using var context = new WpfObjectRenderDataDrawingContext(sink);

        context.DrawRectangle(drawingBrush, null, new FakeRect(1, 2, 30, 40));

        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawGeometry", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Geometries);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 1), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextRegistersAppliedResourcesAsRetainedDependencies()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new RectangleGeometry(new Rect(1, 2, 30, 40));
        var image = new FakeImageSource();
        var transform = new MatrixTransform(new Matrix { M11 = 1, M22 = 1, OffsetX = 3, OffsetY = 4 });
        var guidelines = new FakeGuidelineSet(Array.Empty<double>(), new[] { 2d });
        var drawing = new FakeGeometryDrawing(
            Brushes.Blue,
            null,
            new FakeRectangleGeometry(new FakeRect(5, 6, 7, 8)));

        context.DrawRectangle(Brushes.Red, pen, new Rect(1, 2, 3, 4));
        context.DrawGeometry(Brushes.Green, null, geometry);
        context.DrawImage(image, new Rect(5, 6, 7, 8));
        context.PushClip(geometry);
        context.PushOpacityMask(Brushes.Yellow, new Rect(0, 0, 10, 10));
        context.PushTransform(transform);
        context.PushGuidelineSet(guidelines);
        _ = context.DrawDrawing(drawing);

        Assert.Contains(Brushes.Red, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(Brushes.Green, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Contains(image, sink.VisualDependencies);
        Assert.Contains(Brushes.Yellow, sink.VisualDependencies);
        Assert.Contains(transform, sink.VisualDependencies);
        Assert.Contains(guidelines, sink.VisualDependencies);
        Assert.Contains(drawing, sink.VisualDependencies);
        Assert.Contains(Brushes.Blue, sink.VisualDependencies);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextRegistersOriginalResourcesAsRetainedDependencies()
    {
        var sink = new RecordingSink();
        var adapter = new FakeImageSourceAdapter();
        using var context = new WpfObjectRenderDataDrawingContext(sink, adapter);
        var brush = Brushes.Red;
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new RectangleGeometry(new Rect(1, 2, 30, 40));
        var imageSource = new FakeBitmapSource();
        var transform = new MatrixTransform(new Matrix { M11 = 1, M22 = 1, OffsetX = 3, OffsetY = 4 });
        var guidelines = new FakeGuidelineSet(Array.Empty<double>(), new[] { 2d });
        var drawing = new FakeGeometryDrawing(
            Brushes.Blue,
            null,
            new FakeRectangleGeometry(new FakeRect(5, 6, 7, 8)));

        context.DrawRectangle(brush, pen, new FakeRect(1, 2, 3, 4));
        context.DrawGeometry(Brushes.Green, null, geometry);
        context.DrawImage(imageSource, new FakeRect(5, 6, 7, 8));
        context.PushClip(geometry);
        context.PushOpacityMask(Brushes.Yellow);
        context.PushTransform(transform);
        context.PushGuidelineSet(guidelines);
        context.DrawDrawing(drawing);

        Assert.Contains(brush, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(Brushes.Green, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Contains(imageSource, sink.VisualDependencies);
        Assert.DoesNotContain(adapter.AdaptedImageSource, sink.VisualDependencies);
        Assert.Contains(Brushes.Yellow, sink.VisualDependencies);
        Assert.Contains(transform, sink.VisualDependencies);
        Assert.Contains(guidelines, sink.VisualDependencies);
        Assert.Contains(drawing, sink.VisualDependencies);
        Assert.Contains(Brushes.Blue, sink.VisualDependencies);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextRegistersGradientStopGraphAsRetainedDependencies()
    {
        var sink = new RecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
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

        context.DrawRectangle(brush, null, new Rect(1, 2, 30, 40));

        Assert.Contains(brush, sink.VisualDependencies);
        Assert.Contains(brush.GradientStops, sink.VisualDependencies);
        Assert.Contains(firstStop, sink.VisualDependencies);
        Assert.Contains(secondStop, sink.VisualDependencies);
    }

    [Fact]
    public void GeneratedNoOpDrawGuardsDoNotForwardOrCountOperations()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);

        context.DrawLine(null, new Point(1, 2), new Point(3, 4));
        context.DrawRectangle(null, null, new Rect(5, 6, 7, 8));
        context.DrawRoundedRectangle(null, null, new Rect(9, 10, 11, 12), 2, 3);
        context.DrawEllipse(null, null, new Point(13, 14), 15, 16);
        context.DrawGeometry(Brushes.Red, null, null);
        context.DrawGeometry(null, null, new PathGeometry());
        context.DrawImage(null, new Rect(17, 18, 19, 20));
        context.DrawGlyphRun(null, null);
        context.DrawVideo(null, new Rect(21, 22, 23, 24));

        Assert.Empty(sink.Operations);
        Assert.Equal(default, context.Result);
    }

    [Fact]
    public void AnimatedNoOpDrawGuardsDoNotCountUnsupportedAnimationState()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var animation = new object();

        context.DrawLine(null, new Point(1, 2), animation, new Point(3, 4), animation);
        context.DrawRectangle(null, null, new Rect(5, 6, 7, 8), animation);
        context.DrawRoundedRectangle(null, null, new Rect(9, 10, 11, 12), animation, 2, animation, 3, animation);
        context.DrawEllipse(null, null, new Point(13, 14), animation, 15, animation, 16, animation);
        context.DrawImage(null, new Rect(17, 18, 19, 20), animation);
        context.DrawVideo(null, new Rect(21, 22, 23, 24), animation);

        Assert.Empty(sink.Operations);
        Assert.Equal(default, context.Result);
    }

    [Fact]
    public void DrawDrawingReplaysWpfShapedGeometryDrawing()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var drawing = new FakeGeometryDrawing(
            Brushes.Red,
            null,
            new FakeRectangleGeometry(new FakeRect(1, 2, 30, 40)));

        var status = context.DrawDrawing(drawing);

        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        var replayed = Assert.Single(sink.Geometries);
        Assert.Same(Brushes.Red, replayed.Brush);
        Assert.IsType<PathGeometry>(replayed.Geometry);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DrawDrawingReplaysImageDrawingWithImageSourceAdapter()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var imageSource = new FakeBitmapSource();
        var adapter = new FakeImageSourceAdapter();
        var drawing = new FakeImageDrawing(imageSource, new FakeRect(3, 4, 50, 60));

        var status = context.DrawDrawing(drawing, adapter);

        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        Assert.Same(imageSource, adapter.LastImageSource);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(adapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(3, 4, 50, 60), replayed.Rectangle);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DrawDrawingCountsUnsupportedAndSkippedReplayStatus()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);

        var unsupported = context.DrawDrawing(new object());
        var skipped = context.DrawDrawing(null);

        Assert.Equal(WpfDrawingReplayStatus.Unsupported, unsupported);
        Assert.Equal(WpfDrawingReplayStatus.Skipped, skipped);
        Assert.Empty(sink.Operations);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 0, 1), context.Result);
    }

    [Fact]
    public void DrawDrawingCountsPartiallyReplayedDrawingGroupAsAppliedAndUnsupported()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var drawing = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                Brushes.Red,
                null,
                new FakeRectangleGeometry(new FakeRect(1, 2, 30, 40))),
            new object());

        var status = context.DrawDrawing(drawing);

        Assert.Equal(WpfDrawingReplayStatus.PartiallyApplied, status);
        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Single(sink.Geometries);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 1), context.Result);
    }

    [Fact]
    public void PushesAndPopsTrackNestingAndAutoBalanceOnClose()
    {
        var sink = new RecordingSink();
        var context = new WpfCompositionDrawingContext(sink);
        var transform = new MatrixTransform(new Matrix { M11 = 1, M22 = 1, OffsetX = 4, OffsetY = 5 });

        context.PushOpacity(0.5);
        context.PushTransform(transform);
        context.PushGuidelineY1(10);

        Assert.Equal(3, context.StackDepth);

        context.Pop();

        Assert.Equal(2, context.StackDepth);

        context.Close();

        Assert.Equal(0, context.StackDepth);
        Assert.Equal(new[]
        {
            "PushOpacity",
            "PushTransform",
            "PushGuidelineY1",
            "Pop",
            "Pop",
            "Pop",
            "Close"
        }, sink.Operations);
        Assert.Equal(new WpfCompositionDrawingContextResult(6, 6, 0), context.Result);
    }

    [Fact]
    public void NullGeneratedPushResourcesPreserveScopeBalanceAsNoOpScopes()
    {
        var sink = new RecordingSink();
        var context = new WpfCompositionDrawingContext(sink);

        context.PushClip(null);
        context.PushTransform(null);
        context.PushOpacityMask(null);

        Assert.Equal(3, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(3, 3, 0), context.Result);

        context.Close();

        Assert.Equal(new[]
        {
            "PushNoOpScope",
            "PushNoOpScope",
            "PushNoOpScope",
            "Pop",
            "Pop",
            "Pop",
            "Close"
        }, sink.Operations);
        Assert.Equal(new WpfCompositionDrawingContextResult(6, 6, 0), context.Result);
    }

    [Fact]
    public void DynamicGuidelineSetWithOneYGuidelineUsesGuidelineY1Scope()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);

        context.PushGuidelineSet(new FakeGuidelineSet(Array.Empty<double>(), new[] { 12.5 }));

        Assert.Equal(new[] { "PushGuidelineY1" }, sink.Operations);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DynamicGuidelineSetWithTwoYGuidelinesUsesGuidelineY2Scope()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);

        context.PushGuidelineSet(new FakeGuidelineSet(Array.Empty<double>(), new[] { 10.0, 12.25 }));

        Assert.Equal(new[] { "PushGuidelineY2" }, sink.Operations);
        Assert.Equal((10.0, 2.25), sink.GuidelineY2Values.Single());
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DynamicGuidelineSetThatCannotUseFastPathStillPushesGuidelineSetScope()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);

        context.PushGuidelineSet(new FakeGuidelineSet(new[] { 1.0 }, new[] { 2.0 }));

        Assert.Equal(new[] { "PushGuidelineSet" }, sink.Operations);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DynamicGuidelineSetWithXAndYGuidelinesSnapsPrimitiveThroughProGpuSink()
    {
        var nativeContext = new global::ProGPU.Scene.DrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));
        using var context = new WpfCompositionDrawingContext(sink);

        context.PushGuidelineSet(new FakeGuidelineSet(new[] { 2.25, 42.25 }, new[] { 3.25, 53.25 }));
        context.DrawRectangle(Brushes.Red, null, new Rect(2.25, 3.25, 40, 50));
        context.Pop();

        Assert.Equal(new WpfCompositionDrawingContextResult(3, 3, 0), context.Result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(global::ProGPU.Scene.RenderCommandType.DrawRect, command.Type);
        Assert.Equal(2, command.Rect.X);
        Assert.Equal(3, command.Rect.Y);
        Assert.Equal(40, command.Rect.Width);
        Assert.Equal(50, command.Rect.Height);
    }

    [Fact]
    public void AnimatedPushOpacityForwardsBaseOpacityAndCountsUnsupportedAnimationState()
    {
        var sink = new RecordingSink();
        var context = new WpfCompositionDrawingContext(sink);

        context.PushOpacity(0.5, new object());

        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 1), context.Result);

        context.Close();

        Assert.Equal(new[] { "PushOpacity", "Pop", "Close" }, sink.Operations);
        Assert.Equal(0.5, sink.Opacities.Single());
        Assert.Equal(new WpfCompositionDrawingContextResult(2, 2, 1), context.Result);
    }

    [Fact]
    public void UnsupportedVideoAndEffectAreCountedWithoutSilentlyDroppingScopeBalance()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);

        context.DrawVideo(player: new object(), new Rect(0, 0, 10, 20));
        context.PushEffect(effect: new object(), effectInput: null);

        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(2, 1, 2), context.Result);
        Assert.Equal(new[] { "PushNoOpScope" }, sink.Operations);

        context.Pop();

        Assert.Equal(0, context.StackDepth);
        Assert.Equal(new[] { "PushNoOpScope", "Pop" }, sink.Operations);
        Assert.Equal(new WpfCompositionDrawingContextResult(3, 2, 2), context.Result);
    }

    [Fact]
    public void PushEffectUsesNativeVisualEffectScopeWhenLegacyEffectCanBeEmulated()
    {
        var sink = new RecordingSink { AcceptVisualEffects = true };
        using var context = new WpfCompositionDrawingContext(sink);

        context.PushEffect(new FakeBlurBitmapEffect(7), new FakeContextBitmapEffectInput());

        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
        Assert.Equal(new[] { "PushVisualEffect" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(7f, effect.BlurRadius);

        context.Pop();

        Assert.Equal(0, context.StackDepth);
        Assert.Equal(new[] { "PushVisualEffect", "Pop" }, sink.Operations);
        Assert.Equal(new WpfCompositionDrawingContextResult(2, 2, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataPushEffectUsesNativeVisualEffectScopeWhenLegacyEffectCanBeEmulated()
    {
        var sink = new RecordingSink { AcceptVisualEffects = true };
        using var context = new WpfObjectRenderDataDrawingContext(sink);

        context.PushEffect(new FakeBlurBitmapEffect(9), new FakeContextBitmapEffectInput());

        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
        Assert.Equal(new[] { "PushVisualEffect" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(9f, effect.BlurRadius);
    }

    [Fact]
    public void PushEffectWithNonContextInputFallsBackToUnsupportedNoOpScope()
    {
        var sink = new RecordingSink { AcceptVisualEffects = true };
        using var context = new WpfCompositionDrawingContext(sink);

        context.PushEffect(new FakeBlurBitmapEffect(7), new FakeBitmapSourceEffectInput());

        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 1), context.Result);
        Assert.Equal(new[] { "PushNoOpScope" }, sink.Operations);
        Assert.Empty(sink.VisualEffects);
    }

    [Fact]
    public void AnimatedVideoRemainsUnsupported()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);

        context.DrawVideo(player: new object(), new Rect(0, 0, 10, 20), rectangleAnimations: new object());

        Assert.Empty(sink.Operations);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 0, 1), context.Result);
    }

    [Fact]
    public void PopWithoutMatchingPushThrows()
    {
        using var context = new WpfCompositionDrawingContext(new RecordingSink());

        Assert.Throws<InvalidOperationException>(() => context.Pop());
    }

    [Fact]
    public void CallsAfterCloseThrowObjectDisposedException()
    {
        var context = new WpfCompositionDrawingContext(new RecordingSink());

        context.Close();

        Assert.Throws<ObjectDisposedException>(() => context.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 1, 1)));
    }

    private sealed class FakeImageSource : MediaImageSource
    {
    }

    private sealed class FakeGeometryDrawing
    {
        public FakeGeometryDrawing(object? brush, object? pen, object? geometry)
        {
            Brush = brush;
            Pen = pen;
            Geometry = geometry;
        }

        public object? Brush { get; }

        public object? Pen { get; }

        public object? Geometry { get; }
    }

    private sealed class FakeRectangleGeometry
    {
        public FakeRectangleGeometry(FakeRect rect)
        {
            Rect = rect;
        }

        public FakeRect Rect { get; }
    }

    private sealed class FakeImageDrawing
    {
        public FakeImageDrawing(object? imageSource, FakeRect rect)
        {
            ImageSource = imageSource;
            Rect = rect;
        }

        public object? ImageSource { get; }

        public FakeRect Rect { get; }
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

    private sealed class FakeBitmapSource
    {
    }

    private sealed class FakeImageBrush
    {
        public FakeImageBrush(object? imageSource)
        {
            ImageSource = imageSource;
        }

        public object? ImageSource { get; }
    }

    private sealed class FakeDrawingBrush
    {
        public FakeDrawingBrush(object? drawing)
        {
            Drawing = drawing;
        }

        public object? Drawing { get; }
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

    private sealed class FakeBlurEffect
    {
        public FakeBlurEffect(double radius)
        {
            Radius = radius;
        }

        public double Radius { get; }
    }

    private sealed class FakeContextBitmapEffectInput
    {
        public bool ShouldSerializeInput()
        {
            return false;
        }
    }

    private sealed class FakeBitmapSourceEffectInput
    {
        public bool ShouldSerializeInput()
        {
            return true;
        }
    }

    private sealed class FakeImageSourceAdapter : IWpfImageSourceAdapter
    {
        public MediaImageSource AdaptedImageSource { get; } = new FakeImageSource();

        public object? LastImageSource { get; private set; }

        public MediaImageSource? AdaptImageSource(object? imageSource)
        {
            LastImageSource = imageSource;
            return AdaptedImageSource;
        }
    }

    private readonly record struct FakeRect(double X, double Y, double Width, double Height);

    private readonly record struct FakePoint(double X, double Y);

    private sealed class FakeGuidelineSet
    {
        public FakeGuidelineSet(double[] guidelinesX, double[] guidelinesY)
        {
            GuidelinesX = new FakeDoubleCollection(guidelinesX);
            GuidelinesY = new FakeDoubleCollection(guidelinesY);
        }

        public bool IsFrozen { get; init; } = true;

        public bool IsDynamic { get; init; } = true;

        public FakeDoubleCollection GuidelinesX { get; }

        public FakeDoubleCollection GuidelinesY { get; }
    }

    private sealed class FakeDoubleCollection
    {
        private readonly double[] _values;

        public FakeDoubleCollection(double[] values)
        {
            _values = values;
        }

        public int Count => _values.Length;

        public double this[int index] => _values[index];
    }

    private sealed class RecordingSink :
        IWpfCompositionCommandSink,
        IWpfVisualEffectCommandSink,
        IWpfRetainedVisualBranchSink
    {
        public List<string> Operations { get; } = new();

        public List<(MediaPen? Pen, Point Point0, Point Point1)> Lines { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle)> Rectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle, double RadiusX, double RadiusY)> RoundedRectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, Point Center, double RadiusX, double RadiusY)> Ellipses { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, MediaGeometry Geometry)> Geometries { get; } = new();

        public List<(MediaImageSource ImageSource, Rect Rectangle)> Images { get; } = new();

        public List<(MediaBrush? Brush, MediaGlyphRun GlyphRun)> GlyphRuns { get; } = new();

        public List<MediaTransform> Transforms { get; } = new();

        public List<double> Opacities { get; } = new();

        public List<(double LeadingCoordinate, double OffsetToDrivenCoordinate)> GuidelineY2Values { get; } = new();

        public List<ProGpuEffectBase> VisualEffects { get; } = new();

        public List<object> VisualOwners { get; } = new();

        public List<object> VisualDependencies { get; } = new();

        public bool AcceptVisualEffects { get; init; }

        public MediaDrawingContext DrawingContext => null!;

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
            Operations.Add("DrawLine");
            Lines.Add((pen, point0, point1));
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
            Operations.Add("DrawRectangle");
            Rectangles.Add((brush, pen, rectangle));
        }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            Operations.Add("DrawRoundedRectangle");
            RoundedRectangles.Add((brush, pen, rectangle, radiusX, radiusY));
        }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
        {
            Operations.Add("DrawEllipse");
            Ellipses.Add((brush, pen, center, radiusX, radiusY));
        }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
            Operations.Add("DrawGeometry");
            Geometries.Add((brush, pen, geometry));
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
            Operations.Add("DrawImage");
            Images.Add((imageSource, rectangle));
        }

        public void DrawText(FormattedText formattedText, Point origin)
        {
            Operations.Add("DrawText");
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
            Operations.Add("DrawGlyphRun");
            GlyphRuns.Add((foregroundBrush, glyphRun));
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
            Operations.Add("PushClip");
        }

        public void PushOpacity(double opacity)
        {
            Operations.Add("PushOpacity");
            Opacities.Add(opacity);
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
            Operations.Add("PushOpacityMask");
        }

        public void PushTransform(MediaTransform transform)
        {
            Operations.Add("PushTransform");
            Transforms.Add(transform);
        }

        public void PushNoOpScope()
        {
            Operations.Add("PushNoOpScope");
        }

        public void PushGuidelineSet()
        {
            Operations.Add("PushGuidelineSet");
        }

        public void PushGuidelineY1(double coordinate)
        {
            Operations.Add("PushGuidelineY1");
        }

        public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            Operations.Add("PushGuidelineY2");
            GuidelineY2Values.Add((leadingCoordinate, offsetToDrivenCoordinate));
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
            VisualOwners.Add(sourceVisual);
            return true;
        }

        public void PopVisualOwner()
        {
        }

        public void Pop()
        {
            Operations.Add("Pop");
        }

        public void Close()
        {
            Operations.Add("Close");
        }

        public void Dispose()
        {
        }
    }
}
