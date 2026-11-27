// Portable OpenType shaping for the non-DirectWrite text backend.
//
// Bridges WPF's TextAnalyzer glyph APIs to the renderer-independent ProGPU.Text.Shaping engine
// (GSUB/GPOS, Indic/USE/Khmer/Myanmar/Arabic shaping, bidi-aware direction). It replaces the
// one-glyph-per-code-point SfntSimpleGlyphShaper for the shaping path.

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using ProGPU.Text;
using ProGPU.Text.Shaping;

namespace MS.Internal.Text.TextInterface
{
    /// <summary>
    /// Exposes a parsed portable font to the shaping engine. Tables are served from the SFNT face
    /// without copying; the shaping engine caches its per-script plans on this instance, so one
    /// adapter is kept per <see cref="PortableFontData"/>.
    /// </summary>
    internal sealed class PortableShapingFontFace : IShapingFontFace
    {
        private static readonly ConditionalWeakTable<PortableFontData, PortableShapingFontFace> s_faces = new();

        private readonly PortableFontData _fontData;
        private readonly ProGPU.Text.SfntFontFace _face;
        private readonly ushort _unitsPerEm;

        private PortableShapingFontFace(PortableFontData fontData)
        {
            _fontData = fontData;
            _face = fontData.SfntFace;
            _unitsPerEm = fontData.Metrics.DesignUnitsPerEm == 0 ? (ushort)1 : fontData.Metrics.DesignUnitsPerEm;
        }

        internal static PortableShapingFontFace For(PortableFontData fontData) =>
            s_faces.GetValue(fontData, static data => new PortableShapingFontFace(data));

        public int FaceIndex => checked((int)_fontData.FaceIndex);
        public ushort UnitsPerEm => _unitsPerEm;
        public uint GlyphCount => _fontData.GlyphCount;
        public uint VariationAxisCount => 0;
        public bool HasActiveVariations => false;

        public bool TryGetTable(OpenTypeTag tag, out ReadOnlyMemory<byte> table) =>
            _face.TryGetTable(tag.ToString(), out table);

        // The engine asks by name; answer straight from the SFNT directory instead of building a tag
        // and formatting it back into a string for every lookup.
        public bool TryGetTableByName(string tag, out ReadOnlyMemory<byte> table) =>
            _face.TryGetTable(tag, out table);

        public uint GetNominalGlyph(uint codePoint) => _fontData.GetGlyphIndex(codePoint);

        public bool TryGetVariationGlyph(uint codePoint, uint variationSelector, out uint glyphId)
        {
            glyphId = 0;
            return false;
        }

        public int GetHorizontalAdvance(uint glyphId) =>
            glyphId < _fontData.GlyphCount ? checked((int)_fontData.GetGlyphMetrics((ushort)glyphId).AdvanceWidth) : 0;

        public int GetVerticalAdvance(uint glyphId) =>
            glyphId < _fontData.GlyphCount ? checked((int)_fontData.GetGlyphMetrics((ushort)glyphId).AdvanceHeight) : 0;

        public int GetHorizontalOrigin(uint glyphId) => GetHorizontalAdvance(glyphId) / 2;

        public int GetVerticalOrigin(uint glyphId) =>
            glyphId < _fontData.GlyphCount ? _fontData.GetGlyphMetrics((ushort)glyphId).VerticalOriginY : 0;

        public bool TryGetGlyphExtents(uint glyphId, out ShapingGlyphExtents extents)
        {
            if (glyphId < _fontData.GlyphCount &&
                _face.TryGetGlyphBounds((ushort)glyphId, out ProGPU.Text.SfntGlyphBounds b))
            {
                // Same convention as TtfShapingFontFace and hb_glyph_extents_t: the box is anchored at
                // its top-left (XMin, YMax) and Height runs downwards, so it is negative (YMin - YMax).
                // The engine decodes it back with yMin = YBearing + Height.
                extents = new ShapingGlyphExtents(b.XMin, b.YMax, b.XMax - b.XMin, b.YMin - b.YMax);
                return true;
            }

            extents = default;
            return false;
        }

        public bool TryGetNormalizedVariationCoordinate(uint axisIndex, out short coordinate)
        {
            coordinate = 0;
            return false;
        }

        public float GetLayoutVariationDelta(ushort outerIndex, ushort innerIndex) => 0f;

        // Glyphs without recorded bounds (e.g. CFF fonts) are treated as drawable; the engine
        // only uses this to pick fallback glyphs for default-ignorable code points.
        public bool HasGlyphOutline(uint glyphId) => true;
    }

    internal sealed class PortableShapedRun
    {
        internal ushort[] ClusterMap;
        internal ushort[] GlyphIndices;
        internal int[] Advances;
        internal GlyphOffset[] Offsets;
        internal bool[] CanGlyphAlone;

        /// <summary>
        /// True for glyphs this layer substituted (formatting controls, soft hyphen). Their ids come from
        /// the caller's blank glyph, so a second pass must not compare them against the engine output.
        /// </summary>
        internal bool[] IsSubstitute;
    }

    internal static unsafe class PortableOpenTypeShaping
    {
        private const char SoftHyphen = '\u00AD';

        /// <summary>One engine result in design units, remembered so GetGlyphs and GetGlyphPlacements share it.</summary>
        private sealed class DesignRun
        {
            internal PortableFontData FontData;
            internal string Text;
            internal bool IsRightToLeft;
            internal string Language;
            internal OpenTypeTag Script;
            internal DWriteFontFeature[][] Features;
            internal uint[] FeatureRangeLengths;
            internal ShapingGlyph[] Glyphs;
        }

        // WPF shapes a run (GetGlyphs) and then asks for its placements (GetGlyphPlacements) with the
        // same inputs, so one remembered result per thread avoids shaping every run twice.
        [ThreadStatic]
        private static DesignRun t_last;

        /// <summary>
        /// Shapes one run. Returns null when the engine cannot represent the run (no glyphs, too many
        /// glyphs for WPF's 16-bit cluster map, or a font without a usable em size), so callers can fall
        /// back to simple code-point mapping.
        /// </summary>
        internal static PortableShapedRun Shape(
            char* textString,
            uint textLength,
            Font font,
            ushort blankGlyphIndex,
            bool isRightToLeft,
            CultureInfo cultureInfo,
            ItemProps itemProps,
            DWriteFontFeature[][] features,
            uint[] featureRangeLengths,
            double fontEmSize,
            double scalingFactor)
        {
            if (textLength == 0 || textLength > ushort.MaxValue || font.FontData.Metrics.DesignUnitsPerEm == 0)
            {
                return null;
            }

            int length = checked((int)textLength);
            var text = new ReadOnlySpan<char>(textString, length);
            PortableShapingFontFace face = PortableShapingFontFace.For(font.FontData);

            string language = cultureInfo == null || cultureInfo.Equals(CultureInfo.InvariantCulture)
                ? null
                : cultureInfo.IetfLanguageTag;
            OpenTypeTag script = itemProps == null ? OpenTypeTag.DefaultScript : new OpenTypeTag(itemProps.Script.ToString());

            DesignRun design = t_last;
            if (design == null ||
                !ReferenceEquals(design.FontData, font.FontData) ||
                design.IsRightToLeft != isRightToLeft ||
                design.Language != language ||
                design.Script != script ||
                !text.SequenceEqual(design.Text) ||
                !SameFeatures(design.Features, design.FeatureRangeLengths, features, featureRangeLengths))
            {
                var request = new ShapingRequest(
                    isRightToLeft ? ShapingDirection.RightToLeft : ShapingDirection.LeftToRight,
                    script,
                    language,
                    ShapingClusterLevel.MonotoneCharacters,
                    ShapingBufferFlags.PreserveDefaultIgnorables | ShapingBufferFlags.BeginningOfText | ShapingBufferFlags.EndOfText,
                    ToShapingFeatures(features, featureRangeLengths));

                using var buffer = new ShapingBuffer(length + 8);
                CpuOpenTypeShaper.Instance.Shape(text, face, request, buffer);
                design = new DesignRun
                {
                    FontData = font.FontData,
                    Text = new string(text),
                    IsRightToLeft = isRightToLeft,
                    Language = language,
                    Script = script,
                    Features = features,
                    FeatureRangeLengths = featureRangeLengths,
                    Glyphs = buffer.Glyphs.ToArray(),
                };
                t_last = design;
            }

            int glyphCount = design.Glyphs.Length;
            if (glyphCount == 0 || glyphCount > ushort.MaxValue)
            {
                return null;
            }

            double unitsPerEm = face.UnitsPerEm;
            double scale = fontEmSize * scalingFactor / unitsPerEm;
            ushort hyphenGlyph = font.FontData.GetGlyphIndex(TextAnalyzer.CharHyphen);
            ShapingGlyph[] shaped = design.Glyphs;

            var run = new PortableShapedRun
            {
                ClusterMap = new ushort[length],
                GlyphIndices = new ushort[glyphCount],
                Advances = new int[glyphCount],
                Offsets = new GlyphOffset[glyphCount],
                CanGlyphAlone = new bool[length],
                IsSubstitute = new bool[glyphCount],
            };

            // Per-cluster bookkeeping, indexed by the cluster's first character.
            var firstGlyph = new int[length];
            var glyphsInCluster = new int[length];
            Array.Fill(firstGlyph, -1);

            for (int g = 0; g < glyphCount; g++)
            {
                ref readonly ShapingGlyph glyph = ref shaped[g];
                int cluster = Math.Clamp(glyph.Cluster, 0, length - 1);
                if (firstGlyph[cluster] < 0 || g < firstGlyph[cluster])
                {
                    firstGlyph[cluster] = g;
                }

                glyphsInCluster[cluster]++;

                ushort glyphId = (ushort)Math.Min(glyph.GlyphId, ushort.MaxValue);
                int advance = glyph.AdvanceX;
                uint codePoint = glyph.CodePoint;

                if (IsFormattingControl(codePoint) || IsDefaultIgnorable(codePoint))
                {
                    // DirectWrite shows controls and default-ignorable code points (ZWSP, ZWJ/ZWNJ, bidi
                    // marks, BOM, word joiner...) as the blank glyph with zero advance. The engine keeps
                    // them (PreserveDefaultIgnorables, so every character stays in a cluster) and may pick a
                    // visible fallback glyph, so blank them after shaping.
                    glyphId = blankGlyphIndex;
                    advance = 0;
                    run.IsSubstitute[g] = true;
                }
                else if (codePoint == SoftHyphen)
                {
                    glyphId = hyphenGlyph != 0 ? hyphenGlyph : blankGlyphIndex;
                    advance = face.GetHorizontalAdvance(glyphId);
                    run.IsSubstitute[g] = true;
                }

                run.GlyphIndices[g] = glyphId;
                run.Advances[g] = checked((int)Math.Round(advance * scale));
                run.Offsets[g] = new GlyphOffset
                {
                    du = checked((int)Math.Round(glyph.OffsetX * scale)),
                    dv = checked((int)Math.Round(glyph.OffsetY * scale)),
                };
            }

            // WPF's cluster map gives every character the first glyph of the cluster that holds it.
            var charsInCluster = new int[length];
            int current = Math.Max(0, firstGlyph[0]);
            int currentStart = 0;
            for (int i = 0; i < length; i++)
            {
                if (firstGlyph[i] >= 0)
                {
                    current = firstGlyph[i];
                    currentStart = i;
                }

                run.ClusterMap[i] = (ushort)current;
                charsInCluster[currentStart]++;
            }

            int start = 0;
            for (int i = 0; i < length; i++)
            {
                if (firstGlyph[i] >= 0)
                {
                    start = i;
                }

                run.CanGlyphAlone[i] = charsInCluster[start] == 1 && glyphsInCluster[start] == 1;
            }

            return run;
        }

        private static bool SameFeatures(DWriteFontFeature[][] a, uint[] aRanges, DWriteFontFeature[][] b, uint[] bRanges)
        {
            if (ReferenceEquals(a, b) && ReferenceEquals(aRanges, bRanges))
            {
                return true;
            }

            int aCount = a?.Length ?? 0;
            if (aCount != (b?.Length ?? 0) || (aRanges?.Length ?? 0) != (bRanges?.Length ?? 0))
            {
                return false;
            }

            for (int i = 0; i < (aRanges?.Length ?? 0); i++)
            {
                if (aRanges[i] != bRanges[i])
                {
                    return false;
                }
            }

            for (int i = 0; i < aCount; i++)
            {
                DWriteFontFeature[] x = a[i];
                DWriteFontFeature[] y = b[i];
                if ((x?.Length ?? 0) != (y?.Length ?? 0))
                {
                    return false;
                }

                for (int j = 0; j < (x?.Length ?? 0); j++)
                {
                    if (x[j].nameTag != y[j].nameTag || x[j].parameter != y[j].parameter)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static ReadOnlyMemory<ShapingFeature> ToShapingFeatures(DWriteFontFeature[][] features, uint[] rangeLengths)
        {
            if (features == null || features.Length == 0)
            {
                return default;
            }

            var list = new System.Collections.Generic.List<ShapingFeature>();
            uint start = 0;
            for (int range = 0; range < features.Length; range++)
            {
                uint rangeLength = rangeLengths != null && range < rangeLengths.Length ? rangeLengths[range] : uint.MaxValue - start;
                uint end = rangeLength == uint.MaxValue ? uint.MaxValue : start + rangeLength;
                DWriteFontFeature[] rangeFeatures = features[range];
                if (rangeFeatures != null)
                {
                    foreach (DWriteFontFeature feature in rangeFeatures)
                    {
                        string tag = FeatureTag(feature.nameTag);
                        if (tag != null)
                        {
                            list.Add(new ShapingFeature(OpenTypeTag.Parse(tag), feature.parameter, start, end));
                        }
                    }
                }

                start = end;
            }

            return list.ToArray();
        }

        private static bool IsFormattingControl(uint codePoint) => codePoint < 0x20 || (codePoint >= 0x7F && codePoint <= 0x9F);

        // Unicode Default_Ignorable_Code_Point, minus U+00AD (soft hyphen, handled separately because
        // it renders as a hyphen when a line breaks there) and the variation selectors (merged into
        // the preceding glyph by the engine).
        private static bool IsDefaultIgnorable(uint cp) =>
            cp == 0x034F || cp == 0x061C || (cp >= 0x115F && cp <= 0x1160) || (cp >= 0x17B4 && cp <= 0x17B5) ||
            (cp >= 0x180B && cp <= 0x180F) || (cp >= 0x200B && cp <= 0x200F) || (cp >= 0x202A && cp <= 0x202E) ||
            (cp >= 0x2060 && cp <= 0x206F) || cp == 0x3164 || cp == 0xFEFF || cp == 0xFFA0 ||
            (cp >= 0xFFF0 && cp <= 0xFFF8) || (cp >= 0x1BCA0 && cp <= 0x1BCA3) ||
            (cp >= 0x1D173 && cp <= 0x1D17A) || (cp >= 0xE0000 && cp <= 0xE0FFF && !(cp >= 0xE0100 && cp <= 0xE01EF));

        // DWRITE_FONT_FEATURE_TAG -> OpenType feature tag.
        private static string FeatureTag(DWriteFontFeatureTag tag) => tag switch
        {
            DWriteFontFeatureTag.AlternateAnnotationForms => "nalt",
            DWriteFontFeatureTag.AlternateHalfWidth => "halt",
            DWriteFontFeatureTag.AlternativeFractions => "afrc",
            DWriteFontFeatureTag.CapitalSpacing => "cpsp",
            DWriteFontFeatureTag.CaseSensitiveForms => "case",
            DWriteFontFeatureTag.ContextualAlternates => "calt",
            DWriteFontFeatureTag.ContextualLigatures => "clig",
            DWriteFontFeatureTag.ContextualSwash => "cswh",
            DWriteFontFeatureTag.DiscretionaryLigatures => "dlig",
            DWriteFontFeatureTag.ExpertForms => "expt",
            DWriteFontFeatureTag.Fractions => "frac",
            DWriteFontFeatureTag.FullWidth => "fwid",
            DWriteFontFeatureTag.HalfWidth => "hwid",
            DWriteFontFeatureTag.HistoricalForms => "hist",
            DWriteFontFeatureTag.HistoricalLigatures => "hlig",
            DWriteFontFeatureTag.HojoKanjiForms => "hojo",
            DWriteFontFeatureTag.JIS04Forms => "jp04",
            DWriteFontFeatureTag.JIS78Forms => "jp78",
            DWriteFontFeatureTag.JIS83Forms => "jp83",
            DWriteFontFeatureTag.JIS90Forms => "jp90",
            DWriteFontFeatureTag.Kerning => "kern",
            DWriteFontFeatureTag.LiningFigures => "lnum",
            DWriteFontFeatureTag.MathematicalGreek => "mgrk",
            DWriteFontFeatureTag.NLCKanjiForms => "nlck",
            DWriteFontFeatureTag.OldStyleFigures => "onum",
            DWriteFontFeatureTag.Ordinals => "ordn",
            DWriteFontFeatureTag.PetiteCapitals => "pcap",
            DWriteFontFeatureTag.PetiteCapitalsFromCapitals => "c2pc",
            DWriteFontFeatureTag.ProportionalAlternateWidth => "palt",
            DWriteFontFeatureTag.ProportionalFigures => "pnum",
            DWriteFontFeatureTag.ProportionalWidths => "pwid",
            DWriteFontFeatureTag.QuarterWidths => "qwid",
            DWriteFontFeatureTag.RubyNotationForms => "ruby",
            DWriteFontFeatureTag.ScientificInferiors => "sinf",
            DWriteFontFeatureTag.SimplifiedForms => "smpl",
            DWriteFontFeatureTag.SlashedZero => "zero",
            DWriteFontFeatureTag.SmallCapitals => "smcp",
            DWriteFontFeatureTag.SmallCapitalsFromCapitals => "c2sc",
            DWriteFontFeatureTag.StandardLigatures => "liga",
            DWriteFontFeatureTag.StylisticAlternates => "salt",
            DWriteFontFeatureTag.Subscript => "subs",
            DWriteFontFeatureTag.Superscript => "sups",
            DWriteFontFeatureTag.Swash => "swsh",
            DWriteFontFeatureTag.TabularFigures => "tnum",
            DWriteFontFeatureTag.ThirdWidths => "twid",
            DWriteFontFeatureTag.Titling => "titl",
            DWriteFontFeatureTag.TraditionalForms => "trad",
            DWriteFontFeatureTag.TraditionalNameForms => "tnam",
            DWriteFontFeatureTag.Unicase => "unic",
            >= DWriteFontFeatureTag.StylisticSet1 and <= DWriteFontFeatureTag.StylisticSet20 =>
                "ss" + ((int)tag - (int)DWriteFontFeatureTag.StylisticSet1 + 1).ToString("00", CultureInfo.InvariantCulture),
            _ => null,
        };
    }
}
