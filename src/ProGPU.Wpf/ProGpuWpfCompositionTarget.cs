using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using ProGpuContainerVisual = global::ProGPU.Scene.ContainerVisual;
using ProGpuCompositor = global::ProGPU.Scene.Compositor;
using ProGpuDrawingVisual = global::ProGPU.Scene.DrawingVisual;
using ProGpuWgpuContext = global::ProGPU.Backend.WgpuContext;

namespace System.Windows.Media.ProGPU;

public unsafe sealed class ProGpuWpfCompositionTarget : IDisposable
{
    private readonly WpfVisualTreeReflectionRenderer _visualTreeRenderer = new();
    private readonly bool _ownsContext;
    private readonly bool _ownsCompositor;
    private bool _isDisposed;

    public ProGpuWgpuContext Context { get; }

    public ProGpuCompositor Compositor { get; }

    public ProGpuContainerVisual SceneRootVisual { get; } = new();

    public ProGpuContainerVisual RetainedWpfVisualRoot { get; } = new();

    public ProGpuDrawingVisual RootVisual { get; } = new();

    public event EventHandler? RenderInvalidated;

    public IWpfImageSourceAdapter? WpfImageSourceAdapter { get; set; }

    public WpfVisualInvalidationTracker WpfInvalidationTracker { get; } = new();

    public WpfRetainedVisualBranchMap RetainedVisualBranchMap { get; } = new();

    public long SceneChangeVersion => SceneRootVisual.ChangeVersion;

    public long RetainedWpfChangeVersion => RetainedWpfVisualRoot.ChangeVersion;

    public long FlatDrawingChangeVersion => RootVisual.ChangeVersion;

    public int DirtySourceCount => WpfInvalidationTracker.DirtySourceCount;

    public object? LastDirtySource => WpfInvalidationTracker.LastDirtySource;

    public int RetainedVisualBranchSourceCount => RetainedVisualBranchMap.SourceCount;

    public int RetainedVisualBranchCount => RetainedVisualBranchMap.VisualCount;

    public int LastRetainedBranchInvalidationCount { get; private set; }

    public int LastRetainedBranchDirtySourceCount { get; private set; }

    public int LastRetainedBranchMappedSourceCount { get; private set; }

    public int LastRetainedBranchUnmappedSourceCount { get; private set; }

    public int LastRetainedBranchSharedWithCleanSourceVisualCount { get; private set; }

    public int LastRetainedBranchReplayTargetConflictCount { get; private set; }

    public bool LastRetainedBranchInvalidationUsedFallback { get; private set; }

    internal WpfViewport3DTextureCache Viewport3DTextureCache { get; }

    public ProGpuWpfCompositionTarget(
        ProGpuWgpuContext context,
        ProGpuCompositor compositor,
        bool ownsContext = false,
        bool ownsCompositor = false)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        _ownsContext = ownsContext;
        _ownsCompositor = ownsCompositor;
        Viewport3DTextureCache = new WpfViewport3DTextureCache(Context);
        WpfInvalidationTracker.Invalidated += OnWpfSourceInvalidated;
        ResetSceneRoot();
    }

    public static ProGpuWpfCompositionTarget CreateHeadless(TextureFormat renderFormat = TextureFormat.Rgba8Unorm)
    {
        var context = new ProGpuWgpuContext();
        context.Initialize(null);

        return new ProGpuWpfCompositionTarget(
            context,
            new ProGpuCompositor(context, renderFormat),
            ownsContext: true,
            ownsCompositor: true);
    }

    public static ProGpuWpfCompositionTarget CreateForWindow(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var context = new ProGpuWgpuContext();
        context.Initialize(window);

        return new ProGpuWpfCompositionTarget(
            context,
            new ProGpuCompositor(context, context.SwapChainFormat),
            ownsContext: true,
            ownsCompositor: true);
    }

    public MediaDrawingContext OpenDrawingContext(uint pixelWidth, uint pixelHeight)
    {
        ThrowIfDisposed();

        return BeginDrawingFrame(pixelWidth, pixelHeight).OpenDrawingContext();
    }

    public ProGpuWpfDrawingFrame BeginDrawingFrame(uint pixelWidth, uint pixelHeight)
    {
        return BeginDrawingFrame(pixelWidth, pixelHeight, clearRetainedWpfVisualRoot: true);
    }

    internal ProGpuWpfDrawingFrame BeginDrawingFrame(
        uint pixelWidth,
        uint pixelHeight,
        bool clearRetainedWpfVisualRoot)
    {
        ThrowIfDisposed();

        return new ProGpuWpfDrawingFrame(
            SceneRootVisual,
            RetainedWpfVisualRoot,
            RootVisual,
            pixelWidth,
            pixelHeight,
            Context,
            Viewport3DTextureCache,
            clearRetainedWpfVisualRoot,
            RetainedVisualBranchMap);
    }

    public WpfCompositionDrawingContext OpenCompositionDrawingContext(uint pixelWidth, uint pixelHeight)
    {
        ThrowIfDisposed();
        return BeginDrawingFrame(pixelWidth, pixelHeight).OpenCompositionDrawingContext();
    }

    public WpfCompositionDrawingContext CreateCompositionDrawingContext(MediaDrawingContext drawingContext)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(drawingContext);
        return new WpfCompositionDrawingContext(
            new ProGpuCompositionCommandSink(drawingContext, Context, Viewport3DTextureCache));
    }

    public WpfVisualReplayResult ReplayVisualSubtree(
        object rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);

        WpfInvalidationTracker.AttachIfChanged(rootVisual);
        ProGpuWpfDrawingFrame drawingFrame = BeginDrawingFrame(pixelWidth, pixelHeight);
        IWpfImageSourceAdapter? activeImageSourceAdapter = imageSourceAdapter ?? WpfImageSourceAdapter;
        using IDisposable? renderDataSinkProviderRegistration = drawingFrame.TryRegisterRenderDataSinkProvider(activeImageSourceAdapter, out IDisposable? registration)
            ? registration
            : null;
        using var drawingContext = drawingFrame.OpenDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(drawingContext, Context, Viewport3DTextureCache);
        return ReplayVisualSubtreeCore(
            rootVisual,
            sink,
            resources,
            activeImageSourceAdapter);
    }

    public WpfVisualReplayResult ReplayVisualSubtree(
        object rootVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);
        ArgumentNullException.ThrowIfNull(sink);

        return ReplayVisualSubtreeCore(
            rootVisual,
            sink,
            resources,
            imageSourceAdapter ?? WpfImageSourceAdapter);
    }

    public void Render(uint pixelWidth, uint pixelHeight, TextureView* targetView)
    {
        ThrowIfDisposed();

        if (targetView == null)
        {
            throw new ArgumentNullException(nameof(targetView));
        }

        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        SceneRootVisual.Size = new Vector2(pixelWidth, pixelHeight);
        RetainedWpfVisualRoot.Size = new Vector2(pixelWidth, pixelHeight);
        RootVisual.Size = new Vector2(pixelWidth, pixelHeight);

        Compositor.RenderScene(SceneRootVisual, pixelWidth, pixelHeight, targetView);
    }

    public bool DetectWpfSourceChanges()
    {
        ThrowIfDisposed();
        return WpfInvalidationTracker.DetectVersionChanges();
    }

    public bool ShouldReplayVisualSubtree(object rootVisual)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);

        return !ReferenceEquals(WpfInvalidationTracker.Root, rootVisual) ||
               WpfInvalidationTracker.IsDirty;
    }

    internal bool CanReplayDirtyRetainedVisualBranches(object rootVisual)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);

        return ReferenceEquals(WpfInvalidationTracker.Root, rootVisual) &&
               WpfInvalidationTracker.IsDirty &&
               LastRetainedBranchDirtySourceCount > 0 &&
               !LastRetainedBranchInvalidationUsedFallback &&
               TryGetDirtyRetainedVisualBranchReplayTargets(out _);
    }

    internal bool TryReplayDirtyRetainedVisualBranches(
        object rootVisual,
        ProGpuWpfDrawingFrame drawingFrame,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out WpfVisualReplayResult result)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);
        ArgumentNullException.ThrowIfNull(drawingFrame);

        result = default;
        if (!ReferenceEquals(WpfInvalidationTracker.Root, rootVisual) ||
            !WpfInvalidationTracker.IsDirty ||
            LastRetainedBranchDirtySourceCount == 0 ||
            LastRetainedBranchInvalidationUsedFallback ||
            !TryGetDirtyRetainedVisualBranchReplayTargets(out var targets))
        {
            return false;
        }

        IWpfImageSourceAdapter? activeImageSourceAdapter = imageSourceAdapter ?? WpfImageSourceAdapter;
        var replayResult = default(WpfVisualReplayResult);
        Viewport3DTextureCache.BeginFrame();

        try
        {
            foreach (var target in targets)
            {
                var branchVisual = (ProGpuRetainedDrawingVisual)target.Visual;
                RetainedVisualBranchMap.UnregisterVisualTree(branchVisual);
                ResetRetainedDrawingVisualBranch(branchVisual, drawingFrame.PixelWidth, drawingFrame.PixelHeight);

                using var sink = new ProGpuRetainedCompositionCommandSink(
                    drawingFrame,
                    branchVisual,
                    Context,
                    Viewport3DTextureCache);
                if (!_visualTreeRenderer.TryReplaySubtreeIntoCurrentRetainedVisual(
                    target.Source,
                    sink,
                    resources,
                    activeImageSourceAdapter,
                    out var branchReplayResult))
                {
                    RetainedWpfVisualRoot.ClearChildren();
                    RetainedVisualBranchMap.Clear();
                    return false;
                }

                replayResult = AddReplayResults(replayResult, branchReplayResult);
            }

            WpfInvalidationTracker.ConsumeDirty();
            RootVisual.Invalidate();
            result = replayResult;
            return true;
        }
        finally
        {
            Viewport3DTextureCache.EndFrame();
        }
    }

    public void Clear()
    {
        ThrowIfDisposed();
        RootVisual.Context.Clear();
        RetainedWpfVisualRoot.ClearChildren();
        RetainedVisualBranchMap.Clear();
        ResetSceneRoot();
        Viewport3DTextureCache.Clear();
        SceneRootVisual.Invalidate();
        RootVisual.Invalidate();
        WpfInvalidationTracker.MarkDirty();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Viewport3DTextureCache.Dispose();

        if (_ownsCompositor)
        {
            Compositor.Dispose();
        }

        if (_ownsContext)
        {
            Context.Dispose();
        }

        WpfInvalidationTracker.Invalidated -= OnWpfSourceInvalidated;
        WpfInvalidationTracker.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnWpfSourceInvalidated(object? sender, EventArgs e)
    {
        InvalidateRetainedWpfBranchesForDirtySources();
        RootVisual.Invalidate();
        RenderInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void InvalidateRetainedWpfBranchesForDirtySources()
    {
        var result = RetainedVisualBranchMap.InvalidateVisualsForSources(WpfInvalidationTracker.DirtySources);
        LastRetainedBranchInvalidationCount = result.InvalidatedVisualCount;
        LastRetainedBranchDirtySourceCount = result.DirtySourceCount;
        LastRetainedBranchMappedSourceCount = result.MappedSourceCount;
        LastRetainedBranchUnmappedSourceCount = result.UnmappedSourceCount;
        LastRetainedBranchSharedWithCleanSourceVisualCount = result.SharedWithCleanSourceVisualCount;
        LastRetainedBranchReplayTargetConflictCount = result.ReplayTargetConflictCount;
        LastRetainedBranchInvalidationUsedFallback = !result.CanTargetAllDirtySources;

        if (LastRetainedBranchInvalidationUsedFallback)
        {
            RetainedWpfVisualRoot.Invalidate();
        }
    }

    private WpfVisualReplayResult ReplayVisualSubtreeCore(
        object rootVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        WpfInvalidationTracker.AttachIfChanged(rootVisual);
        Viewport3DTextureCache.BeginFrame();

        try
        {
            var result = _visualTreeRenderer.ReplaySubtree(
                rootVisual,
                sink,
                resources,
                imageSourceAdapter);
            WpfInvalidationTracker.ConsumeDirty();
            RootVisual.Invalidate();
            return result;
        }
        finally
        {
            Viewport3DTextureCache.EndFrame();
        }
    }

    private bool TryGetDirtyRetainedVisualBranchReplayTargets(
        out IReadOnlyList<WpfRetainedVisualBranchReplayTarget> targets)
    {
        targets = RetainedVisualBranchMap.GetReplayTargetsForSources(WpfInvalidationTracker.DirtySources);
        if (targets.Count == 0)
        {
            return false;
        }

        foreach (var target in targets)
        {
            if (target.Visual is not ProGpuRetainedDrawingVisual branchVisual ||
                branchVisual.Effect != null ||
                branchVisual.CacheAsLayer ||
                !_visualTreeRenderer.CanReplaySubtreeIntoCurrentRetainedVisual(target.Source))
            {
                targets = Array.Empty<WpfRetainedVisualBranchReplayTarget>();
                return false;
            }
        }

        return true;
    }

    private static void ResetRetainedDrawingVisualBranch(
        ProGpuRetainedDrawingVisual visual,
        uint pixelWidth,
        uint pixelHeight)
    {
        visual.Context.Clear();
        visual.ClearChildren();
        visual.Offset = Vector2.Zero;
        visual.Size = new Vector2(pixelWidth, pixelHeight);
        visual.IsVisible = true;
        visual.Opacity = 1f;
        visual.Transform = Matrix4x4.Identity;
        visual.CacheAsLayer = false;
        visual.Scale = Vector3.One;
        visual.Rotation = 0f;
        visual.CenterPoint = Vector3.Zero;
        visual.RenderTransformOrigin = new Vector2(0.5f, 0.5f);
        visual.ClipBounds = null;
        visual.Effect = null;
    }

    private static WpfVisualReplayResult AddReplayResults(
        WpfVisualReplayResult left,
        WpfVisualReplayResult right)
    {
        return new WpfVisualReplayResult(
            left.VisualCount + right.VisualCount,
            left.ContentCount + right.ContentCount,
            left.ChildEdgeCount + right.ChildEdgeCount,
            left.UnsupportedContentCount + right.UnsupportedContentCount,
            left.UnsupportedVisualStateCount + right.UnsupportedVisualStateCount,
            new WpfMilDecodeResult(
                left.RenderData.RecordCount + right.RenderData.RecordCount,
                left.RenderData.AppliedCount + right.RenderData.AppliedCount,
                left.RenderData.SkippedCount + right.RenderData.SkippedCount,
                left.RenderData.UnsupportedCount + right.RenderData.UnsupportedCount));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private void ResetSceneRoot()
    {
        SceneRootVisual.ClearChildren();
        SceneRootVisual.AddChild(RetainedWpfVisualRoot);
        SceneRootVisual.AddChild(RootVisual);
    }
}
