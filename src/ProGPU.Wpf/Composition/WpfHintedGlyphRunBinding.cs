using System.Numerics;
using ProGPU.Backend.Native;
using ProGPU.Text;
using ProGPU.Wpf.Interop;
using SceneDrawingContext = ProGPU.Scene.DrawingContext;

namespace System.Windows.Media.ProGPU.Composition;

/// <summary>Concrete transport of the original provider, never a design-font annotation.</summary>
internal sealed class WpfHintedGlyphRunBinding : IPortableHintedGlyphRunBinding
{
    private readonly State _state;
    private readonly WpfHintedTextLifetime.Lease _use;
    private WpfHintedGlyphRunBinding(State state, WpfHintedTextLifetime.Lease use)
    { _state = state; _use = use; }

    internal static void Validate(NativeHintedGlyphResourceReadLease read, ReadOnlySpan<int> indices,
        float sourceEmSize, Vector2 origin)
    {
        if (indices.IsEmpty || indices.Length > ushort.MaxValue || !float.IsFinite(sourceEmSize) || sourceEmSize <= 0 ||
            !float.IsFinite(origin.X) || !float.IsFinite(origin.Y))
            throw new ArgumentException("A hinted source run requires a finite original em/origin and a nonempty canonical-size selection.");
        uint? font = null;
        sbyte? level = null;
        foreach (int index in indices)
        {
            if ((uint)index >= (uint)read.Glyphs.Length) throw new ArgumentOutOfRangeException(nameof(indices));
            var glyph = read.Glyphs[index];
            var owner = read.PositionedOwners[index];
            var run = read.Runs[checked((int)owner.RunIndex)];
            var source = read.FontSources[checked((int)glyph.FontIndex)];
            if (glyph.GlyphId > ushort.MaxValue || (font.HasValue && font.Value != glyph.FontIndex) ||
                (level.HasValue && level.Value != read.BidiLevels[index]) ||
                sourceEmSize / source.UnitsPerEm != run.SourceScale)
                throw new NotSupportedException("A source GlyphRun must retain one exact original font, em size and bidi level.");
            font = glyph.FontIndex;
            level = read.BidiLevels[index];
        }
    }

    // Caller owns geometry and source run until successful return.
    internal static WpfHintedGlyphRunBinding Adopt(IPortableHintedTextGlyphRun owner,
        NativeHintedGlyphResource resource, HintedGlyphGeometry geometry, float em, Vector2 origin,
        NativeHintedGlyphResourceReadLease read, ReadOnlySpan<int> indices)
    {
        var state = new State(owner, resource, geometry, em, origin, read, indices);
        return new(state, state.Lifetime.Acquire());
    }

    private T Read<T>(T value) { ObjectDisposedException.ThrowIf(IsDisposed, this); return value; }
    public bool IsDisposed => _use.IsDisposed;
    public float FontRenderingEmSize => Read(_state.Em);
    public float DpiScale => Read(_state.Geometry.DpiScale);
    public sbyte BidiLevel => Read(_state.Level);
    public Vector2 Origin => Read(_state.Origin);
    public ReadOnlyMemory<ushort> GlyphIndices => Read<ReadOnlyMemory<ushort>>(_state.Ids);
    public ReadOnlyMemory<Vector2> GlyphPositions => Read<ReadOnlyMemory<Vector2>>(_state.Positions);
    public PortableRect InkBounds => Read(_state.Ink);
    public PortableRect BaselineRelativeInkBounds => Read(_state.RelativeInk);
    internal NativeHintedGlyphResource NativeResource => Read(_state.Resource);
    internal HintedGlyphGeometry Geometry => Read(_state.Geometry);
    internal uint FontIndex => Read(_state.FontIndex);
    internal ReadOnlyMemory<uint> NativeIndices => Read<ReadOnlyMemory<uint>>(_state.Indices);
    public IPortableHintedGlyphRunBinding Retain() => new WpfHintedGlyphRunBinding(_state, _use.Retain());
    public IPortableHintedTextGlyphRun AcquireGlyphRun()
    {
        using var hold = _use.Retain();
        return _state.Owner.Retain();
    }
    public void Dispose() => _use.Dispose();

    private sealed class State : IDisposable
    {
        internal readonly WpfHintedTextLifetime Lifetime;
        internal readonly IPortableHintedTextGlyphRun Owner;
        internal readonly NativeHintedGlyphResource Resource;
        internal readonly HintedGlyphGeometry Geometry;
        internal readonly float Em;
        internal readonly sbyte Level;
        internal readonly uint FontIndex;
        internal readonly Vector2 Origin;
        internal readonly ushort[] Ids;
        internal readonly Vector2[] Positions;
        internal readonly uint[] Indices;
        internal readonly PortableRect Ink, RelativeInk;

        internal State(IPortableHintedTextGlyphRun owner, NativeHintedGlyphResource resource,
            HintedGlyphGeometry geometry, float em, Vector2 origin,
            NativeHintedGlyphResourceReadLease read, ReadOnlySpan<int> indices)
        {
            if (!SceneDrawingContext.TryGetHintedGlyphInkBounds(geometry, origin, out var ink, out bool hasInk) ||
                !SceneDrawingContext.TryGetHintedGlyphInkBounds(geometry, Vector2.Zero, out var relative, out bool hasRelativeInk))
                throw new ArgumentException("Original hinted ink is not representable in the source frame.");
            Ids = new ushort[indices.Length];
            Positions = new Vector2[indices.Length];
            Indices = new uint[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                var glyph = read.Glyphs[indices[i]];
                Ids[i] = checked((ushort)glyph.GlyphId);
                Positions[i] = new(glyph.X, glyph.Y);
                Indices[i] = checked((uint)indices[i]);
            }
            Em = em; Origin = origin;
            FontIndex = read.Glyphs[indices[0]].FontIndex;
            Level = read.BidiLevels[indices[0]];
            Ink = hasInk ? new(ink.X, ink.Y, ink.Width, ink.Height) : PortableRect.Empty;
            RelativeInk = hasRelativeInk ? new(relative.X, relative.Y, relative.Width, relative.Height) : PortableRect.Empty;
            Lifetime = new(this);
            Owner = owner; Resource = resource; Geometry = geometry;
        }

        public void Dispose()
        {
            Exception? failure = null;
            try { Geometry.Dispose(); }
            catch (Exception error) { failure = error; }
            try { Owner.Dispose(); }
            catch (Exception error)
            {
                if (failure is null) failure = error;
                else try { failure.Data["HintedSourceOwnerCleanupFailure"] = error; } catch { }
            }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
