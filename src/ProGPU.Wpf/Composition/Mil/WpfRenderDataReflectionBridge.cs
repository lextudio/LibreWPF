using System;
using System.Collections.Generic;
using System.Windows.Media.ProGPU.Composition;
using PortableRenderDataSource = ProGPU.Wpf.Interop.IPortableRenderDataSource;
using PortableRenderDataSnapshot = ProGPU.Wpf.Interop.PortableRenderDataSnapshot;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfRenderDataReflectionBridge
{
    private readonly WpfMilRenderDataDecoder _decoder;

    public WpfRenderDataReflectionBridge()
        : this(new WpfMilRenderDataDecoder())
    {
    }

    public WpfRenderDataReflectionBridge(WpfMilRenderDataDecoder decoder)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
    }

    public static WpfRenderDataSnapshot Extract(object renderData)
    {
        ArgumentNullException.ThrowIfNull(renderData);

        if (renderData is PortableRenderDataSource portableSource
            && portableSource.TryGetPortableRenderDataSnapshot(out var portableSnapshot))
        {
            return CreateSnapshot(portableSnapshot);
        }

        throw new InvalidOperationException(
            $"Type '{renderData.GetType().FullName}' does not implement the portable WPF RenderData source contract.");
    }

    public WpfMilDecodeResult Replay(
        object renderData,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var snapshot = Extract(renderData);
        resources ??= WpfReflectionResourceResolver.FromDependentResources(
            snapshot.DependentResources,
            imageSourceAdapter);
        return _decoder.Decode(snapshot.RenderData, sink, resources);
    }

    public WpfMilDecodeResult Replay(
        WpfRenderDataSnapshot snapshot,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sink);

        resources ??= WpfReflectionResourceResolver.FromDependentResources(
            snapshot.DependentResources,
            imageSourceAdapter);
        return _decoder.Decode(snapshot.RenderData, sink, resources);
    }

    private static WpfRenderDataSnapshot CreateSnapshot(PortableRenderDataSnapshot portableSnapshot)
    {
        ArgumentNullException.ThrowIfNull(portableSnapshot);
        ArgumentNullException.ThrowIfNull(portableSnapshot.RenderData);
        ArgumentNullException.ThrowIfNull(portableSnapshot.DependentResources);

        var renderData = portableSnapshot.RenderData.Length == 0
            ? Array.Empty<byte>()
            : new byte[portableSnapshot.RenderData.Length];
        if (renderData.Length != 0)
        {
            Buffer.BlockCopy(portableSnapshot.RenderData, 0, renderData, 0, renderData.Length);
        }

        var dependentResources = new object?[portableSnapshot.DependentResources.Count];
        for (var i = 0; i < dependentResources.Length; i++)
        {
            dependentResources[i] = portableSnapshot.DependentResources[i];
        }

        return new WpfRenderDataSnapshot(renderData, dependentResources);
    }

}

public sealed class WpfRenderDataSnapshot
{
    public WpfRenderDataSnapshot(byte[] renderData, IReadOnlyList<object?> dependentResources)
    {
        RenderData = renderData ?? throw new ArgumentNullException(nameof(renderData));
        DependentResources = dependentResources ?? throw new ArgumentNullException(nameof(dependentResources));
    }

    public byte[] RenderData { get; }

    public IReadOnlyList<object?> DependentResources { get; }
}
