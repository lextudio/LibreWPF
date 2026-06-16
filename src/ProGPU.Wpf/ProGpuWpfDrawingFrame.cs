using System;
using System.Numerics;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using ProGpuContainerVisual = global::ProGPU.Scene.ContainerVisual;
using ProGpuDrawingVisual = global::ProGPU.Scene.DrawingVisual;
using ProGpuVisual = global::ProGPU.Scene.Visual;
using ProGpuWgpuContext = global::ProGPU.Backend.WgpuContext;

namespace System.Windows.Media.ProGPU;

public sealed class ProGpuWpfDrawingFrame
{
    private readonly ProGpuContainerVisual? _sceneRootVisual;
    private readonly ProGpuContainerVisual? _retainedWpfVisualRoot;
    private readonly ProGpuDrawingVisual _rootVisual;
    private readonly ProGpuWgpuContext? _context;
    private readonly WpfViewport3DTextureCache? _viewport3DTextureCache;
    private readonly WpfRetainedVisualBranchMap? _retainedVisualBranchMap;

    internal ProGpuWpfDrawingFrame(
        ProGpuDrawingVisual rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        ProGpuWgpuContext? context = null,
        WpfViewport3DTextureCache? viewport3DTextureCache = null)
        : this(
            sceneRootVisual: null,
            retainedWpfVisualRoot: null,
            rootVisual,
            pixelWidth,
            pixelHeight,
            context,
            viewport3DTextureCache)
    {
    }

    internal ProGpuWpfDrawingFrame(
        ProGpuContainerVisual? sceneRootVisual,
        ProGpuContainerVisual? retainedWpfVisualRoot,
        ProGpuDrawingVisual rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        ProGpuWgpuContext? context = null,
        WpfViewport3DTextureCache? viewport3DTextureCache = null,
        bool clearRetainedWpfVisualRoot = true,
        WpfRetainedVisualBranchMap? retainedVisualBranchMap = null)
    {
        _sceneRootVisual = sceneRootVisual;
        _retainedWpfVisualRoot = retainedWpfVisualRoot;
        _rootVisual = rootVisual ?? throw new ArgumentNullException(nameof(rootVisual));
        _context = context;
        _viewport3DTextureCache = viewport3DTextureCache;
        _retainedVisualBranchMap = retainedVisualBranchMap;

        PixelWidth = Math.Max(1, pixelWidth);
        PixelHeight = Math.Max(1, pixelHeight);

        _rootVisual.Context.Clear();
        _rootVisual.Size = new Vector2(PixelWidth, PixelHeight);

        if (_retainedWpfVisualRoot != null)
        {
            if (clearRetainedWpfVisualRoot)
            {
                _retainedWpfVisualRoot.ClearChildren();
                _retainedVisualBranchMap?.Clear();
            }

            _retainedWpfVisualRoot.Size = new Vector2(PixelWidth, PixelHeight);
        }

        if (_sceneRootVisual != null)
        {
            _sceneRootVisual.ClearChildren();
            _sceneRootVisual.Size = new Vector2(PixelWidth, PixelHeight);
            if (_retainedWpfVisualRoot != null)
            {
                _sceneRootVisual.AddChild(_retainedWpfVisualRoot);
            }

            _sceneRootVisual.AddChild(_rootVisual);
        }
    }

    public uint PixelWidth { get; }

    public uint PixelHeight { get; }

    public int DrawingContextCount { get; private set; }

    public int CompositionDrawingContextCount { get; private set; }

    public int ObjectRenderDataSinkContextCount { get; private set; }

    public object? LastOwnerVisual { get; private set; }

    public WpfRetainedVisualBranchMap? RetainedVisualBranchMap => _retainedVisualBranchMap;

    internal bool AddRetainedWpfVisual(ProGpuVisual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        if (_retainedWpfVisualRoot == null)
        {
            return false;
        }

        _retainedWpfVisualRoot.AddChild(visual);
        return true;
    }

    internal void RegisterRetainedWpfVisualOwner(object sourceVisual, ProGpuVisual visual)
    {
        ArgumentNullException.ThrowIfNull(sourceVisual);
        ArgumentNullException.ThrowIfNull(visual);

        _retainedVisualBranchMap?.Register(sourceVisual, visual);
    }

    internal void RegisterRetainedWpfVisualDependency(object dependency, ProGpuVisual visual)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        ArgumentNullException.ThrowIfNull(visual);

        _retainedVisualBranchMap?.RegisterDependency(dependency, visual);
    }

    public MediaDrawingContext OpenDrawingContext()
    {
        return OpenDrawingContext(null);
    }

    public MediaDrawingContext OpenDrawingContext(object? ownerVisual)
    {
        DrawingContextCount++;
        LastOwnerVisual = ownerVisual;
        return new MediaDrawingContext(_rootVisual.Context);
    }

    public Func<object?, MediaDrawingContext> CreateDrawingContextFactory()
    {
        return OpenDrawingContext;
    }

    public bool TryRegisterRenderDataSinkProvider(out IDisposable? registration)
    {
        return TryRegisterRenderDataSinkProvider(null, out registration);
    }

    public bool TryRegisterRenderDataSinkProvider(
        IWpfImageSourceAdapter? imageSourceAdapter,
        out IDisposable? registration)
    {
        return WpfRenderDataSinkProviderBridge.TryRegisterRenderDataSinkProvider(
            this,
            imageSourceAdapter,
            out registration);
    }

    public WpfCompositionDrawingContext OpenCompositionDrawingContext()
    {
        return OpenCompositionDrawingContext(null);
    }

    public WpfCompositionDrawingContext OpenCompositionDrawingContext(object? ownerVisual)
    {
        CompositionDrawingContextCount++;
        return new WpfCompositionDrawingContext(
            new ProGpuCompositionCommandSink(
                OpenDrawingContext(ownerVisual),
                _context,
                _viewport3DTextureCache));
    }

    public Func<object?, WpfCompositionDrawingContext> CreateCompositionDrawingContextFactory()
    {
        return OpenCompositionDrawingContext;
    }

    public WpfObjectRenderDataDrawingContext OpenObjectRenderDataSinkContext(
        object? ownerVisual,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ObjectRenderDataSinkContextCount++;
        return new WpfObjectRenderDataDrawingContext(
            new ProGpuCompositionCommandSink(
                OpenDrawingContext(ownerVisual),
                _context,
                _viewport3DTextureCache),
            imageSourceAdapter);
    }

    public Func<object?, object> CreateObjectRenderDataSinkFactory(
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        return ownerVisual => OpenObjectRenderDataSinkContext(ownerVisual, imageSourceAdapter);
    }
}
