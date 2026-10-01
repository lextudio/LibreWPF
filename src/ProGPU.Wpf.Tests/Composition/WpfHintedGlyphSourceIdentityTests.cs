using System.Windows.Media.ProGPU.Composition;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

// Exact record/byte comparisons only. No font parser, native call or device.
public sealed class WpfHintedGlyphSourceIdentityTests
{
    private static NativeHintedGlyphFontSource OriginalFont => new()
    { ByteOffset = 2, ByteCount = 4, FaceIndex = 3, UnitsPerEm = 2048 };
    private static byte[] OriginalBytes => [99, 98, 10, 20, 30, 40, 97];

    [Fact]
    public void InstanceAdmissionUsesEverySelectedOccurrencesOriginalRunAndStyle()
    {
        NativeHintedParagraphGlyphOwner[] owners = [new() { RunIndex = 1 }, new() { RunIndex = 0 }];
        NativeHintedParagraphRun[] runs = [new() { StyleIndex = 1 }, new() { StyleIndex = 0 }];
        NativeHintedParagraphDeviceStyle[] styles = [new(), new() { VariationCount = 1 }];
        WpfHintedGlyphSourceIdentity.ValidateDefaultInstances(owners, runs, styles, [], [0, 0]);
        Assert.Throws<NotSupportedException>(() => WpfHintedGlyphSourceIdentity.ValidateDefaultInstances(owners, runs, styles, [], [0, 1]));
        Assert.Throws<NotSupportedException>(() => WpfHintedGlyphSourceIdentity.ValidateDefaultInstances(owners, runs, styles, [], [1]));
    }

    [Fact]
    public void InstanceAdmissionDoesNotAssumeCoordinateValuesAreDefault()
    {
        NativeHintedParagraphGlyphOwner[] owners = [new()];
        NativeHintedParagraphRun[] runs = [new()];
        // A coordinate-bearing instance is unsupported even when its actual
        // coordinates would be zero/default; source identity cannot express it.
        NativeHintedParagraphDeviceStyle[] styles = [new() { VariationStart = 0, VariationCount = 2 }];
        Assert.Throws<NotSupportedException>(() => WpfHintedGlyphSourceIdentity.ValidateDefaultInstances(owners, runs, styles, [], [0]));
        styles[0].VariationCount = 0;
        Assert.Throws<NotSupportedException>(() => WpfHintedGlyphSourceIdentity.ValidateDefaultInstances(owners, runs, styles, [0, 0], [0]));
    }

    [Fact]
    public void FontAdmissionComparesExactSelectedBytesFaceAndUnitsNotSurroundingStorage()
    {
        var source = new PortableTextFont(new byte[] { 10, 20, 30, 40 }, 3, 2048);
        var bytes = OriginalBytes;
        WpfHintedGlyphSourceIdentity.ValidateFont(source, OriginalFont, bytes);
        bytes[0] = 42; bytes[^1] = 43;
        WpfHintedGlyphSourceIdentity.ValidateFont(source, OriginalFont, bytes);
        Assert.Equal(new byte[] { 10, 20, 30, 40 }, source.Data.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FontAdmissionRejectsAnyDifferentOriginalByte(int offset)
    {
        byte[] sourceBytes = [10, 20, 30, 40]; sourceBytes[offset]++;
        Assert.Throws<ArgumentException>(() => WpfHintedGlyphSourceIdentity.ValidateFont(
            new PortableTextFont(sourceBytes, 3, 2048), OriginalFont, OriginalBytes));
    }

    [Fact]
    public void FontAdmissionRejectsFaceUnitsSizeAndInvalidRanges()
    {
        Assert.Throws<ArgumentException>(() => WpfHintedGlyphSourceIdentity.ValidateFont(
            new PortableTextFont(new byte[] { 10, 20, 30, 40 }, 4, 2048), OriginalFont, OriginalBytes));
        Assert.Throws<ArgumentException>(() => WpfHintedGlyphSourceIdentity.ValidateFont(
            new PortableTextFont(new byte[] { 10, 20, 30, 40 }, 3, 1000), OriginalFont, OriginalBytes));
        Assert.Throws<ArgumentException>(() => WpfHintedGlyphSourceIdentity.ValidateFont(
            new PortableTextFont(new byte[] { 10, 20, 30 }, 3, 2048), OriginalFont, OriginalBytes));
        var invalid = OriginalFont; invalid.ByteOffset = uint.MaxValue;
        Assert.Throws<ArgumentException>(() => WpfHintedGlyphSourceIdentity.ValidateFont(
            new PortableTextFont(new byte[] { 10, 20, 30, 40 }, 3, 2048), invalid, OriginalBytes));
    }

    [Fact]
    public void AdvanceAdmissionUsesOriginalOccurrenceSelectionIncludingRepeatsAndZeroInk()
    {
        NativePositionedTextGlyph[] glyphs = [new() { AdvanceX = 7.25f }, new() { AdvanceX = 0 }, new() { AdvanceX = 3.125f }];
        WpfHintedGlyphSourceIdentity.ValidateAdvances([3.125, 7.25, 3.125, 0], glyphs, [2, 0, 2, 1]);
        Assert.Throws<ArgumentException>(() => WpfHintedGlyphSourceIdentity.ValidateAdvances([7.25, 3.125, 3.125, 0], glyphs, [2, 0, 2, 1]));
        Assert.Equal(7.25f, glyphs[0].AdvanceX);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(7.250000000000001)]
    [InlineData(7.249999999999999)]
    public void AdvanceAdmissionDoesNotNarrowRoundOrIgnoreInvalidSourceValues(double value)
    {
        NativePositionedTextGlyph[] glyphs = [new() { AdvanceX = 7.25f }];
        Assert.Throws<ArgumentException>(() => WpfHintedGlyphSourceIdentity.ValidateAdvances([value], glyphs, [0]));
    }

    [Fact]
    public void AdvanceAdmissionRejectsPartialSelectionVerticalAdvanceAndChangedZeroSign()
    {
        NativePositionedTextGlyph[] glyphs = [new() { AdvanceX = -0.0f }];
        Assert.Throws<ArgumentException>(() => WpfHintedGlyphSourceIdentity.ValidateAdvances([], glyphs, [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => WpfHintedGlyphSourceIdentity.ValidateAdvances([0], glyphs, [1]));
        Assert.Throws<ArgumentException>(() => WpfHintedGlyphSourceIdentity.ValidateAdvances([0.0], glyphs, [0]));
        WpfHintedGlyphSourceIdentity.ValidateAdvances([-0.0], glyphs, [0]);
        glyphs[0].AdvanceY = 1;
        Assert.Throws<ArgumentException>(() => WpfHintedGlyphSourceIdentity.ValidateAdvances([-0.0], glyphs, [0]));
    }
}
