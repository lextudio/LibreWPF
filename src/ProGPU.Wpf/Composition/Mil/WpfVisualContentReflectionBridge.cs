using System;
using System.Windows.Media.ProGPU.Composition;
using PortableDrawingContentSource = ProGPU.Wpf.Interop.IPortableDrawingContentSource;
using PortableRenderDataSource = ProGPU.Wpf.Interop.IPortableRenderDataSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfVisualContentReflectionBridge
{
    private readonly WpfRenderDataReflectionBridge _renderDataBridge;

    public WpfVisualContentReflectionBridge()
        : this(new WpfRenderDataReflectionBridge())
    {
    }

    public WpfVisualContentReflectionBridge(WpfRenderDataReflectionBridge renderDataBridge)
    {
        _renderDataBridge = renderDataBridge ?? throw new ArgumentNullException(nameof(renderDataBridge));
    }

    public static object? ExtractContent(object drawingVisual)
    {
        ArgumentNullException.ThrowIfNull(drawingVisual);

        if (TryExtractContent(drawingVisual, out var content))
        {
            return content;
        }

        throw new InvalidOperationException(
            $"Type '{drawingVisual.GetType().FullName}' does not implement the portable WPF visual content source contract.");
    }

    public static bool TryExtractContent(object drawingVisual, out object? content)
    {
        ArgumentNullException.ThrowIfNull(drawingVisual);

        if (drawingVisual is PortableDrawingContentSource drawingContentSource
            && drawingContentSource.TryGetPortableDrawingContent(out content))
        {
            return true;
        }

        content = null;
        return false;
    }

    public WpfMilDecodeResult ReplayContent(
        object drawingVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var content = ExtractContent(drawingVisual);
        if (content == null)
        {
            return default;
        }

        if (content is not PortableRenderDataSource)
        {
            throw new NotSupportedException(
                $"WPF visual content type '{content.GetType().FullName}' is not supported by the ProGPU RenderData replay bridge.");
        }

        return _renderDataBridge.Replay(content, sink, resources, imageSourceAdapter);
    }
}
