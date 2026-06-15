using System.Buffers.Binary;
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

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfMilRenderDataDecoderTests
{
    [Fact]
    public void DecodeDrawRectangleResolvesBrushAndPen()
    {
        var brush = Brushes.Red;
        var pen = new Pen(Brushes.Black, 2);
        var resolver = new TestResolver
        {
            Brush = brush,
            Pen = pen
        };
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);
        WriteUInt32(payload, 36, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Single(sink.DrawRectangles);
        Assert.Same(brush, sink.DrawRectangles[0].Brush);
        Assert.Same(pen, sink.DrawRectangles[0].Pen);
        Assert.Equal(1, sink.DrawRectangles[0].Rectangle.X);
        Assert.Equal(2, sink.DrawRectangles[0].Rectangle.Y);
        Assert.Equal(30, sink.DrawRectangles[0].Rectangle.Width);
        Assert.Equal(40, sink.DrawRectangles[0].Rectangle.Height);
    }

    [Fact]
    public void DecodeSkipsPopForUnresolvedPush()
    {
        var pushClipPayload = new byte[8];
        WriteUInt32(pushClipPayload, 0, 99);

        var renderData = CreateRecord(WpfMilCommandId.PushClip, pushClipPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var sink = new TestSink();
        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, new TestResolver());

        Assert.Equal(new WpfMilDecodeResult(2, 0, 2, 0), result);
        Assert.Equal(0, sink.PopCount);
    }

    [Fact]
    public void DecodeNullResourcePushesAsNoOpScopes()
    {
        var pushClipPayload = new byte[8];
        WriteUInt32(pushClipPayload, 0, 0);
        var pushOpacityMaskPayload = new byte[24];
        WriteUInt32(pushOpacityMaskPayload, 16, 0);
        var pushTransformPayload = new byte[8];
        WriteUInt32(pushTransformPayload, 0, 0);

        var renderData = CreateRecord(WpfMilCommandId.PushClip, pushClipPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.PushOpacityMask, pushOpacityMaskPayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.PushTransform, pushTransformPayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var sink = new TestSink();
        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, new TestResolver());

        Assert.Equal(new WpfMilDecodeResult(6, 6, 0, 0), result);
        Assert.Equal(3, sink.NoOpScopeCount);
        Assert.Equal(3, sink.PopCount);
    }

    [Fact]
    public void DecodeGuidelinePushesAsNoOpScopes()
    {
        var guidelineSetPayload = new byte[8];
        WriteUInt32(guidelineSetPayload, 0, 1);
        var guidelineY1Payload = new byte[8];
        WriteDouble(guidelineY1Payload, 0, 12.5);
        var guidelineY2Payload = new byte[16];
        WriteDouble(guidelineY2Payload, 0, 20.5);
        WriteDouble(guidelineY2Payload, 8, 3.25);

        var renderData = CreateRecord(WpfMilCommandId.PushGuidelineSet, guidelineSetPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.PushGuidelineY1, guidelineY1Payload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.PushGuidelineY2, guidelineY2Payload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var sink = new TestSink();
        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, new TestResolver());

        Assert.Equal(new WpfMilDecodeResult(6, 6, 0, 0), result);
        Assert.Equal(1, sink.GuidelineSetCount);
        Assert.Equal(new[] { 12.5 }, sink.GuidelineY1Coordinates);
        var guidelineY2 = Assert.Single(sink.GuidelineY2Coordinates);
        Assert.Equal(20.5, guidelineY2.LeadingCoordinate);
        Assert.Equal(3.25, guidelineY2.OffsetToDrivenCoordinate);
        Assert.Equal(3, sink.PopCount);
    }

    [Fact]
    public void DecodeAnimatedRecordsReplayBaseValuesAndCountAnimationHandlesAsUnsupportedState()
    {
        var imageSource = new FakeImageSource();
        var resolver = new TestResolver { ImageSource = imageSource };
        var linePayload = new byte[48];
        WritePoint(linePayload, 0, 1, 2);
        WritePoint(linePayload, 16, 3, 4);
        WriteUInt32(linePayload, 36, 10);
        WriteUInt32(linePayload, 40, 11);
        var rectanglePayload = new byte[48];
        WriteRect(rectanglePayload, 0, 5, 6, 7, 8);
        WriteUInt32(rectanglePayload, 40, 12);
        var roundedRectanglePayload = new byte[72];
        WriteRect(roundedRectanglePayload, 0, 9, 10, 11, 12);
        WriteDouble(roundedRectanglePayload, 32, 2);
        WriteDouble(roundedRectanglePayload, 40, 3);
        WriteUInt32(roundedRectanglePayload, 56, 13);
        WriteUInt32(roundedRectanglePayload, 64, 14);
        var ellipsePayload = new byte[56];
        WritePoint(ellipsePayload, 0, 13, 14);
        WriteDouble(ellipsePayload, 16, 15);
        WriteDouble(ellipsePayload, 24, 16);
        WriteUInt32(ellipsePayload, 44, 15);
        WriteUInt32(ellipsePayload, 48, 16);
        var imagePayload = new byte[40];
        WriteRect(imagePayload, 0, 17, 18, 19, 20);
        WriteUInt32(imagePayload, 32, 1);
        WriteUInt32(imagePayload, 36, 17);
        var opacityPayload = new byte[16];
        WriteDouble(opacityPayload, 0, 0.5);
        WriteUInt32(opacityPayload, 8, 18);

        var renderData = CreateRecord(WpfMilCommandId.DrawLineAnimate, linePayload)
            .Concat(CreateRecord(WpfMilCommandId.DrawRectangleAnimate, rectanglePayload))
            .Concat(CreateRecord(WpfMilCommandId.DrawRoundedRectangleAnimate, roundedRectanglePayload))
            .Concat(CreateRecord(WpfMilCommandId.DrawEllipseAnimate, ellipsePayload))
            .Concat(CreateRecord(WpfMilCommandId.DrawImageAnimate, imagePayload))
            .Concat(CreateRecord(WpfMilCommandId.PushOpacityAnimate, opacityPayload))
            .ToArray();

        var sink = new TestSink();
        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(6, 6, 0, 9), result);
        Assert.Equal(1, sink.LineCount);
        Assert.Single(sink.DrawRectangles);
        Assert.Equal(1, sink.RoundedRectangleCount);
        Assert.Equal(1, sink.EllipseCount);
        Assert.Same(imageSource, Assert.Single(sink.Images));
        Assert.Equal(new[] { 0.5 }, sink.Opacities);
    }

    [Fact]
    public void DecodeAnimatedRecordsWithZeroHandlesDoNotCountUnsupportedState()
    {
        var rectanglePayload = new byte[48];
        WriteRect(rectanglePayload, 0, 5, 6, 7, 8);
        WriteUInt32(rectanglePayload, 40, 0);

        var sink = new TestSink();
        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangleAnimate, rectanglePayload),
            sink,
            new TestResolver());

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void ResourceRegistryUsesOneBasedDependentResourceTokens()
    {
        var brush = Brushes.Blue;
        var pen = new Pen(Brushes.Black, 1);
        var registry = WpfMilResourceRegistry.FromDependentResources(new object?[] { brush, pen });

        Assert.Same(brush, registry.ResolveBrush(1));
        Assert.Same(pen, registry.ResolvePen(2));
        Assert.Null(registry.ResolveBrush(0));
        Assert.Null(registry.ResolvePen(1));
    }

    private static byte[] CreateRecord(WpfMilCommandId commandId, byte[] payload)
    {
        var record = new byte[payload.Length + 8];
        WriteInt32(record, 0, record.Length);
        WriteInt32(record, 4, (int)commandId);
        payload.CopyTo(record.AsSpan(8));
        return record;
    }

    private static void WriteRect(byte[] target, int offset, double x, double y, double width, double height)
    {
        WriteDouble(target, offset, x);
        WriteDouble(target, offset + 8, y);
        WriteDouble(target, offset + 16, width);
        WriteDouble(target, offset + 24, height);
    }

    private static void WritePoint(byte[] target, int offset, double x, double y)
    {
        WriteDouble(target, offset, x);
        WriteDouble(target, offset + 8, y);
    }

    private static void WriteInt32(byte[] target, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), value);
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset, 4), value);
    }

    private static void WriteDouble(byte[] target, int offset, double value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(target.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(value));
    }

    private sealed class TestResolver : IWpfMilResourceResolver
    {
        public MediaBrush? Brush { get; init; }

        public MediaPen? Pen { get; init; }

        public MediaImageSource? ImageSource { get; init; }

        public MediaBrush? ResolveBrush(uint resourceToken) => Brush;

        public MediaPen? ResolvePen(uint resourceToken) => Pen;

        public MediaGeometry? ResolveGeometry(uint resourceToken) => null;

        public MediaImageSource? ResolveImageSource(uint resourceToken) => ImageSource;

        public MediaGlyphRun? ResolveGlyphRun(uint resourceToken) => null;

        public MediaTransform? ResolveTransform(uint resourceToken) => null;
    }

    private sealed class TestSink : IWpfCompositionCommandSink
    {
        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle)> DrawRectangles { get; } = new();

        public List<MediaImageSource> Images { get; } = new();

        public List<double> Opacities { get; } = new();

        public List<double> GuidelineY1Coordinates { get; } = new();

        public List<(double LeadingCoordinate, double OffsetToDrivenCoordinate)> GuidelineY2Coordinates { get; } = new();

        public int GuidelineSetCount { get; private set; }

        public int LineCount { get; private set; }

        public int RoundedRectangleCount { get; private set; }

        public int EllipseCount { get; private set; }

        public int NoOpScopeCount { get; private set; }

        public int PopCount { get; private set; }

        public MediaDrawingContext DrawingContext => null!;

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
            LineCount++;
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
            DrawRectangles.Add((brush, pen, rectangle));
        }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            RoundedRectangleCount++;
        }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
        {
            EllipseCount++;
        }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
            Images.Add(imageSource);
        }

        public void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
        }

        public void PushOpacity(double opacity)
        {
            Opacities.Add(opacity);
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
        }

        public void PushTransform(MediaTransform transform)
        {
        }

        public void PushNoOpScope()
        {
            NoOpScopeCount++;
        }

        public void PushGuidelineSet()
        {
            GuidelineSetCount++;
        }

        public void PushGuidelineY1(double coordinate)
        {
            GuidelineY1Coordinates.Add(coordinate);
        }

        public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            GuidelineY2Coordinates.Add((leadingCoordinate, offsetToDrivenCoordinate));
        }

        public void Pop()
        {
            PopCount++;
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeImageSource : MediaImageSource
    {
    }
}
