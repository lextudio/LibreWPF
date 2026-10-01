using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using ProGPU.Scene.Native;
using ProGPU.Text;
using System.Numerics;

namespace System.Windows.Media.ProGPU.Composition;

/// <summary>
/// An explicit source reference to one original native hinted generation. The
/// source formatter/DrawGlyphRun paths do not select this capability yet.
/// </summary>
internal sealed partial class WpfHintedTextParagraph : IPortableHintedTextParagraph, IPortableInlineTextParagraph
{
    private readonly object _gate = new();
    private readonly Generation _generation;
    private readonly WpfHintedTextLifetime.Lease _use;

    private WpfHintedTextParagraph(Generation generation, WpfHintedTextLifetime.Lease use)
    { _generation = generation; _use = use; }

    // Caller owns resource on failure; successful publication transfers it.
    internal static WpfHintedTextParagraph Adopt(NativeHintedGlyphResource resource, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(sourceText);
        var generation = new Generation(resource, sourceText);
        return Create(generation, generation.Lifetime.Acquire());
    }

    private static WpfHintedTextParagraph Create(Generation generation, WpfHintedTextLifetime.Lease use)
    {
        try { return new(generation, use); }
        catch (Exception error) { ReleaseFailedUse(use, error); throw; }
    }

    private static void ReleaseFailedUse(WpfHintedTextLifetime.Lease use, Exception error)
    {
        try { use.Dispose(); }
        catch (Exception cleanup)
        {
            try { error.Data["HintedSourceUseCleanupFailure"] = cleanup; }
            catch { /* Preserve the original publication failure. */ }
        }
    }

    public bool IsDisposed => _use.IsDisposed;
    public float DpiScale => Read(_generation.DpiScale);
    public ReadOnlyMemory<char> SourceText => Read(_generation.SourceText.AsMemory());
    public ReadOnlyMemory<PortableHintedTextGlyph> Glyphs => Read<ReadOnlyMemory<PortableHintedTextGlyph>>(_generation.Glyphs);
    public ReadOnlyMemory<PortableHintedTextLine> Lines => Read<ReadOnlyMemory<PortableHintedTextLine>>(_generation.Lines);
    public ReadOnlyMemory<PortableHintedTextClusterBox> Boxes => Read<ReadOnlyMemory<PortableHintedTextClusterBox>>(_generation.Boxes);
    public ReadOnlyMemory<PortableHintedTextCaret> Carets => Read<ReadOnlyMemory<PortableHintedTextCaret>>(_generation.Carets);

    private T Read<T>(T value)
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(IsDisposed, this); return value; }
    }

    public IPortableHintedTextParagraph Retain()
    {
        lock (_gate) return Create(_generation, _use.Retain());
    }

    public IPortableHintedTextGlyphRun AcquireGlyphRun(ReadOnlySpan<int> positionedIndices)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            // Own the complete index list before validating; later caller
            // mutation cannot change a checked occurrence or its draw order.
            int[] indices = positionedIndices.ToArray();
            foreach (int index in indices)
                if ((uint)index >= (uint)_generation.Glyphs.Length)
                    throw new ArgumentOutOfRangeException(nameof(positionedIndices));
            return GlyphRun.Create(_generation, _use.Retain(), indices);
        }
    }

    // Original native metadata/geometry is borrowed under an independent
    // producer read lease. No public Font/object annotation crosses the seam.
    internal NativeHintedGlyphResourceReadLease AcquireNativeReadLease()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _generation.Resource.AcquireReadLease();
        }
    }

    public void Dispose() { lock (_gate) _use.Dispose(); }

    private sealed class Generation : IDisposable
    {
        private readonly object _geometryGate = new();
        private HintedGlyphGeometry? _geometry;
        internal readonly NativeHintedGlyphResource Resource;
        internal readonly WpfHintedTextLifetime Lifetime;
        internal readonly string SourceText;
        internal readonly float DpiScale;
        internal readonly PortableHintedTextGlyph[] Glyphs;
        internal readonly PortableHintedTextLine[] Lines;
        internal readonly PortableHintedTextClusterBox[] Boxes;
        internal readonly PortableHintedTextCaret[] Carets;
        internal readonly SourceParagraphData? Source;

        internal Generation(NativeHintedGlyphResource resource, string sourceText)
        {
            using var read = resource.AcquireReadLease();
            if (read.HasLineFrames && read.HasNominalMetrics) Source = new SourceParagraphData(read);
            DpiScale = read.DpiScale;
            SourceText = sourceText;
            Glyphs = new PortableHintedTextGlyph[read.Glyphs.Length];
            for (int i = 0; i < Glyphs.Length; i++)
            {
                var glyph = read.Glyphs[i];
                var owner = read.PositionedOwners[i];
                var run = read.Runs[checked((int)owner.RunIndex)];
                Glyphs[i] = new(i, glyph.GlyphIndex, glyph.GlyphId, glyph.FontIndex, run.StyleIndex,
                    owner.RunIndex, owner.RunGlyphIndex, owner.DescriptorIndex,
                    glyph.Cluster, read.ClusterEnds[i], read.BidiLevels[i],
                    glyph.X, glyph.Y, glyph.AdvanceX, glyph.AdvanceY);
            }
            Lines = new PortableHintedTextLine[read.Lines.Length];
            for (int i = 0; i < Lines.Length; i++)
            {
                var line = read.Lines[i];
                Lines[i] = new(checked((int)line.GlyphStart), checked((int)line.GlyphCount),
                    line.InputStart, line.InputEnd, line.Width, line.BaselineY, line.Height,
                    read.LineOrigins[i], line.Clipped != 0);
            }
            Boxes = new PortableHintedTextClusterBox[read.Boxes.Length];
            for (int i = 0; i < Boxes.Length; i++)
            {
                var box = read.Boxes[i];
                Boxes[i] = new(box.InputStart, box.InputEnd, checked((int)box.LineIndex),
                    box.X, box.Y, box.Width, box.Height, box.BidiLevel);
            }
            Carets = new PortableHintedTextCaret[read.Carets.Length];
            for (int i = 0; i < Carets.Length; i++)
            {
                var caret = read.Carets[i];
                Carets[i] = new(caret.InputPosition, caret.Trailing != 0, checked((int)caret.LineIndex),
                    caret.X, caret.Y, caret.Height, caret.BidiLevel);
            }
            // Native resource publication is last; its paragraph/font/geometry
            // owner remains live until the final source reference retires.
            Lifetime = new(this);
            Resource = resource;
        }

        internal HintedGlyphGeometry SelectGeometry(ReadOnlySpan<int> indices)
        {
            lock (_geometryGate)
            {
                _geometry ??= NativeHintedGlyphGeometryFactory.Create(Resource);
                return _geometry.SelectOccurrences(indices);
            }
        }

        public void Dispose()
        {
            lock (_geometryGate)
            {
                // Retry the exact owners after a failed teardown; no new source
                // use is admitted once Lifetime begins final retirement.
                _geometry?.Dispose();
                Resource.Dispose();
            }
        }
    }

    private sealed class GlyphRun : IPortableHintedTextGlyphRun, IPortableHintedGlyphRunBindingFactory
    {
        private readonly object _gate = new();
        private readonly Generation _generation;
        private readonly WpfHintedTextLifetime.Lease _use;
        private readonly int[] _indices;
        private GlyphRun(Generation generation, WpfHintedTextLifetime.Lease use, int[] indices)
        { _generation = generation; _use = use; _indices = indices; }

        internal static GlyphRun Create(Generation generation, WpfHintedTextLifetime.Lease use, int[] indices)
        {
            try { return new(generation, use, indices); }
            catch (Exception error) { ReleaseFailedUse(use, error); throw; }
        }

        public bool IsDisposed => _use.IsDisposed;
        public ReadOnlyMemory<int> PositionedGlyphIndices
        {
            get { lock (_gate) { ObjectDisposedException.ThrowIf(IsDisposed, this); return _indices; } }
        }
        public IPortableHintedTextGlyphRun Retain()
        {
            lock (_gate) return Create(_generation, _use.Retain(), _indices);
        }
        public IPortableHintedTextParagraph AcquireParagraph()
        {
            lock (_gate) return WpfHintedTextParagraph.Create(_generation, _use.Retain());
        }
        public IPortableHintedGlyphRunBinding BindGlyphRun(float sourceEmSize, Vector2 logicalOrigin)
            => BindGlyphRunCore(sourceEmSize, logicalOrigin, null, default, default, false);

        public void CopySourceOffsets(float sourceEmSize, Span<PortablePoint> sourceOffsets)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                if (sourceOffsets.Length < _indices.Length) throw new ArgumentException("Offset capacity does not cover this selection.", nameof(sourceOffsets));
                using var read = _generation.Resource.AcquireReadLease();
                WpfHintedGlyphRunBinding.Validate(read, _indices, sourceEmSize, Vector2.Zero);
                var indices = new uint[_indices.Length];
                for (int i = 0; i < indices.Length; i++) indices[i] = checked((uint)_indices[i]);
                var offsets = new NativeHintedSourceGlyphOffset[_indices.Length];
                read.CopySourceOffsets(indices, sourceEmSize, offsets);
                for (int i = 0; i < offsets.Length; i++) sourceOffsets[i] = new(offsets[i].X, offsets[i].Y);
            }
        }

        public IPortableHintedGlyphRunBinding BindGlyphRun(PortableTextFont sourceFont, float sourceEmSize,
            Vector2 logicalOrigin, ReadOnlySpan<double> sourceAdvances)
        {
            ArgumentNullException.ThrowIfNull(sourceFont);
            return BindGlyphRunCore(sourceEmSize, logicalOrigin, sourceFont, sourceAdvances, default, false);
        }

        public IPortableHintedGlyphRunBinding BindGlyphRun(PortableTextFont sourceFont, float sourceEmSize,
            Vector2 sourceBaselineOrigin, ReadOnlySpan<double> sourceAdvances, ReadOnlySpan<PortablePoint> sourceOffsets)
        {
            ArgumentNullException.ThrowIfNull(sourceFont);
            return BindGlyphRunCore(sourceEmSize, sourceBaselineOrigin, sourceFont, sourceAdvances, sourceOffsets, true);
        }

        private WpfHintedGlyphRunBinding BindGlyphRunCore(float sourceEmSize, Vector2 logicalOrigin,
            PortableTextFont? sourceFont, ReadOnlySpan<double> sourceAdvances, ReadOnlySpan<PortablePoint> sourceOffsets,
            bool validateSourceFrame)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                using var read = _generation.Resource.AcquireReadLease();
                WpfHintedGlyphRunBinding.Validate(read, _indices, sourceEmSize, logicalOrigin);
                if (sourceFont is not null)
                {
                    WpfHintedGlyphSourceIdentity.ValidateDefaultInstances(read.PositionedOwners, read.Runs, read.DeviceStyles, read.NormalizedCoordinates, _indices);
                    var originalFont = read.FontSources[checked((int)read.Glyphs[_indices[0]].FontIndex)];
                    WpfHintedGlyphSourceIdentity.ValidateFont(sourceFont, originalFont, read.FontBytes);
                    WpfHintedGlyphSourceIdentity.ValidateAdvances(sourceAdvances, read.Glyphs, _indices);
                }
                NativeHintedSourceGlyphFrame? frame = null;
                if (validateSourceFrame)
                {
                    if (sourceOffsets.Length != _indices.Length)
                        throw new ArgumentException("Source offsets must cover every original selected occurrence.", nameof(sourceOffsets));
                    var indices = new uint[_indices.Length];
                    var offsets = new NativeHintedSourceGlyphOffset[_indices.Length];
                    for (int i = 0; i < indices.Length; i++)
                    {
                        indices[i] = checked((uint)_indices[i]);
                        offsets[i] = new() { X = sourceOffsets[i].X, Y = sourceOffsets[i].Y };
                    }
                    frame = read.ValidateSourceFrame(indices, sourceEmSize, logicalOrigin, sourceAdvances, offsets);
                }
                var owner = Create(_generation, _use.Retain(), _indices);
                HintedGlyphGeometry? geometry = null;
                try
                {
                    geometry = _generation.SelectGeometry(_indices);
                    return WpfHintedGlyphRunBinding.Adopt(owner, _generation.Resource,
                        geometry, sourceEmSize, logicalOrigin, read, _indices, frame);
                }
                catch (Exception failure)
                {
                    try { geometry?.Dispose(); }
                    catch (Exception cleanup) { failure.Data["HintedGeometryCleanupFailure"] = cleanup; }
                    try { owner.Dispose(); }
                    catch (Exception cleanup) { failure.Data["HintedRunCleanupFailure"] = cleanup; }
                    throw;
                }
            }
        }
        public void Dispose() { lock (_gate) _use.Dispose(); }
    }
}
