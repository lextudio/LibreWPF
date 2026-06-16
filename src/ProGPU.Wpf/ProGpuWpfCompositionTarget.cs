using System;
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

    public long SceneChangeVersion => SceneRootVisual.ChangeVersion;

    public long RetainedWpfChangeVersion => RetainedWpfVisualRoot.ChangeVersion;

    public long FlatDrawingChangeVersion => RootVisual.ChangeVersion;

    public int DirtySourceCount => WpfInvalidationTracker.DirtySourceCount;

    public object? LastDirtySource => WpfInvalidationTracker.LastDirtySource;

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
            clearRetainedWpfVisualRoot);
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

    public void Clear()
    {
        ThrowIfDisposed();
        RootVisual.Context.Clear();
        RetainedWpfVisualRoot.ClearChildren();
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
        SceneRootVisual.Invalidate();
        RetainedWpfVisualRoot.Invalidate();
        RootVisual.Invalidate();
        RenderInvalidated?.Invoke(this, EventArgs.Empty);
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
