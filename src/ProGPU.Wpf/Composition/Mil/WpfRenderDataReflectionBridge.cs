using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media.ProGPU.Composition;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfRenderDataReflectionBridge
{
    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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

        var renderDataType = renderData.GetType();
        var buffer = GetFieldValue<byte[]?>(renderDataType, renderData, "_buffer");
        var length = GetFieldValue<int>(renderDataType, renderData, "_curOffset");
        var dependentResources = GetFieldValue<object?>(renderDataType, renderData, "_dependentResources");

        if (length < 0)
        {
            throw new InvalidOperationException($"WPF RenderData has a negative active length: {length}.");
        }

        if (buffer == null)
        {
            if (length == 0)
            {
                return new WpfRenderDataSnapshot(Array.Empty<byte>(), ExtractDependentResources(dependentResources));
            }

            throw new InvalidOperationException("WPF RenderData has active bytes but no backing buffer.");
        }

        if (length > buffer.Length)
        {
            throw new InvalidOperationException(
                $"WPF RenderData active length {length} exceeds its backing buffer length {buffer.Length}.");
        }

        var activeRenderData = new byte[length];
        Buffer.BlockCopy(buffer, 0, activeRenderData, 0, length);

        return new WpfRenderDataSnapshot(activeRenderData, ExtractDependentResources(dependentResources));
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

    private static T GetFieldValue<T>(Type declaringType, object instance, string fieldName)
    {
        var field = declaringType.GetField(fieldName, FieldFlags)
            ?? throw new InvalidOperationException(
                $"Type '{declaringType.FullName}' does not expose the expected WPF RenderData field '{fieldName}'.");

        var value = field.GetValue(instance);
        if (value == null)
        {
            if (default(T) == null)
            {
                return default!;
            }

            throw new InvalidOperationException(
                $"WPF RenderData field '{fieldName}' was null but '{typeof(T).FullName}' was expected.");
        }

        if (value is T typedValue)
        {
            return typedValue;
        }

        throw new InvalidOperationException(
            $"WPF RenderData field '{fieldName}' has type '{value.GetType().FullName}', not '{typeof(T).FullName}'.");
    }

    private static IReadOnlyList<object?> ExtractDependentResources(object? dependentResources)
    {
        if (dependentResources == null)
        {
            return Array.Empty<object?>();
        }

        var resourcesType = dependentResources.GetType();
        var countProperty = resourcesType.GetProperty("Count", MemberFlags)
            ?? throw new InvalidOperationException(
                $"Type '{resourcesType.FullName}' does not expose a Count property for WPF dependent resources.");

        var countValue = countProperty.GetValue(dependentResources);
        if (countValue is not int count)
        {
            throw new InvalidOperationException(
                $"WPF dependent resource Count has type '{countValue?.GetType().FullName ?? "<null>"}', not '{typeof(int).FullName}'.");
        }

        if (count < 0)
        {
            throw new InvalidOperationException($"WPF dependent resource Count is negative: {count}.");
        }

        if (count == 0)
        {
            return Array.Empty<object?>();
        }

        var getItem = FindIndexer(resourcesType);
        var resources = new object?[count];
        for (var i = 0; i < resources.Length; i++)
        {
            resources[i] = getItem(dependentResources, i);
        }

        return resources;
    }

    private static Func<object, int, object?> FindIndexer(Type resourcesType)
    {
        var indexer = resourcesType.GetProperty("Item", MemberFlags, binder: null, returnType: null, types: new[] { typeof(int) }, modifiers: null);
        if (indexer != null)
        {
            return (instance, index) => indexer.GetValue(instance, new object[] { index });
        }

        var getter = resourcesType.GetMethod("get_Item", MemberFlags, binder: null, types: new[] { typeof(int) }, modifiers: null);
        if (getter != null)
        {
            return (instance, index) => getter.Invoke(instance, new object[] { index });
        }

        throw new InvalidOperationException(
            $"Type '{resourcesType.FullName}' does not expose an int indexer for WPF dependent resources.");
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
