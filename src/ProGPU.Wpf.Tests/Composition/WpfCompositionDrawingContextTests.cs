using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
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
    public void ObjectRenderDataDrawingContextAdaptsTypedPrimitiveValues()
    {
        var sink = new RecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);

        context.DrawLine(pen, new Point(1, 2), new Point(3, 4));
        context.DrawRectangle(Brushes.Red, pen, new Rect(5, 6, 7, 8));
        context.DrawRoundedRectangle(Brushes.Green, pen, new Rect(9, 10, 11, 12), 2, 3);
        context.DrawEllipse(Brushes.Blue, null, new Point(13, 14), 15, 16);

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
    public void ObjectRenderDataDrawingContextUsesNativePrimitivesWhenAvailable()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var image = new FakeImageSource();
        var glyphRun = new FakePortableGlyphRunSource(new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 4 },
            AdvanceWidths = new[] { 7.0 },
            BaselineOrigin = new PortablePoint(21, 22),
            FontRenderingEmSize = 12,
            FontFamilyNames = new[] { "Arial" }
        });

        context.DrawLine(pen, new Point(1, 2), new Point(3, 4));
        context.DrawRectangle(Brushes.Red, pen, new Rect(5, 6, 7, 8));
        context.DrawRoundedRectangle(Brushes.Green, pen, new Rect(9, 10, 11, 12), 2, 3);
        context.DrawEllipse(Brushes.Blue, null, new Point(13, 14), 15, 16);
        context.DrawImage(image, new Rect(17, 18, 19, 20));
        context.DrawGlyphRun(Brushes.Black, glyphRun);

        Assert.Equal(new[]
        {
            "DrawNativeLine",
            "DrawNativeRectangle",
            "DrawNativeRoundedRectangle",
            "DrawNativeEllipse",
            "DrawNativeImage",
            "DrawNativeGlyphRun"
        }, sink.Operations);
        Assert.Empty(sink.Rectangles);
        Assert.Empty(sink.Lines);
        Assert.Empty(sink.RoundedRectangles);
        Assert.Empty(sink.Ellipses);
        Assert.Empty(sink.Images);
        Assert.Empty(sink.GlyphRuns);
        Assert.Equal((pen, new WpfReplayPoint(1, 2), new WpfReplayPoint(3, 4)), sink.NativeLines.Single());
        Assert.Equal((Brushes.Red, pen, new WpfReplayRect(5, 6, 7, 8)), sink.NativeRectangles.Single());
        Assert.Equal((Brushes.Green, pen, new WpfReplayRect(9, 10, 11, 12), 2d, 3d), sink.NativeRoundedRectangles.Single());
        Assert.Equal((Brushes.Blue, null, new WpfReplayPoint(13, 14), 15d, 16d), sink.NativeEllipses.Single());
        Assert.Equal((image, new WpfReplayRect(17, 18, 19, 20)), sink.NativeImages.Single());
        Assert.Equal((Brushes.Black, glyphRun), sink.NativeGlyphRuns.Single());
        Assert.Equal(0, glyphRun.ReflectedGlyphRunProbeCount);
        Assert.Equal(new WpfCompositionDrawingContextResult(6, 6, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextUsesNativePortableGeometryWhenAvailable()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new FakeRectangleGeometry(new FakeRect(1, 2, 30, 40));

        context.DrawGeometry(Brushes.Green, null, geometry);

        Assert.Equal(new[] { "DrawNativeGeometry" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        var replayed = Assert.Single(sink.NativeGeometries);
        Assert.Same(Brushes.Green, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(PortableGeometryPathKind.Path, replayed.Geometry.Kind);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsPortableRectGeometryAsNativeRectangleWithoutManagedGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new PortableRect(1, 2, 30, 40);

        context.DrawGeometry(Brushes.Green, pen, geometry);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeRectangles);
        Assert.Same(Brushes.Green, replayed.Brush);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayRect(1, 2, 30, 40), replayed.Rectangle);
        Assert.Contains(Brushes.Green, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsReplayRectGeometryAsRectangleWithoutManagedGeometry()
    {
        var sink = new RecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new WpfReplayRect(5, 6, 70, 80);

        context.DrawGeometry(Brushes.Blue, null, geometry);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        var replayed = Assert.Single(sink.Rectangles);
        Assert.Same(Brushes.Blue, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(new Rect(5, 6, 70, 80), replayed.Rectangle);
        Assert.Contains(Brushes.Blue, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsLocalLineGeometryAsNativeLineWithoutGenericGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new LineGeometry(new Point(1, 2), new Point(30, 40));

        context.DrawGeometry(Brushes.Red, pen, geometry);

        Assert.Equal(new[] { "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeLines);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), replayed.Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), replayed.Point1);
        Assert.Contains(Brushes.Red, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsLocalLinePathGeometryAsNativeLineWithoutGenericGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateLinePathGeometry(new Point(1, 2), new Point(30, 40));

        context.DrawGeometry(Brushes.Red, pen, geometry);

        Assert.Equal(new[] { "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeLines);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), replayed.Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), replayed.Point1);
        Assert.Contains(Brushes.Red, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsLocalPolylinePathGeometryAsNativeLinesWithoutGenericGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreatePolylinePathGeometry(
            new Point(1, 2),
            new Point(30, 40),
            new Point(50, 60));

        context.DrawGeometry(null, pen, geometry);

        Assert.Equal(new[] { "DrawNativeLine", "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Equal(2, sink.NativeLines.Count);
        Assert.Same(pen, sink.NativeLines[0].Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), sink.NativeLines[0].Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[0].Point1);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[1].Point0);
        Assert.Equal(new WpfReplayPoint(50, 60), sink.NativeLines[1].Point1);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsClosedPolylinePathGeometryAsNativeLinesWithoutGenericGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateClosedPolylinePathGeometry(
            new Point(1, 2),
            new Point(30, 40),
            new Point(50, 10));

        context.DrawGeometry(null, pen, geometry);

        Assert.Equal(new[] { "DrawNativeLine", "DrawNativeLine", "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Equal(3, sink.NativeLines.Count);
        Assert.Same(pen, sink.NativeLines[0].Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), sink.NativeLines[0].Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[0].Point1);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[1].Point0);
        Assert.Equal(new WpfReplayPoint(50, 10), sink.NativeLines[1].Point1);
        Assert.Equal(new WpfReplayPoint(50, 10), sink.NativeLines[2].Point0);
        Assert.Equal(new WpfReplayPoint(1, 2), sink.NativeLines[2].Point1);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsTransformedLineGeometryAsNativeLine()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new LineGeometry(new Point(1, 2), new Point(30, 40))
        {
            Transform = new MatrixTransform(new Matrix { M11 = 1, M22 = 1, OffsetX = 10 })
        };

        context.DrawGeometry(null, pen, geometry);

        Assert.Equal(new[] { "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        var replayed = Assert.Single(sink.NativeLines);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(11, 2), replayed.Point0);
        Assert.Equal(new WpfReplayPoint(40, 40), replayed.Point1);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsLocalRectangleGeometryAsNativeRectangleWithoutGenericGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new RectangleGeometry(new Rect(10, 20, 30, 40));

        context.DrawGeometry(Brushes.Red, pen, geometry);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Empty(sink.Rectangles);
        var replayed = Assert.Single(sink.NativeRectangles);
        Assert.Same(Brushes.Red, replayed.Brush);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayRect(10, 20, 30, 40), replayed.Rectangle);
        Assert.Contains(Brushes.Red, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsLocalRectanglePathGeometryAsNativeRectangleWithoutGenericGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateRectanglePathGeometry(new Rect(10, 20, 30, 40));

        context.DrawGeometry(Brushes.Red, pen, geometry);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Empty(sink.Rectangles);
        var replayed = Assert.Single(sink.NativeRectangles);
        Assert.Same(Brushes.Red, replayed.Brush);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayRect(10, 20, 30, 40), replayed.Rectangle);
        Assert.Contains(Brushes.Red, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsUnfilledRectanglePathGeometryAsNativeRectangleStrokeWithoutGenericGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateRectanglePathGeometry(new Rect(10, 20, 30, 40), isFilled: false);

        context.DrawGeometry(null, pen, geometry);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Empty(sink.Rectangles);
        var replayed = Assert.Single(sink.NativeRectangles);
        Assert.Null(replayed.Brush);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayRect(10, 20, 30, 40), replayed.Rectangle);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsLocalRoundedRectangleGeometryAsNativeRoundedRectangle()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new RectangleGeometry(new Rect(2, 3, 40, 50))
        {
            RadiusX = 6,
            RadiusY = 7
        };

        context.DrawGeometry(Brushes.Green, null, geometry);

        Assert.Equal(new[] { "DrawNativeRoundedRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Empty(sink.RoundedRectangles);
        var replayed = Assert.Single(sink.NativeRoundedRectangles);
        Assert.Same(Brushes.Green, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(new WpfReplayRect(2, 3, 40, 50), replayed.Rectangle);
        Assert.Equal(6, replayed.RadiusX);
        Assert.Equal(7, replayed.RadiusY);
        Assert.Contains(Brushes.Green, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsLocalRoundedRectangleGeometryAsRoundedRectangle()
    {
        var sink = new RecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new RectangleGeometry(new Rect(5, 6, 70, 80))
        {
            RadiusX = 8,
            RadiusY = 9
        };

        context.DrawGeometry(Brushes.Blue, null, geometry);

        Assert.Equal(new[] { "DrawRoundedRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        var replayed = Assert.Single(sink.RoundedRectangles);
        Assert.Same(Brushes.Blue, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(new Rect(5, 6, 70, 80), replayed.Rectangle);
        Assert.Equal(8, replayed.RadiusX);
        Assert.Equal(9, replayed.RadiusY);
        Assert.Contains(Brushes.Blue, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsTransformedRectangleGeometryAsNativeRectangle()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new RectangleGeometry(new Rect(1, 2, 30, 40))
        {
            Transform = new MatrixTransform(1, 0, 0, 1, 5, 6)
        };

        context.DrawGeometry(Brushes.Green, null, geometry);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.NativeRoundedRectangles);
        Assert.Empty(sink.NativeGeometries);
        Assert.Empty(sink.Geometries);
        var replayed = Assert.Single(sink.NativeRectangles);
        Assert.Same(Brushes.Green, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(new WpfReplayRect(6, 8, 30, 40), replayed.Rectangle);
        Assert.Contains(Brushes.Green, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsLocalEllipseGeometryAsNativeEllipseWithoutGenericGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new EllipseGeometry(new Point(10, 20), 30, 40);

        context.DrawGeometry(Brushes.Red, pen, geometry);

        Assert.Equal(new[] { "DrawNativeEllipse" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Empty(sink.Ellipses);
        var replayed = Assert.Single(sink.NativeEllipses);
        Assert.Same(Brushes.Red, replayed.Brush);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(10, 20), replayed.Center);
        Assert.Equal(30, replayed.RadiusX);
        Assert.Equal(40, replayed.RadiusY);
        Assert.Contains(Brushes.Red, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsLocalEllipseGeometryAsEllipseWithoutGenericGeometry()
    {
        var sink = new RecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new EllipseGeometry(new Point(5, 6), 70, 80);

        context.DrawGeometry(Brushes.Blue, null, geometry);

        Assert.Equal(new[] { "DrawEllipse" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        var replayed = Assert.Single(sink.Ellipses);
        Assert.Same(Brushes.Blue, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(new Point(5, 6), replayed.Center);
        Assert.Equal(70, replayed.RadiusX);
        Assert.Equal(80, replayed.RadiusY);
        Assert.Contains(Brushes.Blue, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsTransformedEllipseGeometryAsNativeEllipse()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new EllipseGeometry(new Point(10, 20), 30, 40)
        {
            Transform = new MatrixTransform(2, 0, 0, 3, 5, -1)
        };

        context.DrawGeometry(Brushes.Green, null, geometry);

        Assert.Equal(new[] { "DrawNativeEllipse" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeEllipses);
        Assert.Same(Brushes.Green, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(new WpfReplayPoint(25, 59), replayed.Center);
        Assert.Equal(60, replayed.RadiusX);
        Assert.Equal(120, replayed.RadiusY);
        Assert.Contains(Brushes.Green, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextKeepsSkewedEllipseGeometryOnGenericGeometryPath()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new EllipseGeometry(new Point(10, 20), 30, 40)
        {
            Transform = new MatrixTransform(1, 0.25, 0, 1, 5, 6)
        };

        context.DrawGeometry(Brushes.Green, null, geometry);

        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Empty(sink.NativeEllipses);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.Geometries);
        Assert.Same(Brushes.Green, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Same(geometry, replayed.Geometry);
        Assert.Contains(Brushes.Green, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextDrawsPortableRectTileBrushPenAsRectangleWithoutManagedGeometry()
    {
        var sink = new RecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var nestedDrawing = new FakeGeometryDrawing(
            Brushes.Red,
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawingBrush = new FakeDrawingBrush(nestedDrawing);
        var geometry = new PortableRect(1, 2, 30, 40);

        context.DrawGeometry(drawingBrush, pen, geometry);

        Assert.Equal(
            new[]
            {
                "PushClip",
                "PushNativeTransform",
                "DrawGeometry",
                "Pop",
                "Pop",
                "DrawRectangle"
            },
            sink.Operations);
        Assert.Single(sink.Geometries);
        var replayedPen = Assert.Single(sink.Rectangles);
        Assert.Null(replayedPen.Brush);
        Assert.Same(pen, replayedPen.Pen);
        Assert.Equal(new Rect(1, 2, 30, 40), replayedPen.Rectangle);
        Assert.Contains(drawingBrush, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextReplaysPortableGeometryTileBrushWithoutManagedGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new FakeRectangleGeometry(new FakeRect(1, 2, 30, 40));
        var nestedDrawing = new FakeGeometryDrawing(
            Brushes.Red,
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawingBrush = new FakeDrawingBrush(nestedDrawing);

        context.DrawGeometry(drawingBrush, null, geometry);

        Assert.Equal(
            new[]
            {
                "PushNativeClip",
                "PushNativeTransform",
                "DrawNativeGeometry",
                "Pop",
                "Pop"
            },
            sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Equal(new WpfReplayRect(1, 2, 30, 40), Assert.Single(sink.NativeClips));
        Assert.Single(sink.NativeGeometries);
        Assert.Contains(drawingBrush, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextPushesPortableRectangleClipAsNativeClip()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new FakeRectangleGeometry(new FakeRect(5, 6, 70, 80));

        context.PushClip(geometry);

        Assert.Equal(new[] { "PushNativeClip" }, sink.Operations);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Equal(new WpfReplayRect(5, 6, 70, 80), Assert.Single(sink.NativeClips));
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextUsesNativePortableGeometryClipForNonRectangleClip()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new FakeTriangleGeometry(new FakeRect(5, 6, 70, 80));

        context.PushClip(geometry);

        Assert.Equal(new[] { "PushNativeGeometryClip" }, sink.Operations);
        var clip = Assert.Single(sink.NativeGeometryClips);
        Assert.Equal(PortableGeometryPathKind.Path, clip.Kind);
        Assert.Empty(sink.NativeClips);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextPushesLocalRectangleGeometryClipAsNativeClipWithoutGenericClipFallback()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new RectangleGeometry(new Rect(1, 2, 30, 40));

        context.PushClip(geometry);

        Assert.Equal(new[] { "PushNativeClip" }, sink.Operations);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Equal(new WpfReplayRect(1, 2, 30, 40), Assert.Single(sink.NativeClips));
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextPushesPortableRectClipAsNativeClipWithoutManagedGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var rectangle = new PortableRect(2, 3, 40, 50);

        context.PushClip(rectangle);

        Assert.Equal(new[] { "PushNativeClip" }, sink.Operations);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Equal(new WpfReplayRect(2, 3, 40, 50), Assert.Single(sink.NativeClips));
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextKeepsRoundedRectangleGeometryClipOnGenericClipPath()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new RectangleGeometry(new Rect(2, 3, 40, 50))
        {
            RadiusX = 4,
            RadiusY = 6
        };

        context.PushClip(geometry);

        Assert.Equal(new[] { "PushClip" }, sink.Operations);
        Assert.Empty(sink.NativeClips);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextPushesTransformedRectangleGeometryClipAsNativeClip()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfObjectRenderDataDrawingContext(sink);
        var geometry = new RectangleGeometry(new Rect(2, 3, 40, 50))
        {
            Transform = new MatrixTransform(new Matrix { M11 = 1, M22 = 1, OffsetX = 10 })
        };

        context.PushClip(geometry);

        Assert.Equal(new[] { "PushNativeClip" }, sink.Operations);
        Assert.Equal(new WpfReplayRect(12, 3, 40, 50), Assert.Single(sink.NativeClips));
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextUsesImageSourceAdapter()
    {
        var sink = new RecordingSink();
        var imageSource = new FakeBitmapSource();
        var adapter = new FakeImageSourceAdapter();
        using var context = new WpfObjectRenderDataDrawingContext(sink, adapter);

        context.DrawImage(imageSource, new Rect(17, 18, 19, 20));

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

        context.DrawRectangle(imageBrush, null, new Rect(1, 2, 30, 40));

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

        context.DrawRectangle(drawingBrush, null, new Rect(1, 2, 30, 40));

        Assert.Equal(new[] { "PushClip", "PushNativeTransform", "DrawGeometry", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.NativeTransforms);
        var replayed = Assert.Single(sink.Geometries);
        Assert.Same(Brushes.Red, replayed.Brush);
        Assert.IsType<PathGeometry>(replayed.Geometry);
        Assert.Contains(drawingBrush, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextUsesNativeTileClipForStretchedDrawingBrush()
    {
        var sink = new NativeClipRecordingSink();
        var nestedDrawing = new FakeGeometryDrawing(
            Brushes.Red,
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawingBrush = new FakeDrawingBrush(nestedDrawing, PortableStretch.UniformToFill);
        using var context = new WpfObjectRenderDataDrawingContext(sink);

        context.DrawRectangle(drawingBrush, null, new Rect(1, 2, 30, 40));

        Assert.Equal(
            new[]
            {
                "PushNativeClip",
                "PushNativeClip",
                "PushNativeTransform",
                "DrawGeometry",
                "Pop",
                "Pop",
                "Pop"
            },
            sink.Operations);
        Assert.Equal(2, sink.NativeClips.Count);
        var nativeClip = sink.NativeClips[0];
        Assert.Equal(1, nativeClip.X);
        Assert.Equal(2, nativeClip.Y);
        Assert.Equal(30, nativeClip.Width);
        Assert.Equal(40, nativeClip.Height);
        Assert.Equal(nativeClip, sink.NativeClips[1]);
        Assert.Contains(drawingBrush, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextReplaysMediaDrawingBrushBeforeGenericMediaBrushPath()
    {
        var sink = new RecordingSink();
        var drawingBrush = new FakeMediaDrawingBrush(
            new FakeGeometryDrawing(
                Brushes.Red,
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10))));
        using var context = new WpfObjectRenderDataDrawingContext(sink);

        context.DrawRectangle(drawingBrush, null, new Rect(1, 2, 30, 40));

        Assert.Equal(new[] { "PushClip", "PushNativeTransform", "DrawGeometry", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.NativeTransforms);
        var replayed = Assert.Single(sink.Geometries);
        Assert.Same(Brushes.Red, replayed.Brush);
        Assert.Contains(drawingBrush, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextReplaysMediaImageBrushRectangleThroughImageSourceAdapter()
    {
        var sink = new RecordingSink();
        var imageSource = new FakeBitmapSource();
        var imageBrush = new FakeMediaImageBrush(imageSource);
        var adapter = new FakeImageSourceAdapter();
        using var context = new WpfCompositionDrawingContext(sink, adapter);

        context.DrawRectangle(imageBrush, null, new Rect(1, 2, 30, 40));

        Assert.Equal(new[] { "PushClip", "DrawImage", "Pop" }, sink.Operations);
        Assert.Same(imageSource, adapter.LastImageSource);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(adapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(1, 2, 30, 40), replayed.Rectangle);
        Assert.Contains(imageBrush, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextFallsBackToGenericMediaBrushWhenTileReplayUnsupported()
    {
        var sink = new RecordingSink();
        var imageBrush = new FakeMediaImageBrush(imageSource: null);
        using var context = new WpfCompositionDrawingContext(sink);

        context.DrawRectangle(imageBrush, null, new Rect(1, 2, 30, 40));

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        var replayed = Assert.Single(sink.Rectangles);
        Assert.Same(imageBrush, replayed.Brush);
        Assert.Empty(sink.Images);
        Assert.Contains(imageBrush, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextFallsBackToGenericMediaBrushWhenTileReplayUnsupported()
    {
        var sink = new RecordingSink();
        var imageBrush = new FakeMediaImageBrush(imageSource: null);
        using var context = new WpfObjectRenderDataDrawingContext(sink);

        context.DrawRectangle(imageBrush, null, new Rect(1, 2, 30, 40));

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        var replayed = Assert.Single(sink.Rectangles);
        Assert.Same(imageBrush, replayed.Brush);
        Assert.Empty(sink.Images);
        Assert.Contains(imageBrush, sink.VisualDependencies);
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

        context.DrawRectangle(drawingBrush, null, new Rect(1, 2, 30, 40));

        Assert.Equal(new[] { "PushClip", "PushNativeTransform", "DrawGeometry", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.NativeTransforms);
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
    public void GeneratedDrawingContextDrawsRectangleGeometryAsNativeRectangleWithoutGenericGeometryFallback()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new RectangleGeometry(new Rect(1, 2, 30, 40));

        context.DrawGeometry(Brushes.Green, pen, geometry);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Empty(sink.Rectangles);
        var replayed = Assert.Single(sink.NativeRectangles);
        Assert.Same(Brushes.Green, replayed.Brush);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayRect(1, 2, 30, 40), replayed.Rectangle);
        Assert.Contains(Brushes.Green, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsRectanglePathGeometryAsNativeRectangleWithoutGenericGeometryFallback()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateRectanglePathGeometry(new Rect(1, 2, 30, 40));

        context.DrawGeometry(Brushes.Green, pen, geometry);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Empty(sink.Rectangles);
        var replayed = Assert.Single(sink.NativeRectangles);
        Assert.Same(Brushes.Green, replayed.Brush);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayRect(1, 2, 30, 40), replayed.Rectangle);
        Assert.Contains(Brushes.Green, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsUnfilledRectanglePathGeometryAsNativeRectangleStrokeWithoutGenericGeometryFallback()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateRectanglePathGeometry(new Rect(1, 2, 30, 40), isFilled: false);

        context.DrawGeometry(null, pen, geometry);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Empty(sink.Rectangles);
        var replayed = Assert.Single(sink.NativeRectangles);
        Assert.Null(replayed.Brush);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayRect(1, 2, 30, 40), replayed.Rectangle);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsLineGeometryAsNativeLineWithoutGenericGeometryFallback()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new LineGeometry(new Point(1, 2), new Point(30, 40));

        context.DrawGeometry(Brushes.Green, pen, geometry);

        Assert.Equal(new[] { "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeLines);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), replayed.Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), replayed.Point1);
        Assert.Contains(Brushes.Green, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsLinePathGeometryAsNativeLineWithoutGenericGeometryFallback()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateLinePathGeometry(new Point(1, 2), new Point(30, 40));

        context.DrawGeometry(Brushes.Green, pen, geometry);

        Assert.Equal(new[] { "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeLines);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), replayed.Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), replayed.Point1);
        Assert.Contains(Brushes.Green, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsPolylinePathGeometryAsNativeLinesWithoutGenericGeometryFallback()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreatePolylinePathGeometry(
            new Point(1, 2),
            new Point(30, 40),
            new Point(50, 60));

        context.DrawGeometry(null, pen, geometry);

        Assert.Equal(new[] { "DrawNativeLine", "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Equal(2, sink.NativeLines.Count);
        Assert.Same(pen, sink.NativeLines[0].Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), sink.NativeLines[0].Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[0].Point1);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[1].Point0);
        Assert.Equal(new WpfReplayPoint(50, 60), sink.NativeLines[1].Point1);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsTransformedPolylinePathGeometryAsNativeLines()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreatePolylinePathGeometry(
            new Point(1, 2),
            new Point(30, 40),
            new Point(50, 60));
        geometry.Transform = new MatrixTransform(new Matrix { M11 = 2, M22 = 3, OffsetX = 5, OffsetY = -1 });

        context.DrawGeometry(null, pen, geometry);

        Assert.Equal(new[] { "DrawNativeLine", "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Equal(2, sink.NativeLines.Count);
        Assert.Same(pen, sink.NativeLines[0].Pen);
        Assert.Equal(new WpfReplayPoint(7, 5), sink.NativeLines[0].Point0);
        Assert.Equal(new WpfReplayPoint(65, 119), sink.NativeLines[0].Point1);
        Assert.Equal(new WpfReplayPoint(65, 119), sink.NativeLines[1].Point0);
        Assert.Equal(new WpfReplayPoint(105, 179), sink.NativeLines[1].Point1);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsClosedPolylinePathGeometryAsNativeLinesWithoutGenericGeometryFallback()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateClosedPolylinePathGeometry(
            new Point(1, 2),
            new Point(30, 40),
            new Point(50, 10));

        context.DrawGeometry(null, pen, geometry);

        Assert.Equal(new[] { "DrawNativeLine", "DrawNativeLine", "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Equal(3, sink.NativeLines.Count);
        Assert.Same(pen, sink.NativeLines[0].Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), sink.NativeLines[0].Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[0].Point1);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[1].Point0);
        Assert.Equal(new WpfReplayPoint(50, 10), sink.NativeLines[1].Point1);
        Assert.Equal(new WpfReplayPoint(50, 10), sink.NativeLines[2].Point0);
        Assert.Equal(new WpfReplayPoint(1, 2), sink.NativeLines[2].Point1);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsTransformedLineGeometryAsNativeLine()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new LineGeometry(new Point(1, 2), new Point(30, 40))
        {
            Transform = new MatrixTransform(new Matrix { M11 = 1, M22 = 1, OffsetX = 10 })
        };

        context.DrawGeometry(null, pen, geometry);

        Assert.Equal(new[] { "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        var replayed = Assert.Single(sink.NativeLines);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(11, 2), replayed.Point0);
        Assert.Equal(new WpfReplayPoint(40, 40), replayed.Point1);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsRectangleGeometryAsRectangleWithoutGenericGeometryFallback()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = new RectangleGeometry(new Rect(5, 6, 70, 80));

        context.DrawGeometry(Brushes.Blue, null, geometry);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        var replayed = Assert.Single(sink.Rectangles);
        Assert.Same(Brushes.Blue, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(new Rect(5, 6, 70, 80), replayed.Rectangle);
        Assert.Contains(Brushes.Blue, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsRoundedRectangleGeometryAsNativeRoundedRectangle()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = new RectangleGeometry(new Rect(2, 3, 40, 50))
        {
            RadiusX = 4,
            RadiusY = 6
        };

        context.DrawGeometry(Brushes.Yellow, null, geometry);

        Assert.Equal(new[] { "DrawNativeRoundedRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeRoundedRectangles);
        Assert.Same(Brushes.Yellow, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(new WpfReplayRect(2, 3, 40, 50), replayed.Rectangle);
        Assert.Equal(4d, replayed.RadiusX);
        Assert.Equal(6d, replayed.RadiusY);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsTransformedRectangleGeometryAsNativeRectangle()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = new RectangleGeometry(new Rect(1, 2, 30, 40))
        {
            Transform = new MatrixTransform(new Matrix { M11 = 1, M22 = 1, OffsetX = 10 })
        };

        context.DrawGeometry(Brushes.Green, null, geometry);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        var replayed = Assert.Single(sink.NativeRectangles);
        Assert.Same(Brushes.Green, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(new WpfReplayRect(11, 2, 30, 40), replayed.Rectangle);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsEllipseGeometryAsNativeEllipseWithoutGenericGeometryFallback()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new EllipseGeometry(new Point(10, 20), 30, 40);

        context.DrawGeometry(Brushes.Red, pen, geometry);

        Assert.Equal(new[] { "DrawNativeEllipse" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Empty(sink.Ellipses);
        var replayed = Assert.Single(sink.NativeEllipses);
        Assert.Same(Brushes.Red, replayed.Brush);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(10, 20), replayed.Center);
        Assert.Equal(30d, replayed.RadiusX);
        Assert.Equal(40d, replayed.RadiusY);
        Assert.Contains(Brushes.Red, sink.VisualDependencies);
        Assert.Contains(pen, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsEllipseGeometryAsEllipseWithoutGenericGeometryFallback()
    {
        var sink = new RecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = new EllipseGeometry(new Point(5, 6), 70, 80);

        context.DrawGeometry(Brushes.Blue, null, geometry);

        Assert.Equal(new[] { "DrawEllipse" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        var replayed = Assert.Single(sink.Ellipses);
        Assert.Same(Brushes.Blue, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(new Point(5, 6), replayed.Center);
        Assert.Equal(70d, replayed.RadiusX);
        Assert.Equal(80d, replayed.RadiusY);
        Assert.Contains(Brushes.Blue, sink.VisualDependencies);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextDrawsTransformedEllipseGeometryAsNativeEllipse()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = new EllipseGeometry(new Point(10, 20), 30, 40)
        {
            Transform = new MatrixTransform(new Matrix { M11 = 2, M22 = 3, OffsetX = 5, OffsetY = -1 })
        };

        context.DrawGeometry(Brushes.Green, null, geometry);

        Assert.Equal(new[] { "DrawNativeEllipse" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        var replayed = Assert.Single(sink.NativeEllipses);
        Assert.Same(Brushes.Green, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Equal(new WpfReplayPoint(25, 59), replayed.Center);
        Assert.Equal(60, replayed.RadiusX);
        Assert.Equal(120, replayed.RadiusY);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextKeepsSkewedEllipseGeometryOnGenericGeometryPath()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = new EllipseGeometry(new Point(10, 20), 30, 40)
        {
            Transform = new MatrixTransform(new Matrix { M11 = 1, M12 = 0.25, M22 = 1, OffsetX = 5, OffsetY = 6 })
        };

        context.DrawGeometry(Brushes.Green, null, geometry);

        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Empty(sink.NativeEllipses);
        var replayed = Assert.Single(sink.Geometries);
        Assert.Same(Brushes.Green, replayed.Brush);
        Assert.Null(replayed.Pen);
        Assert.Same(geometry, replayed.Geometry);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextPushesRectangleGeometryClipAsNativeClipWithoutGenericClipFallback()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = new RectangleGeometry(new Rect(1, 2, 30, 40));

        context.PushClip(geometry);

        Assert.Equal(new[] { "PushNativeClip" }, sink.Operations);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Equal(new WpfReplayRect(1, 2, 30, 40), Assert.Single(sink.NativeClips));
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextPushesRectanglePathGeometryClipAsNativeClipWithoutGenericClipFallback()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = CreateRectanglePathGeometry(new Rect(1, 2, 30, 40));

        context.PushClip(geometry);

        Assert.Equal(new[] { "PushNativeClip" }, sink.Operations);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Equal(new WpfReplayRect(1, 2, 30, 40), Assert.Single(sink.NativeClips));
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextKeepsRoundedRectangleGeometryClipOnGenericClipPath()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = new RectangleGeometry(new Rect(2, 3, 40, 50))
        {
            RadiusX = 4,
            RadiusY = 6
        };

        context.PushClip(geometry);

        Assert.Equal(new[] { "PushClip" }, sink.Operations);
        Assert.Empty(sink.NativeClips);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextPushesTransformedRectanglePathGeometryClipAsNativeClip()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = CreateRectanglePathGeometry(new Rect(2, 3, 40, 50));
        geometry.Transform = new MatrixTransform(new Matrix { M11 = 1, M22 = 1, OffsetX = 10 });

        context.PushClip(geometry);

        Assert.Equal(new[] { "PushNativeClip" }, sink.Operations);
        Assert.Equal(new WpfReplayRect(12, 3, 40, 50), Assert.Single(sink.NativeClips));
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextKeepsNonRectanglePathGeometryClipOnGenericClipPath()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = CreateTrianglePathGeometry(new Rect(2, 3, 40, 50));

        context.PushClip(geometry);

        Assert.Equal(new[] { "PushClip" }, sink.Operations);
        Assert.Empty(sink.NativeClips);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void GeneratedDrawingContextKeepsIncompleteRectanglePathGeometryClipOnGenericClipPath()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var geometry = CreateIncompleteRectanglePathGeometry(new Rect(2, 3, 40, 50));

        context.PushClip(geometry);

        Assert.Equal(new[] { "PushClip" }, sink.Operations);
        Assert.Empty(sink.NativeClips);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Contains(geometry, sink.VisualDependencies);
        Assert.Equal(1, context.StackDepth);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
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

        context.DrawRectangle(brush, pen, new Rect(1, 2, 3, 4));
        context.DrawGeometry(Brushes.Green, null, geometry);
        context.DrawImage(imageSource, new Rect(5, 6, 7, 8));
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
    public void ObjectRenderDataDrawingContextPushesPortableTransformsThroughNativeSink()
    {
        var sink = new RecordingSink();
        var transform = new FakeTranslateTransform(6, 7);
        using var context = new WpfObjectRenderDataDrawingContext(sink);

        context.PushTransform(transform);

        Assert.Equal(new[] { "PushNativeTransform" }, sink.Operations);
        var nativeTransform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(6, nativeTransform.M41);
        Assert.Equal(7, nativeTransform.M42);
        Assert.Contains(transform, sink.VisualDependencies);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void ObjectRenderDataDrawingContextKeepsGradientStopGraphBehindPortableBrushDependency()
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
        Assert.DoesNotContain(brush.GradientStops, sink.VisualDependencies);
        Assert.DoesNotContain(firstStop, sink.VisualDependencies);
        Assert.DoesNotContain(secondStop, sink.VisualDependencies);
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
    public void DrawDrawingReplaysPortableGeometryDrawing()
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
    public void DrawDrawingReplaysLocalLineGeometryAsNativeLineWithoutManagedGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new LineGeometry(new Point(1, 2), new Point(30, 40));
        var drawing = new FakeGeometryDrawing(Brushes.Red, pen, geometry);

        var status = context.DrawDrawing(drawing);

        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        Assert.Equal(new[] { "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeLines);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), replayed.Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), replayed.Point1);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DrawDrawingReplaysTransformedLocalLineGeometryAsNativeLineWithoutManagedGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new LineGeometry(new Point(1, 2), new Point(30, 40))
        {
            Transform = new MatrixTransform(new Matrix { M11 = 1, M22 = 1, OffsetX = 10 })
        };
        var drawing = new FakeGeometryDrawing(Brushes.Red, pen, geometry);

        var status = context.DrawDrawing(drawing);

        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        Assert.Equal(new[] { "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeLines);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(11, 2), replayed.Point0);
        Assert.Equal(new WpfReplayPoint(40, 40), replayed.Point1);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DrawDrawingReplaysLocalLinePathGeometryAsNativeLineWithoutManagedGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateLinePathGeometry(new Point(1, 2), new Point(30, 40));
        var drawing = new FakeGeometryDrawing(Brushes.Red, pen, geometry);

        var status = context.DrawDrawing(drawing);

        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        Assert.Equal(new[] { "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeLines);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), replayed.Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), replayed.Point1);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DrawDrawingReplaysLocalPolylinePathGeometryAsNativeLinesWithoutManagedGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreatePolylinePathGeometry(
            new Point(1, 2),
            new Point(30, 40),
            new Point(50, 60));
        var drawing = new FakeGeometryDrawing(null, pen, geometry);

        var status = context.DrawDrawing(drawing);

        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        Assert.Equal(new[] { "DrawNativeLine", "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Equal(2, sink.NativeLines.Count);
        Assert.Same(pen, sink.NativeLines[0].Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), sink.NativeLines[0].Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[0].Point1);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[1].Point0);
        Assert.Equal(new WpfReplayPoint(50, 60), sink.NativeLines[1].Point1);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DrawDrawingReplaysClosedPolylinePathGeometryAsNativeLinesWithoutManagedGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateClosedPolylinePathGeometry(
            new Point(1, 2),
            new Point(30, 40),
            new Point(50, 10));
        var drawing = new FakeGeometryDrawing(null, pen, geometry);

        var status = context.DrawDrawing(drawing);

        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        Assert.Equal(new[] { "DrawNativeLine", "DrawNativeLine", "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        Assert.Equal(3, sink.NativeLines.Count);
        Assert.Same(pen, sink.NativeLines[0].Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), sink.NativeLines[0].Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[0].Point1);
        Assert.Equal(new WpfReplayPoint(30, 40), sink.NativeLines[1].Point0);
        Assert.Equal(new WpfReplayPoint(50, 10), sink.NativeLines[1].Point1);
        Assert.Equal(new WpfReplayPoint(50, 10), sink.NativeLines[2].Point0);
        Assert.Equal(new WpfReplayPoint(1, 2), sink.NativeLines[2].Point1);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DrawDrawingReplaysLocalRectanglePathGeometryAsNativeRectangleWithoutManagedGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateRectanglePathGeometry(new Rect(1, 2, 30, 40));
        var drawing = new FakeGeometryDrawing(Brushes.Red, pen, geometry);

        var status = context.DrawDrawing(drawing);

        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeRectangles);
        Assert.Same(Brushes.Red, replayed.Brush);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayRect(1, 2, 30, 40), replayed.Rectangle);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DrawDrawingReplaysUnfilledRectanglePathGeometryAsNativeRectangleStrokeWithoutManagedGeometry()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = CreateRectanglePathGeometry(new Rect(1, 2, 30, 40), isFilled: false);
        var drawing = new FakeGeometryDrawing(null, pen, geometry);

        var status = context.DrawDrawing(drawing);

        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeRectangles);
        Assert.Null(replayed.Brush);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayRect(1, 2, 30, 40), replayed.Rectangle);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 0), context.Result);
    }

    [Fact]
    public void DrawDrawingReportsPartialLineReplayWhenBrushIsUnsupported()
    {
        var sink = new NativeRecordingSink();
        using var context = new WpfCompositionDrawingContext(sink);
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new LineGeometry(new Point(1, 2), new Point(30, 40));
        var drawing = new FakeGeometryDrawing(new object(), pen, geometry);

        var status = context.DrawDrawing(drawing);

        Assert.Equal(WpfDrawingReplayStatus.PartiallyApplied, status);
        Assert.Equal(new[] { "DrawNativeLine" }, sink.Operations);
        Assert.Empty(sink.Geometries);
        Assert.Empty(sink.NativeGeometries);
        var replayed = Assert.Single(sink.NativeLines);
        Assert.Same(pen, replayed.Pen);
        Assert.Equal(new WpfReplayPoint(1, 2), replayed.Point0);
        Assert.Equal(new WpfReplayPoint(30, 40), replayed.Point1);
        Assert.Equal(new WpfCompositionDrawingContextResult(1, 1, 1), context.Result);
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
            "PushNativeTransform",
            "PushGuidelineY1",
            "Pop",
            "Pop",
            "Pop",
            "Close"
        }, sink.Operations);
        var nativeTransform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(4, nativeTransform.M41);
        Assert.Equal(5, nativeTransform.M42);
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

    private static PathGeometry CreateRectanglePathGeometry(Rect bounds, bool isFilled = true)
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(bounds.X, bounds.Y),
            IsClosed = true,
            IsFilled = isFilled
        };
        figure.Segments.Add(new LineSegment(new Point(bounds.X + bounds.Width, bounds.Y), isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(bounds.X + bounds.Width, bounds.Y + bounds.Height), isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(bounds.X, bounds.Y + bounds.Height), isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry CreateLinePathGeometry(Point startPoint, Point endPoint)
    {
        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new LineSegment(endPoint, isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry CreatePolylinePathGeometry(params Point[] points)
    {
        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false
        };
        for (var i = 1; i < points.Length; i++)
        {
            figure.Segments.Add(new LineSegment(points[i], isStroked: true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry CreateClosedPolylinePathGeometry(params Point[] points)
    {
        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = false
        };

        for (var i = 1; i < points.Length; i++)
        {
            figure.Segments.Add(new LineSegment(points[i], isStroked: true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry CreateTrianglePathGeometry(Rect bounds)
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(bounds.X, bounds.Y),
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new LineSegment(new Point(bounds.X + bounds.Width, bounds.Y), isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height), isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry CreateIncompleteRectanglePathGeometry(Rect bounds)
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(bounds.X, bounds.Y),
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new LineSegment(new Point(bounds.X + bounds.Width, bounds.Y), isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(bounds.X, bounds.Y), isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(bounds.X, bounds.Y + bounds.Height), isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private sealed class FakeImageSource : MediaImageSource
    {
    }

    private sealed class FakeGeometryDrawing : IPortableGeometryDrawingStateSource
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

        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = new PortableGeometryDrawingState
            {
                HasBrush = Brush != null,
                Brush = Brush,
                HasPen = Pen != null,
                Pen = Pen,
                HasGeometry = Geometry != null,
                Geometry = Geometry
            };
            return true;
        }
    }

    private sealed class FakeRectangleGeometry : IPortableGeometryPathSource
    {
        public FakeRectangleGeometry(FakeRect rect)
        {
            Rect = rect;
        }

        public FakeRect Rect { get; }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                FillRule = PortableFillRule.Nonzero,
                Bounds = new PortableRect(Rect.X, Rect.Y, Rect.Width, Rect.Height),
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(Rect.X, Rect.Y),
                        IsClosed = true,
                        IsFilled = true,
                        Segments =
                        [
                            PortablePathSegment.Line(new PortablePoint(Rect.X + Rect.Width, Rect.Y), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(Rect.X + Rect.Width, Rect.Y + Rect.Height), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(Rect.X, Rect.Y + Rect.Height), isSmoothJoin: false, isStroked: true)
                        ]
                    }
                ]
            };
            return true;
        }
    }

    private sealed class FakeTriangleGeometry : IPortableGeometryPathSource
    {
        public FakeTriangleGeometry(FakeRect rect)
        {
            Rect = rect;
        }

        public FakeRect Rect { get; }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                FillRule = PortableFillRule.Nonzero,
                Bounds = new PortableRect(Rect.X, Rect.Y, Rect.Width, Rect.Height),
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(Rect.X, Rect.Y),
                        IsClosed = true,
                        IsFilled = true,
                        Segments =
                        [
                            PortablePathSegment.Line(new PortablePoint(Rect.X + Rect.Width, Rect.Y), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(Rect.X + Rect.Width / 2, Rect.Y + Rect.Height), isSmoothJoin: false, isStroked: true)
                        ]
                    }
                ]
            };
            return true;
        }
    }

    private sealed class FakePortableGlyphRunSource : IPortableGlyphRunSource
    {
        private readonly PortableGlyphRun _glyphRun;

        public FakePortableGlyphRunSource(PortableGlyphRun glyphRun)
        {
            _glyphRun = glyphRun;
        }

        public int ReflectedGlyphRunProbeCount { get; private set; }

        public object? GlyphIndices => ThrowReflectedGlyphRunProbe();

        public object? AdvanceWidths => ThrowReflectedGlyphRunProbe();

        public object? BaselineOrigin => ThrowReflectedGlyphRunProbe();

        public object? FontRenderingEmSize => ThrowReflectedGlyphRunProbe();

        public object? GlyphTypeface => ThrowReflectedGlyphRunProbe();

        public bool TryGetPortableGlyphRun(out PortableGlyphRun glyphRun)
        {
            glyphRun = _glyphRun;
            return true;
        }

        private object? ThrowReflectedGlyphRunProbe([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            ReflectedGlyphRunProbeCount++;
            throw new InvalidOperationException($"Reflected glyph-run property '{propertyName}' should not be read.");
        }
    }

    private sealed class FakeImageDrawing : IPortableImageDrawingStateSource
    {
        public FakeImageDrawing(object? imageSource, FakeRect rect)
        {
            ImageSource = imageSource;
            Rect = rect;
        }

        public object? ImageSource { get; }

        public FakeRect Rect { get; }

        public bool TryGetPortableImageDrawingState(out PortableImageDrawingState state)
        {
            state = new PortableImageDrawingState
            {
                HasImageSource = ImageSource != null,
                ImageSource = ImageSource,
                HasRect = true,
                Rect = new PortableRect(Rect.X, Rect.Y, Rect.Width, Rect.Height)
            };
            return true;
        }
    }

    private sealed class FakeDrawingGroup : IPortableDrawingGroupStateSource
    {
        private readonly object[] _children;

        public FakeDrawingGroup(params object[] children)
        {
            _children = children;
            Children = new FakeDrawingCollection(children);
        }

        public FakeDrawingCollection Children { get; }

        public bool TryGetPortableDrawingGroupState(out PortableDrawingGroupState state)
        {
            state = new PortableDrawingGroupState
            {
                Children = _children
            };
            return true;
        }
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

    private static bool TryCreatePortableTileBrush(
        PortableTileBrushKind kind,
        object? content,
        out PortableTileBrush brush)
    {
        return TryCreatePortableTileBrush(kind, content, PortableStretch.Fill, out brush);
    }

    private static bool TryCreatePortableTileBrush(
        PortableTileBrushKind kind,
        object? content,
        PortableStretch stretch,
        out PortableTileBrush brush)
    {
        brush = null!;
        if (content == null)
        {
            return false;
        }

        brush = new PortableTileBrush(
            kind,
            content,
            opacity: 1,
            viewport: new PortableRect(0, 0, 1, 1),
            viewbox: new PortableRect(0, 0, 1, 1),
            viewportUnits: PortableBrushMappingMode.RelativeToBoundingBox,
            viewboxUnits: PortableBrushMappingMode.RelativeToBoundingBox,
            tileMode: PortableTileMode.None,
            stretch: stretch,
            alignmentX: PortableAlignmentX.Center,
            alignmentY: PortableAlignmentY.Center,
            hasTransform: false,
            transform: PortableMatrix3x2.Identity,
            hasRelativeTransform: false,
            relativeTransform: PortableMatrix3x2.Identity);
        return true;
    }

    private sealed class FakeImageBrush : IPortableTileBrushSource
    {
        public FakeImageBrush(object? imageSource)
        {
            ImageSource = imageSource;
        }

        public object? ImageSource { get; }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            return TryCreatePortableTileBrush(PortableTileBrushKind.Image, ImageSource, out brush);
        }
    }

    private sealed class FakeMediaImageBrush : MediaBrush, IPortableTileBrushSource
    {
        public FakeMediaImageBrush(object? imageSource)
        {
            ImageSource = imageSource;
        }

        public object? ImageSource { get; }

        public override global::ProGPU.Vector.Brush ToNative()
        {
            return Brushes.Red.ToNative();
        }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            return TryCreatePortableTileBrush(PortableTileBrushKind.Image, ImageSource, out brush);
        }
    }

    private sealed class FakeDrawingBrush : IPortableTileBrushSource
    {
        private readonly PortableStretch _stretch;

        public FakeDrawingBrush(object? drawing, PortableStretch stretch = PortableStretch.Fill)
        {
            Drawing = drawing;
            _stretch = stretch;
        }

        public object? Drawing { get; }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            return TryCreatePortableTileBrush(PortableTileBrushKind.Drawing, Drawing, _stretch, out brush);
        }
    }

    private sealed class FakeMediaDrawingBrush : MediaBrush, IPortableTileBrushSource
    {
        public FakeMediaDrawingBrush(object? drawing)
        {
            Drawing = drawing;
        }

        public object? Drawing { get; }

        public override global::ProGPU.Vector.Brush ToNative()
        {
            return Brushes.Blue.ToNative();
        }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            return TryCreatePortableTileBrush(PortableTileBrushKind.Drawing, Drawing, out brush);
        }
    }

    private sealed class FakeTranslateTransform : IPortableTransformMatrixSource
    {
        public FakeTranslateTransform(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }

        public bool TryGetPortableTransformMatrix(out PortableMatrix3x2 matrix)
        {
            matrix = new PortableMatrix3x2(1, 0, 0, 1, X, Y);
            return true;
        }
    }

    private sealed class FakeBlurBitmapEffect : IPortableEffectSource
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

    private sealed class FakeBlurEffect : IPortableEffectSource
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

    private sealed class FakeContextBitmapEffectInput : IPortableBitmapEffectInputSource
    {
        public bool TryGetPortableBitmapEffectInput(out PortableBitmapEffectInput input)
        {
            input = new PortableBitmapEffectInput(
                usesContextInput: true,
                hasDefaultAreaToApplyEffect: true);
            return true;
        }
    }

    private sealed class FakeBitmapSourceEffectInput : IPortableBitmapEffectInputSource
    {
        public bool TryGetPortableBitmapEffectInput(out PortableBitmapEffectInput input)
        {
            input = new PortableBitmapEffectInput(
                usesContextInput: false,
                hasDefaultAreaToApplyEffect: true);
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

    private sealed class FakeGuidelineSet : IPortableGuidelineSetSource
    {
        private readonly PortableGuidelineSet _guidelineSet;

        public FakeGuidelineSet(double[] guidelinesX, double[] guidelinesY)
        {
            _guidelineSet = new PortableGuidelineSet(
                isFrozen: true,
                isDynamic: true,
                guidelinesX,
                guidelinesY);
        }

        public bool TryGetPortableGuidelineSet(out PortableGuidelineSet guidelineSet)
        {
            guidelineSet = _guidelineSet;
            return true;
        }
    }

    private class RecordingSink :
        IWpfCompositionCommandSink,
        IWpfVisualEffectCommandSink,
        IWpfRetainedVisualBranchSink,
        IWpfNativeTransformCommandSink
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

        public List<Matrix4x4> NativeTransforms { get; } = new();

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

        public void PushNativeTransform(Matrix4x4 transform)
        {
            Operations.Add("PushNativeTransform");
            NativeTransforms.Add(transform);
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

    private sealed class NativeRecordingSink :
        RecordingSink,
        IWpfNativePrimitiveCommandSink,
        IWpfNativeGeometryCommandSink,
        IWpfNativeClipCommandSink
    {
        public List<(MediaPen? Pen, WpfReplayPoint Point0, WpfReplayPoint Point1)> NativeLines { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, WpfReplayRect Rectangle)> NativeRectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, WpfReplayRect Rectangle, double RadiusX, double RadiusY)> NativeRoundedRectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, WpfReplayPoint Center, double RadiusX, double RadiusY)> NativeEllipses { get; } = new();

        public List<(MediaImageSource ImageSource, WpfReplayRect Rectangle)> NativeImages { get; } = new();

        public List<(MediaBrush? ForegroundBrush, object GlyphRunResource)> NativeGlyphRuns { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, PortableGeometryPath Geometry)> NativeGeometries { get; } = new();

        public List<PortableGeometryPath> NativeGeometryClips { get; } = new();

        public List<WpfReplayRect> NativeClips { get; } = new();

        public void DrawNativeLine(MediaPen? pen, WpfReplayPoint point0, WpfReplayPoint point1)
        {
            Operations.Add("DrawNativeLine");
            NativeLines.Add((pen, point0, point1));
        }

        public void DrawNativeRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle)
        {
            Operations.Add("DrawNativeRectangle");
            NativeRectangles.Add((brush, pen, rectangle));
        }

        public void DrawNativeRoundedRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle, double radiusX, double radiusY)
        {
            Operations.Add("DrawNativeRoundedRectangle");
            NativeRoundedRectangles.Add((brush, pen, rectangle, radiusX, radiusY));
        }

        public void DrawNativeEllipse(MediaBrush? brush, MediaPen? pen, WpfReplayPoint center, double radiusX, double radiusY)
        {
            Operations.Add("DrawNativeEllipse");
            NativeEllipses.Add((brush, pen, center, radiusX, radiusY));
        }

        public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle)
        {
            Operations.Add("DrawNativeImage");
            NativeImages.Add((imageSource, rectangle));
        }

        public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle, WpfReplayRect sourceRectangle)
        {
            Operations.Add("DrawNativeImageSourceRect");
        }

        public void DrawNativeGlyphRun(MediaBrush? foregroundBrush, object glyphRunResource)
        {
            Operations.Add("DrawNativeGlyphRun");
            NativeGlyphRuns.Add((foregroundBrush, glyphRunResource));
        }

        public void PushNativeOpacityMask(MediaBrush? opacityMask, WpfReplayRect bounds)
        {
            Operations.Add("PushNativeOpacityMask");
        }

        public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, PortableGeometryPath geometry)
        {
            Operations.Add("DrawNativeGeometry");
            NativeGeometries.Add((brush, pen, geometry));
            return true;
        }

        public bool PushNativeGeometryClip(PortableGeometryPath clipGeometry)
        {
            Operations.Add("PushNativeGeometryClip");
            NativeGeometryClips.Add(clipGeometry);
            return true;
        }

        public void PushNativeClip(WpfReplayRect bounds)
        {
            Operations.Add("PushNativeClip");
            NativeClips.Add(bounds);
        }
    }

    private sealed class NativeClipRecordingSink : RecordingSink, IWpfNativeClipCommandSink
    {
        public List<WpfReplayRect> NativeClips { get; } = new();

        public void PushNativeClip(WpfReplayRect bounds)
        {
            Operations.Add("PushNativeClip");
            NativeClips.Add(bounds);
        }
    }
}
