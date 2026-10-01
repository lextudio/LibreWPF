using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Wpf.Interop;
using ProGPU.Text;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfHintedGlyphBidiTests
{
    [Fact]
    public void SourceBindingProjectsDirectionAndRetainsExactNativeEmbeddingIdentity()
    {
        byte[] bytes = ReadFont();
        var face = new TtfFont(bytes);
        var font = new PortableTextFont(bytes, 0, face.UnitsPerEm);
        const string text = "a\u202ab\u202c\u202ec\u202ed\u202c\u202c";
        var request = new PortableTextParagraphRequest(text.AsMemory(), font, 16, 20, 1000,
            false, PortableTextAlignment.Left, Styles: new PortableTextStyle[] { new(0, text.Length, font, 16) });
        using var paragraph = new WpfPortableTextFormatting().FormatHintedWithNominalMetrics(request,
            [new(12, 4)], [new(1024, 1024, PortableTextHintInterpreter.TrueType40)],
            new(1, PortableHintedTextProjection.ScalarReference, PortableHintedTextCoverage.AntialiasedVector));
        var original = paragraph.Glyphs.ToArray();
        int[] selected = new[] { 0, 2, 5, 7 }.Select(cluster =>
            Array.FindIndex(original, glyph => glyph.Cluster == cluster)).ToArray();
        Assert.All(selected, index => Assert.True(index >= 0));
        Assert.Equal(new sbyte[] { 0, 2, 1, 3 }, selected.Select(index => original[index].BidiLevel));

        foreach (int index in selected)
        {
            using var run = paragraph.AcquireGlyphRun([index]);
            using var binding = ((IPortableHintedGlyphRunBindingFactory)run).BindGlyphRun(16, Vector2.Zero);
            Assert.Equal((sbyte)(original[index].BidiLevel & 1), binding.BidiLevel);
            var concrete = Assert.IsType<WpfHintedGlyphRunBinding>(binding);
            using var read = concrete.NativeResource.AcquireReadLease();
            Assert.Equal(original[index].BidiLevel, read.BidiLevels[index]);
            Assert.Equal(new uint[] { (uint)index }, concrete.NativeIndices.ToArray());
            using var retained = binding.Retain();
            Assert.Equal(binding.BidiLevel, retained.BidiLevel);
        }

        // Equal direction is insufficient for one source run: raw levels remain exact.
        foreach (int[] mixed in new[] { new[] { selected[0], selected[1] }, new[] { selected[2], selected[3] } })
        {
            using var run = paragraph.AcquireGlyphRun(mixed);
            Assert.Throws<NotSupportedException>(() =>
                ((IPortableHintedGlyphRunBindingFactory)run).BindGlyphRun(16, Vector2.Zero));
        }
        Assert.Equal(original, paragraph.Glyphs.ToArray());
        Assert.Equal(new sbyte[] { 0, 2, 1, 3 }, selected.Select(index =>
            ((IPortableTextParagraph)paragraph).Glyphs.Span[index].BidiLevel));
    }

    private static byte[] ReadFont([CallerFilePath] string testFile = "") => File.ReadAllBytes(Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(testFile)!, "..", "..", "Microsoft.DotNet.Wpf", "src", "PresentationCore", "Fonts", "trado.ttf")));
}
