using System;
using System.Numerics;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using ProGpuDrawingVisual = global::ProGPU.Scene.DrawingVisual;
using ProGpuWgpuContext = global::ProGPU.Backend.WgpuContext;

namespace System.Windows.Media.ProGPU;

public sealed class ProGpuWpfDrawingFrame
{
    private readonly ProGpuDrawingVisual _rootVisual;
    private readonly ProGpuWgpuContext? _context;
    private readonly WpfViewport3DTextureCache? _viewport3DTextureCache;

    internal ProGpuWpfDrawingFrame(
        ProGpuDrawingVisual rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        ProGpuWgpuContext? context = null,
        WpfViewport3DTextureCache? viewport3DTextureCache = null)
    {
        _rootVisual = rootVisual ?? throw new ArgumentNullException(nameof(rootVisual));
        _context = context;
        _viewport3DTextureCache = viewport3DTextureCache;

        PixelWidth = Math.Max(1, pixelWidth);
        PixelHeight = Math.Max(1, pixelHeight);

        _rootVisual.Context.Clear();
        _rootVisual.Size = new Vector2(PixelWidth, PixelHeight);
    }

    public uint PixelWidth { get; }

    public uint PixelHeight { get; }

    public int DrawingContextCount { get; private set; }

    public int CompositionDrawingContextCount { get; private set; }

    public int ObjectRenderDataSinkContextCount { get; private set; }

    public object? LastOwnerVisual { get; private set; }

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
