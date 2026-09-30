using ProGPU.Text;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class GlyphComputeReadbackTests
{
    [Fact]
    public void CoverageMustBelongToTheRequestedGlyph()
    {
        byte[] pixels = new byte[16];
        var glyph = new GlyphInfo { X = 1, Y = 1, Width = 2, Height = 2 };
        pixels[0] = 255;
        Assert.Throws<InvalidOperationException>(() => GlyphComputeReadback.RequireCoverage(pixels, 4, 4, glyph));
        pixels[5] = 201;
        GlyphComputeReadback.RequireCoverage(pixels, 4, 4, glyph);
    }

    [Theory]
    [InlineData(0, 0, 0, 2, false)]
    [InlineData(0, 0, 2, 0, false)]
    [InlineData(3, 0, 2, 2, false)]
    [InlineData(0, 3, 2, 2, false)]
    [InlineData(uint.MaxValue, 0, 2, 2, false)]
    [InlineData(0, uint.MaxValue, 2, 2, false)]
    [InlineData(0, 0, 2, 2, true)]
    public void InvalidGlyphRegionsAreRejected(uint x, uint y, uint width, uint height, bool color)
    {
        byte[] pixels = new byte[16];
        Array.Fill(pixels, (byte)255);
        var glyph = new GlyphInfo { X = x, Y = y, Width = width, Height = height, IsColorBitmap = color };
        Assert.Throws<InvalidOperationException>(() => GlyphComputeReadback.RequireCoverage(pixels, 4, 4, glyph));
    }

    [Fact]
    public void ReadbackLengthMustMatchTheActualSingleChannelAtlas()
    {
        var glyph = new GlyphInfo { Width = 1, Height = 1 };
        Assert.Throws<InvalidOperationException>(() => GlyphComputeReadback.RequireCoverage([255], 4, 4, glyph));
        Assert.Throws<InvalidOperationException>(() => GlyphComputeReadback.RequireCoverage([], 0, 0, glyph));
    }
}
