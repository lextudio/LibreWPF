using System.Buffers;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

// Existing measured source paragraph contracts over the SAME hinted generation.
// No design-font alias, alternate layout or empty-row caret is manufactured.
internal sealed partial class WpfHintedTextParagraph
{
    private SourceParagraphData Source => _generation.Source
        ?? throw new NotSupportedException("Source formatting requires original nominal metrics and writer line frames.");

    ReadOnlyMemory<PortableTextGlyph> IPortableTextParagraph.Glyphs => Read<ReadOnlyMemory<PortableTextGlyph>>(Source.Glyphs);
    ReadOnlyMemory<PortableTextLineInfo> IPortableTextParagraph.Lines => Read<ReadOnlyMemory<PortableTextLineInfo>>(Source.Lines);
    ReadOnlyMemory<PortableTextInlineObjectPlacement> IPortableInlineTextParagraph.InlineObjects
        => Read(ReadOnlyMemory<PortableTextInlineObjectPlacement>.Empty);
    object? IPortableTextParagraph.NativeFont => throw new NotSupportedException("Hinted source glyphs have original typed owners, not a design font.");
    object? IPortableTextParagraph.GetNativeFont(uint fontIndex) => throw new NotSupportedException("Acquire the original hinted occurrence selection.");

    public IPortableTextParagraph Reflow(int inputStart, float maximumWidth)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_reflowing)
                throw new InvalidOperationException("A hinted paragraph cannot recursively publish a continuation.");
            _ = Source; // Require the same nominal metrics and native line frames.
            _reflowing = true;
            NativeHintedGlyphResource? resource = null;
            try
            {
                _failedReflow.Dispose(); // Do not accumulate failed native owners.
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                // The original producer owns boundary validation, full shaping
                // context and suffix placement. Source never shapes a substring,
                // rounds a cluster boundary or reconstructs glyph positions.
                resource = _generation.Resource.Reflow(inputStart, maximumWidth);
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                var paragraph = Adopt(resource, _generation.SourceText);
                resource = null; // The new independently owned generation adopted it.
                return paragraph;
            }
            catch (Exception failure)
            {
                if (resource is not null)
                {
                    _failedReflow.Capture(resource);
                    _failedReflow.DisposePreservingFailure(failure);
                }
                throw;
            }
            finally { _reflowing = false; }
        }
    }

    public float GetBaselineOffset(int lineIndex)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return Source.Frame(lineIndex).BaselineOffset;
        }
    }

    public PortableTextHit HitTest(int lineIndex, float distance)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var range = Source.BoxRange(lineIndex);
            using var read = _generation.Resource.AcquireReadLease();
            var boxes = read.Boxes.Slice(range.Start, range.Count);
            if (boxes.IsEmpty) throw new NotSupportedException("The original line has no retained hit geometry.");
            Check(NativeTextInteractionInterop.HitTest(boxes, distance, boxes[0].Y, out var hit));
            return new(hit.InputPosition, hit.Trailing != 0);
        }
    }

    public float GetCaretDistance(int lineIndex, PortableTextHit hit)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var range = Source.CaretRange(lineIndex);
            using var read = _generation.Resource.AcquireReadLease();
            if (range.Count == 0) throw new NotSupportedException("The original line has no retained caret geometry.");
            Check(NativeTextInteractionInterop.GetCaret(read.Carets.Slice(range.Start, range.Count), hit.Position, hit.Trailing, out var caret));
            return caret.X;
        }
    }

    public int GetNextLogicalCaret(int lineIndex, int position, bool previous)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            int[] stops = Source.LogicalCarets(lineIndex);
            if (stops.Length == 0) throw new NotSupportedException("The original line has no logical caret stops.");
            int index = Array.BinarySearch(stops, position);
            index = index >= 0 ? index + (previous ? -1 : 1) : ~index - (previous ? 1 : 0);
            return stops[Math.Clamp(index, 0, stops.Length - 1)];
        }
    }

    public int GetSelection(int lineIndex, int start, int end, Span<PortableRect> rectangles)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            _ = Source.Frame(lineIndex);
            using var read = _generation.Resource.AcquireReadLease();
            var temporary = ArrayPool<NativeTextRectangle>.Shared.Rent(rectangles.Length);
            try
            {
                int count = read.GetLineSelection(lineIndex, start, end, temporary.AsSpan(0, rectangles.Length));
                for (int i = 0; i < count; i++)
                {
                    var item = temporary[i];
                    rectangles[i] = new(item.X, item.Y, item.Width, item.Height);
                }
                return count;
            }
            finally { ArrayPool<NativeTextRectangle>.Shared.Return(temporary); }
        }
    }

    private static void Check(NativeRendererStatus status)
    {
        if (status == NativeRendererStatus.Success) return;
        throw new InvalidOperationException($"Original hinted paragraph interaction rejected the query: {status}.");
    }

    internal sealed class SourceParagraphData
    {
        internal readonly PortableTextGlyph[] Glyphs;
        internal readonly PortableTextLineInfo[] Lines;
        private readonly NativeHintedTextLineFrame[] _frames;
        private readonly (int Start, int Count)[] _boxes, _carets;
        private readonly int[][] _logicalCarets;

        internal SourceParagraphData(NativeHintedGlyphResourceReadLease read)
            : this(read.Glyphs, read.ClusterEnds, read.BidiLevels, read.Lines, read.LineFrames, read.Boxes, read.Carets) { }

        internal SourceParagraphData(ReadOnlySpan<NativePositionedTextGlyph> glyphs, ReadOnlySpan<int> ends,
            ReadOnlySpan<sbyte> levels, ReadOnlySpan<NativePositionedTextLine> lines, ReadOnlySpan<NativeHintedTextLineFrame> frames,
            ReadOnlySpan<NativeTextClusterBox> boxes, ReadOnlySpan<NativeTextCaretStop> carets)
        {
            if (ends.Length != glyphs.Length || levels.Length != glyphs.Length || frames.Length != lines.Length)
                throw new ArgumentException("Source snapshots must preserve complete original glyph and line coverage.");
            _frames = frames.ToArray();
            Lines = new PortableTextLineInfo[lines.Length];
            _boxes = new (int, int)[Lines.Length]; _carets = new (int, int)[Lines.Length];
            _logicalCarets = new int[Lines.Length][];
            Glyphs = new PortableTextGlyph[glyphs.Length];
            for (int i = 0; i < Glyphs.Length; i++)
            {
                var glyph = glyphs[i];
                if (glyph.AdvanceY != 0 || !float.IsFinite(glyph.AdvanceX))
                    throw new NotSupportedException("The source paragraph requires original horizontal measured advances.");
                Glyphs[i] = new(glyph.GlyphId, glyph.Cluster, ends[i], glyph.X, glyph.Y,
                    glyph.AdvanceX, levels[i], glyph.FontIndex);
            }
            int box = 0, caret = 0;
            for (int i = 0; i < Lines.Length; i++)
            {
                var line = lines[i]; var frame = _frames[i];
                if (frame.Flags != 1 || !double.IsFinite(frame.Top) || frame.Top < 0 || !float.IsFinite((float)frame.Top) ||
                    !float.IsFinite(frame.BaselineOffset) || frame.BaselineOffset < 0 || frame.BaselineOffset > line.Height ||
                    !float.IsFinite(line.Height) || line.Height <= 0 || line.GlyphStart > glyphs.Length || line.GlyphCount > glyphs.Length - line.GlyphStart)
                    throw new NotSupportedException("The source line requires its actual measured writer frame.");
                Lines[i] = new(checked((int)line.GlyphStart), checked((int)line.GlyphCount), line.InputStart, line.InputEnd,
                    line.Width, (float)frame.Top, line.Height);
                int firstBox = box, firstCaret = caret;
                while (box < boxes.Length && boxes[box].LineIndex == i) box++;
                while (caret < carets.Length && carets[caret].LineIndex == i) caret++;
                _boxes[i] = (firstBox, box - firstBox); _carets[i] = (firstCaret, caret - firstCaret);
                var logical = new SortedSet<int>();
                for (int j = firstCaret; j < caret; j++) logical.Add(carets[j].InputPosition);
                _logicalCarets[i] = [.. logical]; // Existing original stops only; no synthetic row endpoints.
            }
            if (box != boxes.Length || caret != carets.Length)
                throw new InvalidOperationException("Original interaction records do not partition the retained source lines.");
        }

        internal NativeHintedTextLineFrame Frame(int line)
        { if ((uint)line >= (uint)Lines.Length) throw new ArgumentOutOfRangeException(nameof(line)); return _frames[line]; }
        internal (int Start, int Count) BoxRange(int line) { _ = Frame(line); return _boxes[line]; }
        internal (int Start, int Count) CaretRange(int line) { _ = Frame(line); return _carets[line]; }
        internal int[] LogicalCarets(int line) { _ = Frame(line); return _logicalCarets[line]; }
    }
}
