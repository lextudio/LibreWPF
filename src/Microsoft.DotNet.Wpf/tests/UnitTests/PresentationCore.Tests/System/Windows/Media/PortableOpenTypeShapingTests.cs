// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Globalization;
using System.Linq;
using MS.Internal.Text.TextInterface;

namespace System.Windows.Media;

/// <summary>
/// Covers the portable text backend's script itemization and its ProGPU OpenType shaping path.
/// </summary>
[Collection("Sequential")]
public unsafe class PortableOpenTypeShapingTests
{
    private static GlyphTypeface Load(string file) =>
        new(new Uri(Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", file)));

    private static IList<(string Script, int Length)> Itemize(string text)
    {
        fixed (char* p = text)
        {
            return TextAnalyzer.Itemize(
                    p, (uint)text.Length, CultureInfo.InvariantCulture, null, false,
                    CultureInfo.InvariantCulture, false, 0, null, null, null, null, null)
                .Select(span => (((ItemProps)span.element).Script.ToString(), span.length))
                .ToList();
        }
    }

    [Fact]
    public void ItemizeSplitsAtScriptChangesAndKeepsCommonCharactersWithTheirNeighbours()
    {
        // Leading space joins the first real script; the space, digits and the combining acute
        // follow the script before them (UAX #24).
        IList<(string Script, int Length)> runs = Itemize(" abc שלום 123 é");

        Assert.Equal(
            new[] { ("latn", 5), ("hebr", 9), ("latn", 2) },
            runs.ToArray());
    }

    [Fact]
    public void ItemizeLeavesCommonOnlyTextWithoutAScript()
    {
        IList<(string Script, int Length)> runs = Itemize(" 12, 34 ");
        Assert.Single(runs);
        Assert.Equal("DFLT", runs[0].Script);
        Assert.Equal(8, runs[0].Length);
    }

    [Fact]
    public void CombiningMarkSharesAClusterWithItsBaseCharacter()
    {
        GlyphTypeface face = Load("Inter-Medium.ttf");
        const string text = "aéb";
        ushort[] clusterMap;
        ushort[] glyphs;
        int[] advances;
        GlyphOffset[] offsets;
        fixed (char* p = text)
        {
            new TextAnalyzer().GetGlyphsAndTheirPlacements(
                p, (uint)text.Length, face.FontDWrite, 0, false, false, CultureInfo.InvariantCulture,
                null, null, 16, 1, 1f, TextFormattingMode.Ideal, new ItemProps(CultureInfo.InvariantCulture),
                out clusterMap, out glyphs, out advances, out offsets);
        }

        Assert.Equal(text.Length, clusterMap.Length);
        Assert.Equal(clusterMap[1], clusterMap[2]);       // e + U+0301 form one cluster
        Assert.NotEqual(clusterMap[0], clusterMap[1]);
        Assert.NotEqual(clusterMap[2], clusterMap[3]);
        Assert.Equal(glyphs.Length, advances.Length);
        Assert.All(advances.Take(1), a => Assert.True(a > 0));
    }

    [Fact]
    public void ArabicRunUsesContextualFormsInRightToLeftOrder()
    {
        GlyphTypeface face = Load("trado.ttf");
        const string text = "ببب"; // three BEH: initial, medial, final forms
        ushort[] clusterMap;
        ushort[] glyphs;
        int[] advances;
        GlyphOffset[] offsets;
        var props = new ItemProps(CultureInfo.InvariantCulture, new ProGPU.Text.Shaping.OpenTypeTag("arab"), false);
        fixed (char* p = text)
        {
            new TextAnalyzer().GetGlyphsAndTheirPlacements(
                p, (uint)text.Length, face.FontDWrite, 0, false, true, CultureInfo.InvariantCulture,
                null, null, 16, 1, 1f, TextFormattingMode.Ideal, props,
                out clusterMap, out glyphs, out advances, out offsets);
        }

        ushort isolated = face.CharacterToGlyphMap[0x0628];
        Assert.Equal(3, glyphs.Length);
        // Joining forms replace the nominal (isolated) glyph on at least the initial/medial letters.
        Assert.Contains(glyphs, g => g != isolated);
        // Right-to-left runs return glyphs in visual order: the first logical character is last.
        Assert.True(clusterMap[0] > clusterMap[2]);
    }

    [Fact]
    public void TwoPhaseShapingMatchesTheSinglePassResult()
    {
        GlyphTypeface face = Load("Inter-Medium.ttf");
        const string text = "Wave AV e\u0301 fi\u200B\u200Dx";
        const uint maxGlyphs = 64;
        var props = new ItemProps(CultureInfo.InvariantCulture, new ProGPU.Text.Shaping.OpenTypeTag("latn"), true);
        var analyzer = new TextAnalyzer();

        ushort[] oneClusters, oneGlyphs;
        int[] oneAdvances;
        GlyphOffset[] oneOffsets;
        ushort* clusterMap = stackalloc ushort[text.Length];
        ushort* textProps = stackalloc ushort[text.Length];
        ushort* glyphIndices = stackalloc ushort[(int)maxGlyphs];
        uint* glyphProps = stackalloc uint[(int)maxGlyphs];
        int* canAlone = stackalloc int[text.Length];
        int* advances = stackalloc int[(int)maxGlyphs];
        uint glyphCount;
        GlyphOffset[] twoOffsets;
        fixed (char* p = text)
        {
            analyzer.GetGlyphsAndTheirPlacements(
                p, (uint)text.Length, face.FontDWrite, 0, false, false, CultureInfo.InvariantCulture,
                null, null, 20, 1, 1f, TextFormattingMode.Ideal, props,
                out oneClusters, out oneGlyphs, out oneAdvances, out oneOffsets);

            analyzer.GetGlyphs(
                p, (uint)text.Length, face.FontDWrite, 0, false, false, CultureInfo.InvariantCulture,
                null, null, maxGlyphs, TextFormattingMode.Ideal, props,
                clusterMap, textProps, glyphIndices, glyphProps, canAlone, out glyphCount);
            analyzer.GetGlyphPlacements(
                p, clusterMap, textProps, (uint)text.Length, glyphIndices, glyphProps, glyphCount,
                face.FontDWrite, 20, 1, false, false, CultureInfo.InvariantCulture,
                null, null, TextFormattingMode.Ideal, props, 1f, advances, out twoOffsets);
        }

        Assert.Equal(oneGlyphs.Length, (int)glyphCount);
        for (int i = 0; i < glyphCount; i++)
        {
            Assert.Equal(oneGlyphs[i], glyphIndices[i]);
            Assert.Equal(oneAdvances[i], advances[i]);
            Assert.Equal(oneOffsets[i].du, twoOffsets[i].du);
            Assert.Equal(oneOffsets[i].dv, twoOffsets[i].dv);
        }

        for (int i = 0; i < text.Length; i++)
        {
            Assert.Equal(oneClusters[i], clusterMap[i]);
        }
    }

    [Fact]
    public void GetGlyphsReportsTheNeededCountWithoutWritingPastTheBuffers()
    {
        // Line Services retries with larger buffers when actualGlyphCount > maxGlyphCount.
        GlyphTypeface face = Load("Inter-Medium.ttf");
        const string text = "abcdef";
        ushort* clusterMap = stackalloc ushort[text.Length];
        ushort* textProps = stackalloc ushort[text.Length];
        ushort* glyphIndices = stackalloc ushort[2];
        glyphIndices[0] = glyphIndices[1] = 0xFFFF;
        uint* glyphProps = stackalloc uint[2];
        int* canAlone = stackalloc int[text.Length];
        uint glyphCount;
        fixed (char* p = text)
        {
            new TextAnalyzer().GetGlyphs(
                p, (uint)text.Length, face.FontDWrite, 0, false, false, CultureInfo.InvariantCulture,
                null, null, 2, TextFormattingMode.Ideal, new ItemProps(CultureInfo.InvariantCulture),
                clusterMap, textProps, glyphIndices, glyphProps, canAlone, out glyphCount);
        }

        Assert.Equal((uint)text.Length, glyphCount);
        Assert.Equal(0xFFFF, glyphIndices[0]);
        Assert.Equal(0xFFFF, glyphIndices[1]);
    }

    [Fact]
    public void DefaultIgnorableCharactersBecomeBlankZeroAdvanceGlyphs()
    {
        GlyphTypeface face = Load("Inter-Medium.ttf");
        // ZWSP, ZWJ, left-to-right mark, BOM, word joiner between visible letters.
        const string text = "a\u200Bb\u200D\u200Ec\uFEFFd\u2060e";
        const ushort blank = 7;
        ushort[] clusterMap, glyphs;
        int[] advances;
        GlyphOffset[] offsets;
        fixed (char* p = text)
        {
            new TextAnalyzer().GetGlyphsAndTheirPlacements(
                p, (uint)text.Length, face.FontDWrite, blank, false, false, CultureInfo.InvariantCulture,
                null, null, 20, 1, 1f, TextFormattingMode.Ideal,
                new ItemProps(CultureInfo.InvariantCulture, new ProGPU.Text.Shaping.OpenTypeTag("latn"), false),
                out clusterMap, out glyphs, out advances, out offsets);
        }

        Assert.Equal(text.Length, glyphs.Length);
        for (int i = 0; i < text.Length; i++)
        {
            bool ignorable = "\u200B\u200D\u200E\uFEFF\u2060".Contains(text[i]);
            if (ignorable)
            {
                // ZWJ/ZWNJ are grapheme extenders, so they may share the previous letter's cluster.
                Assert.True(clusterMap[i] <= i);
                Assert.Equal(blank, glyphs[i]);
                Assert.Equal(0, advances[i]);
            }
            else
            {
                Assert.Equal(i, clusterMap[i]);
                Assert.NotEqual(blank, glyphs[i]);
                Assert.True(advances[i] > 0);
            }
        }
    }

    [Fact]
    public void AdapterGlyphExtentsUseTheEngineConvention()
    {
        // The engine decodes extents as yMin = YBearing + Height (hb_glyph_extents_t style: the box is
        // anchored at its top-left and Height is negative). Cross-check against the font's own metrics.
        GlyphTypeface face = Load("Inter-Medium.ttf");
        MS.Internal.Text.TextInterface.PortableFontData data = face.FontDWrite.FontData;
        var adapter = PortableShapingFontFace.For(data);

        foreach (char c in "Hgx\u00E9")
        {
            uint glyph = adapter.GetNominalGlyph(c);
            Assert.True(adapter.TryGetGlyphExtents(glyph, out ProGPU.Text.Shaping.ShapingGlyphExtents extents));
            GlyphMetrics metrics = data.GetGlyphMetrics((ushort)glyph);
            int ascent = data.Metrics.Ascent;
            int yMax = ascent - metrics.TopSideBearing;
            int yMin = metrics.BottomSideBearing - (int)metrics.AdvanceHeight + ascent;

            Assert.True(extents.Width > 0);
            Assert.True(extents.Height < 0);
            Assert.Equal(yMax, extents.YBearing);
            Assert.Equal(yMin, extents.YBearing + extents.Height);
        }
    }
}
