using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Text;
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
using ProGpuLinearGradientBrush = ProGPU.Vector.LinearGradientBrush;
using ProGpuRadialGradientBrush = ProGPU.Vector.RadialGradientBrush;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfReflectionResourceResolverTests
{
    [Fact]
    public void DecodeRectangleAdaptsWpfShapedBrushAndPen()
    {
        var brush = new FakeSolidColorBrush(new FakeColor(128, 10, 20, 30), opacity: 0.5);
        var pen = new FakePen(new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)), 4);
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { brush, pen });
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
        var adaptedBrush = Assert.IsType<SolidColorBrush>(sink.DrawRectangles[0].Brush);
        Assert.Equal(64, adaptedBrush.Color.A);
        Assert.Equal(10, adaptedBrush.Color.R);
        Assert.Equal(20, adaptedBrush.Color.G);
        Assert.Equal(30, adaptedBrush.Color.B);
        Assert.NotNull(sink.DrawRectangles[0].Pen);
        Assert.Equal(4, sink.DrawRectangles[0].Pen!.Thickness);
    }

    [Fact]
    public void DecodeRectangleAdaptsPortableSolidBrushAndPen()
    {
        var brush = new FakePortableBrush(
            PortableBrush.SolidColor(new PortableColor(128, 10, 20, 30), opacity: 0.5));
        var pen = new FakePortablePen(
            PortableBrush.SolidColor(new PortableColor(255, 1, 2, 3)),
            thickness: 4,
            startLineCap: PortablePenLineCap.Square,
            endLineCap: PortablePenLineCap.Round,
            dashCap: PortablePenLineCap.Round,
            lineJoin: PortablePenLineJoin.Bevel,
            miterLimit: 2.0,
            dashArray: new[] { 2.0, 3.0 },
            dashOffset: 1.5);
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { brush, pen });
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
        var adaptedBrush = Assert.IsType<SolidColorBrush>(sink.DrawRectangles[0].Brush);
        Assert.Equal(64, adaptedBrush.Color.A);
        Assert.Equal(10, adaptedBrush.Color.R);
        Assert.Equal(20, adaptedBrush.Color.G);
        Assert.Equal(30, adaptedBrush.Color.B);
        var adaptedPen = Assert.IsType<MediaPen>(sink.DrawRectangles[0].Pen);
        Assert.Equal(4, adaptedPen.Thickness);
        Assert.Equal(PenLineCap.Square, adaptedPen.StartLineCap);
        Assert.Equal(PenLineCap.Round, adaptedPen.EndLineCap);
        Assert.Equal(PenLineCap.Round, adaptedPen.DashCap);
        Assert.Equal(PenLineJoin.Bevel, adaptedPen.LineJoin);
        Assert.Equal(2.0, adaptedPen.MiterLimit);
        Assert.NotNull(adaptedPen.DashStyle);
        Assert.Equal(new[] { 2.0, 3.0 }, adaptedPen.DashStyle!.Dashes);
        Assert.Equal(1.5, adaptedPen.DashStyle.Offset);
    }

    [Fact]
    public void DecodeRectangleAdaptsPortableLinearGradientBrush()
    {
        var brush = new FakePortableBrush(
            PortableBrush.LinearGradient(
                new PortablePoint(0, 0),
                new PortablePoint(1, 1),
                new[]
                {
                    new PortableGradientStop(new PortableColor(255, 255, 0, 0), 0),
                    new PortableGradientStop(new PortableColor(128, 0, 0, 255), 1)
                },
                opacity: 0.75,
                spreadMethod: PortableGradientSpreadMethod.Repeat,
                colorInterpolationMode: PortableGradientColorInterpolationMode.ScRgbLinearInterpolation));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { brush });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(Assert.Single(sink.DrawRectangles).Brush!.ToNative());
        Assert.Equal(0, nativeBrush.StartPoint.X);
        Assert.Equal(0, nativeBrush.StartPoint.Y);
        Assert.Equal(1, nativeBrush.EndPoint.X);
        Assert.Equal(1, nativeBrush.EndPoint.Y);
        Assert.Equal(0.75f, nativeBrush.Opacity);
        Assert.Equal(ProGPU.Vector.GradientSpreadMethod.Repeat, nativeBrush.SpreadMethod);
        Assert.Equal(ProGPU.Vector.GradientColorInterpolationMode.ScRgbLinearInterpolation, nativeBrush.ColorInterpolationMode);
        Assert.Equal(2, nativeBrush.Stops.Length);
        Assert.Equal(0f, nativeBrush.Stops[0].Offset);
        Assert.Equal(1f, nativeBrush.Stops[1].Offset);
        Assert.Equal(1f, nativeBrush.Stops[0].Color.X);
        Assert.Equal(0.5f, nativeBrush.Stops[1].Color.W, precision: 2);
    }

    [Fact]
    public void AdaptNativeBrushMapsPortableRelativeRadialGradient()
    {
        var brush = new FakePortableBrush(
            PortableBrush.RadialGradient(
                new PortablePoint(0.5, 0.5),
                new PortablePoint(0.25, 0.75),
                radiusX: 0.25,
                radiusY: 0.5,
                new[]
                {
                    new PortableGradientStop(new PortableColor(255, 0, 255, 0), 0),
                    new PortableGradientStop(new PortableColor(255, 0, 0, 0), 1)
                },
                spreadMethod: PortableGradientSpreadMethod.Reflect));

        var nativeBrush = WpfReflectionResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(10, 20, 100, 50),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        var radialBrush = Assert.IsType<ProGpuRadialGradientBrush>(nativeBrush);
        Assert.Equal(60, radialBrush.Center.X);
        Assert.Equal(45, radialBrush.Center.Y);
        Assert.Equal(35, radialBrush.GradientOrigin.X);
        Assert.Equal(57.5f, radialBrush.GradientOrigin.Y);
        Assert.Equal(25, radialBrush.RadiusX);
        Assert.Equal(25, radialBrush.RadiusY);
        Assert.Equal(ProGPU.Vector.GradientSpreadMethod.Reflect, radialBrush.SpreadMethod);
        Assert.Equal(2, radialBrush.Stops.Length);
    }

    [Fact]
    public void AdaptNativeBrushAppliesPortableLinearGradientTransform()
    {
        var brush = new FakePortableBrush(
            PortableBrush.LinearGradient(
                new PortablePoint(0, 0),
                new PortablePoint(10, 0),
                new[]
                {
                    new PortableGradientStop(new PortableColor(255, 255, 0, 0), 0),
                    new PortableGradientStop(new PortableColor(255, 0, 0, 255), 1)
                },
                mappingMode: PortableBrushMappingMode.Absolute,
                hasTransform: true,
                transform: new PortableMatrix3x2(1, 0, 0, 1, 5, 7)));

        var nativeBrush = WpfReflectionResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 100, 100),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        var linearBrush = Assert.IsType<ProGpuLinearGradientBrush>(nativeBrush);
        Assert.Equal(-5, linearBrush.CoordinateTransform.M41);
        Assert.Equal(-7, linearBrush.CoordinateTransform.M42);
    }

    [Fact]
    public void AdaptNativeBrushCountsNonInvertiblePortableLinearGradientTransform()
    {
        var brush = new FakePortableBrush(
            PortableBrush.LinearGradient(
                new PortablePoint(0, 0),
                new PortablePoint(10, 0),
                new[]
                {
                    new PortableGradientStop(new PortableColor(255, 255, 0, 0), 0),
                    new PortableGradientStop(new PortableColor(255, 0, 0, 255), 1)
                },
                mappingMode: PortableBrushMappingMode.Absolute,
                hasTransform: true,
                transform: new PortableMatrix3x2(0, 0, 0, 1, 0, 0)));

        var nativeBrush = WpfReflectionResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 100, 100),
            out var unsupportedStateCount);

        Assert.Equal(1, unsupportedStateCount);
        var linearBrush = Assert.IsType<ProGpuLinearGradientBrush>(nativeBrush);
        Assert.Equal(Matrix4x4.Identity, linearBrush.CoordinateTransform);
    }

    [Fact]
    public void AdaptNativeBrushAppliesPortableMatrixBrushTransform()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(10, 0),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1))
        {
            MappingMode = "Absolute",
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 5, 7))
        };

        var nativeBrush = WpfReflectionResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 100, 100),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        var linearBrush = Assert.IsType<ProGpuLinearGradientBrush>(nativeBrush);
        Assert.Equal(-5, linearBrush.CoordinateTransform.M41);
        Assert.Equal(-7, linearBrush.CoordinateTransform.M42);
    }

    [Fact]
    public void AdaptNativeBrushCountsNonInvertiblePortableMatrixBrushTransform()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(10, 0),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1))
        {
            MappingMode = "Absolute",
            Transform = new FakeMatrixTransform(new FakeMatrix(0, 0, 0, 1, 0, 0))
        };

        var nativeBrush = WpfReflectionResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 100, 100),
            out var unsupportedStateCount);

        Assert.Equal(1, unsupportedStateCount);
        var linearBrush = Assert.IsType<ProGpuLinearGradientBrush>(nativeBrush);
        Assert.Equal(Matrix4x4.Identity, linearBrush.CoordinateTransform);
    }

    [Fact]
    public void AdaptNativeBrushDoesNotStringProbeUnadaptableBrushTransform()
    {
        var transform = new ThrowingStringTransform();
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(10, 0),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1))
        {
            MappingMode = "Absolute",
            Transform = transform
        };

        var nativeBrush = WpfReflectionResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 100, 100),
            out var unsupportedStateCount);

        Assert.Equal(1, unsupportedStateCount);
        Assert.NotNull(nativeBrush);
        Assert.Equal(0, transform.StringProbeCount);
    }

    [Fact]
    public void AdaptNativePenUsesPortableSolidBrushAndPen()
    {
        var pen = new FakePortablePen(
            PortableBrush.SolidColor(new PortableColor(128, 10, 20, 30), opacity: 0.5),
            thickness: 4,
            startLineCap: PortablePenLineCap.Square,
            endLineCap: PortablePenLineCap.Round,
            dashCap: PortablePenLineCap.Triangle,
            lineJoin: PortablePenLineJoin.Bevel,
            miterLimit: 2.0,
            dashArray: new[] { 2.0, 3.0 },
            dashOffset: 1.5);

        var nativePen = WpfReflectionResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(0, 0, 10, 10),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.NotNull(nativePen);
        Assert.Equal(4, nativePen!.Thickness);
        Assert.Equal(ProGPU.Vector.PenLineCap.Square, nativePen.StartLineCap);
        Assert.Equal(ProGPU.Vector.PenLineCap.Round, nativePen.EndLineCap);
        Assert.Equal(ProGPU.Vector.PenLineCap.Triangle, nativePen.DashCap);
        Assert.Equal(ProGPU.Vector.PenLineJoin.Bevel, nativePen.LineJoin);
        Assert.Equal(2.0f, nativePen.MiterLimit);
        Assert.Equal(new[] { 2.0, 3.0 }, nativePen.DashArray);
        Assert.Equal(1.5, nativePen.DashOffset);
        var nativeBrush = Assert.IsType<ProGPU.Vector.SolidColorBrush>(nativePen.Brush);
        Assert.Equal(10 / 255f, nativeBrush.Color.X, precision: 6);
        Assert.Equal(20 / 255f, nativeBrush.Color.Y, precision: 6);
        Assert.Equal(30 / 255f, nativeBrush.Color.Z, precision: 6);
        Assert.Equal(64 / 255f, nativeBrush.Color.W, precision: 6);
    }

    [Fact]
    public void PortableBrushSourceAbsenceDoesNotFallBackToReflectedBrushShape()
    {
        var brush = new FakeUnavailablePortableSolidColorBrush();

        var mediaBrush = WpfReflectionResourceResolver.AdaptBrush(brush);
        var nativeBrush = WpfReflectionResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 10, 10),
            out var unsupportedStateCount);

        Assert.Null(mediaBrush);
        Assert.Null(nativeBrush);
        Assert.Equal(0, unsupportedStateCount);
        Assert.Equal(0, brush.ReflectedPropertyProbeCount);
    }

    [Fact]
    public void PortablePenSourceAbsenceDoesNotFallBackToReflectedPenShape()
    {
        var pen = new FakeUnavailablePortablePen();

        var mediaPen = WpfReflectionResourceResolver.AdaptPen(pen);
        var nativePen = WpfReflectionResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(0, 0, 10, 10),
            out var unsupportedStateCount);

        Assert.Null(mediaPen);
        Assert.Null(nativePen);
        Assert.Equal(0, unsupportedStateCount);
        Assert.Equal(0, pen.ReflectedPropertyProbeCount);
    }

    [Fact]
    public void AdaptNativeBrushUsesTypedMediaBrushWithoutReflection()
    {
        var brush = new DirectNativeBrush();

        var nativeBrush = WpfReflectionResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(1, 2, 30, 40),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.Same(brush.NativeBrush, nativeBrush);
        Assert.Equal(1, brush.BoundsCallCount);
        Assert.Equal(0, brush.ParameterlessCallCount);
        Assert.Equal(1, brush.LastBounds.X);
        Assert.Equal(2, brush.LastBounds.Y);
        Assert.Equal(30, brush.LastBounds.Width);
        Assert.Equal(40, brush.LastBounds.Height);
    }

    [Fact]
    public void AdaptNativeBrushDoesNotInvokeDuckTypedToNativeMethods()
    {
        var brush = new DuckTypedNativeBrush();

        var nativeBrush = WpfReflectionResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(1, 2, 30, 40),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.Null(nativeBrush);
        Assert.Equal(0, brush.ToNativeCallCount);
    }

    [Fact]
    public void AdaptNativePenDoesNotInvokeDuckTypedToNativeMethod()
    {
        var pen = new DuckTypedNativePen();

        var nativePen = WpfReflectionResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(1, 2, 30, 40),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.Null(nativePen);
        Assert.Equal(0, pen.ToNativeCallCount);
    }

    [Fact]
    public void DecodeRectanglePreservesPositiveWpfShapedPenDashStyleMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)),
            4)
        {
            DashStyle = new FakeDashStyle(new[] { 2.0, 3.0 }, 1.5)
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { Brushes.Red, pen });
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
        var adaptedPen = Assert.IsType<MediaPen>(sink.DrawRectangles[0].Pen);
        Assert.NotNull(adaptedPen.DashStyle);
        Assert.Equal(new[] { 2.0, 3.0 }, adaptedPen.DashStyle!.Dashes);
        Assert.Equal(1.5, adaptedPen.DashStyle.Offset);
    }

    [Fact]
    public void DecodeRectanglePreservesZeroLengthWpfShapedPenDotDashMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)),
            4)
        {
            DashStyle = new FakeDashStyle(new[] { 0.0, 2.0 }, 0)
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { Brushes.Red, pen });
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
        var adaptedPen = Assert.IsType<MediaPen>(sink.DrawRectangles[0].Pen);
        Assert.NotNull(adaptedPen.DashStyle);
        Assert.Equal(new[] { 0.0, 2.0 }, adaptedPen.DashStyle!.Dashes);
    }

    [Fact]
    public void DecodeRectanglePreservesWpfShapedPenLineCapMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)),
            4)
        {
            StartLineCap = "Square",
            EndLineCap = "Round",
            DashCap = "Round"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { Brushes.Red, pen });
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
        var adaptedPen = Assert.IsType<MediaPen>(sink.DrawRectangles[0].Pen);
        Assert.Equal(PenLineCap.Square, adaptedPen.StartLineCap);
        Assert.Equal(PenLineCap.Round, adaptedPen.EndLineCap);
        Assert.Equal(PenLineCap.Round, adaptedPen.DashCap);
    }

    [Fact]
    public void DecodeRectanglePreservesWpfShapedPenLineJoinAndMiterMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)),
            4)
        {
            LineJoin = "Round",
            MiterLimit = 3.5
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { Brushes.Red, pen });
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
        var adaptedPen = Assert.IsType<MediaPen>(sink.DrawRectangles[0].Pen);
        Assert.Equal(PenLineJoin.Round, adaptedPen.LineJoin);
        Assert.Equal(3.5, adaptedPen.MiterLimit);
    }

    [Fact]
    public void DecodeRectangleAdaptsWpfShapedLinearGradientBrush()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(1, 1),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(128, 0, 0, 255), 1))
        {
            Opacity = 0.75,
            SpreadMethod = "Repeat",
            ColorInterpolationMode = "ScRgbLinearInterpolation"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { brush });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(Assert.Single(sink.DrawRectangles).Brush!.ToNative());
        Assert.Equal(0, nativeBrush.StartPoint.X);
        Assert.Equal(0, nativeBrush.StartPoint.Y);
        Assert.Equal(1, nativeBrush.EndPoint.X);
        Assert.Equal(1, nativeBrush.EndPoint.Y);
        Assert.Equal(0.75f, nativeBrush.Opacity);
        Assert.Equal(ProGPU.Vector.GradientSpreadMethod.Repeat, nativeBrush.SpreadMethod);
        Assert.Equal(ProGPU.Vector.GradientColorInterpolationMode.ScRgbLinearInterpolation, nativeBrush.ColorInterpolationMode);
        Assert.Equal(2, nativeBrush.Stops.Length);
        Assert.Equal(0f, nativeBrush.Stops[0].Offset);
        Assert.Equal(1f, nativeBrush.Stops[1].Offset);
        Assert.Equal(1f, nativeBrush.Stops[0].Color.X);
        Assert.Equal(0.5f, nativeBrush.Stops[1].Color.W, precision: 2);
    }

    [Fact]
    public void DecodeRectangleAdaptsWpfShapedRadialGradientBrush()
    {
        var brush = new FakeRadialGradientBrush(
            new FakePoint(0.5, 0.5),
            new FakePoint(0.25, 0.75),
            radiusX: 0.25,
            radiusY: 0.5,
            new FakeGradientStop(new FakeColor(255, 0, 255, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 0), 1))
        {
            SpreadMethod = "Reflect"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { brush });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var nativeBrush = Assert.IsType<ProGpuRadialGradientBrush>(Assert.Single(sink.DrawRectangles).Brush!.ToNative());
        Assert.Equal(0.5f, nativeBrush.Center.X);
        Assert.Equal(0.5f, nativeBrush.Center.Y);
        Assert.Equal(0.25f, nativeBrush.GradientOrigin.X);
        Assert.Equal(0.75f, nativeBrush.GradientOrigin.Y);
        Assert.Equal(0.25f, nativeBrush.RadiusX);
        Assert.Equal(0.5f, nativeBrush.RadiusY);
        Assert.Equal(0.5f, nativeBrush.Radius);
        Assert.Equal(ProGPU.Vector.GradientSpreadMethod.Reflect, nativeBrush.SpreadMethod);
        Assert.Equal(2, nativeBrush.Stops.Length);
    }

    [Fact]
    public void DecodePushTransformAdaptsPortableMatrixTransformContract()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 2, 3, 4, 10, 20));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { transform });
        var sink = new TestSink();

        var pushPayload = new byte[8];
        WriteUInt32(pushPayload, 0, 1);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, pushPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result);
        var adaptedTransform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(1, adaptedTransform.Matrix.M11);
        Assert.Equal(2, adaptedTransform.Matrix.M12);
        Assert.Equal(3, adaptedTransform.Matrix.M21);
        Assert.Equal(4, adaptedTransform.Matrix.M22);
        Assert.Equal(10, adaptedTransform.Matrix.OffsetX);
        Assert.Equal(20, adaptedTransform.Matrix.OffsetY);
        Assert.Equal(1, sink.PopCount);
    }

    [Fact]
    public void DecodePushTransformAdaptsPortableTransformMatrixSource()
    {
        var transform = new FakePortableTransform(new PortableMatrix3x2(1, 2, 3, 4, 10, 20));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { transform });
        var sink = new TestSink();

        var pushPayload = new byte[8];
        WriteUInt32(pushPayload, 0, 1);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, pushPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result);
        var adaptedTransform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(1, adaptedTransform.Matrix.M11);
        Assert.Equal(2, adaptedTransform.Matrix.M12);
        Assert.Equal(3, adaptedTransform.Matrix.M21);
        Assert.Equal(4, adaptedTransform.Matrix.M22);
        Assert.Equal(10, adaptedTransform.Matrix.OffsetX);
        Assert.Equal(20, adaptedTransform.Matrix.OffsetY);
        Assert.Equal(1, sink.PopCount);
    }

    [Fact]
    public void PortableTransformSourceAbsenceDoesNotFallBackToReflectedTransformShape()
    {
        var transform = new FakeUnavailablePortableMatrixTransform(new FakeMatrix(1, 2, 3, 4, 10, 20));

        var mediaTransform = WpfReflectionResourceResolver.AdaptTransform(transform);
        var hasNativeMatrix = WpfReflectionResourceResolver.TryAdaptTransformMatrix(transform, out _);

        Assert.Null(mediaTransform);
        Assert.False(hasNativeMatrix);
        Assert.Equal(0, transform.ReflectedPropertyProbeCount);
    }

    [Fact]
    public void DecodePushTransformRejectsReflectedMatrixShapeWithoutPortableContract()
    {
        var transform = new FakeReflectedMatrixTransform(new FakeMatrix(1, 0, 0, 1, 6, 7));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { transform });
        var sink = new TestSink();

        var pushPayload = new byte[8];
        WriteUInt32(pushPayload, 0, 1);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, pushPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(2, 0, 2, 0), result);
        Assert.Empty(sink.Transforms);
        Assert.Equal(0, sink.PopCount);
        Assert.Equal(0, transform.ReflectedPropertyProbeCount);
    }

    [Fact]
    public void DecodeGeometryAdaptsWpfShapedRectangleGeometry()
    {
        var brush = new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60));
        var geometry = new FakeRectangleGeometry(new FakeRect(5, 6, 70, 80));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { brush, geometry });
        var sink = new TestSink();

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var adaptedGeometry = Assert.IsType<PathGeometry>(Assert.Single(sink.DrawGeometries).Geometry);
        Assert.Equal(new Rect(5, 6, 70, 80), adaptedGeometry.Bounds);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedGeometryDrawing()
    {
        var drawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 100, 110, 120)),
            null,
            new FakeRectangleGeometry(new FakeRect(5, 6, 70, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var replayed = Assert.Single(sink.DrawGeometries);
        Assert.IsType<SolidColorBrush>(replayed.Brush);
        Assert.IsType<PathGeometry>(replayed.Geometry);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushGeometryDrawing()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Viewport = new FakeRect(0.25, 0.125, 0.5, 0.5),
                Opacity = 0.5
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(imageSource, imageAdapter.LastImageSource);
        Assert.Equal(new[] { "PushClip", "PushOpacity", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Equal(0.5, Assert.Single(sink.Opacities));
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(35, 30, 50, 40), replayed.Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysPortableImageTileBrushWithoutReflectedTypeName()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var tileBrush = new PortableTileBrush(
            PortableTileBrushKind.Image,
            imageSource,
            opacity: 0.5,
            viewport: new PortableRect(0.25, 0.125, 0.5, 0.5),
            viewbox: new PortableRect(0, 0, 1, 1),
            viewportUnits: PortableBrushMappingMode.RelativeToBoundingBox,
            viewboxUnits: PortableBrushMappingMode.RelativeToBoundingBox,
            tileMode: PortableTileMode.None,
            stretch: PortableStretch.Fill,
            alignmentX: PortableAlignmentX.Center,
            alignmentY: PortableAlignmentY.Center,
            hasTransform: false,
            transform: PortableMatrix3x2.Identity,
            hasRelativeTransform: false,
            relativeTransform: PortableMatrix3x2.Identity);
        var drawing = new FakeGeometryDrawing(
            new FakePortableTileBrushSource(tileBrush),
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(imageSource, imageAdapter.LastImageSource);
        Assert.Equal(new[] { "PushClip", "PushOpacity", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Equal(0.5, Assert.Single(sink.Opacities));
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(35, 30, 50, 40), replayed.Rectangle);
    }

    [Fact]
    public void PortableTileBrushSourceAbsenceDoesNotFallBackToReflectedImageBrushShape()
    {
        var brush = new FakeUnavailablePortableImageBrush(new FakeBitmapSource());
        var sink = new TestSink();

        var replayed = WpfReflectionDrawingReplay.TryReplayTileBrushFill(
            brush,
            new RectangleGeometry(new Rect(0, 0, 10, 10)),
            sink,
            imageSourceAdapter: _ => new FakeImageSource(),
            out var status);

        Assert.False(replayed);
        Assert.Equal(WpfDrawingReplayStatus.Skipped, status);
        Assert.Empty(sink.Operations);
        Assert.Equal(0, brush.ReflectedPropertyProbeCount);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushWithAbsoluteViewport()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Viewport = new FakeRect(12, 24, 30, 40),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(12, 24, 30, 40), replayed.Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushWithTransform()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(7, transform.Matrix.OffsetX);
        Assert.Equal(9, transform.Matrix.OffsetY);
        Assert.Equal(new Rect(10, 20, 100, 80), Assert.Single(sink.Images).Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushUniformStretch()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Stretch = "Uniform"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        Assert.Equal(new Rect(0, 25, 100, 50), Assert.Single(sink.Images).Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushUniformStretchWithLeftBottomAlignment()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Stretch = "Uniform",
                AlignmentX = "Left",
                AlignmentY = "Bottom"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        Assert.Equal(new Rect(0, 50, 100, 50), Assert.Single(sink.Images).Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushWithRelativeViewbox()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Viewbox = new FakeRect(0.25, 0.2, 0.5, 0.4)
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        var replayed = Assert.Single(sink.SourceImages);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(10, 20, 100, 80), replayed.Rectangle);
        Assert.Equal(new Rect(50, 20, 100, 40), replayed.SourceRectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushWithAbsoluteViewbox()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Viewbox = new FakeRect(12, 16, 32, 40),
                ViewboxUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        var replayed = Assert.Single(sink.SourceImages);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(10, 20, 100, 80), replayed.Rectangle);
        Assert.Equal(new Rect(12, 16, 32, 40), replayed.SourceRectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushTileMode()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        Assert.Equal(new[] { new Rect(0, 0, 10, 10), new Rect(10, 0, 10, 10), new Rect(20, 0, 10, 10) }, sink.Images.Select(image => image.Rectangle).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushFlipXTiling()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                TileMode = "FlipX",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(new[] { new Rect(0, 0, 10, 10), new Rect(10, 0, 10, 10), new Rect(20, 0, 10, 10) }, sink.Images.Select(image => image.Rectangle).ToArray());
        var flipTransform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(-1, flipTransform.Matrix.M11);
        Assert.Equal(1, flipTransform.Matrix.M22);
        Assert.Equal(30, flipTransform.Matrix.OffsetX);
        Assert.Equal(0, flipTransform.Matrix.OffsetY);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushTileModeWithTransform()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(new[] { new Rect(0, 0, 10, 10), new Rect(10, 0, 10, 10), new Rect(20, 0, 10, 10) }, sink.Images.Select(image => image.Rectangle).ToArray());
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(3, transform.Matrix.OffsetX);
        Assert.Equal(4, transform.Matrix.OffsetY);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushTileModeWithRelativeTransform()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                RelativeTransform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 0.5, 0.25))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(new[] { new Rect(0, 0, 10, 10), new Rect(10, 0, 10, 10), new Rect(20, 0, 10, 10) }, sink.Images.Select(image => image.Rectangle).ToArray());
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(1, transform.Matrix.M11);
        Assert.Equal(1, transform.Matrix.M22);
        Assert.Equal(12.5, transform.Matrix.OffsetX);
        Assert.Equal(2.5, transform.Matrix.OffsetY);
    }

    [Fact]
    public void DecodeDrawDrawingCountsUnsupportedImageBrushUnadaptableRelativeTransformButReplaysStroke()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                TileMode = "FlipX",
                RelativeTransform = new object()
            },
            new FakePen(new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)), 2),
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Null(imageAdapter.LastImageSource);
        Assert.Empty(sink.Images);
        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        var replayed = Assert.Single(sink.DrawGeometries);
        Assert.Null(replayed.Brush);
        Assert.NotNull(replayed.Pen);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushGeometryDrawing()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Viewport = new FakeRect(0.25, 0.125, 0.5, 0.5),
                Opacity = 0.5
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushOpacity", "PushTransform", "DrawGeometry", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Equal(0.5, Assert.Single(sink.Opacities));
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(5, transform.Matrix.M11);
        Assert.Equal(4, transform.Matrix.M22);
        Assert.Equal(35, transform.Matrix.OffsetX);
        Assert.Equal(30, transform.Matrix.OffsetY);
        var replayed = Assert.Single(sink.DrawGeometries);
        Assert.IsType<SolidColorBrush>(replayed.Brush);
        Assert.Null(replayed.Pen);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushWithAbsoluteViewport()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Viewport = new FakeRect(12, 24, 30, 40),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawGeometry", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(3, transform.Matrix.M11);
        Assert.Equal(4, transform.Matrix.M22);
        Assert.Equal(12, transform.Matrix.OffsetX);
        Assert.Equal(24, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushUniformToFillStretch()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 50)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Stretch = "UniformToFill"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushClip", "PushTransform", "DrawGeometry", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Clips.Count);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(2, transform.Matrix.M11);
        Assert.Equal(2, transform.Matrix.M22);
        Assert.Equal(-50, transform.Matrix.OffsetX);
        Assert.Equal(0, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushUniformToFillStretchWithRightBottomAlignment()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 50)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Stretch = "UniformToFill",
                AlignmentX = "Right",
                AlignmentY = "Bottom"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushClip", "PushTransform", "DrawGeometry", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Clips.Count);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(2, transform.Matrix.M11);
        Assert.Equal(2, transform.Matrix.M22);
        Assert.Equal(-100, transform.Matrix.OffsetX);
        Assert.Equal(0, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushWithAbsoluteViewbox()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Viewbox = new FakeRect(25, 20, 50, 40),
                ViewboxUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "PushClip", "DrawGeometry", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Clips.Count);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(2, transform.Matrix.M11);
        Assert.Equal(2, transform.Matrix.M22);
        Assert.Equal(-40, transform.Matrix.OffsetX);
        Assert.Equal(-20, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushTileMode()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(
            new[]
            {
                "PushClip",
                "PushTransform",
                "DrawGeometry",
                "Pop",
                "PushTransform",
                "DrawGeometry",
                "Pop",
                "PushTransform",
                "DrawGeometry",
                "Pop",
                "Pop"
            },
            sink.Operations);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
        Assert.Equal(3, sink.DrawGeometries.Count);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushFlipXYTiling()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                TileMode = "FlipXY",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 20, 20)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(4, sink.DrawGeometries.Count);
        var matrices = sink.Transforms.Select(transform => ((MatrixTransform)transform).Matrix).ToArray();
        Assert.Equal(new[] { 1d, -1d, 1d, -1d }, matrices.Select(matrix => matrix.M11).ToArray());
        Assert.Equal(new[] { 1d, 1d, -1d, -1d }, matrices.Select(matrix => matrix.M22).ToArray());
        Assert.Equal(new[] { 0d, 20d, 0d, 20d }, matrices.Select(matrix => matrix.OffsetX).ToArray());
        Assert.Equal(new[] { 0d, 0d, 20d, 20d }, matrices.Select(matrix => matrix.OffsetY).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushTileModeWithTransform()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(3, sink.DrawGeometries.Count);
        Assert.Equal(4, sink.Transforms.Count);
        var brushTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(7, brushTransform.Matrix.OffsetX);
        Assert.Equal(9, brushTransform.Matrix.OffsetY);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Skip(1).Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushTileModeWithRelativeTransform()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                RelativeTransform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 0.5, 0.25))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(3, sink.DrawGeometries.Count);
        Assert.Equal(4, sink.Transforms.Count);
        var relativeTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(12.5, relativeTransform.Matrix.OffsetX);
        Assert.Equal(2.5, relativeTransform.Matrix.OffsetY);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Skip(1).Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushWithTransform()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "PushTransform", "DrawGeometry", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Transforms.Count);
        var brushTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(7, brushTransform.Matrix.OffsetX);
        Assert.Equal(9, brushTransform.Matrix.OffsetY);
        var contentTransform = Assert.IsType<MatrixTransform>(sink.Transforms[1]);
        Assert.Equal(10, contentTransform.Matrix.M11);
        Assert.Equal(8, contentTransform.Matrix.M22);
        Assert.Equal(10, contentTransform.Matrix.OffsetX);
        Assert.Equal(20, contentTransform.Matrix.OffsetY);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingCountsPartiallyReplayedDrawingBrushContentAsUnsupported()
    {
        var nestedGroup = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10))),
            new object());
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedGroup),
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawGeometry", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Single(sink.Transforms);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushGeometryDrawing()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Viewport = new FakeRect(0.25, 0.125, 0.5, 0.5),
                Opacity = 0.5
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushOpacity", "PushTransform", "DrawRectangle", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Equal(0.5, Assert.Single(sink.Opacities));
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(5, transform.Matrix.M11);
        Assert.Equal(4, transform.Matrix.M22);
        Assert.Equal(35, transform.Matrix.OffsetX);
        Assert.Equal(30, transform.Matrix.OffsetY);
        var replayed = Assert.Single(sink.DrawRectangles);
        Assert.Same(Brushes.Green, replayed.Brush);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushWithAbsoluteViewport()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Viewport = new FakeRect(12, 24, 30, 40),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(3, transform.Matrix.M11);
        Assert.Equal(4, transform.Matrix.M22);
        Assert.Equal(12, transform.Matrix.OffsetX);
        Assert.Equal(24, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushNoneStretch()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Stretch = "None"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(1, transform.Matrix.M11);
        Assert.Equal(1, transform.Matrix.M22);
        Assert.Equal(45, transform.Matrix.OffsetX);
        Assert.Equal(35, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushNoneStretchWithRightBottomAlignment()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Stretch = "None",
                AlignmentX = "Right",
                AlignmentY = "Bottom"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(1, transform.Matrix.M11);
        Assert.Equal(1, transform.Matrix.M22);
        Assert.Equal(90, transform.Matrix.OffsetX);
        Assert.Equal(70, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushWithRelativeViewbox()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 100, 100)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Viewbox = new FakeRect(0.25, 0.2, 0.5, 0.4)
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "PushClip", "DrawRectangle", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Clips.Count);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(2, transform.Matrix.M11);
        Assert.Equal(2, transform.Matrix.M22);
        Assert.Equal(-40, transform.Matrix.OffsetX);
        Assert.Equal(-20, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushTileMode()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(
            new[]
            {
                "PushClip",
                "PushTransform",
                "DrawRectangle",
                "Pop",
                "PushTransform",
                "DrawRectangle",
                "Pop",
                "PushTransform",
                "DrawRectangle",
                "Pop",
                "Pop"
            },
            sink.Operations);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
        Assert.Equal(3, sink.DrawRectangles.Count);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushFlipXYTiling()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                TileMode = "FlipXY",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 20, 20)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(4, sink.DrawRectangles.Count);
        var matrices = sink.Transforms.Select(transform => ((MatrixTransform)transform).Matrix).ToArray();
        Assert.Equal(new[] { 1d, -1d, 1d, -1d }, matrices.Select(matrix => matrix.M11).ToArray());
        Assert.Equal(new[] { 1d, 1d, -1d, -1d }, matrices.Select(matrix => matrix.M22).ToArray());
        Assert.Equal(new[] { 0d, 20d, 0d, 20d }, matrices.Select(matrix => matrix.OffsetX).ToArray());
        Assert.Equal(new[] { 0d, 0d, 20d, 20d }, matrices.Select(matrix => matrix.OffsetY).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushTileModeWithTransform()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(3, sink.DrawRectangles.Count);
        Assert.Equal(4, sink.Transforms.Count);
        var brushTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(7, brushTransform.Matrix.OffsetX);
        Assert.Equal(9, brushTransform.Matrix.OffsetY);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Skip(1).Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushTileModeWithRelativeTransform()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                RelativeTransform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 0.5, 0.25))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(3, sink.DrawRectangles.Count);
        Assert.Equal(4, sink.Transforms.Count);
        var relativeTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(12.5, relativeTransform.Matrix.OffsetX);
        Assert.Equal(2.5, relativeTransform.Matrix.OffsetY);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Skip(1).Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushWithTransform()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "PushTransform", "DrawRectangle", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Transforms.Count);
        var brushTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(7, brushTransform.Matrix.OffsetX);
        Assert.Equal(9, brushTransform.Matrix.OffsetY);
        var contentTransform = Assert.IsType<MatrixTransform>(sink.Transforms[1]);
        Assert.Equal(10, contentTransform.Matrix.M11);
        Assert.Equal(8, contentTransform.Matrix.M22);
        Assert.Equal(10, contentTransform.Matrix.OffsetX);
        Assert.Equal(20, contentTransform.Matrix.OffsetY);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingCountsPartiallyReplayedVisualBrushContentAsUnsupported()
    {
        var visual = new FakeDrawingVisual(new object())
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        visual.Children.Add(new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green)));
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual),
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Single(sink.Transforms);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingGroupStateAndChildren()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            Opacity = 0.5
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushTransform", "PushOpacity", "DrawGeometry", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.DrawGeometries);
        Assert.Equal(0.5, Assert.Single(sink.Opacities));
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(3, transform.Matrix.OffsetX);
        Assert.Equal(4, transform.Matrix.OffsetY);
    }

    [Fact]
    public void DecodeDrawDrawingCountsPartiallyReplayedDrawingGroupAsAppliedAndUnsupported()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))),
            new object());
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingGroupOpacityMask()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            Bounds = new FakeRect(1, 2, 30, 40),
            OpacityMask = new FakeSolidColorBrush(new FakeColor(128, 255, 255, 255))
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushOpacityMask", "DrawGeometry", "Pop" }, sink.Operations);
        var replayedMask = Assert.Single(sink.OpacityMasks);
        Assert.IsType<SolidColorBrush>(replayedMask.OpacityMask);
        Assert.Equal(new Rect(1, 2, 30, 40), replayedMask.Bounds);
    }

    [Fact]
    public void DecodeDrawDrawingInfersDrawingGroupOpacityMaskBoundsFromChildren()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(2, 3, 10, 20))),
            new FakeImageDrawing(new FakeBitmapSource(), new FakeRect(20, 30, 40, 50)))
        {
            OpacityMask = new FakeSolidColorBrush(new FakeColor(128, 255, 255, 255))
        };
        var imageAdapter = new FakeImageSourceAdapter();
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushOpacityMask", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new Rect(2, 3, 58, 77), Assert.Single(sink.OpacityMasks).Bounds);
    }

    [Fact]
    public void DecodeDrawDrawingIntersectsInferredDrawingGroupOpacityMaskBoundsWithClip()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100))))
        {
            ClipGeometry = new FakeRectangleGeometry(new FakeRect(10, 20, 30, 40)),
            OpacityMask = new FakeSolidColorBrush(new FakeColor(128, 255, 255, 255))
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new Rect(10, 20, 30, 40), Assert.Single(sink.OpacityMasks).Bounds);
    }

    [Fact]
    public void DecodeDrawDrawingPushesNativeDrawingGroupCacheWhenSinkSupportsDrawingCaches()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(2, 3, 10, 20))))
        {
            Bounds = new FakeRect(1, 2, 30, 40),
            CacheMode = new object()
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink { AcceptDrawingCaches = true };

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushDrawingCache", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new Rect(1, 2, 30, 40), Assert.Single(sink.DrawingCacheBounds));
    }

    [Fact]
    public void DecodeDrawDrawingReportsUnsupportedDrawingGroupCacheAsPartialWhenSinkCannotCache()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            CacheMode = new object()
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Empty(sink.DrawingCacheBounds);
    }

    [Fact]
    public void DecodeDrawDrawingPassesWpfShapedDrawingGroupGuidelineSetToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            GuidelineSet = new object()
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushGuidelineSetObject", "DrawGeometry", "Pop" }, sink.Operations);
    }

    [Fact]
    public void DecodeDrawDrawingPassesSupportedDrawingGroupBitmapScalingModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            BitmapScalingMode = "NearestNeighbor"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushBitmapScalingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "NearestNeighbor" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingPassesHighQualityDrawingGroupBitmapScalingModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            BitmapScalingMode = "HighQuality"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushBitmapScalingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "HighQuality" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingReportsUnsupportedDrawingGroupBitmapScalingModeAsPartial()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            BitmapScalingMode = "Supersampled"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Empty(sink.BitmapScalingModes);
    }

    [Fact]
    public void DecodeDrawDrawingPassesSupportedDrawingGroupEdgeModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            EdgeMode = "Aliased"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushEdgeMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "Aliased" }, sink.EdgeModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingPassesSupportedDrawingGroupTextRenderingModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            TextRenderingMode = "Aliased"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushTextRenderingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "Aliased" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingPassesDrawingGroupClearTypeTextRenderingModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            TextRenderingMode = "ClearType"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushTextRenderingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingPassesDrawingGroupClearTypeHintToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            ClearTypeHint = "Enabled"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushTextRenderingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingPassesSupportedDrawingGroupTextHintingModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            TextHintingMode = "Animated"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushTextHintingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "Animated" }, sink.TextHintingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingCountsUnknownDrawingGroupTextHintingModeAsPartial()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            TextHintingMode = "Display"
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Empty(sink.TextHintingModes);
    }

    [Fact]
    public void DecodeDrawDrawingSkipsDrawingGroupWhenTransformCannotBeAdapted()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            Transform = new object()
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 0, 1), result);
        Assert.Empty(sink.Operations);
    }

    [Fact]
    public void DecodeDrawDrawingSkipsDrawingGroupWhenClipCannotBeAdapted()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            ClipGeometry = new object()
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 0, 1), result);
        Assert.Empty(sink.Operations);
    }

    [Fact]
    public void DecodeDrawDrawingSkipsDrawingGroupWithUnsupportedEffectInput()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            BitmapEffectInput = new object()
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 0, 1), result);
        Assert.Empty(sink.Operations);
    }

    [Fact]
    public void DecodeDrawDrawingPushesNativeDrawingGroupEffectWhenSinkSupportsVisualEffects()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(2, 3, 10, 20))))
        {
            Bounds = new FakeRect(1, 2, 30, 40),
            Effect = new FakeBlurEffect(8)
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink { AcceptVisualEffects = true };

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushVisualEffect", "DrawGeometry", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(8, effect.BlurRadius);
        Assert.Equal(new Rect(1, 2, 30, 40), Assert.Single(sink.VisualEffectBounds));
    }

    [Fact]
    public void DecodeDrawDrawingPushesNativeDrawingGroupBitmapEffectWhenEmulationIsSupported()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(2, 3, 10, 20))))
        {
            BitmapEffect = new FakeBlurBitmapEffect(6),
            BitmapEffectInput = new FakeContextBitmapEffectInput()
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink { AcceptVisualEffects = true };

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushVisualEffect", "DrawGeometry", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(6, effect.BlurRadius);
        Assert.Equal(new Rect(2, 3, 10, 20), Assert.Single(sink.VisualEffectBounds));
    }

    [Fact]
    public void DecodeDrawDrawingSkipsDrawingGroupEffectWhenSinkCannotApplyVisualEffects()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            Effect = new FakeBlurEffect(4)
        };
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 0, 1), result);
        Assert.Empty(sink.VisualEffects);
        Assert.Empty(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingSkipsMissingDrawingResourceToken()
    {
        var resolver = WpfReflectionResourceResolver.FromDependentResources(Array.Empty<object?>());
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 1, 0), result);
        Assert.Empty(sink.Operations);
    }

    [Fact]
    public void DecodeImageUsesInjectedImageSourceAdapter()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { imageSource }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawImage, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(imageSource, imageAdapter.LastImageSource);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(1, 2, 30, 40), replayed.Rectangle);
    }

    [Fact]
    public void DecodeImageUsesInjectedImageSourceAdapterForMediaImageWithoutProGpuTexture()
    {
        var imageSource = new FakeImageSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { imageSource }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawImage, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(imageSource, imageAdapter.LastImageSource);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(1, 2, 30, 40), replayed.Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageDrawingWithInjectedImageSourceAdapter()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeImageDrawing(imageSource, new FakeRect(3, 4, 50, 60));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(imageSource, imageAdapter.LastImageSource);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(3, 4, 50, 60), replayed.Rectangle);
    }

    [Fact]
    public void DecodeGlyphRunAdaptsWpfShapedGlyphRun()
    {
        var brush = new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30));
        var glyphRun = new FakeGlyphRun(
            glyphIndices: new ushort[] { 3, 4 },
            advanceWidths: new[] { 5.5, 6.5 },
            glyphOffsets: new FakePointCollection(new FakePoint(0, 0), new FakePoint(1.5, -2)),
            baselineOrigin: new FakePoint(10, 20),
            fontRenderingEmSize: 12.5);
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { brush, glyphRun });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGlyphRun, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var replayed = Assert.Single(sink.GlyphRuns);
        Assert.IsType<SolidColorBrush>(replayed.ForegroundBrush);
        Assert.Equal(new ushort[] { 3, 4 }, replayed.GlyphRun.GlyphIndices);
        Assert.Equal(12.5f, replayed.GlyphRun.FontSize);
        Assert.Equal(10, replayed.GlyphRun.Position.X);
        Assert.Equal(20, replayed.GlyphRun.Position.Y);
        Assert.Equal(0, replayed.GlyphRun.GlyphPositions[0].X);
        Assert.Equal(0, replayed.GlyphRun.GlyphPositions[0].Y);
        Assert.Equal(7, replayed.GlyphRun.GlyphPositions[1].X);
        Assert.Equal(-2, replayed.GlyphRun.GlyphPositions[1].Y);
    }

    [Fact]
    public void DecodeGlyphRunPrefersGlyphTypefaceFontUri()
    {
        if (!TryFindLoadableFontPathDifferentFromFallback(out var fontPath, out var expectedFont))
        {
            return;
        }

        var glyphRun = new FakeGlyphRun(
            glyphIndices: new ushort[] { 3 },
            advanceWidths: new[] { 5.0 },
            glyphOffsets: null,
            baselineOrigin: new FakePoint(0, 0),
            fontRenderingEmSize: 12,
            glyphTypeface: new FakeGlyphTypeface(
                new Uri(fontPath),
                "ProGpuMissingFamilyNameForFontUriTest"));

        var adapted = WpfReflectionResourceResolver.AdaptGlyphRun(glyphRun);

        Assert.NotNull(adapted);
        Assert.Equal(expectedFont.UnitsPerEm, adapted.Font.UnitsPerEm);
        Assert.Equal(expectedFont.Ascender, adapted.Font.Ascender);
        Assert.Equal(expectedFont.Descender, adapted.Font.Descender);
        Assert.Equal(expectedFont.NumGlyphs, adapted.Font.NumGlyphs);
    }

    [Fact]
    public void DecodeGlyphRunPreservesGlyphTypefaceStyleSimulations()
    {
        var glyphRun = new FakeGlyphRun(
            glyphIndices: new ushort[] { 3 },
            advanceWidths: new[] { 5.0 },
            glyphOffsets: null,
            baselineOrigin: new FakePoint(0, 0),
            fontRenderingEmSize: 12,
            glyphTypeface: new FakeGlyphTypeface(
                fontUri: null,
                new[] { "Arial" },
                styleSimulations: 0x3));

        var adapted = WpfReflectionResourceResolver.AdaptGlyphRun(glyphRun);

        Assert.NotNull(adapted);
        Assert.True(adapted.IsBold);
        Assert.True(adapted.IsItalic);
    }

    [Fact]
    public void AdaptGlyphRunAdaptsPortableGlyphRunWithoutTypeNameShape()
    {
        var glyphRun = new FakePortableGlyphRunSource(new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 3, 4 },
            GlyphPositions =
            [
                new PortablePoint(2, 3),
                new PortablePoint(7, -1)
            ],
            BaselineOrigin = new PortablePoint(10, 20),
            FontRenderingEmSize = 12.5,
            FontFamilyNames = new[] { "Arial" },
            IsBold = true,
            IsItalic = true
        });

        var adapted = WpfReflectionResourceResolver.AdaptGlyphRun(glyphRun);

        Assert.NotNull(adapted);
        Assert.Equal(new ushort[] { 3, 4 }, adapted.GlyphIndices);
        Assert.Equal(12.5f, adapted.FontSize);
        Assert.Equal(new Vector2(10, 20), adapted.Position);
        Assert.Equal(new Vector2(2, 3), adapted.GlyphPositions[0]);
        Assert.Equal(new Vector2(7, -1), adapted.GlyphPositions[1]);
        Assert.True(adapted.IsBold);
        Assert.True(adapted.IsItalic);
    }

    [Fact]
    public void AdaptNativeGlyphRunAdaptsPortableGlyphRunWithoutReflection()
    {
        var glyphRun = new FakePortableGlyphRunSource(new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 5 },
            AdvanceWidths = new[] { 9.0 },
            GlyphOffsets = [new PortablePoint(1, 2)],
            BaselineOrigin = new PortablePoint(3, 4),
            FontRenderingEmSize = 14,
            FontFamilyNames = new[] { "Arial" },
            HasTransform = true,
            Transform = new PortableMatrix3x2(1, 0, 0, 1, 6, 7)
        });

        Assert.True(WpfReflectionResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var adapted));
        Assert.Equal(new ushort[] { 5 }, adapted.GlyphIndices);
        Assert.Equal(14f, adapted.FontSize);
        Assert.Equal(new Vector2(3, 4), adapted.Position);
        Assert.Equal(new Vector2(1, 2), adapted.GlyphPositions[0]);
        Assert.Equal(6, adapted.Transform.M41);
        Assert.Equal(7, adapted.Transform.M42);
    }

    [Fact]
    public void AdaptGlyphRunSkipsUnavailablePortableGlyphRunWithoutReflectionFallback()
    {
        var glyphRun = new UnavailablePortableGlyphRun();

        Assert.Null(WpfReflectionResourceResolver.AdaptGlyphRun(glyphRun));
        Assert.False(WpfReflectionResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out _));
        Assert.Equal(0, glyphRun.ReflectedGlyphRunProbeCount);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedGlyphRunDrawing()
    {
        var drawing = new FakeGlyphRunDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 80, 90, 100)),
            new FakeGlyphRun(
                glyphIndices: new ushort[] { 7 },
                advanceWidths: new[] { 8.0 },
                glyphOffsets: null,
                baselineOrigin: new FakePoint(1, 2),
                fontRenderingEmSize: 14));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var replayed = Assert.Single(sink.GlyphRuns);
        Assert.IsType<SolidColorBrush>(replayed.ForegroundBrush);
        Assert.Equal(new ushort[] { 7 }, replayed.GlyphRun.GlyphIndices);
        Assert.Equal(1, replayed.GlyphRun.Position.X);
        Assert.Equal(2, replayed.GlyphRun.Position.Y);
    }

    [Fact]
    public void DecodeGeometryAdaptsWpfShapedLineGeometry()
    {
        var geometry = new FakeLineGeometry(new FakePoint(1, 2), new FakePoint(31, 42));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { null, geometry });
        var sink = new TestSink();

        var payload = new byte[16];
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var adaptedGeometry = Assert.IsType<PathGeometry>(Assert.Single(sink.DrawGeometries).Geometry);
        Assert.Equal(new Rect(1, 2, 30, 40), adaptedGeometry.Bounds);
        Assert.False(adaptedGeometry.Figures[0].IsClosed);
        Assert.False(adaptedGeometry.Figures[0].IsFilled);
    }

    [Fact]
    public void AdaptGeometryAdaptsWpfShapedEllipseGeometry()
    {
        var geometry = new FakeEllipseGeometry(new FakePoint(10, 20), 3, 4);

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfReflectionResourceResolver.AdaptGeometry(geometry));

        Assert.Equal(new Rect(7, 16, 6, 8), adaptedGeometry.Bounds);
        Assert.Single(adaptedGeometry.Figures);
        Assert.Equal(4, adaptedGeometry.Figures[0].Segments.Count);
        Assert.True(adaptedGeometry.Figures[0].IsClosed);
    }

    [Fact]
    public void AdaptGeometryAdaptsWpfShapedGeometryGroup()
    {
        var group = new FakeGeometryGroup(
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)),
            new FakeLineGeometry(new FakePoint(20, 5), new FakePoint(30, 5)))
        {
            FillRule = FakeFillRule.Nonzero
        };

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfReflectionResourceResolver.AdaptGeometry(group));

        Assert.Equal(FillRule.Nonzero, adaptedGeometry.FillRule);
        Assert.Equal(2, adaptedGeometry.Figures.Count);
        Assert.Equal(new Rect(0, 0, 30, 10), adaptedGeometry.Bounds);
    }

    [Fact]
    public void AdaptGeometryAdaptsPortableGeometryPathWithoutTypeNameShape()
    {
        var source = new FakePortableGeometryPathSource(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            FillRule = PortableFillRule.EvenOdd,
            Transform = new PortableMatrix3x2(1, 0, 0, 1, 3, 4),
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(0, 0),
                    IsClosed = true,
                    IsFilled = false,
                    Segments =
                    [
                        PortablePathSegment.Line(new PortablePoint(10, 0), isSmoothJoin: true, isStroked: false),
                        PortablePathSegment.Arc(
                            new PortablePoint(20, 10),
                            new PortableSize(5, 6),
                            rotationAngle: 30,
                            isLargeArc: true,
                            PortableSweepDirection.Clockwise,
                            isSmoothJoin: true,
                            isStroked: true)
                    ]
                }
            ]
        });

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfReflectionResourceResolver.AdaptGeometry(source));

        Assert.Equal(FillRule.EvenOdd, adaptedGeometry.FillRule);
        var transform = Assert.IsType<MatrixTransform>(adaptedGeometry.Transform);
        Assert.Equal(3, transform.Matrix.OffsetX);
        Assert.Equal(4, transform.Matrix.OffsetY);

        var figure = Assert.Single(adaptedGeometry.Figures);
        Assert.True(figure.IsClosed);
        Assert.False(figure.IsFilled);
        Assert.Equal(new Point(0, 0), figure.StartPoint);

        var line = Assert.IsType<LineSegment>(figure.Segments[0]);
        Assert.True(line.IsSmoothJoin);
        Assert.False(line.IsStroked);
        Assert.Equal(new Point(10, 0), line.Point);

        var arc = Assert.IsType<ArcSegment>(figure.Segments[1]);
        Assert.True(arc.IsSmoothJoin);
        Assert.True(arc.IsStroked);
        Assert.True(arc.IsLargeArc);
        Assert.Equal(SweepDirection.Clockwise, arc.SweepDirection);
        Assert.Equal(30, arc.RotationAngle);
        Assert.Equal(new Size(5, 6), arc.Size);
    }

    [Fact]
    public void AdaptGeometrySkipsUnavailablePortableGeometryPathWithoutReflectionFallback()
    {
        var geometry = new UnavailablePortableLineGeometry();

        Assert.Null(WpfReflectionResourceResolver.AdaptGeometry(geometry));
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
    }

    [Fact]
    public void DecodeGeometryPreservesCombinedGeometryInsideWpfShapedGeometryGroup()
    {
        var group = new FakeGeometryGroup(
            new FakeCombinedGeometry(
                "Intersect",
                new FakeRectangleGeometry(new FakeRect(0, 0, 20, 20)),
                new FakeRectangleGeometry(new FakeRect(10, 10, 20, 20))),
            new FakeRectangleGeometry(new FakeRect(40, 0, 10, 10)));
        var resolver = WpfReflectionResourceResolver.FromDependentResources(new object?[] { Brushes.White, group });
        var nativeContext = new ProGPU.Scene.DrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(ProGPU.Scene.RenderCommandType.DrawPath, command.Type);
        Assert.NotNull(command.Path);
        Assert.True(command.Path!.IsCombined);
        Assert.Equal(2, command.Path.Op);
        Assert.NotNull(command.Path.PathA);
        Assert.NotNull(command.Path.PathB);
        Assert.True(command.Path.PathA!.IsCombined);
        Assert.Equal(1, command.Path.PathA.Op);
        Assert.Single(command.Path.PathA.PathA!.Figures);
        Assert.Single(command.Path.PathA.PathB!.Figures);
        Assert.Single(command.Path.PathB!.Figures);
    }

    [Fact]
    public void AdaptGeometryAppliesChildTransformsInsideWpfShapedGeometryGroup()
    {
        var group = new FakeGeometryGroup(
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10))
            {
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 5, 7))
            },
            new FakeLineGeometry(new FakePoint(20, 5), new FakePoint(30, 5))
            {
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 2, 3))
            });

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfReflectionResourceResolver.AdaptGeometry(group));

        Assert.Equal(new Rect(5, 7, 27, 10), adaptedGeometry.Bounds);
        Assert.Equal(new Point(5, 7), adaptedGeometry.Figures[0].StartPoint);
        Assert.Equal(new Point(22, 8), adaptedGeometry.Figures[1].StartPoint);
        var line = Assert.IsType<LineSegment>(Assert.Single(adaptedGeometry.Figures[1].Segments));
        Assert.Equal(new Point(32, 8), line.Point);
    }

    [Fact]
    public void AdaptGeometryPreservesTransformedArcsInsideWpfShapedGeometryGroup()
    {
        var childTransform = new FakeMatrixTransform(new FakeMatrix(1, 0.35, 0.2, 1, 5, 7));
        var transformMatrix = new Matrix4x4(
            1f, 0.35f, 0f, 0f,
            0.2f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            5f, 7f, 0f, 1f);
        var group = new FakeGeometryGroup(
            new FakePathGeometry(
                new FakePathFigure(
                    new FakePoint(0, 0),
                    isClosed: false,
                    isFilled: true,
                    new FakeArcSegment(new FakePoint(30, 40), new FakeSize(10, 20))
                    {
                        RotationAngle = 45,
                        IsLargeArc = true,
                        SweepDirection = FakeSweepDirection.Clockwise
                    }))
            {
                Transform = childTransform
            });

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfReflectionResourceResolver.AdaptGeometry(group));

        var figure = Assert.Single(adaptedGeometry.Figures);
        var arc = Assert.IsType<ArcSegment>(Assert.Single(figure.Segments));

        var expectedStart = Vector2.Transform(new Vector2(0, 0), transformMatrix);
        var expectedEnd = Vector2.Transform(new Vector2(30, 40), transformMatrix);
        Assert.Equal(expectedStart.X, figure.StartPoint.X, 4);
        Assert.Equal(expectedStart.Y, figure.StartPoint.Y, 4);
        Assert.Equal(expectedEnd.X, arc.Point.X, 4);
        Assert.Equal(expectedEnd.Y, arc.Point.Y, 4);
        Assert.True(arc.Size.Width > 0);
        Assert.True(arc.Size.Height > 0);
        Assert.True(double.IsFinite(arc.RotationAngle));
        Assert.Equal(SweepDirection.Clockwise, arc.SweepDirection);
    }

    [Fact]
    public void AdaptGeometryAdaptsWpfShapedPathGeometryFiguresAndSegments()
    {
        var geometry = new FakePathGeometry(
            new FakePathFigure(
                new FakePoint(0, 0),
                isClosed: true,
                isFilled: false,
                new FakeLineSegment(new FakePoint(10, 0)) { IsSmoothJoin = true },
                new FakePolyLineSegment(new FakePointCollection(new FakePoint(20, 0), new FakePoint(30, 0))) { IsSmoothJoin = true },
                new FakeQuadraticBezierSegment(new FakePoint(35, 5), new FakePoint(40, 0)) { IsSmoothJoin = true },
                new FakePolyQuadraticBezierSegment(new FakePointCollection(new FakePoint(45, 5), new FakePoint(50, 0))) { IsSmoothJoin = true },
                new FakeBezierSegment(new FakePoint(55, 5), new FakePoint(60, 5), new FakePoint(65, 0)) { IsSmoothJoin = true },
                new FakePolyBezierSegment(new FakePointCollection(new FakePoint(70, 5), new FakePoint(75, 5), new FakePoint(80, 0))) { IsSmoothJoin = true },
                new FakeArcSegment(new FakePoint(100, 100), new FakeSize(3, 4))
                {
                    RotationAngle = 45,
                    IsLargeArc = true,
                    SweepDirection = FakeSweepDirection.Clockwise,
                    IsSmoothJoin = true
                }))
        {
            FillRule = FakeFillRule.Nonzero
        };

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfReflectionResourceResolver.AdaptGeometry(geometry));

        Assert.Equal(FillRule.Nonzero, adaptedGeometry.FillRule);
        var figure = Assert.Single(adaptedGeometry.Figures);
        Assert.True(figure.IsClosed);
        Assert.False(figure.IsFilled);
        Assert.Equal(8, figure.Segments.Count);
        Assert.IsType<LineSegment>(figure.Segments[0]);
        Assert.IsType<LineSegment>(figure.Segments[1]);
        Assert.IsType<LineSegment>(figure.Segments[2]);
        Assert.IsType<QuadraticBezierSegment>(figure.Segments[3]);
        Assert.IsType<QuadraticBezierSegment>(figure.Segments[4]);
        Assert.IsType<BezierSegment>(figure.Segments[5]);
        Assert.IsType<BezierSegment>(figure.Segments[6]);
        Assert.All(figure.Segments, segment => Assert.True(segment.IsSmoothJoin));
        var arc = Assert.IsType<ArcSegment>(figure.Segments[7]);
        Assert.Equal(100f, arc.Point.X);
        Assert.Equal(100f, arc.Point.Y);
        Assert.Equal(3, arc.Size.Width);
        Assert.Equal(4, arc.Size.Height);
        Assert.Equal(45, arc.RotationAngle);
        Assert.True(arc.IsLargeArc);
        Assert.Equal(SweepDirection.Clockwise, arc.SweepDirection);
    }

    private static byte[] CreateRecord(WpfMilCommandId commandId, byte[] payload)
    {
        var record = new byte[payload.Length + 8];
        WriteInt32(record, 0, record.Length);
        WriteInt32(record, 4, (int)commandId);
        payload.CopyTo(record.AsSpan(8));
        return record;
    }

    private static FakeRenderData CreateRectangleRenderData(MediaBrush brush)
    {
        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);
        var record = CreateRecord(WpfMilCommandId.DrawRectangle, payload);
        return new FakeRenderData(record, record.Length, new FakeDependentResources(brush));
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

    private sealed class DirectNativeBrush : MediaBrush
    {
        public DirectNativeBrush()
        {
            NativeBrush = new ProGPU.Vector.SolidColorBrush(Vector4.One);
        }

        public ProGPU.Vector.Brush NativeBrush { get; }

        public int ParameterlessCallCount { get; private set; }

        public int BoundsCallCount { get; private set; }

        public Rect LastBounds { get; private set; }

        public override ProGPU.Vector.Brush ToNative()
        {
            ParameterlessCallCount++;
            return NativeBrush;
        }

        public override ProGPU.Vector.Brush ToNative(Rect targetBounds)
        {
            BoundsCallCount++;
            LastBounds = targetBounds;
            return NativeBrush;
        }
    }

    private sealed class DuckTypedNativeBrush
    {
        public int ToNativeCallCount { get; private set; }

        public ProGPU.Vector.Brush ToNative()
        {
            ToNativeCallCount++;
            return new ProGPU.Vector.SolidColorBrush(Vector4.One);
        }

        public ProGPU.Vector.Brush ToNative(WpfReplayRect bounds)
        {
            ToNativeCallCount++;
            return new ProGPU.Vector.SolidColorBrush(Vector4.One);
        }
    }

    private sealed class DuckTypedNativePen
    {
        public int ToNativeCallCount { get; private set; }

        public ProGPU.Vector.Pen ToNative()
        {
            ToNativeCallCount++;
            return new ProGPU.Vector.Pen(
                new ProGPU.Vector.SolidColorBrush(Vector4.One),
                1);
        }
    }

    private sealed class FakeSolidColorBrush
    {
        public FakeSolidColorBrush(FakeColor color, double opacity = 1)
        {
            Color = color;
            Opacity = opacity;
        }

        public FakeColor Color { get; }

        public double Opacity { get; }
    }

    private readonly record struct FakeColor(byte A, byte R, byte G, byte B);

    private sealed class FakePortableBrush : IPortableBrushSource
    {
        private readonly PortableBrush _brush;

        public FakePortableBrush(PortableBrush brush)
        {
            _brush = brush;
        }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = _brush;
            return true;
        }
    }

    private sealed class FakeUnavailablePortableSolidColorBrush : IPortableBrushSource
    {
        public int ReflectedPropertyProbeCount { get; private set; }

        public FakeColor Color
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return new FakeColor(255, 1, 2, 3);
            }
        }

        public double Opacity
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return 1.0;
            }
        }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = null!;
            return false;
        }
    }

    private sealed class FakePortablePen : IPortablePenSource
    {
        private readonly PortablePen _pen;

        public FakePortablePen(
            PortableBrush brush,
            double thickness,
            PortablePenLineCap startLineCap = PortablePenLineCap.Flat,
            PortablePenLineCap endLineCap = PortablePenLineCap.Flat,
            PortablePenLineCap dashCap = PortablePenLineCap.Flat,
            PortablePenLineJoin lineJoin = PortablePenLineJoin.Miter,
            double miterLimit = 10.0,
            double[]? dashArray = null,
            double dashOffset = 0.0)
        {
            _pen = new PortablePen(
                brush,
                thickness,
                startLineCap,
                endLineCap,
                dashCap,
                lineJoin,
                miterLimit,
                dashArray,
                dashOffset);
        }

        public bool TryGetPortablePen(out PortablePen pen)
        {
            pen = _pen;
            return true;
        }
    }

    private sealed class FakeUnavailablePortablePen : IPortablePenSource
    {
        public int ReflectedPropertyProbeCount { get; private set; }

        public object Brush
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3));
            }
        }

        public double Thickness
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return 2.0;
            }
        }

        public bool TryGetPortablePen(out PortablePen pen)
        {
            pen = null!;
            return false;
        }
    }

    private sealed class FakePortableTileBrushSource : IPortableTileBrushSource
    {
        private readonly PortableTileBrush _brush;

        public FakePortableTileBrushSource(PortableTileBrush brush)
        {
            _brush = brush;
        }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            brush = _brush;
            return true;
        }
    }

    private sealed class FakeUnavailablePortableImageBrush : IPortableTileBrushSource
    {
        private readonly object? _imageSource;

        public FakeUnavailablePortableImageBrush(object? imageSource)
        {
            _imageSource = imageSource;
        }

        public int ReflectedPropertyProbeCount { get; private set; }

        public object? ImageSource => Probe(_imageSource);

        public FakeRect? Viewport => Probe<FakeRect?>(null);

        public FakeRect? Viewbox => Probe<FakeRect?>(null);

        public string TileMode => Probe("None");

        public string Stretch => Probe("Fill");

        public string ViewportUnits => Probe("RelativeToBoundingBox");

        public string ViewboxUnits => Probe("RelativeToBoundingBox");

        public double Opacity => Probe(1.0);

        public object? Transform => Probe<object?>(null);

        public object? RelativeTransform => Probe<object?>(null);

        public string AlignmentX => Probe("Center");

        public string AlignmentY => Probe("Center");

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            brush = null!;
            return false;
        }

        private T Probe<T>(T value)
        {
            ReflectedPropertyProbeCount++;
            return value;
        }
    }

    private sealed class FakePen
    {
        public FakePen(object brush, double thickness)
        {
            Brush = brush;
            Thickness = thickness;
        }

        public object Brush { get; }

        public double Thickness { get; }

        public object? DashStyle { get; init; }

        public object? StartLineCap { get; init; }

        public object? EndLineCap { get; init; }

        public object? DashCap { get; init; }

        public object? LineJoin { get; init; }

        public double MiterLimit { get; init; } = 10.0;
    }

    private sealed class FakeDashStyle
    {
        public FakeDashStyle(double[] dashes, double offset)
        {
            Dashes = dashes;
            Offset = offset;
        }

        public double[] Dashes { get; }

        public double Offset { get; }
    }

    private sealed class FakeLinearGradientBrush
    {
        public FakeLinearGradientBrush(FakePoint startPoint, FakePoint endPoint, params FakeGradientStop[] stops)
        {
            StartPoint = startPoint;
            EndPoint = endPoint;
            GradientStops = new FakeGradientStopCollection(stops);
        }

        public FakePoint StartPoint { get; }

        public FakePoint EndPoint { get; }

        public FakeGradientStopCollection GradientStops { get; }

        public double Opacity { get; init; } = 1;

        public string SpreadMethod { get; init; } = "Pad";

        public string ColorInterpolationMode { get; init; } = "SRgbLinearInterpolation";

        public string MappingMode { get; init; } = "RelativeToBoundingBox";

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }
    }

    private sealed class FakeRadialGradientBrush
    {
        public FakeRadialGradientBrush(FakePoint center, FakePoint gradientOrigin, double radiusX, double radiusY, params FakeGradientStop[] stops)
        {
            Center = center;
            GradientOrigin = gradientOrigin;
            RadiusX = radiusX;
            RadiusY = radiusY;
            GradientStops = new FakeGradientStopCollection(stops);
        }

        public FakePoint Center { get; }

        public FakePoint GradientOrigin { get; }

        public double RadiusX { get; }

        public double RadiusY { get; }

        public FakeGradientStopCollection GradientStops { get; }

        public string SpreadMethod { get; init; } = "Pad";

        public string ColorInterpolationMode { get; init; } = "SRgbLinearInterpolation";

        public string MappingMode { get; init; } = "RelativeToBoundingBox";

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }
    }

    private sealed class ThrowingStringTransform
    {
        public int StringProbeCount { get; private set; }

        public override string ToString()
        {
            StringProbeCount++;
            throw new InvalidOperationException("Brush transform should not be string-probed.");
        }
    }

    private sealed class FakeImageBrush
    {
        public FakeImageBrush(object? imageSource)
        {
            ImageSource = imageSource;
        }

        public object? ImageSource { get; }

        public FakeRect? Viewport { get; init; }

        public FakeRect? Viewbox { get; init; }

        public string TileMode { get; init; } = "None";

        public string Stretch { get; init; } = "Fill";

        public string ViewportUnits { get; init; } = "RelativeToBoundingBox";

        public string ViewboxUnits { get; init; } = "RelativeToBoundingBox";

        public double Opacity { get; init; } = 1;

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }

        public string AlignmentX { get; init; } = "Center";

        public string AlignmentY { get; init; } = "Center";
    }

    private sealed class FakeDrawingBrush
    {
        public FakeDrawingBrush(object? drawing)
        {
            Drawing = drawing;
        }

        public object? Drawing { get; }

        public FakeRect? Viewport { get; init; }

        public FakeRect? Viewbox { get; init; }

        public string TileMode { get; init; } = "None";

        public string Stretch { get; init; } = "Fill";

        public string ViewportUnits { get; init; } = "RelativeToBoundingBox";

        public string ViewboxUnits { get; init; } = "RelativeToBoundingBox";

        public double Opacity { get; init; } = 1;

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }

        public string AlignmentX { get; init; } = "Center";

        public string AlignmentY { get; init; } = "Center";
    }

    private sealed class FakeVisualBrush
    {
        public FakeVisualBrush(object? visual)
        {
            Visual = visual;
        }

        public object? Visual { get; }

        public FakeRect? Viewport { get; init; }

        public FakeRect? Viewbox { get; init; }

        public string TileMode { get; init; } = "None";

        public string Stretch { get; init; } = "Fill";

        public string ViewportUnits { get; init; } = "RelativeToBoundingBox";

        public string ViewboxUnits { get; init; } = "RelativeToBoundingBox";

        public double Opacity { get; init; } = 1;

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }

        public string AlignmentX { get; init; } = "Center";

        public string AlignmentY { get; init; } = "Center";
    }

    private sealed class FakeGradientStopCollection
    {
        private readonly FakeGradientStop[] _items;

        public FakeGradientStopCollection(FakeGradientStop[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public FakeGradientStop this[int index] => _items[index];
    }

    private sealed class FakeGradientStop
    {
        public FakeGradientStop(FakeColor color, double offset)
        {
            Color = color;
            Offset = offset;
        }

        public FakeColor Color { get; }

        public double Offset { get; }
    }

    private sealed class FakeMatrixTransform : IPortableTransformMatrixSource
    {
        public FakeMatrixTransform(FakeMatrix value)
        {
            Value = value;
        }

        public FakeMatrix Value { get; }

        public bool TryGetPortableTransformMatrix(out PortableMatrix3x2 matrix)
        {
            matrix = new PortableMatrix3x2(
                Value.M11,
                Value.M12,
                Value.M21,
                Value.M22,
                Value.OffsetX,
                Value.OffsetY);
            return true;
        }
    }

    private sealed class FakeReflectedMatrixTransform
    {
        private readonly FakeMatrix _value;

        public FakeReflectedMatrixTransform(FakeMatrix value)
        {
            _value = value;
        }

        public int ReflectedPropertyProbeCount { get; private set; }

        public FakeMatrix Value
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return _value;
            }
        }
    }

    private sealed class FakePortableTransform : IPortableTransformMatrixSource
    {
        private readonly PortableMatrix3x2 _matrix;

        public FakePortableTransform(PortableMatrix3x2 matrix)
        {
            _matrix = matrix;
        }

        public bool TryGetPortableTransformMatrix(out PortableMatrix3x2 matrix)
        {
            matrix = _matrix;
            return true;
        }
    }

    private sealed class FakeUnavailablePortableMatrixTransform : IPortableTransformMatrixSource
    {
        private readonly FakeMatrix _value;

        public FakeUnavailablePortableMatrixTransform(FakeMatrix value)
        {
            _value = value;
        }

        public int ReflectedPropertyProbeCount { get; private set; }

        public FakeMatrix Value
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return _value;
            }
        }

        public bool TryGetPortableTransformMatrix(out PortableMatrix3x2 matrix)
        {
            matrix = default;
            return false;
        }
    }

    private readonly record struct FakeMatrix(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY);

    private sealed class FakeRectangleGeometry
    {
        public FakeRectangleGeometry(FakeRect rect)
        {
            Rect = rect;
        }

        public FakeRect Rect { get; }

        public object? Transform { get; init; }
    }

    private readonly record struct FakeRect(double X, double Y, double Width, double Height);

    private sealed class FakeLineGeometry
    {
        public FakeLineGeometry(FakePoint startPoint, FakePoint endPoint)
        {
            StartPoint = startPoint;
            EndPoint = endPoint;
        }

        public FakePoint StartPoint { get; }

        public FakePoint EndPoint { get; }

        public object? Transform { get; init; }
    }

    private sealed class FakePortableGeometryPathSource : IPortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public FakePortableGeometryPathSource(PortableGeometryPath path)
        {
            _path = path;
        }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = _path;
            return true;
        }
    }

    private sealed class UnavailablePortableLineGeometry : IPortableGeometryPathSource
    {
        public int ReflectedGeometryProbeCount { get; private set; }

        public object? StartPoint => ThrowReflectedGeometryProbe();

        public object? EndPoint => ThrowReflectedGeometryProbe();

        public object? Transform => ThrowReflectedGeometryProbe();

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = null!;
            return false;
        }

        private object? ThrowReflectedGeometryProbe([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            ReflectedGeometryProbeCount++;
            throw new InvalidOperationException($"Reflected geometry property '{propertyName}' should not be read.");
        }
    }

    private sealed class FakeEllipseGeometry
    {
        public FakeEllipseGeometry(FakePoint center, double radiusX, double radiusY)
        {
            Center = center;
            RadiusX = radiusX;
            RadiusY = radiusY;
        }

        public FakePoint Center { get; }

        public double RadiusX { get; }

        public double RadiusY { get; }
    }

    private sealed class FakeGeometryGroup
    {
        public FakeGeometryGroup(params object[] children)
        {
            Children = new FakeGeometryCollection(children);
        }

        public FakeGeometryCollection Children { get; }

        public FakeFillRule FillRule { get; init; } = FakeFillRule.EvenOdd;
    }

    private sealed class FakeCombinedGeometry
    {
        public FakeCombinedGeometry(string geometryCombineMode, object? geometry1, object? geometry2)
        {
            GeometryCombineMode = geometryCombineMode;
            Geometry1 = geometry1;
            Geometry2 = geometry2;
        }

        public string GeometryCombineMode { get; }

        public object? Geometry1 { get; }

        public object? Geometry2 { get; }

        public object? Transform { get; init; }
    }

    private sealed class FakeGeometryCollection
    {
        private readonly object[] _items;

        public FakeGeometryCollection(object[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public object this[int index] => _items[index];
    }

    private readonly record struct FakePoint(double X, double Y);

    private sealed class FakePointCollection
    {
        private readonly FakePoint[] _items;

        public FakePointCollection(params FakePoint[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public FakePoint this[int index] => _items[index];
    }

    private enum FakeFillRule
    {
        EvenOdd,
        Nonzero
    }

    private enum FakeSweepDirection
    {
        Counterclockwise,
        Clockwise
    }

    private sealed class FakePathGeometry
    {
        public FakePathGeometry(params FakePathFigure[] figures)
        {
            Figures = new FakePathFigureCollection(figures);
        }

        public FakePathFigureCollection Figures { get; }

        public FakeFillRule FillRule { get; init; } = FakeFillRule.EvenOdd;

        public object? Transform { get; init; }
    }

    private sealed class FakePathFigureCollection
    {
        private readonly FakePathFigure[] _items;

        public FakePathFigureCollection(FakePathFigure[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public FakePathFigure this[int index] => _items[index];
    }

    private sealed class FakePathFigure
    {
        public FakePathFigure(FakePoint startPoint, bool isClosed, bool isFilled, params object[] segments)
        {
            StartPoint = startPoint;
            IsClosed = isClosed;
            IsFilled = isFilled;
            Segments = new FakePathSegmentCollection(segments);
        }

        public FakePoint StartPoint { get; }

        public bool IsClosed { get; }

        public bool IsFilled { get; }

        public FakePathSegmentCollection Segments { get; }
    }

    private sealed class FakePathSegmentCollection
    {
        private readonly object[] _items;

        public FakePathSegmentCollection(object[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public object this[int index] => _items[index];
    }

    private sealed class FakeLineSegment
    {
        public FakeLineSegment(FakePoint point)
        {
            Point = point;
        }

        public FakePoint Point { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakePolyLineSegment
    {
        public FakePolyLineSegment(FakePointCollection points)
        {
            Points = points;
        }

        public FakePointCollection Points { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakeQuadraticBezierSegment
    {
        public FakeQuadraticBezierSegment(FakePoint point1, FakePoint point2)
        {
            Point1 = point1;
            Point2 = point2;
        }

        public FakePoint Point1 { get; }

        public FakePoint Point2 { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakePolyQuadraticBezierSegment
    {
        public FakePolyQuadraticBezierSegment(FakePointCollection points)
        {
            Points = points;
        }

        public FakePointCollection Points { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakeBezierSegment
    {
        public FakeBezierSegment(FakePoint point1, FakePoint point2, FakePoint point3)
        {
            Point1 = point1;
            Point2 = point2;
            Point3 = point3;
        }

        public FakePoint Point1 { get; }

        public FakePoint Point2 { get; }

        public FakePoint Point3 { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakePolyBezierSegment
    {
        public FakePolyBezierSegment(FakePointCollection points)
        {
            Points = points;
        }

        public FakePointCollection Points { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakeArcSegment
    {
        public FakeArcSegment(FakePoint point, FakeSize size)
        {
            Point = point;
            Size = size;
        }

        public FakePoint Point { get; }

        public FakeSize Size { get; }

        public double RotationAngle { get; init; }

        public bool IsLargeArc { get; init; }

        public FakeSweepDirection SweepDirection { get; init; } = FakeSweepDirection.Counterclockwise;

        public bool IsSmoothJoin { get; init; }
    }

    private readonly record struct FakeSize(double Width, double Height);

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

    private sealed class FakeDrawingGroup : IPortableDrawingGroupStateSource
    {
        private readonly object[] _children;

        public FakeDrawingGroup(params object[] children)
        {
            _children = children;
            Children = new FakeDrawingCollection(children);
        }

        public FakeDrawingCollection Children { get; }

        public object? Transform { get; init; }

        public object? ClipGeometry { get; init; }

        public double Opacity { get; init; } = 1;

        public object? Bounds { get; init; }

        public object? OpacityMask { get; init; }

        public object? GuidelineSet { get; init; }

        public object? EdgeMode { get; init; }

        public object? BitmapScalingMode { get; init; }

        public object? ClearTypeHint { get; init; }

        public object? TextRenderingMode { get; init; }

        public object? TextHintingMode { get; init; }

        public object? BitmapEffect { get; init; }

        public object? BitmapEffectInput { get; init; }

        public object? Effect { get; init; }

        public object? CacheMode { get; init; }

        public bool TryGetPortableDrawingGroupState(out PortableDrawingGroupState state)
        {
            state = new PortableDrawingGroupState
            {
                HasTransform = Transform != null,
                Transform = Transform,
                HasClipGeometry = ClipGeometry != null,
                ClipGeometry = ClipGeometry,
                HasOpacity = true,
                Opacity = Opacity,
                HasOpacityMask = OpacityMask != null,
                OpacityMask = OpacityMask,
                HasGuidelineSet = GuidelineSet != null,
                GuidelineSet = GuidelineSet,
                HasEdgeMode = EdgeMode != null,
                EdgeMode = EdgeMode,
                HasBitmapScalingMode = BitmapScalingMode != null,
                BitmapScalingMode = BitmapScalingMode,
                HasClearTypeHint = ClearTypeHint != null,
                ClearTypeHint = ClearTypeHint,
                HasTextRenderingMode = TextRenderingMode != null,
                TextRenderingMode = TextRenderingMode,
                HasTextHintingMode = TextHintingMode != null,
                TextHintingMode = TextHintingMode,
                HasBitmapEffect = BitmapEffect != null,
                BitmapEffect = BitmapEffect,
                HasBitmapEffectInput = BitmapEffectInput != null,
                BitmapEffectInput = BitmapEffectInput,
                HasEffect = Effect != null,
                Effect = Effect,
                HasCacheMode = CacheMode != null,
                CacheMode = CacheMode,
                Children = _children
            };

            if (Bounds is FakeRect bounds)
            {
                state.HasBounds = true;
                state.Bounds = new PortableRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }

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

    private sealed class FakeDrawingVisual : IPortableDrawingContentSource, IPortableVisualChildrenSource, IPortableVisualBoundsSource
    {
        private readonly object? _content;

        public FakeDrawingVisual(object? content)
        {
            _content = content;
        }

        public FakeVisualCollection Children { get; } = new();

        public object? Bounds { get; init; }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }

        public bool TryGetPortableVisualChildCount(out int count)
        {
            count = Children.Count;
            return true;
        }

        public bool TryGetPortableVisualChild(int index, out object? child)
        {
            if (index < 0 || index >= Children.Count)
            {
                child = null;
                return false;
            }

            child = Children[index];
            return true;
        }

        public bool TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        {
            bounds = new PortableVisualBounds();
            if (Bounds is not FakeRect rect)
            {
                return true;
            }

            var portableBounds = new PortableRect(rect.X, rect.Y, rect.Width, rect.Height);
            bounds.HasContentBounds = true;
            bounds.ContentBounds = portableBounds;
            bounds.HasDescendantBounds = true;
            bounds.DescendantBounds = portableBounds;
            return true;
        }
    }

    private sealed class FakeVisualCollection
    {
        private readonly List<object> _children = new();

        public int Count => _children.Count;

        public object this[int index] => _children[index];

        public void Add(object child)
        {
            _children.Add(child);
        }
    }

    private sealed class FakeRenderData : IPortableRenderDataSource
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

        public bool TryGetPortableRenderDataSnapshot(out PortableRenderDataSnapshot snapshot)
        {
            snapshot = new PortableRenderDataSnapshot(
                _buffer.AsSpan(0, _curOffset).ToArray(),
                _dependentResources.Items);
            return true;
        }
    }

    private sealed class FakeDependentResources
    {
        private readonly object?[] _items;

        public FakeDependentResources(params object?[] items)
        {
            _items = items;
        }

        public IReadOnlyList<object?> Items => _items;

        public int Count => _items.Length;

        public object? this[int index] => _items[index];
    }

    private sealed class FakeBitmapSource
    {
    }

    private sealed class FakeImageSource : MediaImageSource
    {
        public int PixelWidth { get; init; } = 200;

        public int PixelHeight { get; init; } = 100;
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

    private sealed class FakeGlyphRunDrawing : IPortableGlyphRunDrawingStateSource
    {
        public FakeGlyphRunDrawing(object? foregroundBrush, object? glyphRun)
        {
            ForegroundBrush = foregroundBrush;
            GlyphRun = glyphRun;
        }

        public object? ForegroundBrush { get; }

        public object? GlyphRun { get; }

        public bool TryGetPortableGlyphRunDrawingState(out PortableGlyphRunDrawingState state)
        {
            state = new PortableGlyphRunDrawingState
            {
                HasForegroundBrush = ForegroundBrush != null,
                ForegroundBrush = ForegroundBrush,
                HasGlyphRun = GlyphRun != null,
                GlyphRun = GlyphRun
            };
            return true;
        }
    }

    private static bool TryFindLoadableFontPathDifferentFromFallback(out string fontPath, out TtfFont expectedFont)
    {
        fontPath = string.Empty;
        expectedFont = null!;

        TtfFont? fallbackFont = null;
        try
        {
            fallbackFont = new FontFamily("Arial").NativeFont;
        }
        catch (Exception ex) when (IsRecoverableFontLoadException(ex))
        {
        }

        foreach (var fontInfo in FontApi.GetSystemFonts())
        {
            if (string.IsNullOrWhiteSpace(fontInfo.FilePath) || !File.Exists(fontInfo.FilePath))
            {
                continue;
            }

            try
            {
                var font = new TtfFont(fontInfo.FilePath);
                if (fallbackFont != null && !HasDifferentFontMetrics(font, fallbackFont))
                {
                    continue;
                }

                fontPath = fontInfo.FilePath;
                expectedFont = font;
                return true;
            }
            catch (Exception ex) when (IsRecoverableFontLoadException(ex))
            {
            }
        }

        return false;
    }

    private static bool HasDifferentFontMetrics(TtfFont left, TtfFont right)
    {
        return left.UnitsPerEm != right.UnitsPerEm
            || left.Ascender != right.Ascender
            || left.Descender != right.Descender
            || left.NumGlyphs != right.NumGlyphs;
    }

    private static bool IsRecoverableFontLoadException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or FormatException
            or InvalidDataException
            or KeyNotFoundException
            or IndexOutOfRangeException
            or OverflowException
            or NotSupportedException;
    }

    private sealed class FakeGlyphRun
    {
        public FakeGlyphRun(
            ushort[] glyphIndices,
            double[] advanceWidths,
            FakePointCollection? glyphOffsets,
            FakePoint baselineOrigin,
            double fontRenderingEmSize,
            FakeGlyphTypeface? glyphTypeface = null)
        {
            GlyphIndices = glyphIndices;
            AdvanceWidths = advanceWidths;
            GlyphOffsets = glyphOffsets;
            BaselineOrigin = baselineOrigin;
            FontRenderingEmSize = fontRenderingEmSize;
            GlyphTypeface = glyphTypeface ?? new FakeGlyphTypeface();
        }

        public ushort[] GlyphIndices { get; }

        public double[] AdvanceWidths { get; }

        public FakePointCollection? GlyphOffsets { get; }

        public FakePoint BaselineOrigin { get; }

        public double FontRenderingEmSize { get; }

        public FakeGlyphTypeface GlyphTypeface { get; }
    }

    private sealed class FakePortableGlyphRunSource : IPortableGlyphRunSource
    {
        private readonly PortableGlyphRun _glyphRun;

        public FakePortableGlyphRunSource(PortableGlyphRun glyphRun)
        {
            _glyphRun = glyphRun;
        }

        public bool TryGetPortableGlyphRun(out PortableGlyphRun glyphRun)
        {
            glyphRun = _glyphRun;
            return true;
        }
    }

    private sealed class UnavailablePortableGlyphRun : IPortableGlyphRunSource
    {
        public int ReflectedGlyphRunProbeCount { get; private set; }

        public object? GlyphIndices => ThrowReflectedGlyphRunProbe();

        public object? AdvanceWidths => ThrowReflectedGlyphRunProbe();

        public object? GlyphOffsets => ThrowReflectedGlyphRunProbe();

        public object? BaselineOrigin => ThrowReflectedGlyphRunProbe();

        public object? FontRenderingEmSize => ThrowReflectedGlyphRunProbe();

        public object? GlyphTypeface => ThrowReflectedGlyphRunProbe();

        public object? Font => ThrowReflectedGlyphRunProbe();

        public bool TryGetPortableGlyphRun(out PortableGlyphRun glyphRun)
        {
            glyphRun = null!;
            return false;
        }

        private object? ThrowReflectedGlyphRunProbe([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            ReflectedGlyphRunProbeCount++;
            throw new InvalidOperationException($"Reflected glyph-run property '{propertyName}' should not be read.");
        }
    }

    private sealed class FakeGlyphTypeface
    {
        public FakeGlyphTypeface()
            : this(fontUri: null, new[] { "Arial" }, styleSimulations: 0)
        {
        }

        public FakeGlyphTypeface(Uri? fontUri, params string[] familyNames)
            : this(fontUri, familyNames, styleSimulations: 0)
        {
        }

        public FakeGlyphTypeface(Uri? fontUri, string[] familyNames, object styleSimulations)
        {
            FontUri = fontUri;
            FamilyNames = familyNames;
            StyleSimulations = styleSimulations;
        }

        public Uri? FontUri { get; }

        public string[] FamilyNames { get; }

        public object StyleSimulations { get; }
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

    private sealed class TestSink : IWpfCompositionCommandSink, IWpfVisualEffectCommandSink, IWpfDrawingCacheCommandSink
    {
        public List<string> Operations { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle)> DrawRectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, MediaGeometry Geometry)> DrawGeometries { get; } = new();

        public List<(MediaImageSource ImageSource, Rect Rectangle)> Images { get; } = new();

        public List<(MediaImageSource ImageSource, Rect Rectangle, Rect SourceRectangle)> SourceImages { get; } = new();

        public List<MediaGeometry> Clips { get; } = new();

        public List<(MediaBrush? ForegroundBrush, MediaGlyphRun GlyphRun)> GlyphRuns { get; } = new();

        public List<(MediaBrush? OpacityMask, Rect Bounds)> OpacityMasks { get; } = new();

        public List<MediaTransform> Transforms { get; } = new();

        public List<double> Opacities { get; } = new();

        public List<object?> BitmapScalingModes { get; } = new();

        public List<object?> EdgeModes { get; } = new();

        public List<object?> TextRenderingModes { get; } = new();

        public List<object?> TextHintingModes { get; } = new();

        public List<ProGpuEffectBase> VisualEffects { get; } = new();

        public List<Rect?> VisualEffectBounds { get; } = new();

        public List<Rect?> DrawingCacheBounds { get; } = new();

        public bool AcceptVisualEffects { get; init; }

        public bool AcceptDrawingCaches { get; init; }

        public int PopCount { get; private set; }

        public MediaDrawingContext DrawingContext => null!;

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
            Operations.Add("DrawRectangle");
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
            Operations.Add("DrawGeometry");
            DrawGeometries.Add((brush, pen, geometry));
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
            Images.Add((imageSource, rectangle));
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle, Rect sourceRectangle)
        {
            Images.Add((imageSource, rectangle));
            SourceImages.Add((imageSource, rectangle, sourceRectangle));
        }

        public void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
            GlyphRuns.Add((foregroundBrush, glyphRun));
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
            Operations.Add("PushClip");
            Clips.Add(clipGeometry);
        }

        public void PushOpacity(double opacity)
        {
            Operations.Add("PushOpacity");
            Opacities.Add(opacity);
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
            Operations.Add("PushOpacityMask");
            OpacityMasks.Add((opacityMask, bounds));
        }

        public void PushTransform(MediaTransform transform)
        {
            Operations.Add("PushTransform");
            Transforms.Add(transform);
        }

        public void PushGuidelineSet()
        {
            Operations.Add("PushGuidelineSet");
        }

        public void PushGuidelineSet(object? guidelines)
        {
            Operations.Add("PushGuidelineSetObject");
            Assert.NotNull(guidelines);
        }

        public void PushBitmapScalingMode(object? bitmapScalingMode)
        {
            Operations.Add("PushBitmapScalingMode");
            BitmapScalingModes.Add(bitmapScalingMode);
        }

        public void PushEdgeMode(object? edgeMode)
        {
            Operations.Add("PushEdgeMode");
            EdgeModes.Add(edgeMode);
        }

        public void PushTextRenderingMode(object? textRenderingMode)
        {
            Operations.Add("PushTextRenderingMode");
            TextRenderingModes.Add(textRenderingMode);
        }

        public void PushTextHintingMode(object? textHintingMode)
        {
            Operations.Add("PushTextHintingMode");
            TextHintingModes.Add(textHintingMode);
        }

        public void Pop()
        {
            Operations.Add("Pop");
            PopCount++;
        }

        public bool PushVisualEffect(ProGpuEffectBase effect)
        {
            return PushVisualEffect(effect, bounds: null);
        }

        public bool PushVisualEffect(ProGpuEffectBase effect, Rect? bounds)
        {
            if (!AcceptVisualEffects)
            {
                return false;
            }

            Operations.Add("PushVisualEffect");
            VisualEffects.Add(effect);
            VisualEffectBounds.Add(bounds);
            return true;
        }

        public bool PushDrawingCache(Rect? bounds = null)
        {
            if (!AcceptDrawingCaches)
            {
                return false;
            }

            Operations.Add("PushDrawingCache");
            DrawingCacheBounds.Add(bounds);
            return true;
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }
}
