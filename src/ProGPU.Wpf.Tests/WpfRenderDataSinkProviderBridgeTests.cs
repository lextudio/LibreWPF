using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Wpf.Interop;
using Xunit;
using ProGpuContainerVisual = ProGPU.Scene.ContainerVisual;
using ProGpuDrawingVisual = ProGPU.Scene.DrawingVisual;
using ProGpuRenderCommandType = ProGPU.Scene.RenderCommandType;
using ProGpuRetainedDrawingVisual = System.Windows.Media.ProGPU.Composition.ProGpuRetainedDrawingVisual;

namespace ProGPU.Wpf.Tests;

[CollectionDefinition(PortableRenderDataSinkProviderCollection.Name)]
public sealed class PortableRenderDataSinkProviderCollection
{
    public const string Name = "Portable render-data sink provider";
}

[Collection(PortableRenderDataSinkProviderCollection.Name)]
public sealed class WpfRenderDataSinkProviderBridgeTests
{
    [Fact]
    public void TryRegisterDrawingContextFactoryPushesTypedProviderFactory()
    {
        using var expectedContext = new DrawingContext(new ProGPU.Scene.DrawingContext());
        var ownerVisual = new FakeVisual();
        object? capturedOwner = null;

        var registered = WpfRenderDataSinkProviderBridge.TryRegisterDrawingContextFactory(
            owner =>
            {
                capturedOwner = owner;
                return expectedContext;
            },
            out var registration);

        Assert.True(registered);
        Assert.NotNull(registration);
        Assert.NotNull(PortableRenderDataDrawingContextSinkProvider.DrawingContextFactory);
        Assert.Same(expectedContext, PortableRenderDataDrawingContextSinkProvider.DrawingContextFactory(ownerVisual));
        Assert.Same(ownerVisual, capturedOwner);

        registration.Dispose();

        Assert.Null(PortableRenderDataDrawingContextSinkProvider.DrawingContextFactory);
    }

    [Fact]
    public void TryRegisterDrawingFrameUsesFrameDrawingContextFactory()
    {
        var root = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(root, 100, 50);

        var registered = WpfRenderDataSinkProviderBridge.TryRegisterDrawingContextFactory(
            frame.CreateDrawingContextFactory(),
            out var registration);

        Assert.True(registered);
        Assert.NotNull(registration);
        Assert.NotNull(PortableRenderDataDrawingContextSinkProvider.DrawingContextFactory);

        using (var context = PortableRenderDataDrawingContextSinkProvider.DrawingContextFactory!(new FakeVisual())!)
        {
            context.DrawRectangle(Brushes.Red, null, new System.Windows.Rect(1, 2, 3, 4));
        }

        Assert.Single(root.Context.Commands);

        registration.Dispose();
    }

    [Fact]
    public void TryRegisterObjectSinkFactoryPushesTypedProviderFactory()
    {
        var expectedSink = new RecordingPortableSink();
        var ownerVisual = new FakeVisual();
        object? capturedOwner = null;

        var registered = WpfRenderDataSinkProviderBridge.TryRegisterObjectSinkFactory(
            owner =>
            {
                capturedOwner = owner;
                return expectedSink;
            },
            out var registration);

        Assert.True(registered);
        Assert.NotNull(registration);
        Assert.NotNull(PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory);
        Assert.Same(expectedSink, PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory!(ownerVisual));
        Assert.Same(ownerVisual, capturedOwner);

        registration.Dispose();

        Assert.Null(PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory);
    }

    [Fact]
    public void TryRegisterRenderDataSinkProviderPrefersObjectSinkFactory()
    {
        var root = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(root, 100, 50);

        var registered = WpfRenderDataSinkProviderBridge.TryRegisterRenderDataSinkProvider(
            frame,
            imageSourceAdapter: null,
            out var registration);

        Assert.True(registered);
        Assert.NotNull(registration);
        Assert.NotNull(PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory);
        Assert.Null(PortableRenderDataDrawingContextSinkProvider.DrawingContextFactory);

        var context = Assert.IsType<WpfObjectRenderDataDrawingContext>(
            PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory!(new FakeVisual()));
        context.DrawRectangle(Brushes.Red, null, new PortableRect(1, 2, 3, 4));
        context.Close();

        Assert.Equal(1, frame.ObjectRenderDataSinkContextCount);
        Assert.Equal(0, frame.DrawingContextCount);
        Assert.Single(root.Context.Commands);

        registration.Dispose();
    }

    [Fact]
    public void TryRegisterRenderDataSinkProviderRoutesObjectSinkOwnersToRetainedBranches()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(
            new ProGpuContainerVisual(),
            retainedRoot,
            flatRoot,
            100,
            50,
            retainedVisualBranchMap: branchMap);
        var ownerVisual = new FakeVisual();
        var brush = Brushes.Red;

        var registered = WpfRenderDataSinkProviderBridge.TryRegisterRenderDataSinkProvider(
            frame,
            imageSourceAdapter: null,
            out var registration);

        Assert.True(registered);
        Assert.NotNull(registration);
        Assert.NotNull(PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory);

        using (var context = Assert.IsType<WpfObjectRenderDataDrawingContext>(
                   PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory!(ownerVisual)))
        {
            context.DrawRectangle(brush, null, new PortableRect(1, 2, 3, 4));
        }

        Assert.Equal(1, frame.ObjectRenderDataSinkContextCount);
        Assert.Equal(0, frame.DrawingContextCount);
        Assert.Empty(flatRoot.Context.Commands);
        var retainedFrameRoot = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var ownerBranch = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedFrameRoot.Children));
        Assert.Equal(ProGpuRenderCommandType.DrawRect, Assert.Single(ownerBranch.Context.Commands).Type);
        Assert.True(branchMap.TryGetVisuals(ownerVisual, out var ownerVisuals));
        Assert.Same(ownerBranch, Assert.Single(ownerVisuals));

        var dependencyTarget = Assert.Single(branchMap.GetReplayTargetsForSources(new object[] { brush }));
        Assert.Same(ownerVisual, dependencyTarget.Source);
        Assert.Same(ownerBranch, dependencyTarget.Visual);

        registration.Dispose();
    }

    [Fact]
    public void RenderDataSinkProviderBridgeUsesTypedProviderWithoutReflection()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "WpfRenderDataSinkProviderBridge.cs"));

        Assert.Contains("PortableRenderDataDrawingContextSinkProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BindingFlags", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MethodInfo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Expression.", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Invoke(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositionTargetDirectReplayUsesFrameScopedProviderRegistration()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfCompositionTarget.cs"));

        Assert.Contains("ProGpuWpfDrawingFrame drawingFrame = BeginDrawingFrame(pixelWidth, pixelHeight);", source, StringComparison.Ordinal);
        Assert.Contains("IWpfImageSourceAdapter? activeImageSourceAdapter = CreateFrameImageSourceAdapter(", source, StringComparison.Ordinal);
        Assert.Contains("drawingFrame.TryRegisterRenderDataSinkProvider(activeImageSourceAdapter, out IDisposable? registration)", source, StringComparison.Ordinal);
        Assert.Contains("using var sink = drawingFrame.OpenCompositionCommandSink(null);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositionTargetExposesNativeChangeVersionsForWpfInvalidation()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfCompositionTarget.cs"));

        Assert.Contains("public long SceneChangeVersion => SceneRootVisual.ChangeVersion;", source, StringComparison.Ordinal);
        Assert.Contains("public long RetainedWpfChangeVersion => RetainedWpfVisualRoot.ChangeVersion;", source, StringComparison.Ordinal);
        Assert.Contains("public long FlatDrawingChangeVersion => RootVisual.ChangeVersion;", source, StringComparison.Ordinal);
        Assert.Contains("public int DirtySourceCount => WpfInvalidationTracker.DirtySourceCount;", source, StringComparison.Ordinal);
        Assert.Contains("public object? LastDirtySource => WpfInvalidationTracker.LastDirtySource;", source, StringComparison.Ordinal);
        Assert.Contains("public int LastRetainedBranchInvalidationCount { get; private set; }", source, StringComparison.Ordinal);
        Assert.Contains("public int LastRetainedBranchUnmappedSourceCount { get; private set; }", source, StringComparison.Ordinal);
        Assert.Contains("public int LastRetainedBranchSharedWithCleanSourceVisualCount { get; private set; }", source, StringComparison.Ordinal);
        Assert.Contains("public int LastRetainedBranchReplayTargetConflictCount { get; private set; }", source, StringComparison.Ordinal);
        Assert.Contains(
            "RetainedVisualBranchMap.InvalidateVisualsForReferenceSources(\n            WpfInvalidationTracker.DirtySourceSet,\n            WpfInvalidationTracker.LastDirtySource)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("LastRetainedBranchInvalidationUsedFallback = !result.CanTargetAllDirtySources;", source, StringComparison.Ordinal);
        Assert.Contains("internal bool TryPrepareDirtyRetainedVisualBranchReplayTargets(", source, StringComparison.Ordinal);
        Assert.Contains("internal bool TryReplayDirtyRetainedVisualBranches(", source, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<WpfRetainedVisualBranchReplayTarget> targets,", source, StringComparison.Ordinal);
        Assert.Contains("return TryGetDirtyRetainedVisualBranchReplayTargets(imageSourceAdapter, out targets);", source, StringComparison.Ordinal);
        Assert.Contains("IWpfImageSourceAdapter? activeImageSourceAdapter = imageSourceAdapter ?? WpfImageSourceAdapter;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("!TryGetDirtyRetainedVisualBranchReplayTargets(out var targets)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateFrameImageSourceAdapter(WpfImageSourceAdapter)))", source, StringComparison.Ordinal);
        Assert.Contains(
            "RetainedVisualBranchMap.GetReplayTargetsForReferenceSources(\n            WpfInvalidationTracker.DirtySourceSet,\n            WpfInvalidationTracker.LastDirtySource)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("RetainedVisualBranchMap.InvalidateVisualsForSources(WpfInvalidationTracker.DirtySources)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RetainedVisualBranchMap.GetReplayTargetsForSources(WpfInvalidationTracker.DirtySources)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WpfInvalidationTracker.DirtySources,\n            WpfInvalidationTracker.LastDirtySource", source, StringComparison.Ordinal);
        Assert.Contains("RetainedWpfVisualRoot.Invalidate();", source, StringComparison.Ordinal);
        Assert.Contains("public WpfVisualReplayResult ReplayVisualSubtreeRetained(", source, StringComparison.Ordinal);
        Assert.Contains("new ProGpuRetainedCompositionCommandSink(", source, StringComparison.Ordinal);
        var hostSource = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfWindowHost.cs"));
        Assert.Contains("target.LastRetainedBranchSharedWithCleanSourceVisualCount", hostSource, StringComparison.Ordinal);
        Assert.Contains("target.LastRetainedBranchReplayTargetConflictCount", hostSource, StringComparison.Ordinal);
        Assert.Contains("public long RetainedWpfBranchReplayCount { get; private set; }", hostSource, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<WpfRetainedVisualBranchReplayTarget> dirtyBranchReplayTargets = Array.Empty<WpfRetainedVisualBranchReplayTarget>();", hostSource, StringComparison.Ordinal);
        Assert.Contains("_target.TryPrepareDirtyRetainedVisualBranchReplayTargets(", hostSource, StringComparison.Ordinal);
        Assert.Contains("_target.TryReplayDirtyRetainedVisualBranches(", hostSource, StringComparison.Ordinal);
        Assert.Contains("dirtyBranchReplayTargets,", hostSource, StringComparison.Ordinal);
        Assert.Contains("RetainedWpfBranchReplayCount++;", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RetainedVisualBranchMapFastPathsSingleSourceReplayTargets()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "WpfRetainedVisualBranchMap.cs"));

        var referenceSetFastPathIndex = source.IndexOf("return GetReplayTargetsForReferenceSourceSet(referenceDirtySources, singleSourceHint);", StringComparison.Ordinal);
        var genericCollectionIndex = source.IndexOf("if (sources is IReadOnlyCollection<object> sourceCollection)", StringComparison.Ordinal);
        var multiSourceIndex = source.IndexOf(
            "_scratchDistinctSources.Clear();",
            genericCollectionIndex,
            StringComparison.Ordinal);
        var invalidationReferenceSetFastPathIndex = source.IndexOf("return InvalidateVisualsForReferenceSourceSet(referenceVisitedSources, singleSourceHint);", StringComparison.Ordinal);
        var invalidationGenericCollectionIndex = source.IndexOf(
            "if (sources is IReadOnlyCollection<object> sourceCollection)",
            invalidationReferenceSetFastPathIndex,
            StringComparison.Ordinal);

        Assert.True(referenceSetFastPathIndex >= 0);
        Assert.True(genericCollectionIndex > referenceSetFastPathIndex);
        Assert.True(multiSourceIndex > genericCollectionIndex);
        Assert.True(invalidationReferenceSetFastPathIndex >= 0);
        Assert.True(invalidationGenericCollectionIndex > invalidationReferenceSetFastPathIndex);
        Assert.Contains("return GetReplayTargetsForSources(sources, singleSourceHint: null);", source, StringComparison.Ordinal);
        Assert.Contains("return InvalidateVisualsForSources(sources, singleSourceHint: null).InvalidatedVisualCount;", source, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForSources(\n        IEnumerable<object> sources,\n        object? singleSourceHint)", source, StringComparison.Ordinal);
        Assert.Contains("internal IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForReferenceSources(", source, StringComparison.Ordinal);
        Assert.Contains("return GetReplayTargetsForReferenceSourceSet(sources, singleSourceHint);", source, StringComparison.Ordinal);
        Assert.Contains("public WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForSources(\n        IEnumerable<object> sources,\n        object? singleSourceHint)", source, StringComparison.Ordinal);
        Assert.Contains("GetReplayTargetsForSingleSource(singleSource)", source, StringComparison.Ordinal);
        Assert.Contains("TryGetReferenceEqualityHashSet(sources, out var referenceDirtySources)", source, StringComparison.Ordinal);
        Assert.Contains("private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForReferenceSourceSet(", source, StringComparison.Ordinal);
        Assert.Contains("private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForDistinctSourceSet(", source, StringComparison.Ordinal);
        Assert.Contains("private WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForReferenceSourceSet(", source, StringComparison.Ordinal);
        Assert.Contains("AddDistinctSources(sources, _scratchDistinctSources);", source, StringComparison.Ordinal);
        Assert.Contains("private static void AddDistinctSources(", source, StringComparison.Ordinal);
        Assert.Contains("sources is IList<object> list", source, StringComparison.Ordinal);
        Assert.Contains("distinctSources.Add(list[i]);", source, StringComparison.Ordinal);
        Assert.Contains("sources is IReadOnlyList<object> readOnlyList", source, StringComparison.Ordinal);
        Assert.Contains("distinctSources.Add(readOnlyList[i]);", source, StringComparison.Ordinal);
        Assert.Contains("source = list[0];", source, StringComparison.Ordinal);
        Assert.Contains("source = readOnlyList[0];", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var source in sources)\n            {\n                _scratchDistinctSources.Add(source);\n            }", source, StringComparison.Ordinal);
        Assert.Contains("TryGetSingleSource(dirtySources, singleSourceHint, out var singleSource)", source, StringComparison.Ordinal);
        Assert.Contains("TryGetSingleSource(visitedSources, singleSourceHint, out var singleSource)", source, StringComparison.Ordinal);
        Assert.Contains("private static bool TryGetSingleSource(\n        HashSet<object> sources,", source, StringComparison.Ordinal);
        Assert.Contains("object? singleSourceHint,", source, StringComparison.Ordinal);
        Assert.Contains("singleSourceHint != null && sources.Contains(singleSourceHint)", source, StringComparison.Ordinal);
        Assert.Contains("var dirtySourceEnumerator = dirtySources.GetEnumerator();", source, StringComparison.Ordinal);
        Assert.Contains("while (dirtySourceEnumerator.MoveNext())", source, StringComparison.Ordinal);
        Assert.Contains("var sourceEnumerator = sources.GetEnumerator();", source, StringComparison.Ordinal);
        Assert.Contains("private readonly HashSet<object> _scratchDistinctSources = new(ReferenceEqualityComparer.Instance);", source, StringComparison.Ordinal);
        Assert.Contains("private readonly HashSet<ProGpuVisual> _scratchVisitedVisuals = new(ReferenceEqualityComparer.Instance);", source, StringComparison.Ordinal);
        Assert.Contains("private readonly HashSet<ProGpuVisual> _scratchTargetVisuals = new(ReferenceEqualityComparer.Instance);", source, StringComparison.Ordinal);
        Assert.Contains("private readonly ReplayTargetList _scratchReplayTargets = new();", source, StringComparison.Ordinal);
        Assert.Contains("private readonly ReplayTargetList _scratchTopLevelReplayTargets = new();", source, StringComparison.Ordinal);
        Assert.Contains("private readonly Dictionary<object, VisualSet> _visualsBySource = new(ReferenceEqualityComparer.Instance);", source, StringComparison.Ordinal);
        Assert.Contains("private readonly Dictionary<ProGpuVisual, ReferenceOwnerSet> _sourcesByVisual = new(ReferenceEqualityComparer.Instance);", source, StringComparison.Ordinal);
        Assert.Contains("private readonly Dictionary<ProGpuVisual, ReferenceOwnerSet> _sourceOwnersByVisual = new(ReferenceEqualityComparer.Instance);", source, StringComparison.Ordinal);
        Assert.Contains("private readonly Dictionary<ProGpuVisual, ReferenceOwnerSet> _dependenciesByVisual = new(ReferenceEqualityComparer.Instance);", source, StringComparison.Ordinal);
        Assert.Contains("ref var visuals = ref CollectionsMarshal.GetValueRefOrAddDefault(_visualsBySource, source, out _);", source, StringComparison.Ordinal);
        Assert.Contains("private struct VisualSet : IReadOnlyList<ProGpuVisual>", source, StringComparison.Ordinal);
        Assert.Contains("public bool Remove(ProGpuVisual visual)", source, StringComparison.Ordinal);
        Assert.Contains("sources.Contains(source)", source, StringComparison.Ordinal);
        Assert.Contains("private readonly SingleReplayTargetList _scratchSingleReplayTarget = new();", source, StringComparison.Ordinal);
        Assert.Contains("private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> CreateSingleReplayTarget(", source, StringComparison.Ordinal);
        Assert.Contains("private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> ReturnReplayTargets(", source, StringComparison.Ordinal);
        Assert.Contains("private sealed class SingleReplayTargetList : IReadOnlyList<WpfRetainedVisualBranchReplayTarget>", source, StringComparison.Ordinal);
        Assert.Contains("private sealed class ReplayTargetList : IReadOnlyList<WpfRetainedVisualBranchReplayTarget>", source, StringComparison.Ordinal);
        Assert.Contains("public void Add(WpfRetainedVisualBranchReplayTarget target)", source, StringComparison.Ordinal);
        Assert.Contains("return targets;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_scratchReplayTargetSnapshot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SnapshotReplayTargets(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Set(List<WpfRetainedVisualBranchReplayTarget> targets)", source, StringComparison.Ordinal);
        Assert.Contains("using System.Runtime.InteropServices;", source, StringComparison.Ordinal);
        Assert.Contains("CollectionsMarshal.GetValueRefOrAddDefault(", source, StringComparison.Ordinal);
        Assert.Contains("private struct ReferenceOwnerSet", source, StringComparison.Ordinal);
        Assert.Contains("!sourceOwners.TryGetSingle(out replaySource)", source, StringComparison.Ordinal);
        Assert.Contains("public void ClassifyAgainst(\n            object dirtySource,", source, StringComparison.Ordinal);
        Assert.Contains("public void ClassifyAgainst(\n            HashSet<object> dirtySources,", source, StringComparison.Ordinal);
        Assert.Contains("SelectTopLevelReplayTargets(_scratchReplayTargets)", source, StringComparison.Ordinal);
        Assert.Contains("var targetCount = targets.Count;", source, StringComparison.Ordinal);
        Assert.Contains("for (var i = 0; i < targetCount; i++)", source, StringComparison.Ordinal);
        Assert.Contains("if (!IsCoveredByTargetAncestor(target.Visual, _scratchTargetVisuals))", source, StringComparison.Ordinal);
        Assert.Contains("private static bool IsCoveredByTargetAncestor(", source, StringComparison.Ordinal);
        Assert.Contains("private bool RemoveVisualForSource(", source, StringComparison.Ordinal);
        Assert.Contains("if (visuals.Count == 1)", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(visuals[0], visual)", source, StringComparison.Ordinal);
        Assert.Contains("_visualsBySource.Remove(source);", source, StringComparison.Ordinal);
        Assert.Contains("if (!visuals.Remove(visual))", source, StringComparison.Ordinal);
        Assert.Contains("_visualsBySource[source] = visuals;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<object, List<ProGpuVisual>>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<ProGpuVisual, HashSet<object>>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("visuals = new List<ProGpuVisual>();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_visualsBySource.Add(source, visuals);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ReferenceOwnerSet()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sources = new HashSet<object>(ReferenceEqualityComparer.Instance)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("owners = new HashSet<object>(ReferenceEqualityComparer.Instance)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var existing in visuals)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var source in dirtySources)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var candidate in sources)\n        {\n            source = candidate;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var dirtySources = new HashSet<object>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var targets = new List<WpfRetainedVisualBranchReplayTarget>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new WpfRetainedVisualBranchReplayTarget[targets.Count]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var visitedVisuals = new HashSet<ProGpuVisual>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var topLevelTargets = new List<WpfRetainedVisualBranchReplayTarget>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new[] { new WpfRetainedVisualBranchReplayTarget", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var candidateAncestor in targets)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var target in targets)", source, StringComparison.Ordinal);

        var compositionTargetSource = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfCompositionTarget.cs"));
        Assert.Contains("for (var i = 0; i < targets.Count; i++)", compositionTargetSource, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var target in targets)", compositionTargetSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RetainedVisualBranchMapFastPathsSingleSourceInvalidation()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "WpfRetainedVisualBranchMap.cs"));

        var referenceSetFastPathIndex = source.IndexOf("return InvalidateVisualsForReferenceSourceSet(referenceVisitedSources, singleSourceHint);", StringComparison.Ordinal);
        var genericCollectionIndex = source.IndexOf(
            "if (sources is IReadOnlyCollection<object> sourceCollection)",
            referenceSetFastPathIndex,
            StringComparison.Ordinal);
        var multiSourceIndex = source.IndexOf(
            "_scratchDistinctSources.Clear();",
            genericCollectionIndex,
            StringComparison.Ordinal);

        Assert.True(referenceSetFastPathIndex >= 0);
        Assert.True(genericCollectionIndex > referenceSetFastPathIndex);
        Assert.True(multiSourceIndex > genericCollectionIndex);
        Assert.Contains("private WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForSingleSource(object source)", source, StringComparison.Ordinal);
        Assert.Contains("internal WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForReferenceSources(", source, StringComparison.Ordinal);
        Assert.Contains("return InvalidateVisualsForReferenceSourceSet(sources, singleSourceHint);", source, StringComparison.Ordinal);
        Assert.Contains("TryGetReferenceEqualityHashSet(sources, out var referenceVisitedSources)", source, StringComparison.Ordinal);
        Assert.Contains("private WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForReferenceSourceSet(", source, StringComparison.Ordinal);
        Assert.Contains("private WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForDistinctSourceSet(", source, StringComparison.Ordinal);
        Assert.Contains("private readonly HashSet<ProGpuVisual> _scratchInvalidatedVisuals = new(ReferenceEqualityComparer.Instance);", source, StringComparison.Ordinal);
        Assert.Contains("_scratchInvalidatedVisuals.Clear();", source, StringComparison.Ordinal);
        Assert.Contains("var visitedSourceEnumerator = visitedSources.GetEnumerator();", source, StringComparison.Ordinal);
        Assert.Contains("while (visitedSourceEnumerator.MoveNext())", source, StringComparison.Ordinal);
        Assert.Contains("var invalidatedVisualEnumerator = _scratchInvalidatedVisuals.GetEnumerator();", source, StringComparison.Ordinal);
        Assert.Contains("while (invalidatedVisualEnumerator.MoveNext())", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(candidate.Comparer, ReferenceEqualityComparer.Instance)", source, StringComparison.Ordinal);
        Assert.Contains("if (sourceOwners.Count == 1)\n            {\n                continue;\n            }\n\n            replayTargetConflictCount++;", source, StringComparison.Ordinal);
        Assert.Contains("sourceOwners.ClassifyAgainst(\n                source,", source, StringComparison.Ordinal);
        Assert.Contains("sourceOwners.ClassifyAgainst(\n                    visitedSources,", source, StringComparison.Ordinal);
        Assert.Contains("hasDirtySourceOwner = _many.Contains(dirtySource);", source, StringComparison.Ordinal);
        Assert.Contains("hasCleanSourceOwner = _many.Count > (hasDirtySourceOwner ? 1 : 0);", source, StringComparison.Ordinal);
        Assert.Contains("new WpfRetainedVisualBranchInvalidationResult(\n            1,\n            1,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var visitedSources = new HashSet<object>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var invalidatedVisuals = new HashSet<ProGpuVisual>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var source in visitedSources)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var visual in _scratchInvalidatedVisuals)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var sourceOwner in sourceOwners)", source, StringComparison.Ordinal);

        var trackerSource = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "Mil",
            "WpfVisualInvalidationTracker.cs"));
        Assert.Contains("internal HashSet<object> DirtySourceSet => _dirtySources;", trackerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class ReferenceEqualityComparer", trackerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositionTargetAndHostPollTrackedWpfSourceVersionsBeforeFrameSkip()
    {
        var targetSource = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfCompositionTarget.cs"));
        var hostSource = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfWindowHost.cs"));

        Assert.Contains("public bool DetectWpfSourceChanges()", targetSource, StringComparison.Ordinal);
        Assert.Contains("return WpfInvalidationTracker.DetectVersionChanges();", targetSource, StringComparison.Ordinal);
        Assert.Contains("public bool ShouldReplayVisualSubtree(object rootVisual)", targetSource, StringComparison.Ordinal);
        Assert.Contains("WpfInvalidationTracker.IsDirty", targetSource, StringComparison.Ordinal);
        Assert.Contains("_target.DetectWpfSourceChanges();", hostSource, StringComparison.Ordinal);
        Assert.Contains("_target.ShouldReplayVisualSubtree(wpfRootVisual)", hostSource, StringComparison.Ordinal);
        Assert.Contains("clearRetainedWpfVisualRoot", hostSource, StringComparison.Ordinal);
        Assert.Contains("RetainedWpfReplaySkipCount++;", hostSource, StringComparison.Ordinal);
        Assert.True(
            hostSource.IndexOf("_target.DetectWpfSourceChanges();", StringComparison.Ordinal) <
            hostSource.IndexOf("var frameState = CaptureFrameState(", StringComparison.Ordinal));
    }

    private sealed class FakeVisual
    {
    }

    private sealed class RecordingPortableSink : IPortableRenderDataDrawingContextSink
    {
        public void DrawLine(object? pen, object? point0, object? point1) { }

        public void DrawLine(object? pen, object? point0, object? point0Animations, object? point1, object? point1Animations) { }

        public void DrawRectangle(object? brush, object? pen, object? rectangle) { }

        public void DrawRectangle(object? brush, object? pen, object? rectangle, object? rectangleAnimations) { }

        public void DrawRoundedRectangle(object? brush, object? pen, object? rectangle, object? radiusX, object? radiusY) { }

        public void DrawRoundedRectangle(
            object? brush,
            object? pen,
            object? rectangle,
            object? rectangleAnimations,
            object? radiusX,
            object? radiusXAnimations,
            object? radiusY,
            object? radiusYAnimations) { }

        public void DrawEllipse(object? brush, object? pen, object? center, object? radiusX, object? radiusY) { }

        public void DrawEllipse(
            object? brush,
            object? pen,
            object? center,
            object? centerAnimations,
            object? radiusX,
            object? radiusXAnimations,
            object? radiusY,
            object? radiusYAnimations) { }

        public void DrawGeometry(object? brush, object? pen, object? geometry) { }

        public void DrawImage(object? imageSource, object? rectangle) { }

        public void DrawImage(object? imageSource, object? rectangle, object? rectangleAnimations) { }

        public void DrawGlyphRun(object? foregroundBrush, object? glyphRun) { }

        public void DrawDrawing(object? drawing) { }

        public void DrawVideo(object? player, object? rectangle) { }

        public void DrawVideo(object? player, object? rectangle, object? rectangleAnimations) { }

        public void PushClip(object? clipGeometry) { }

        public void PushOpacityMask(object? opacityMask) { }

        public void PushOpacity(object? opacity) { }

        public void PushOpacity(object? opacity, object? opacityAnimations) { }

        public void PushTransform(object? transform) { }

        public void PushGuidelineSet(object? guidelines) { }

        public void PushGuidelineY1(object? coordinate) { }

        public void PushGuidelineY2(object? leadingCoordinate, object? offsetToDrivenCoordinate) { }

        public void PushEffect(object? effect, object? effectInput) { }

        public void Pop() { }

        public void Close() { }
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}
