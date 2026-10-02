using ProGPU.Backend.Native;
using System.Windows.Media.ProGPU.Composition;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

// Pure transport/partition controls. Native writer and interaction execution are
// independently authored controls, not replaced by these synthetic input arrays.
public sealed class WpfHintedSourceParagraphTests
{
    [Fact]
    public void SourceSnapshotsUseExplicitFramesAndOnlyOriginalCaretStops()
    {
        NativePositionedTextGlyph[] glyphs = [new() { GlyphId = 17, Cluster = 2, X = 25, Y = 70.5f, AdvanceX = 7, FontIndex = 3 }];
        NativePositionedTextLine[] lines = [new() { GlyphCount = 1, InputStart = 0, InputEnd = 7, Height = 19, BaselineY = 70.5f }];
        NativeHintedTextLineFrame[] frames = [new() { Top = 62.125, BaselineOffset = 8.375f, Flags = 1 }];
        NativeTextClusterBox[] boxes = [new() { LineIndex = 0, InputStart = 2, InputEnd = 4, X = 25, Y = 62.125f, Width = 7, Height = 19 }];
        NativeTextCaretStop[] carets = [new() { LineIndex = 0, InputPosition = 4 }, new() { LineIndex = 0, InputPosition = 2 }, new() { LineIndex = 0, InputPosition = 4 }];
        var data = new WpfHintedTextParagraph.SourceParagraphData(glyphs, [4], [1], lines, frames, boxes, carets);
        glyphs[0].X = -1; frames[0].Top = -1; carets[0].InputPosition = 99;
        Assert.Equal(62.125f, data.Lines[0].Y); Assert.Equal(8.375f, data.Frame(0).BaselineOffset);
        Assert.Equal(25, data.Glyphs[0].X); Assert.Equal(70.5f, data.Glyphs[0].Y);
        Assert.Equal(3U, data.Glyphs[0].FontIndex); Assert.Equal((sbyte)1, data.Glyphs[0].BidiLevel);
        Assert.Equal(new[] { 2, 4 }, data.LogicalCarets(0)); // Not synthetic input 0/7 endpoints.
        Assert.Equal((0, 1), data.BoxRange(0)); Assert.Equal((0, 3), data.CaretRange(0));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void InvalidOrUnmeasuredFrameRejects(int invalid)
    {
        var frame = new NativeHintedTextLineFrame { Flags = 1, BaselineOffset = 5 };
        switch (invalid)
        { case 0: frame.Flags = 0; break; case 1: frame.Top = double.NaN; break; case 2: frame.BaselineOffset = 11; break; case 3: frame.Top = -1; break; }
        Assert.Throws<NotSupportedException>(() => new WpfHintedTextParagraph.SourceParagraphData([], [], [],
            [new() { Height = 10 }], [frame], [], []));
    }

    [Fact]
    public void MissingMetadataAndUnpartitionedInteractionReject()
    {
        Assert.Throws<ArgumentException>(() => new WpfHintedTextParagraph.SourceParagraphData([], [], [], [default], [], [], []));
        Assert.Throws<InvalidOperationException>(() => new WpfHintedTextParagraph.SourceParagraphData([], [], [],
            [new() { Height = 10 }], [new() { Flags = 1 }], [], [new() { LineIndex = 1 }]));
    }

    [Fact]
    public void EmptyRowsNeverInventLogicalCaretStops()
    {
        var data = new WpfHintedTextParagraph.SourceParagraphData([], [], [],
            [new() { Height = 10, InputStart = 3, InputEnd = 4 }], [new() { Flags = 1, BaselineOffset = 5 }], [], []);
        Assert.Empty(data.LogicalCarets(0));
        Assert.Equal((0, 0), data.BoxRange(0)); Assert.Equal((0, 0), data.CaretRange(0));
    }
}
