using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfBitmapSourceImageAdapterTests
{
    [Fact]
    public void CopyPixelsAsPbgra32CopiesPbgra32()
    {
        var source = new FakeBitmapSource(
            width: 2,
            height: 1,
            formatName: "Pbgra32",
            bitsPerPixel: 32,
            pixels: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(source, 2, 1, out var pixels, out var stride));

        Assert.Equal(8, stride);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, pixels);
        Assert.Equal(8, source.LastStride);
    }

    [Fact]
    public void CopyPixelsAsPbgra32PremultipliesBgra32()
    {
        var source = new FakeBitmapSource(
            width: 1,
            height: 1,
            formatName: "Bgra32",
            bitsPerPixel: 32,
            pixels: new byte[] { 100, 50, 200, 128 });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(source, 1, 1, out var pixels, out _));

        Assert.Equal(new byte[] { 50, 25, 100, 128 }, pixels);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsBgr32ToOpaquePbgra32()
    {
        var source = new FakeBitmapSource(
            width: 1,
            height: 1,
            formatName: "Bgr32",
            bitsPerPixel: 32,
            pixels: new byte[] { 10, 20, 30, 0 });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(source, 1, 1, out var pixels, out _));

        Assert.Equal(new byte[] { 10, 20, 30, 255 }, pixels);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsBgr24Rows()
    {
        var source = new FakeBitmapSource(
            width: 2,
            height: 1,
            formatName: "Bgr24",
            bitsPerPixel: 24,
            pixels: new byte[] { 1, 2, 3, 4, 5, 6 });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(source, 2, 1, out var pixels, out var stride));

        Assert.Equal(8, stride);
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, pixels);
        Assert.Equal(6, source.LastStride);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsRgb24Rows()
    {
        var source = new FakeBitmapSource(
            width: 2,
            height: 1,
            formatName: "Rgb24",
            bitsPerPixel: 24,
            pixels: new byte[] { 1, 2, 3, 4, 5, 6 });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(source, 2, 1, out var pixels, out _));

        Assert.Equal(new byte[] { 3, 2, 1, 255, 6, 5, 4, 255 }, pixels);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsGray8Rows()
    {
        var source = new FakeBitmapSource(
            width: 3,
            height: 1,
            formatName: "Gray8",
            bitsPerPixel: 8,
            pixels: new byte[] { 0, 127, 255 });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(source, 3, 1, out var pixels, out var stride));

        Assert.Equal(12, stride);
        Assert.Equal(
            new byte[] { 0, 0, 0, 255, 127, 127, 127, 255, 255, 255, 255, 255 },
            pixels);
        Assert.Equal(3, source.LastStride);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsPackedGrayRows()
    {
        var blackWhite = new FakeBitmapSource(
            width: 4,
            height: 1,
            formatName: "BlackWhite",
            bitsPerPixel: 1,
            pixels: new byte[] { 0b1010_0000 });
        var gray2 = new FakeBitmapSource(
            width: 4,
            height: 1,
            formatName: "Gray2",
            bitsPerPixel: 2,
            pixels: new byte[] { 0b0001_1011 });
        var gray4 = new FakeBitmapSource(
            width: 2,
            height: 1,
            formatName: "Gray4",
            bitsPerPixel: 4,
            pixels: new byte[] { 0x5f });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(blackWhite, 4, 1, out var blackWhitePixels, out var blackWhiteStride));
        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(gray2, 4, 1, out var gray2Pixels, out var gray2Stride));
        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(gray4, 2, 1, out var gray4Pixels, out var gray4Stride));

        Assert.Equal(16, blackWhiteStride);
        Assert.Equal(
            new byte[] { 255, 255, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0, 255 },
            blackWhitePixels);
        Assert.Equal(16, gray2Stride);
        Assert.Equal(
            new byte[] { 0, 0, 0, 255, 85, 85, 85, 255, 170, 170, 170, 255, 255, 255, 255, 255 },
            gray2Pixels);
        Assert.Equal(8, gray4Stride);
        Assert.Equal(new byte[] { 85, 85, 85, 255, 255, 255, 255, 255 }, gray4Pixels);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsGray16Rows()
    {
        var source = new FakeBitmapSource(
            width: 3,
            height: 1,
            formatName: "Gray16",
            bitsPerPixel: 16,
            pixels: new byte[] { 0x00, 0x00, 0x00, 0x80, 0xff, 0xff });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(source, 3, 1, out var pixels, out var stride));

        Assert.Equal(12, stride);
        Assert.Equal(
            new byte[] { 0, 0, 0, 255, 128, 128, 128, 255, 255, 255, 255, 255 },
            pixels);
        Assert.Equal(6, source.LastStride);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsBgr555Rows()
    {
        var source = new FakeBitmapSource(
            width: 2,
            height: 1,
            formatName: "Bgr555",
            bitsPerPixel: 16,
            pixels: new byte[] { 0x00, 0x7c, 0x1f, 0x00 });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(source, 2, 1, out var pixels, out var stride));

        Assert.Equal(8, stride);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, pixels);
        Assert.Equal(4, source.LastStride);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsBgr565Rows()
    {
        var source = new FakeBitmapSource(
            width: 3,
            height: 1,
            formatName: "Bgr565",
            bitsPerPixel: 16,
            pixels: new byte[] { 0x00, 0xf8, 0xe0, 0x07, 0x1f, 0x00 });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(source, 3, 1, out var pixels, out var stride));

        Assert.Equal(12, stride);
        Assert.Equal(
            new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255 },
            pixels);
        Assert.Equal(6, source.LastStride);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsIndexedRowsUsingPalette()
    {
        var palette = new FakeBitmapPalette(
            new FakeColor(255, 0, 0, 0),
            new FakeColor(128, 100, 50, 20),
            new FakeColor(255, 0, 255, 0),
            new FakeColor(255, 10, 20, 30));
        var indexed1 = new FakeBitmapSource(
            width: 4,
            height: 1,
            formatName: "Indexed1",
            bitsPerPixel: 1,
            pixels: new byte[] { 0b1010_0000 },
            palette: palette);
        var indexed2 = new FakeBitmapSource(
            width: 4,
            height: 1,
            formatName: "Indexed2",
            bitsPerPixel: 2,
            pixels: new byte[] { 0b0001_1011 },
            palette: palette);
        var indexed4 = new FakeBitmapSource(
            width: 2,
            height: 1,
            formatName: "Indexed4",
            bitsPerPixel: 4,
            pixels: new byte[] { 0x12 },
            palette: palette);
        var indexed8 = new FakeBitmapSource(
            width: 2,
            height: 1,
            formatName: "Indexed8",
            bitsPerPixel: 8,
            pixels: new byte[] { 3, 1 },
            palette: palette);

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(indexed1, 4, 1, out var indexed1Pixels, out _));
        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(indexed2, 4, 1, out var indexed2Pixels, out _));
        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(indexed4, 2, 1, out var indexed4Pixels, out _));
        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(indexed8, 2, 1, out var indexed8Pixels, out _));

        Assert.Equal(
            new byte[] { 10, 25, 50, 128, 0, 0, 0, 255, 10, 25, 50, 128, 0, 0, 0, 255 },
            indexed1Pixels);
        Assert.Equal(
            new byte[] { 0, 0, 0, 255, 10, 25, 50, 128, 0, 255, 0, 255, 30, 20, 10, 255 },
            indexed2Pixels);
        Assert.Equal(new byte[] { 10, 25, 50, 128, 0, 255, 0, 255 }, indexed4Pixels);
        Assert.Equal(new byte[] { 30, 20, 10, 255, 10, 25, 50, 128 }, indexed8Pixels);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsHighBitDepthIntegerRows()
    {
        var rgb48 = new FakeBitmapSource(
            width: 1,
            height: 1,
            formatName: "Rgb48",
            bitsPerPixel: 48,
            pixels: new byte[] { 0xff, 0xff, 0x00, 0x80, 0x00, 0x00 });
        var rgba64 = new FakeBitmapSource(
            width: 1,
            height: 1,
            formatName: "Rgba64",
            bitsPerPixel: 64,
            pixels: new byte[] { 0xff, 0xff, 0x00, 0x80, 0x00, 0x00, 0x00, 0x80 });
        var prgba64 = new FakeBitmapSource(
            width: 1,
            height: 1,
            formatName: "Prgba64",
            bitsPerPixel: 64,
            pixels: new byte[] { 0x00, 0x80, 0x00, 0x40, 0x00, 0x00, 0x00, 0x80 });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(rgb48, 1, 1, out var rgb48Pixels, out var rgb48Stride));
        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(rgba64, 1, 1, out var rgba64Pixels, out var rgba64Stride));
        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(prgba64, 1, 1, out var prgba64Pixels, out var prgba64Stride));

        Assert.Equal(4, rgb48Stride);
        Assert.Equal(new byte[] { 0, 128, 255, 255 }, rgb48Pixels);
        Assert.Equal(4, rgba64Stride);
        Assert.Equal(new byte[] { 0, 64, 128, 128 }, rgba64Pixels);
        Assert.Equal(4, prgba64Stride);
        Assert.Equal(new byte[] { 0, 64, 128, 128 }, prgba64Pixels);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsPackedBgr101010AndCmyk32Rows()
    {
        var bgr101010Value = 1023u | (512u << 10);
        var bgr101010 = new FakeBitmapSource(
            width: 1,
            height: 1,
            formatName: "Bgr101010",
            bitsPerPixel: 32,
            pixels: new[]
            {
                (byte)bgr101010Value,
                (byte)(bgr101010Value >> 8),
                (byte)(bgr101010Value >> 16),
                (byte)(bgr101010Value >> 24)
            });
        var cmyk32 = new FakeBitmapSource(
            width: 2,
            height: 1,
            formatName: "Cmyk32",
            bitsPerPixel: 32,
            pixels: new byte[]
            {
                0, 255, 255, 0,
                0, 0, 0, 128
            });

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(bgr101010, 1, 1, out var bgr101010Pixels, out var bgr101010Stride));
        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(cmyk32, 2, 1, out var cmyk32Pixels, out var cmyk32Stride));

        Assert.Equal(4, bgr101010Stride);
        Assert.Equal(new byte[] { 255, 128, 0, 255 }, bgr101010Pixels);
        Assert.Equal(8, cmyk32Stride);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 127, 127, 127, 255 }, cmyk32Pixels);
    }

    [Fact]
    public void CopyPixelsAsPbgra32ConvertsScRgbFloatRows()
    {
        var gray32Float = new FakeBitmapSource(
            width: 3,
            height: 1,
            formatName: "Gray32Float",
            bitsPerPixel: 32,
            pixels: FloatPixels(0f, 0.5f, 1f));
        var rgb128Float = new FakeBitmapSource(
            width: 1,
            height: 1,
            formatName: "Rgb128Float",
            bitsPerPixel: 128,
            pixels: FloatPixels(1f, 0.5f, 0f, 0f));
        var rgba128Float = new FakeBitmapSource(
            width: 1,
            height: 1,
            formatName: "Rgba128Float",
            bitsPerPixel: 128,
            pixels: FloatPixels(1f, 0.5f, 0f, 0.5f));
        var prgba128Float = new FakeBitmapSource(
            width: 1,
            height: 1,
            formatName: "Prgba128Float",
            bitsPerPixel: 128,
            pixels: FloatPixels(0.5f, 0.25f, 0f, 0.5f));

        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(gray32Float, 3, 1, out var gray32FloatPixels, out var gray32FloatStride));
        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(rgb128Float, 1, 1, out var rgb128FloatPixels, out var rgb128FloatStride));
        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(rgba128Float, 1, 1, out var rgba128FloatPixels, out var rgba128FloatStride));
        Assert.True(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(prgba128Float, 1, 1, out var prgba128FloatPixels, out var prgba128FloatStride));

        Assert.Equal(12, gray32FloatStride);
        Assert.Equal(
            new byte[] { 0, 0, 0, 255, 188, 188, 188, 255, 255, 255, 255, 255 },
            gray32FloatPixels);
        Assert.Equal(4, rgb128FloatStride);
        Assert.Equal(new byte[] { 0, 188, 255, 255 }, rgb128FloatPixels);
        Assert.Equal(4, rgba128FloatStride);
        Assert.Equal(new byte[] { 0, 94, 128, 128 }, rgba128FloatPixels);
        Assert.Equal(4, prgba128FloatStride);
        Assert.Equal(new byte[] { 0, 94, 128, 128 }, prgba128FloatPixels);
    }

    [Fact]
    public void CopyPixelsAsPbgra32RejectsUnsupportedFormat()
    {
        var source = new FakeBitmapSource(
            width: 1,
            height: 1,
            formatName: "Indexed8",
            bitsPerPixel: 8,
            pixels: new byte[] { 0 });

        Assert.False(WpfBitmapSourceImageAdapter.TryCopyPixelsAsPbgra32(source, 1, 1, out var pixels, out var stride));
        Assert.Empty(pixels);
        Assert.Equal(0, stride);
        Assert.Equal(0, source.CopyPixelsCallCount);
    }

    [Fact]
    public void ReadPixelFormatDefaultsUnnamed32BitFormatToPbgra32()
    {
        var source = new FakeBitmapSource(
            width: 1,
            height: 1,
            formatName: string.Empty,
            bitsPerPixel: 32,
            pixels: new byte[] { 1, 2, 3, 4 });

        Assert.True(WpfBitmapSourceImageAdapter.TryReadPixelFormat(source, out var formatKind, out var bitsPerPixel));

        Assert.Equal(PixelDataFormat.Pbgra32, formatKind);
        Assert.Equal(32, bitsPerPixel);
    }

    private sealed class FakeBitmapSource
    {
        private readonly byte[] _pixels;

        public FakeBitmapSource(
            int width,
            int height,
            string formatName,
            int bitsPerPixel,
            byte[] pixels,
            FakeBitmapPalette? palette = null)
        {
            PixelWidth = width;
            PixelHeight = height;
            Format = new FakePixelFormat(formatName, bitsPerPixel);
            _pixels = pixels;
            Palette = palette;
        }

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public FakePixelFormat Format { get; }

        public FakeBitmapPalette? Palette { get; }

        public int LastStride { get; private set; }

        public int CopyPixelsCallCount { get; private set; }

        public void CopyPixels(Array destination, int stride, int offset)
        {
            CopyPixelsCallCount++;
            LastStride = stride;
            var bytes = Assert.IsType<byte[]>(destination);
            Assert.Equal(0, offset);
            Assert.True(bytes.Length >= _pixels.Length);
            Buffer.BlockCopy(_pixels, 0, bytes, 0, _pixels.Length);
        }
    }

    private sealed class FakePixelFormat
    {
        private readonly string _formatName;

        public FakePixelFormat(string formatName, int bitsPerPixel)
        {
            _formatName = formatName;
            BitsPerPixel = bitsPerPixel;
        }

        public int BitsPerPixel { get; }

        public override string ToString()
        {
            return _formatName;
        }
    }

    private static byte[] FloatPixels(params float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        for (var i = 0; i < values.Length; i++)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(values[i]), 0, bytes, i * sizeof(float), sizeof(float));
        }

        return bytes;
    }

    private sealed class FakeBitmapPalette
    {
        public FakeBitmapPalette(params FakeColor[] colors)
        {
            Colors = colors;
        }

        public FakeColor[] Colors { get; }
    }

    private readonly record struct FakeColor(byte A, byte R, byte G, byte B);
}
