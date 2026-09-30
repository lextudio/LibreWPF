using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfMilRenderDataDecoderTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    [InlineData(false, false, 2)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 2)]
    public void RetainedRectangleReplaysRawTileBrushBeforeGenericAdaptation(bool native, bool drawing, int mapping)
    {
        var image = new FakeImageSource();
        object content = drawing
            ? new FakeGeometryDrawing(Brushes.Red, new FakePortableGeometry(CreatePortableRectangleGeometry(0, 0, 10, 10)))
            : image;
        var brush = new RectangleTileBrush(drawing ? PortableTileBrushKind.Drawing : PortableTileBrushKind.Image,
            content, mapping);
        var pen = new Pen(Brushes.Black, 2);
        // Use the actual retained dependent-resource resolver. A canonical
        // TileBrush has no generic PortableBrush representation.
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, pen });
        TestSink sink = native ? new NativeTestSink() : new TestSink();

        var result = new WpfMilRenderDataDecoder().Decode(RectangleRecord(), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(0, brush.GenericReads);
        Assert.Equal(1, brush.TileReads);
        var source = Assert.Single(sink.SourceRectangles);
        Assert.Equal(new WpfReplayRect(0, 0, 20, 20), source);
        int tiles = mapping == 0 ? 1 : 4;
        if (drawing)
        {
            int draws = sink is NativeTestSink nativeSink
                ? nativeSink.NativeDrawGeometries.Count : sink.DrawGeometries.Count;
            Assert.Equal(tiles, draws);
            Assert.Empty(sink.Images);
        }
        else
        {
            Assert.Equal(tiles, sink.Images.Count);
            Assert.All(sink.Images, actual => Assert.Same(image, actual));
        }
        if (sink is NativeTestSink nativePenSink)
        {
            var stroke = Assert.Single(nativePenSink.NativeRectangles);
            Assert.Null(stroke.Brush);
            Assert.Same(pen, stroke.Pen);
            Assert.Equal(source, stroke.Rectangle);
            Assert.Equal(source, Assert.Single(nativePenSink.NativeClipBounds));
        }
        else
        {
            var stroke = Assert.Single(sink.DrawRectangles);
            Assert.Null(stroke.Brush);
            Assert.Same(pen, stroke.Pen);
            Assert.Equal(new Rect(0, 0, 20, 20), stroke.Rectangle);
            Assert.Equal(1, sink.ClipCount);
        }
        Assert.Equal(2 + (drawing ? tiles : 0), sink.PopCount);
        Assert.Equal(new[] { "Pop", "Rectangle" }, sink.Operations.TakeLast(2));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RetainedRectangleUnavailableTileBrushIsNotReportedAsNullFillSuccess(bool native, bool penPresent)
    {
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, new FakeImageSource(), 0, available: false);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, new Pen(Brushes.Black, 2) });
        TestSink sink = native ? new NativeTestSink() : new TestSink();

        var result = new WpfMilRenderDataDecoder().Decode(RectangleRecord(penPresent: penPresent), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, penPresent ? 1 : 0, 0, 1), result);
        Assert.Equal(0, brush.GenericReads);
        Assert.Empty(sink.Images);
        Assert.Empty(sink.SourceRectangles);
        Assert.Equal(penPresent ? new[] { "Rectangle" } : Array.Empty<string>(), sink.Operations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedAnimatedTileRectangleKeepsBaseReplayAndUnsupportedAnimationCount(bool native)
    {
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, new FakeImageSource(), 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush });
        TestSink sink = native ? new NativeTestSink() : new TestSink();
        var payload = new byte[48];
        WriteRect(payload, 0, 0, 0, 20, 20);
        WriteUInt32(payload, 32, 1);
        WriteUInt32(payload, 40, 99);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangleAnimate, payload), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Single(sink.Images);
        Assert.Equal(0, brush.GenericReads);
        Assert.Equal(2, sink.PopCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedEmptyTileRectangleDoesNotReadBrushAndPreservesFollowingDraw(bool native)
    {
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, new FakeImageSource(), 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush });
        TestSink sink = native ? new NativeTestSink() : new TestSink();
        var empty = new byte[40];
        WriteRect(empty, 0, double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);
        WriteUInt32(empty, 32, 1);
        var records = CreateRecord(WpfMilCommandId.DrawRectangle, empty)
            .Concat(RectangleRecord(penPresent: false)).ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(records, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result);
        Assert.Equal(1, brush.TileReads);
        Assert.Equal(0, brush.GenericReads);
        Assert.Single(sink.Images);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedTileRectangleUsesTheOwnedResourceImageAdapter(bool native)
    {
        object source = new();
        var image = new FakeImageSource();
        var adapter = new RecordingImageSourceAdapter(image);
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, source, 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush }, adapter);
        TestSink sink = native ? new NativeTestSink() : new TestSink();

        var result = new WpfMilRenderDataDecoder().Decode(RectangleRecord(penPresent: false), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(source, Assert.Single(adapter.Sources));
        Assert.Same(image, Assert.Single(sink.Images));
        Assert.Equal(0, brush.GenericReads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RetainedTileRectanglePreservesUnavailablePenAsUnsupported(bool native, bool emptyDrawing)
    {
        var brush = new RectangleTileBrush(emptyDrawing ? PortableTileBrushKind.Drawing : PortableTileBrushKind.Image,
            emptyDrawing ? new EmptyRectangleDrawing() : new FakeImageSource(), 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, new object() });
        TestSink sink = native ? new NativeTestSink() : new TestSink();

        var result = new WpfMilRenderDataDecoder().Decode(RectangleRecord(), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, emptyDrawing ? 0 : 1, 0, 1), result);
        Assert.Equal(emptyDrawing ? 0 : 1, sink.Images.Count);
        Assert.DoesNotContain("Rectangle", sink.Operations);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RetainedEmptyDrawingBrushKeepsIndependentPenWithoutUnsupportedFill(bool native, bool penPresent)
    {
        var brush = new RectangleTileBrush(PortableTileBrushKind.Drawing, new EmptyRectangleDrawing(), 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, new Pen(Brushes.Black, 2) });
        TestSink sink = native ? new NativeTestSink() : new TestSink();

        var result = new WpfMilRenderDataDecoder().Decode(RectangleRecord(penPresent), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, penPresent ? 1 : 0, penPresent ? 0 : 1, 0), result);
        Assert.Empty(sink.Images);
        Assert.Empty(sink.DrawGeometries);
        Assert.Equal(1, sink.PopCount);
        Assert.Equal(penPresent ? new[] { "SourceRectangle", "Pop", "Rectangle" }
            : new[] { "SourceRectangle", "Pop" }, sink.Operations);
    }

    private sealed class EmptyRectangleDrawing : IPortableDrawingBoundsSource
    {
        public bool TryGetPortableDrawingBounds(out PortableRect bounds)
        {
            bounds = PortableRect.Empty;
            return true;
        }
    }

    private static byte[] RectangleRecord(bool penPresent = true)
    {
        var payload = new byte[40];
        WriteRect(payload, 0, 0, 0, 20, 20);
        WriteUInt32(payload, 32, 1);
        WriteUInt32(payload, 36, penPresent ? 2U : 0U);
        return CreateRecord(WpfMilCommandId.DrawRectangle, payload);
    }

    private sealed class RectangleTileBrush(PortableTileBrushKind kind, object content, int mapping, bool available = true)
        : IPortableTileBrushSource, IPortableBrushSource
    {
        public int GenericReads { get; private set; }
        public int TileReads { get; private set; }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            GenericReads++;
            brush = null!;
            return false;
        }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            TileReads++;
            brush = new PortableTileBrush(kind, content, opacity: 1,
                viewport: mapping == 1 ? new PortableRect(0, 0, 10, 10)
                    : new PortableRect(0, 0, mapping == 2 ? 0.5 : 1, mapping == 2 ? 0.5 : 1),
                viewbox: new PortableRect(0, 0, 1, 1),
                viewportUnits: mapping == 1 ? PortableBrushMappingMode.Absolute : PortableBrushMappingMode.RelativeToBoundingBox,
                viewboxUnits: PortableBrushMappingMode.RelativeToBoundingBox,
                tileMode: mapping == 0 ? PortableTileMode.None : PortableTileMode.Tile,
                stretch: PortableStretch.Fill, alignmentX: PortableAlignmentX.Center, alignmentY: PortableAlignmentY.Center,
                hasTransform: false, transform: PortableMatrix3x2.Identity,
                hasRelativeTransform: false, relativeTransform: PortableMatrix3x2.Identity);
            return available;
        }
    }
}
