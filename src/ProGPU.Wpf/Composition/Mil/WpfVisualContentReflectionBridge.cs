using System;
using System.Reflection;
using System.Windows.Media.ProGPU.Composition;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfVisualContentReflectionBridge
{
    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;

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
            $"Type '{drawingVisual.GetType().FullName}' does not expose the expected WPF DrawingVisual field '_content'.");
    }

    public static bool TryExtractContent(object drawingVisual, out object? content)
    {
        ArgumentNullException.ThrowIfNull(drawingVisual);

        var contentField = FindField(drawingVisual.GetType(), "_content");
        if (contentField == null)
        {
            content = null;
            return false;
        }

        content = contentField.GetValue(drawingVisual);
        return true;
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

        if (!HasRenderDataShape(content.GetType()))
        {
            throw new NotSupportedException(
                $"WPF visual content type '{content.GetType().FullName}' is not supported by the ProGPU RenderData replay bridge.");
        }

        return _renderDataBridge.Replay(content, sink, resources, imageSourceAdapter);
    }

    private static bool HasRenderDataShape(Type contentType)
    {
        return FindField(contentType, "_buffer") != null
            && FindField(contentType, "_curOffset") != null
            && FindField(contentType, "_dependentResources") != null;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name, FieldFlags);
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }
}
