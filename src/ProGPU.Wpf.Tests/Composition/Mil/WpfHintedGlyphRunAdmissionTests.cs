using System.Numerics;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

// No font or native generation is created. These verify capability routing,
// fail-closed source admission and rejected-owner retirement, not hinted pixels.
public sealed class WpfHintedGlyphRunAdmissionTests
{
    [Fact]
    public void MissingHintedCapabilityDoesNotInventAnOwner()
    {
        Assert.False(WpfResourceResolver.TryAcquireHintedGlyphRun(new object(), out var binding));
        Assert.Null(binding);
        var absent = new Source(false, null);
        Assert.False(WpfResourceResolver.TryAcquireHintedGlyphRun(absent, out binding));
        Assert.Null(binding);
        Assert.Equal(0, absent.DesignExports);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownHintedProviderIsNeverAdaptedAsDesignFont(bool cleanupFault)
    {
        var owner = new UnknownBinding(cleanupFault);
        var source = new Source(true, owner);
        var error = Assert.Throws<NotSupportedException>(() => WpfResourceResolver.TryAdaptNativeGlyphRun(source, out _));
        Assert.Equal(1, owner.DisposeCalls);
        Assert.Equal(0, source.DesignExports);
        Assert.Equal(cleanupFault, error.Data.Contains("HintedBindingCleanupFailure"));
    }

    [Fact]
    public void MalformedSuccessCannotFallThroughToDesignFont()
    {
        var source = new Source(true, null);
        Assert.Throws<InvalidOperationException>(() => WpfResourceResolver.AdaptGlyphRun(source));
        Assert.Equal(0, source.DesignExports);
    }

    [Fact]
    public void MalformedFailureReleasesUnexpectedOwner()
    {
        var owner = new UnknownBinding(false);
        var source = new Source(false, owner);
        Assert.Throws<InvalidOperationException>(() => WpfResourceResolver.TryAcquireHintedGlyphRun(source, out _));
        Assert.Equal(1, owner.DisposeCalls);
        Assert.Equal(0, source.DesignExports);
    }

    [Fact]
    public void RecordedSinkRejectsUnknownOriginalProviderBeforePublishingCommands()
    {
        var owner = new UnknownBinding(false);
        var source = new Source(true, owner);
        var context = new global::ProGPU.Scene.DrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new System.Windows.Media.DrawingContext(context));
        Assert.Throws<NotSupportedException>(() => ((IWpfNativePrimitiveCommandSink)sink)
            .DrawNativeGlyphRun(System.Windows.Media.Brushes.Black, source));
        Assert.Empty(context.Commands);
        Assert.Equal(1, owner.DisposeCalls);
        Assert.Equal(0, source.DesignExports);
    }

    [Fact]
    public void NativeBatchCopiesOwnIndependentRetirementHandles()
    {
        var owners = WpfNativeHintedGlyphResources.Create([]);
        var batch = new WpfNativeMilBatch([], 1) { HintedGlyphResources = owners };
        var copy = batch with { TargetHandle = 2 };
        batch.Dispose();
        Assert.Throws<ObjectDisposedException>(() => owners.Retain());
        Assert.Throws<ObjectDisposedException>(() => { _ = batch with { }; });
        Assert.Equal(0, copy.HintedGlyphResources!.BindingCount);
        Assert.True(copy.HintedGlyphResources.HasSameProducerTopology(copy.HintedGlyphResources));
        copy.Dispose(); copy.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = copy.HintedGlyphResources.BindingCount; });
    }

    private sealed class Source(bool success, IPortableHintedGlyphRunBinding? owner) :
        IPortableHintedGlyphRunSource, IPortableNativeGlyphRunSource
    {
        internal int DesignExports;
        public bool TryAcquirePortableHintedGlyphRun(out IPortableHintedGlyphRunBinding? binding)
        { binding = owner; return success; }
        public bool TryGetPortableNativeGlyphRun(out PortableNativeGlyphRun glyphRun)
        { DesignExports++; throw new InvalidOperationException("A hinted source must not request a design-font export."); }
    }

    private sealed class UnknownBinding(bool cleanupFault) : IPortableHintedGlyphRunBinding
    {
        internal int DisposeCalls;
        public bool IsDisposed => DisposeCalls != 0;
        public float FontRenderingEmSize => throw new InvalidOperationException();
        public float DpiScale => throw new InvalidOperationException();
        public sbyte BidiLevel => throw new InvalidOperationException();
        public Vector2 Origin => throw new InvalidOperationException();
        public ReadOnlyMemory<ushort> GlyphIndices => throw new InvalidOperationException();
        public ReadOnlyMemory<Vector2> GlyphPositions => throw new InvalidOperationException();
        public PortableRect InkBounds => throw new InvalidOperationException();
        public PortableRect BaselineRelativeInkBounds => throw new InvalidOperationException();
        public IPortableHintedGlyphRunBinding Retain() => throw new InvalidOperationException();
        public IPortableHintedTextGlyphRun AcquireGlyphRun() => throw new InvalidOperationException();
        public void Dispose()
        {
            DisposeCalls++;
            if (cleanupFault) throw new InvalidOperationException("Retirement failed.");
        }
    }
}
