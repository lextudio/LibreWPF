using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfMilRenderDataDecoderTests
{
    public static IEnumerable<object[]> GeometryTileMappings()
    {
        foreach (bool native in new[] { false, true })
        foreach (bool drawing in new[] { false, true })
        foreach (int mapping in new[] { 0, 1, 2 })
            yield return new object[] { native, drawing, mapping };
    }

    [Theory]
    [MemberData(nameof(GeometryTileMappings))]
    public void RetainedPathTileFillAndPenUseOneOriginalSourceGeometry(bool native, bool drawing, int mapping)
    {
        var path = CreateNonuniformCornerPath();
        var source = new SingleReadTileGeometry(path);
        var image = new FakeImageSource();
        object content = drawing
            ? new FakeGeometryDrawing(Brushes.Red, new FakePortableGeometry(CreatePortableRectangleGeometry(0, 0, 10, 10)))
            : image;
        var brush = new RectangleTileBrush(drawing ? PortableTileBrushKind.Drawing : PortableTileBrushKind.Image, content, mapping);
        var pen = new Pen(Brushes.Black, 2);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, pen, source });
        TestSink sink = native ? new NativeTestSink() : new TypedNativeGeometryTestSink();

        var result = new WpfMilRenderDataDecoder().Decode(TileGeometryRecord(), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(1, source.Reads);
        Assert.Equal(0, brush.GenericReads);
        Assert.Equal(1, brush.TileReads);
        Assert.Same(path, Assert.Single(native
            ? ((NativeTestSink)sink).NativeGeometryClips : ((TypedNativeGeometryTestSink)sink).NativeGeometryClips));
        var draws = native ? ((NativeTestSink)sink).NativeDrawGeometries : ((TypedNativeGeometryTestSink)sink).NativeDrawGeometries;
        var stroke = Assert.Single(draws.Where(draw => draw.Pen != null));
        Assert.Same(path, stroke.Geometry);
        Assert.Same(pen, stroke.Pen);
        Assert.Null(stroke.Brush);
        int tiles = mapping == 0 ? 1 : 4;
        Assert.Equal(drawing ? tiles + 1 : 1, draws.Count);
        Assert.Equal(drawing ? 0 : tiles, sink.Images.Count);
        Assert.All(sink.Images, actual => Assert.Same(image, actual));
        Assert.Empty(sink.SourceRectangles);
        Assert.Empty(sink.CurvedClips);
        Assert.Empty(sink.DrawGeometries);
        Assert.Equal(1 + (drawing ? tiles : 0), sink.PopCount);
        Assert.Equal(new[] { "Pop", "GeometryPen" }, sink.Operations.TakeLast(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedPathTileClipPreservesOriginalTransformFillRuleAndCurvedHole(bool native)
    {
        var path = CreateNonuniformCornerPath();
        path.FillRule = PortableFillRule.EvenOdd;
        path.Transform = new PortableMatrix3x2(1, 0.25, 0.125, 1, 3, 4);
        path.Figures = [path.Figures[0], new PortablePathFigure
        {
            StartPoint = new PortablePoint(5, 5), IsClosed = true, IsFilled = true,
            Segments = [PortablePathSegment.CubicBezier(new(5, 15), new(15, 15), new(15, 5), false, true)]
        }];
        var source = new SingleReadTileGeometry(path);
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, new FakeImageSource(), 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, new Pen(Brushes.Black, 2), source });
        TestSink sink = native ? new NativeTestSink() : new TypedNativeGeometryTestSink();

        var result = new WpfMilRenderDataDecoder().Decode(TileGeometryRecord(), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(path, Assert.Single(native
            ? ((NativeTestSink)sink).NativeGeometryClips : ((TypedNativeGeometryTestSink)sink).NativeGeometryClips));
        Assert.Equal(PortableFillRule.EvenOdd, path.FillRule);
        Assert.Equal(2, path.Figures.Length);
        Assert.Equal(0.25, path.Transform.M12);
        Assert.Equal(PortablePathSegmentKind.CubicBezier, path.Figures[1].Segments[0].Kind);
        Assert.Equal(1, source.Reads);
        Assert.Empty(sink.SourceRectangles);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RetainedPathTilePenRejectionRemainsUnsupported(bool native, bool unavailablePen)
    {
        var source = new SingleReadTileGeometry(CreateNonuniformCornerPath());
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, new FakeImageSource(), 0);
        object pen = unavailablePen ? new object() : new Pen(Brushes.Black, 2);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, pen, source });
        TestSink sink = native ? new NativeTestSink { AcceptGeometryPen = false }
            : new TypedNativeGeometryTestSink { AcceptGeometryPen = false };

        var result = new WpfMilRenderDataDecoder().Decode(TileGeometryRecord(), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Single(sink.Images);
        Assert.Empty(sink.DrawGeometries);
        Assert.Empty(native ? ((NativeTestSink)sink).NativeDrawGeometries : ((TypedNativeGeometryTestSink)sink).NativeDrawGeometries);
        Assert.Equal(1, sink.PopCount);
        Assert.Equal(1, source.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedPathTileClipRejectionDoesNotFallBackToMediaOrBounds(bool native)
    {
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, new FakeImageSource(), 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, new object(), new SingleReadTileGeometry(CreateNonuniformCornerPath()) });
        TestSink sink = native ? new NativeTestSink { AcceptGeometryClip = false }
            : new TypedNativeGeometryTestSink { AcceptGeometryClip = false };

        var result = new WpfMilRenderDataDecoder().Decode(TileGeometryRecord(penPresent: false), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 0, 1), result);
        Assert.Empty(sink.Images);
        Assert.Empty(sink.CurvedClips);
        Assert.Empty(sink.SourceRectangles);
        Assert.Empty(sink.DrawGeometries);
        Assert.Equal(0, sink.PopCount);
        if (sink is NativeTestSink nativeSink) Assert.Empty(nativeSink.NativeClipBounds);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RetainedPathUnavailableFillKeepsIndependentPenAndFollowingDraw(bool native, bool emptyDrawing)
    {
        var brush = new RectangleTileBrush(emptyDrawing ? PortableTileBrushKind.Drawing : PortableTileBrushKind.Image,
            emptyDrawing ? new EmptyRectangleDrawing() : new FakeImageSource(), 0, available: emptyDrawing);
        var resolver = WpfResourceResolver.FromDependentResources(new object[]
            { brush, new Pen(Brushes.Black, 2), new SingleReadTileGeometry(CreateNonuniformCornerPath()) });
        TestSink sink = native ? new NativeTestSink() : new TypedNativeGeometryTestSink();
        var following = new byte[40];
        WriteRect(following, 0, 30, 40, 10, 10);

        var result = new WpfMilRenderDataDecoder().Decode(TileGeometryRecord()
            .Concat(CreateRecord(WpfMilCommandId.DrawRectangle, following)).ToArray(), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, emptyDrawing ? 0 : 1), result);
        Assert.Empty(sink.Images);
        Assert.Equal(new[] { "GeometryPen", "Rectangle" }, sink.Operations);
        Assert.Equal(0, sink.PopCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedUnavailablePathDoesNotReadBrushOrDrawPen(bool native)
    {
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, new FakeImageSource(), 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[]
            { brush, new Pen(Brushes.Black, 2), new SingleReadTileGeometry(null) });
        TestSink sink = native ? new NativeTestSink() : new TypedNativeGeometryTestSink();

        var result = new WpfMilRenderDataDecoder().Decode(TileGeometryRecord(), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 0, 1), result);
        Assert.Equal(0, brush.TileReads);
        Assert.Equal(0, brush.GenericReads);
        Assert.Empty(sink.Operations);
    }

    private sealed class SingleReadTileGeometry(PortableGeometryPath? path) : IPortableGeometryPathSource
    {
        public int Reads { get; private set; }
        public bool TryGetPortableGeometryPath(out PortableGeometryPath geometry)
        {
            if (++Reads != 1) throw new InvalidOperationException("Source geometry was read again during replay.");
            geometry = path!;
            return path != null;
        }
    }

    private static byte[] TileGeometryRecord(bool penPresent = true)
    {
        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, penPresent ? 2U : 0U);
        WriteUInt32(payload, 8, 3);
        return CreateRecord(WpfMilCommandId.DrawGeometry, payload);
    }

    private static PortableGeometryPath CreateNonuniformCornerPath() => new()
    {
        Bounds = new PortableRect(0, 0, 20, 20),
        Figures = [new PortablePathFigure
        {
            StartPoint = new PortablePoint(3, 0), IsClosed = true, IsFilled = true,
            Segments =
            [
                PortablePathSegment.Line(new(15, 0), false, true),
                PortablePathSegment.Arc(new(20, 5), new(5, 5), 0, false, PortableSweepDirection.Clockwise, false, true),
                PortablePathSegment.Line(new(20, 18), false, true),
                PortablePathSegment.Arc(new(18, 20), new(2, 2), 0, false, PortableSweepDirection.Clockwise, false, true),
                PortablePathSegment.Line(new(7, 20), false, true),
                PortablePathSegment.Arc(new(0, 13), new(7, 7), 0, false, PortableSweepDirection.Clockwise, false, true),
                PortablePathSegment.Line(new(0, 3), false, true),
                PortablePathSegment.Arc(new(3, 0), new(3, 3), 0, false, PortableSweepDirection.Clockwise, false, true)
            ]
        }]
    };
}
