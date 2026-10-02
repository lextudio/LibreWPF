using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfMilRenderDataDecoderTests
{
    private sealed record CurvedPrimitiveDraw(bool Ellipse, Brush? Brush, Pen? Pen,
        Rect Rectangle, Point Center, double RadiusX, double RadiusY);

    public static IEnumerable<object[]> CurvedTileMappings()
    {
        foreach (bool native in new[] { false, true })
        foreach (bool ellipse in new[] { false, true })
        foreach (bool drawing in new[] { false, true })
        foreach (int mapping in new[] { 0, 1, 2 })
            yield return new object[] { native, ellipse, drawing, mapping };
    }

    public static IEnumerable<object[]> CurvedTilePenCases()
    {
        foreach (bool native in new[] { false, true })
        foreach (bool ellipse in new[] { false, true })
        foreach (bool penPresent in new[] { false, true })
            yield return new object[] { native, ellipse, penPresent };
    }

    [Theory]
    [MemberData(nameof(CurvedTileMappings))]
    public void RetainedCurvedPrimitiveReplaysRawTileBrushWithExactClipAndIndependentPen(
        bool native, bool ellipse, bool drawing, int mapping)
    {
        var image = new FakeImageSource();
        object content = drawing
            ? new FakeGeometryDrawing(Brushes.Red, new FakePortableGeometry(CreatePortableRectangleGeometry(0, 0, 10, 10)))
            : image;
        var brush = new RectangleTileBrush(drawing ? PortableTileBrushKind.Drawing : PortableTileBrushKind.Image, content, mapping);
        var pen = new Pen(Brushes.Black, 2);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, pen });
        TestSink sink = native ? new NativeTestSink() : new TestSink();

        var result = new WpfMilRenderDataDecoder().Decode(CurvedRecord(ellipse), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(0, brush.GenericReads);
        Assert.Equal(1, brush.TileReads);
        // A curved fill must never become the rectangular source-hit scope used
        // by square backgrounds or a destination-bounds-only clip.
        Assert.Empty(sink.SourceRectangles);
        Geometry clip = sink is NativeTestSink nativeClipSink
            ? Assert.Single(nativeClipSink.NativeMediaGeometryClips) : Assert.Single(sink.CurvedClips);
        if (ellipse)
        {
            var exact = Assert.IsType<EllipseGeometry>(clip);
            Assert.Equal(new Point(10, 10), exact.Center);
            Assert.Equal(10, exact.RadiusX);
            Assert.Equal(10, exact.RadiusY);
        }
        else
        {
            var exact = Assert.IsType<RectangleGeometry>(clip);
            Assert.Equal(new Rect(0, 0, 20, 20), exact.Rect);
            Assert.Equal(3, exact.RadiusX);
            Assert.Equal(5, exact.RadiusY);
        }
        if (sink is NativeTestSink nativeBoundsSink)
            Assert.Empty(nativeBoundsSink.NativeClipBounds);
        int tiles = mapping == 0 ? 1 : 4;
        if (drawing)
        {
            Assert.Equal(tiles, sink is NativeTestSink nativeDrawingSink
                ? nativeDrawingSink.NativeDrawGeometries.Count : sink.DrawGeometries.Count);
            Assert.Empty(sink.Images);
        }
        else
        {
            Assert.Equal(tiles, sink.Images.Count);
            Assert.All(sink.Images, actual => Assert.Same(image, actual));
        }
        var stroke = Assert.Single(sink.CurvedDraws);
        Assert.Equal(ellipse, stroke.Ellipse);
        Assert.Null(stroke.Brush);
        Assert.Same(pen, stroke.Pen);
        Assert.Equal(ellipse ? 10 : 3, stroke.RadiusX);
        Assert.Equal(ellipse ? 10 : 5, stroke.RadiusY);
        Assert.Equal(1 + (drawing ? tiles : 0), sink.PopCount);
        Assert.Equal(new[] { "Pop", ellipse ? "Ellipse" : "RoundedRectangle" }, sink.Operations.TakeLast(2));
    }

    [Theory]
    [MemberData(nameof(CurvedTilePenCases))]
    public void RetainedUnavailableCurvedTileFillCannotBecomeNullBrushSuccess(bool native, bool ellipse, bool penPresent)
    {
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, new FakeImageSource(), 0, available: false);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, new Pen(Brushes.Black, 2) });
        TestSink sink = native ? new NativeTestSink() : new TestSink();

        var result = new WpfMilRenderDataDecoder().Decode(CurvedRecord(ellipse, penPresent), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, penPresent ? 1 : 0, 0, 1), result);
        Assert.Equal(0, brush.GenericReads);
        Assert.Empty(sink.Images);
        Assert.Empty(sink.CurvedClips);
        Assert.Equal(penPresent ? 1 : 0, sink.CurvedDraws.Count);
        Assert.Equal(0, sink.PopCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RetainedCurvedTileFillKeepsUnavailablePenUnsupported(bool native, bool ellipse)
    {
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, new FakeImageSource(), 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, new object() });
        TestSink sink = native ? new NativeTestSink() : new TestSink();

        var result = new WpfMilRenderDataDecoder().Decode(CurvedRecord(ellipse), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Single(sink.Images);
        Assert.Empty(sink.CurvedDraws);
        Assert.Equal(1, sink.PopCount);
    }

    [Theory]
    [MemberData(nameof(CurvedTilePenCases))]
    public void RetainedEmptyCurvedDrawingKeepsItsIndependentPen(bool native, bool ellipse, bool penPresent)
    {
        var brush = new RectangleTileBrush(PortableTileBrushKind.Drawing, new EmptyRectangleDrawing(), 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush, new Pen(Brushes.Black, 2) });
        TestSink sink = native ? new NativeTestSink() : new TestSink();

        var result = new WpfMilRenderDataDecoder().Decode(CurvedRecord(ellipse, penPresent), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, penPresent ? 1 : 0, penPresent ? 0 : 1, 0), result);
        Assert.Empty(sink.Images);
        Assert.Empty(sink.DrawGeometries);
        Assert.Equal(penPresent ? 1 : 0, sink.CurvedDraws.Count);
        Assert.Equal(0, sink.PopCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RetainedAnimatedCurvedTileFillKeepsOriginalAnimationAccounting(bool native, bool ellipse)
    {
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, new FakeImageSource(), 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush });
        TestSink sink = native ? new NativeTestSink() : new TestSink();

        var result = new WpfMilRenderDataDecoder().Decode(CurvedRecord(ellipse, false, animated: true), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 3), result);
        Assert.Equal(0, brush.GenericReads);
        Assert.Single(sink.Images);
        Assert.Equal(1, sink.PopCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RetainedCurvedTileFillUsesTheResourceOwnedImageAdapter(bool native, bool ellipse)
    {
        object source = new();
        var image = new FakeImageSource();
        var adapter = new RecordingImageSourceAdapter(image);
        var brush = new RectangleTileBrush(PortableTileBrushKind.Image, source, 0);
        var resolver = WpfResourceResolver.FromDependentResources(new object[] { brush }, adapter);
        TestSink sink = native ? new NativeTestSink() : new TestSink();

        var result = new WpfMilRenderDataDecoder().Decode(CurvedRecord(ellipse, false), sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(source, Assert.Single(adapter.Sources));
        Assert.Same(image, Assert.Single(sink.Images));
        Assert.Equal(0, brush.GenericReads);
    }

    private static byte[] CurvedRecord(bool ellipse, bool penPresent = true, bool animated = false)
    {
        var payload = new byte[ellipse ? (animated ? 56 : 40) : (animated ? 72 : 56)];
        if (ellipse)
        {
            WritePoint(payload, 0, 10, 10);
            WriteDouble(payload, 16, 10);
            WriteDouble(payload, 24, 10);
        }
        else
        {
            WriteRect(payload, 0, 0, 0, 20, 20);
            WriteDouble(payload, 32, 3);
            WriteDouble(payload, 40, 5);
        }
        int brushOffset = ellipse ? 32 : 48;
        WriteUInt32(payload, brushOffset, 1);
        WriteUInt32(payload, brushOffset + 4, penPresent ? 2U : 0U);
        if (animated)
        {
            WriteUInt32(payload, brushOffset + 8, 99);
            WriteUInt32(payload, brushOffset + 12, 100);
            WriteUInt32(payload, brushOffset + 16, 101);
        }
        return CreateRecord(ellipse
            ? animated ? WpfMilCommandId.DrawEllipseAnimate : WpfMilCommandId.DrawEllipse
            : animated ? WpfMilCommandId.DrawRoundedRectangleAnimate : WpfMilCommandId.DrawRoundedRectangle, payload);
    }
}
