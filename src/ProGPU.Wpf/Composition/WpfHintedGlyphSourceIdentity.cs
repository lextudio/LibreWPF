using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

/// <summary>Exact synchronous admission against original provider records, without font execution or positioning.</summary>
internal static class WpfHintedGlyphSourceIdentity
{
    internal static void ValidateDefaultInstances(ReadOnlySpan<NativeHintedParagraphGlyphOwner> owners,
        ReadOnlySpan<NativeHintedParagraphRun> runs, ReadOnlySpan<NativeHintedParagraphDeviceStyle> styles,
        ReadOnlySpan<short> normalizedCoordinates, ReadOnlySpan<int> positionedIndices)
    {
        // Normalized coordinates belong to the original shared shaping context;
        // design-coordinate ranges belong to each selected physical style.
        if (!normalizedCoordinates.IsEmpty)
            throw new NotSupportedException("A source GlyphTypeface cannot identify the original hinted normalized-coordinate instance.");
        foreach (int index in positionedIndices)
        {
            var run = runs[checked((int)owners[index].RunIndex)];
            // The source GlyphTypeface contract has no variation-instance selector.
            if (styles[checked((int)run.StyleIndex)].VariationCount != 0)
                throw new NotSupportedException("A source GlyphTypeface cannot identify the original hinted variation-coordinate instance.");
        }
    }

    internal static void ValidateFont(PortableTextFont source, in NativeHintedGlyphFontSource original,
        ReadOnlySpan<byte> originalFontBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.FaceIndex != original.FaceIndex || source.UnitsPerEm != original.UnitsPerEm ||
            source.UnitsPerEm == 0 || original.ByteCount == 0 || original.ByteOffset > (uint)originalFontBytes.Length ||
            original.ByteCount > (uint)originalFontBytes.Length - original.ByteOffset ||
            !source.Data.Span.SequenceEqual(originalFontBytes.Slice(checked((int)original.ByteOffset), checked((int)original.ByteCount))))
            throw new ArgumentException("The source GlyphTypeface is not the hinted generation's exact original face bytes, index and units per em.", nameof(source));
    }

    internal static void ValidateAdvances(ReadOnlySpan<double> sourceAdvances,
        ReadOnlySpan<NativePositionedTextGlyph> originalGlyphs, ReadOnlySpan<int> positionedIndices)
    {
        if (sourceAdvances.Length != positionedIndices.Length)
            throw new ArgumentException("The source advance count must cover every selected original occurrence.", nameof(sourceAdvances));
        for (int i = 0; i < positionedIndices.Length; i++)
        {
            int index = positionedIndices[i];
            if ((uint)index >= (uint)originalGlyphs.Length) throw new ArgumentOutOfRangeException(nameof(positionedIndices));
            var original = originalGlyphs[index];
            if (!float.IsFinite(original.AdvanceX) || !float.IsFinite(original.AdvanceY) || original.AdvanceY != 0 ||
                !double.IsFinite(sourceAdvances[i]) ||
                BitConverter.DoubleToInt64Bits(sourceAdvances[i]) != BitConverter.DoubleToInt64Bits((double)original.AdvanceX))
                throw new ArgumentException("Source advances must equal the retained horizontal writer's exact per-occurrence advances.", nameof(sourceAdvances));
        }
    }
}
