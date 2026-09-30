using System.Linq;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using System.Windows.Media.ProGPU.Composition.Mil;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfNativeMilSceneCompilerTests
{
    [Theory]
    [InlineData(NativeMilBackend.WgpuNative, PortableVisualVisibility.Hidden)]
    [InlineData(NativeMilBackend.WgpuNative, PortableVisualVisibility.Collapsed)]
    [InlineData(NativeMilBackend.Dawn, PortableVisualVisibility.Hidden)]
    [InlineData(NativeMilBackend.Dawn, PortableVisualVisibility.Collapsed)]
    public void LocalVisibilitySnapshotExcludesMaskedSubtreeAndRestoresRetainedContent(
        NativeMilBackend backend, PortableVisualVisibility hidden)
    {
        var brush = new FakeBrush(new PortableColor(255, 255, 0, 0));
        var child = new FakeVisual(new FakeRenderData(CreateRectangleRecord(1, 0), [brush]),
            new PortableVisualState { HasOpacityMask = true, OpacityMask = brush });
        var state = new PortableVisualState { HasVisibility = true };
        var root = new FakeVisual(null, state, child);
        var compiler = new WpfNativeMilSceneCompiler();
        var batch = compiler.BuildBatch(root, 80, 60);
        var values = batch.VisualVisibilities.ToArray();
        Assert.Equal(0U, Assert.Single(values).Visibility);
        // Deliberately reuse producer storage: the session must own its previous
        // generation rather than comparing the caller's mutated memory to itself.
        batch = batch with { VisualVisibilities = values };
        using var session = new WpfNativeMilCompilationSession(backend);
        session.Update(batch);
        Assert.Equal(1U, session.CompileFrame(8251, 1, 0, 1,
            flags: NativeMilSceneBuildRequestFlags.HitTestIndex).Scene.Metrics.RectangleCount);

        state.Visibility = hidden;
        var hiddenBatch = compiler.BuildBatch(root, 80, 60);
        Assert.Equal(batch.Bytes, hiddenBatch.Bytes);
        Assert.Equal(2, hiddenBatch.VisualOwners.Count);
        values[0].Visibility = (uint)hidden;
        var update = session.Update(batch);
        Assert.False(update.RecreatedChannel);
        Assert.Equal(1U, update.AppliedSidebandCount);
        Assert.Equal(0U, session.CompileFrame(8251, 2, 0, 2,
            flags: NativeMilSceneBuildRequestFlags.HitTestIndex).Scene.Metrics.RectangleCount);

        values[0].Visibility = 0;
        Assert.Equal(1U, session.Update(batch).AppliedSidebandCount);
        Assert.Equal(1U, session.CompileFrame(8251, 3, 0, 3,
            flags: NativeMilSceneBuildRequestFlags.HitTestIndex).Scene.Metrics.RectangleCount);
        values[0].Visibility = (uint)hidden;
        session.Update(batch);
        // A complete empty snapshot restores compatibility-visible defaults.
        Assert.Equal(1U, session.Update(batch with { VisualVisibilities = default }).AppliedSidebandCount);
        Assert.Equal(1U, session.CompileFrame(8251, 4, 0, 4,
            flags: NativeMilSceneBuildRequestFlags.HitTestIndex).Scene.Metrics.RectangleCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenSourceKeepsSharedCacheBrushContentInEitherVisitOrder(bool sourceFirst)
    {
        var color = new FakeBrush(new PortableColor(255, 255, 0, 0));
        var state = new PortableVisualState { HasVisibility = true, Visibility = PortableVisualVisibility.Hidden };
        var source = new FakeVisual(new FakeRenderData(CreateRectangleRecord(1, 0), [color]), state);
        var brush = new FakeCacheBrush(new PortableBitmapCacheBrush(source));
        var painter = new FakeVisual(new FakeRenderData(CreateRectangleRecord(1, 0), [brush]));
        var root = new FakeVisual(null, null, sourceFirst ? [source, painter] : [painter, source]);
        var batch = new WpfNativeMilSceneCompiler().BuildBatch(root, 80, 60);
        Assert.Equal(3, batch.VisualOwners.Count);
        uint sourceHandle = Assert.Single(batch.VisualVisibilities.ToArray()).Handle;
        Assert.Equal((uint)PortableVisualVisibility.Hidden, batch.VisualVisibilities.Span[0].Visibility);
        Assert.Contains(batch.VisualCacheBounds!, item => item.Handle == sourceHandle);
        // The cache source remains the same content-bearing visual, never an
        // empty ordinary-owner replacement or a second guessed source identity.
        int cache = FindCommand(batch.Bytes, 0x84);
        Assert.Equal(sourceHandle, ReadUInt32(batch.Bytes, cache + 36));
        Assert.Equal(2, ReadCommands(batch.Bytes).Count(command => command == 0x22));
    }

    [Fact]
    public void UnknownDeclaredLocalVisibilityFailsClosed()
    {
        var state = new PortableVisualState { HasVisibility = true, Visibility = (PortableVisualVisibility)99 };
        var root = new FakeVisual(null, state);
        Assert.Throws<System.NotSupportedException>(() => new WpfNativeMilSceneCompiler().BuildBatch(root, 80, 60));
        state.HasVisibility = false;
        Assert.Empty(new WpfNativeMilSceneCompiler().BuildBatch(root, 80, 60).VisualVisibilities.ToArray());
    }
}
