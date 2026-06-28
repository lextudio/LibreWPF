using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using Xunit;
using ProGpuContainerVisual = ProGPU.Scene.ContainerVisual;
using ProGpuDrawingVisual = ProGPU.Scene.DrawingVisual;
using ProGpuRenderCommandType = ProGPU.Scene.RenderCommandType;
using ProGpuRetainedDrawingVisual = System.Windows.Media.ProGPU.Composition.ProGpuRetainedDrawingVisual;

namespace ProGPU.Wpf.Tests;

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
        context.DrawRectangle(Brushes.Red, null, new Rect(1, 2, 3, 4));
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
            context.DrawRectangle(brush, null, new Rect(1, 2, 3, 4));
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
        Assert.Contains("RetainedVisualBranchMap.InvalidateVisualsForSources(WpfInvalidationTracker.DirtySources)", source, StringComparison.Ordinal);
        Assert.Contains("LastRetainedBranchInvalidationUsedFallback = !result.CanTargetAllDirtySources;", source, StringComparison.Ordinal);
        Assert.Contains("internal bool CanReplayDirtyRetainedVisualBranches(object rootVisual)", source, StringComparison.Ordinal);
        Assert.Contains("internal bool TryReplayDirtyRetainedVisualBranches(", source, StringComparison.Ordinal);
        Assert.Contains("RetainedVisualBranchMap.GetReplayTargetsForSources(WpfInvalidationTracker.DirtySources)", source, StringComparison.Ordinal);
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
        Assert.Contains("_target.CanReplayDirtyRetainedVisualBranches(wpfRootVisual)", hostSource, StringComparison.Ordinal);
        Assert.Contains("_target.TryReplayDirtyRetainedVisualBranches(", hostSource, StringComparison.Ordinal);
        Assert.Contains("RetainedWpfBranchReplayCount++;", hostSource, StringComparison.Ordinal);
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
