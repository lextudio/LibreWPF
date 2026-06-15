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

public sealed class WpfVisualContentReflectionBridgeTests
{
    [Fact]
    public void ExtractContentReadsDrawingVisualContentField()
    {
        var content = new object();
        var visual = new FakeDrawingVisual(content);

        Assert.Same(content, WpfVisualContentReflectionBridge.ExtractContent(visual));
    }

    [Fact]
    public void ReplayContentReturnsEmptyResultWhenContentIsNull()
    {
        var result = new WpfVisualContentReflectionBridge().ReplayContent(new FakeDrawingVisual(null), new TestSink());

        Assert.Equal(default, result);
    }

    [Fact]
    public void ReplayContentDecodesRenderDataContent()
    {
        var brush = Brushes.Gold;
        var pen = new Pen(Brushes.Black, 2);
        var record = CreateRectangleRecord(1, 2);
        var renderData = new FakeRenderData(record, record.Length, new FakeDependentResources(brush, pen));
        var visual = new FakeDrawingVisual(renderData);
        var sink = new TestSink();

        var result = new WpfVisualContentReflectionBridge().ReplayContent(visual, sink);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Single(sink.DrawRectangles);
        Assert.Same(brush, sink.DrawRectangles[0].Brush);
        Assert.Same(pen, sink.DrawRectangles[0].Pen);
        Assert.Equal(new Rect(1, 2, 30, 40), sink.DrawRectangles[0].Rectangle);
    }

    [Fact]
    public void ReplayContentRejectsUnsupportedContentShape()
    {
        var visual = new FakeDrawingVisual(new object());

        var exception = Assert.Throws<NotSupportedException>(
            () => new WpfVisualContentReflectionBridge().ReplayContent(visual, new TestSink()));

        Assert.Contains("not supported", exception.Message);
    }

    private static byte[] CreateRectangleRecord(uint brushToken, uint penToken)
    {
        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, brushToken);
        WriteUInt32(payload, 36, penToken);
        return CreateRecord(WpfMilCommandId.DrawRectangle, payload);
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

    private sealed class FakeDrawingVisual
    {
        private readonly object? _content;

        public FakeDrawingVisual(object? content)
        {
            _content = content;
        }
    }

    private sealed class FakeRenderData
    {
        private readonly byte[] _buffer;
        private readonly int _curOffset;
        private readonly FakeDependentResources _dependentResources;

        public FakeRenderData(byte[] buffer, int curOffset, FakeDependentResources dependentResources)
        {
            _buffer = buffer;
            _curOffset = curOffset;
            _dependentResources = dependentResources;
        }
    }

    private sealed class FakeDependentResources
    {
        private readonly object?[] _items;

        public FakeDependentResources(params object?[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public object? this[int index] => _items[index];
    }

    private sealed class TestSink : IWpfCompositionCommandSink
    {
        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle)> DrawRectangles { get; } = new();

        public MediaDrawingContext DrawingContext => null!;

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
            DrawRectangles.Add((brush, pen, rectangle));
        }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
        {
        }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
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
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
        }

        public void PushTransform(MediaTransform transform)
        {
        }

        public void Pop()
        {
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }
}
