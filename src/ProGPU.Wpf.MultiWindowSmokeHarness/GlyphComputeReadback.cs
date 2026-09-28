using System;
using System.IO;
using ProGPU.Backend;
using ProGPU.Text;

internal static class GlyphComputeReadback
{
    internal static void Validate(WgpuContext context)
    {
        var font = new TtfFont(Path.Combine(AppContext.BaseDirectory, "Fonts", "Inter-Regular.ttf"));
        using var atlas = CreateComputeAtlas(context);
        const string text = "Ag09";
        var glyphs = new GlyphInfo[text.Length];
        atlas.BeginBatch();
        try
        {
            for (int index = 0; index < text.Length; index++)
            {
                if (font.GetGlyphIndex(text[index]) == 0)
                    throw new InvalidOperationException("The pinned smoke font is missing a required glyph.");
                glyphs[index] = atlas.GetOrCreateGlyph(font, text[index], 24f);
            }
        }
        finally { atlas.EndBatch(); }

        // Completion/readback uses the product path and its existing deadlines.
        // Presented clear frames alone do not exercise Compute_GlyphRasterizer.
        byte[] pixels = atlas.AtlasTexture.ReadPixels();
        if (atlas.CapacityExceeded || atlas.RasterComputePassCount == 0 ||
            atlas.RasterBatchSubmissionCount == 0 || atlas.CompiledGpuGlyphCount != text.Length)
            throw new InvalidOperationException("Expected actual native-compute glyph submissions without fallback.");
        foreach (GlyphInfo glyph in glyphs)
            RequireCoverage(pixels, atlas.AtlasTexture.Width, atlas.AtlasTexture.Height, glyph);
        if (pixels[0] != 0)
            throw new InvalidOperationException("The untouched atlas guard texel was modified.");

        Console.WriteLine($"ProGPU WPF glyph compute readback succeeded: glyphs={text.Length}, " +
            $"passes={atlas.RasterComputePassCount}, adapter='{context.AdapterName} ({context.AdapterBackendType})'");
    }

    private static GlyphAtlas CreateComputeAtlas(WgpuContext context)
    {
        // This explicit qualification choice is captured by the new atlas only.
        // Restore the host preference even when atlas construction fails; neither
        // existing compositor policy nor backend/compiler defaults are changed.
        var original = context.ComputeExecutionPreference;
        try
        {
            context.ComputeExecutionPreference = GpuComputeExecutionPreference.NativeCompute;
            if (context.GlyphRasterizationPath != GpuComputeExecutionPath.NativeCompute)
                throw new InvalidOperationException("Native-compute glyph qualification cannot use a fallback.");
            return new GlyphAtlas(context, atlasSize: 256);
        }
        finally { context.ComputeExecutionPreference = original; }
    }

    internal static void RequireCoverage(byte[] pixels, uint width, uint height, GlyphInfo glyph)
    {
        if (width == 0 || height == 0 || (ulong)width * height != (ulong)pixels.LongLength ||
            glyph.IsColorBitmap || glyph.Width == 0 || glyph.Height == 0 ||
            (ulong)glyph.X + glyph.Width > width || (ulong)glyph.Y + glyph.Height > height)
            throw new InvalidOperationException("Expected a nonempty monochrome glyph inside the readback atlas.");
        for (uint y = glyph.Y; y < glyph.Y + glyph.Height; y++)
            for (uint x = glyph.X; x < glyph.X + glyph.Width; x++)
                if (pixels[checked((int)(y * width + x))] > 200) return;
        throw new InvalidOperationException("The glyph compute pipeline produced no solid coverage in its own atlas region.");
    }
}
