// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using MS.Internal.TextFormatting;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media;

public class PortableTextParagraphReferenceTests
{
    [Fact]
    public void LineBreakAndCloneReferencesSurviveTheirOriginalOwners()
    {
        var producer = new HintedParagraph();
        using var line = PortableTextParagraphReference.Acquire(producer);
        using var lineBreak = line.Retain();
        using var clone = lineBreak.Retain();
        producer.Dispose();
        line.Dispose();
        lineBreak.Dispose();
        Assert.Equal(1, producer.State.Uses);
        Assert.NotSame(producer, clone.Paragraph);
        using var nextLine = clone.Retain();
        clone.Dispose();
        Assert.False(((IPortableHintedTextParagraph)nextLine.Paragraph).IsDisposed);
        Assert.Equal(1, producer.State.Uses);
        nextLine.Dispose();
        Assert.Equal(0, producer.State.Uses);
        Assert.Equal(1, producer.State.Retirements);
        Assert.Throws<ObjectDisposedException>(() => clone.Retain());
    }

    [Fact]
    public void FailedRetirementRetriesTheSameEndedReferenceWithoutAnotherUseEnd()
    {
        var producer = new HintedParagraph();
        using var line = PortableTextParagraphReference.Acquire(producer);
        producer.Dispose();
        producer.State.Failures = 1;
        Assert.Throws<InvalidOperationException>(() => line.Dispose());
        Assert.Equal(0, producer.State.Uses);
        Assert.Throws<ObjectDisposedException>(() => line.Retain());
        Assert.Throws<ObjectDisposedException>(() => line.Paragraph);
        line.Dispose();
        Assert.Equal(0, producer.State.Uses);
        Assert.Equal(2, producer.State.Attempts);
        Assert.Equal(1, producer.State.Retirements);
        line.Dispose();
        Assert.Equal(2, producer.State.Attempts);
    }

    [Fact]
    public void ReentrantRetirementCannotPublishOrRepeatTheEndedUse()
    {
        var producer = new HintedParagraph();
        using var line = PortableTextParagraphReference.Acquire(producer);
        producer.Dispose();
        producer.State.DuringRetirement = () =>
        {
            line.Dispose();
            Assert.Throws<ObjectDisposedException>(() => line.Retain());
        };
        line.Dispose();
        Assert.Equal(0, producer.State.Uses);
        Assert.Equal(1, producer.State.Attempts);
    }

    [Fact]
    public void ConstructionCleanupPreservesTheFailureAndKeepsRetirementRetryable()
    {
        var producer = new HintedParagraph();
        using var line = PortableTextParagraphReference.Acquire(producer);
        producer.Dispose();
        producer.State.Failures = 1;
        var original = new ArgumentException("source construction");
        var caught = Assert.Throws<ArgumentException>((Action)(() =>
        {
            try { throw original; }
            catch { line.DisposePreservingFailure(); throw; }
        }));
        Assert.Same(original, caught);
        line.Dispose();
        Assert.Equal(0, producer.State.Uses);
        Assert.Equal(1, producer.State.Retirements);
    }

    [Fact]
    public void OrdinaryManagedParagraphRemainsBorrowedAndUnchanged()
    {
        var paragraph = new Paragraph();
        using var first = PortableTextParagraphReference.Acquire(paragraph);
        using var second = first.Retain();
        first.Dispose();
        Assert.Same(paragraph, second.Paragraph);
        second.Dispose();
        Assert.Equal(0, paragraph.DisposeCalls);
    }

    private class Paragraph : IPortableTextParagraph, IDisposable
    {
        public int DisposeCalls;
        public ReadOnlyMemory<PortableTextGlyph> Glyphs => default;
        public ReadOnlyMemory<PortableTextLineInfo> Lines => default;
        public PortableTextHit HitTest(int lineIndex, float distance) => default;
        public float GetCaretDistance(int lineIndex, PortableTextHit hit) => 0;
        public int GetNextLogicalCaret(int lineIndex, int position, bool previous) => 0;
        public int GetSelection(int lineIndex, int start, int end, Span<PortableRect> rectangles) => 0;
        public virtual void Dispose() => DisposeCalls++;
    }

    private sealed class Lifetime
    {
        internal int Uses { get; set; } = 1;
        internal int Attempts { get; set; }
        internal int Retirements { get; set; }
        internal int Failures { get; set; }
        internal Action? DuringRetirement { get; set; }
    }

    private sealed class HintedParagraph : Paragraph, IPortableHintedTextParagraph
    {
        internal Lifetime State { get; }
        private bool _ended, _retired;
        internal HintedParagraph() : this(new Lifetime()) { }
        private HintedParagraph(Lifetime state) => State = state;
        public bool IsDisposed => _ended;
        public float DpiScale => 1;
        public ReadOnlyMemory<char> SourceText => default;
        ReadOnlyMemory<PortableHintedTextGlyph> IPortableHintedTextParagraph.Glyphs => default;
        ReadOnlyMemory<PortableHintedTextLine> IPortableHintedTextParagraph.Lines => default;
        public ReadOnlyMemory<PortableHintedTextClusterBox> Boxes => default;
        public ReadOnlyMemory<PortableHintedTextCaret> Carets => default;
        public IPortableHintedTextParagraph Retain()
        {
            ObjectDisposedException.ThrowIf(_ended, this);
            State.Uses++;
            return new HintedParagraph(State);
        }
        public IPortableHintedTextGlyphRun AcquireGlyphRun(ReadOnlySpan<int> positionedIndices) => throw new NotSupportedException();
        public override void Dispose()
        {
            if (!_ended) { _ended = true; State.Uses--; }
            if (State.Uses != 0 || _retired) return;
            State.Attempts++;
            State.DuringRetirement?.Invoke();
            if (State.Failures > 0) { State.Failures--; throw new InvalidOperationException("native retirement"); }
            State.Retirements++; _retired = true;
        }
    }
}
