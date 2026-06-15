using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfPortableTextInterfaceTests
{
    [Fact]
    public void PortableTextInterfaceContainsManagedSfntFontFaceBoundary()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "MS",
            "internal",
            "Text",
            "TextInterface",
            "PortableTextInterface.cs"));

        Assert.Contains("TTO_GSUB = 0x47535542", source, StringComparison.Ordinal);
        Assert.Contains("TTO_GPOS = 0x47504F53", source, StringComparison.Ordinal);
        Assert.Contains("TTO_GDEF = 0x47444546", source, StringComparison.Ordinal);
        Assert.Contains("FontCollection.FromFontSources(fontSources)", source, StringComparison.Ordinal);
        Assert.Contains("internal sealed class PortableFontData", source, StringComparison.Ordinal);
        Assert.Contains("private CmapData ParseCmap()", source, StringComparison.Ordinal);
        Assert.Contains("internal ushort GetGlyphIndex(uint codePoint)", source, StringComparison.Ordinal);
        Assert.Contains("internal GlyphMetrics GetGlyphMetrics(ushort glyphIndex)", source, StringComparison.Ordinal);
        Assert.Contains("internal bool TryGetTable(uint tag, out byte[] tableData)", source, StringComparison.Ordinal);
        Assert.Contains("return _fontData.TryGetTable((uint)openTypeTableTag, out tableData);", source, StringComparison.Ordinal);
        Assert.Contains("return _fontData.TryGetEmbeddingRights(out fsType);", source, StringComparison.Ordinal);
        Assert.Contains("SimpleGlyphRun glyphRun = CreateSimpleGlyphRun(textString, textLength, font, blankGlyphIndex);", source, StringComparison.Ordinal);
        Assert.Contains("private static ushort GetSimpleGlyphIndex(Font font, uint codePoint, ushort blankGlyphIndex)", source, StringComparison.Ordinal);
        Assert.Contains("private static void FillGlyphPlacements(", source, StringComparison.Ordinal);
        Assert.Contains("designAdvance * fontEmSize * scalingFactor / font.Metrics.DesignUnitsPerEm", source, StringComparison.Ordinal);
        Assert.Contains("private static uint ReadCodePoint(char* textString, uint textLength, uint textIndex, out uint codeUnitCount)", source, StringComparison.Ordinal);
        Assert.Contains("Marshal.Copy((IntPtr)fontData, fontCopy, 0, fileSize);", source, StringComparison.Ordinal);

        Assert.DoesNotContain("The portable WPF font face is not yet backed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The portable WPF font object is not yet backed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The portable WPF font collection is not yet backed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The portable WPF text analyzer is not yet backed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The portable WPF TrueType subsetter is not yet backed", source, StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}
