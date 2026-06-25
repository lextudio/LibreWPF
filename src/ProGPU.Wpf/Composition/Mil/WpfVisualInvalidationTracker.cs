using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfVisualInvalidationTracker : IDisposable
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly string[] s_eventNames = { "Changed", "Invalidated" };
    private static readonly string[] s_versionPropertyNames = { "ChangeVersion", "InternalVersion", "Version" };
    private static readonly string[] s_versionFieldNames = { "_changeVersion", "_internalVersion", "_version" };
    private static readonly string[] s_referencePropertyNames =
    {
        "Children",
        "Content",
        "Drawing",
        "Drawings",
        "Visual",
        "Brush",
        "ForegroundBrush",
        "Pen",
        "Geometry",
        "Geometry1",
        "Geometry2",
        "Figures",
        "Segments",
        "Points",
        "ImageSource",
        "GlyphRun",
        "Transform",
        "RelativeTransform",
        "VisualClip",
        "Clip",
        "ClipGeometry",
        "OpacityMask",
        "Effect",
        "BitmapEffect",
        "BitmapEffectInput",
        "CacheMode",
        "Input",
        "PixelShader",
        "GuidelineSet",
        "XSnappingGuidelines",
        "YSnappingGuidelines",
        "VisualXSnappingGuidelines",
        "VisualYSnappingGuidelines",
        "GradientStops",
        "Camera",
        "Model",
        "ContentBounds",
        "Material",
        "BackMaterial",
        "Geometry3D",
        "Positions",
        "TriangleIndices",
        "Normals",
        "TextureCoordinates"
    };
    private static readonly string[] s_fieldNames =
    {
        "_content",
        "_drawingContent",
        "_floatRegisters",
        "_samplerData",
        "_brush",
        "_samplingMode",
        "_shaderBytecode"
    };

    private readonly List<Action> _unsubscribeActions = new();
    private readonly Dictionary<object, object> _versionSnapshots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, VisualStateSnapshot> _visualStateSnapshots = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _dirtySources = new(ReferenceEqualityComparer.Instance);
    private object? _root;
    private object? _lastDirtySource;
    private bool _isDirty;
    private bool _isRefreshing;

    public event EventHandler? Invalidated;

    public object? Root => _root;

    public bool IsDirty => _isDirty;

    public int SubscriptionCount => _unsubscribeActions.Count;

    public int VersionSnapshotCount => _versionSnapshots.Count;

    public int VisualStateSnapshotCount => _visualStateSnapshots.Count;

    public int DirtySourceCount => _dirtySources.Count;

    public object? LastDirtySource => _lastDirtySource;

    public IReadOnlyCollection<object> DirtySources => _dirtySources;

    internal static IReadOnlyList<object> EnumerateTrackedDependencies(object? source)
    {
        if (source == null)
        {
            return Array.Empty<object>();
        }

        var dependencies = new List<object>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectTrackedDependencies(source, dependencies, visited);
        return dependencies;
    }

    public void AttachIfChanged(object? root)
    {
        if (!ReferenceEquals(_root, root))
        {
            Attach(root);
        }
    }

    public void Attach(object? root)
    {
        Detach();
        _root = root;

        if (root == null)
        {
            return;
        }

        SubscribeGraph(root);
        MarkDirty(root);
    }

    public bool ConsumeDirty()
    {
        var wasDirty = _isDirty;
        _isDirty = false;
        _dirtySources.Clear();
        _lastDirtySource = null;
        return wasDirty;
    }

    public bool DetectVersionChanges()
    {
        if (_root == null)
        {
            return false;
        }

        if (_isDirty)
        {
            return true;
        }

        var currentSnapshots = CaptureVersionSnapshots(_root);
        var currentVisualStateSnapshots = CaptureVisualStateSnapshots(_root);
        var changedSources = new List<object>(CollectVersionChanges(_versionSnapshots, currentSnapshots));
        foreach (var changedSource in CollectVisualStateChanges(_visualStateSnapshots, currentVisualStateSnapshots))
        {
            changedSources.Add(changedSource);
        }

        if (changedSources.Count == 0)
        {
            return false;
        }

        MarkDirtyAndRefresh(changedSources);
        return true;
    }

    public void MarkDirty()
    {
        MarkDirty(null);
    }

    public void MarkDirty(object? source)
    {
        if (source != null)
        {
            _dirtySources.Add(source);
            _lastDirtySource = source;
        }

        if (_isDirty)
        {
            return;
        }

        _isDirty = true;
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public void Detach()
    {
        ClearSubscriptions();
        _versionSnapshots.Clear();
        _visualStateSnapshots.Clear();
        _dirtySources.Clear();
        _root = null;
        _lastDirtySource = null;
        _isDirty = false;
    }

    public void Dispose()
    {
        Detach();
    }

    private void MarkDirtyAndRefresh(object? source)
    {
        MarkDirty(source);
        RefreshSubscriptions();
    }

    private void MarkDirtyAndRefresh(IEnumerable<object> sources)
    {
        foreach (var source in sources)
        {
            MarkDirty(source);
        }

        RefreshSubscriptions();
    }

    private void RefreshSubscriptions()
    {
        if (_root == null || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            ClearSubscriptions();
            _versionSnapshots.Clear();
            _visualStateSnapshots.Clear();
            SubscribeGraph(_root);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void SubscribeGraph(object root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        SubscribeObject(root, visited);
    }

    private void SubscribeObject(object? source, HashSet<object> visited)
    {
        if (source == null || IsTerminalValue(source) || !visited.Add(source))
        {
            return;
        }

        SubscribeInvalidationEvents(source);
        CaptureVersionSnapshot(source);
        CaptureVisualStateSnapshot(source);

        if (source is INotifyCollectionChanged collectionChanged)
        {
            NotifyCollectionChangedEventHandler handler = (_, _) => MarkDirtyAndRefresh(source);
            TrySubscribeInvalidationCallback(
                () => collectionChanged.CollectionChanged += handler,
                () => collectionChanged.CollectionChanged -= handler);
        }

        foreach (var item in EnumerateCollection(source))
        {
            SubscribeObject(item, visited);
        }

        foreach (var propertyName in s_referencePropertyNames)
        {
            if (TryGetPropertyValue(source, propertyName, out var value))
            {
                SubscribeObject(value, visited);
            }
        }

        foreach (var fieldName in s_fieldNames)
        {
            if (TryGetFieldValue(source, fieldName, out var value))
            {
                SubscribeObject(value, visited);
            }
        }
    }

    private void CaptureVersionSnapshot(object source)
    {
        if (TryReadVersionValue(source, out var version))
        {
            _versionSnapshots[source] = version;
        }
    }

    private void CaptureVisualStateSnapshot(object source)
    {
        if (TryReadVisualStateSnapshot(source, out var snapshot))
        {
            _visualStateSnapshots[source] = snapshot;
        }
    }

    private static Dictionary<object, object> CaptureVersionSnapshots(object root)
    {
        var snapshots = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CaptureObjectVersions(root, snapshots, visited);
        return snapshots;
    }

    private static Dictionary<object, VisualStateSnapshot> CaptureVisualStateSnapshots(object root)
    {
        var snapshots = new Dictionary<object, VisualStateSnapshot>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CaptureObjectVisualStates(root, snapshots, visited);
        return snapshots;
    }

    private static void CaptureObjectVersions(
        object? source,
        Dictionary<object, object> snapshots,
        HashSet<object> visited)
    {
        if (source == null || IsTerminalValue(source) || !visited.Add(source))
        {
            return;
        }

        if (TryReadVersionValue(source, out var version))
        {
            snapshots[source] = version;
        }

        foreach (var item in EnumerateCollection(source))
        {
            CaptureObjectVersions(item, snapshots, visited);
        }

        foreach (var propertyName in s_referencePropertyNames)
        {
            if (TryGetPropertyValue(source, propertyName, out var value))
            {
                CaptureObjectVersions(value, snapshots, visited);
            }
        }

        foreach (var fieldName in s_fieldNames)
        {
            if (TryGetFieldValue(source, fieldName, out var value))
            {
                CaptureObjectVersions(value, snapshots, visited);
            }
        }
    }

    private static void CaptureObjectVisualStates(
        object? source,
        Dictionary<object, VisualStateSnapshot> snapshots,
        HashSet<object> visited)
    {
        if (source == null || IsTerminalValue(source) || !visited.Add(source))
        {
            return;
        }

        if (TryReadVisualStateSnapshot(source, out var snapshot))
        {
            snapshots[source] = snapshot;
        }

        foreach (var item in EnumerateCollection(source))
        {
            CaptureObjectVisualStates(item, snapshots, visited);
        }

        foreach (var propertyName in s_referencePropertyNames)
        {
            if (TryGetPropertyValue(source, propertyName, out var value))
            {
                CaptureObjectVisualStates(value, snapshots, visited);
            }
        }

        foreach (var fieldName in s_fieldNames)
        {
            if (TryGetFieldValue(source, fieldName, out var value))
            {
                CaptureObjectVisualStates(value, snapshots, visited);
            }
        }
    }

    private static void CollectTrackedDependencies(
        object? source,
        List<object> dependencies,
        HashSet<object> visited)
    {
        if (source == null || IsTerminalValue(source) || !visited.Add(source))
        {
            return;
        }

        dependencies.Add(source);

        foreach (var item in EnumerateCollection(source))
        {
            CollectTrackedDependencies(item, dependencies, visited);
        }

        foreach (var propertyName in s_referencePropertyNames)
        {
            if (TryGetPropertyValue(source, propertyName, out var value))
            {
                CollectTrackedDependencies(value, dependencies, visited);
            }
        }

        foreach (var fieldName in s_fieldNames)
        {
            if (TryGetFieldValue(source, fieldName, out var value))
            {
                CollectTrackedDependencies(value, dependencies, visited);
            }
        }
    }

    private static IReadOnlyList<object> CollectVersionChanges(
        IReadOnlyDictionary<object, object> previous,
        IReadOnlyDictionary<object, object> current)
    {
        var changedSources = new List<object>();

        foreach (var snapshot in current)
        {
            if (!previous.TryGetValue(snapshot.Key, out var previousVersion) ||
                !Equals(previousVersion, snapshot.Value))
            {
                changedSources.Add(snapshot.Key);
            }
        }

        foreach (var snapshot in previous)
        {
            if (!current.ContainsKey(snapshot.Key))
            {
                changedSources.Add(snapshot.Key);
            }
        }

        return changedSources;
    }

    private static List<object> CollectVisualStateChanges(
        IReadOnlyDictionary<object, VisualStateSnapshot> previous,
        IReadOnlyDictionary<object, VisualStateSnapshot> current)
    {
        var changedSources = new List<object>();

        foreach (var snapshot in current)
        {
            if (!previous.TryGetValue(snapshot.Key, out var previousSnapshot) ||
                !previousSnapshot.Equals(snapshot.Value))
            {
                changedSources.Add(snapshot.Key);
            }
        }

        foreach (var snapshot in previous)
        {
            if (!current.ContainsKey(snapshot.Key))
            {
                changedSources.Add(snapshot.Key);
            }
        }

        return changedSources;
    }

    private static bool TryReadVersionValue(object source, out object version)
    {
        foreach (var propertyName in s_versionPropertyNames)
        {
            if (TryGetVersionPropertyValue(source, propertyName, out version))
            {
                return true;
            }
        }

        foreach (var fieldName in s_versionFieldNames)
        {
            if (TryGetVersionFieldValue(source, fieldName, out version))
            {
                return true;
            }
        }

        version = 0;
        return false;
    }

    private static bool TryGetVersionPropertyValue(object instance, string propertyName, out object version)
    {
        version = 0;
        var property = instance.GetType().GetProperty(propertyName, MemberFlags);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            return false;
        }

        try
        {
            var value = property.GetValue(instance);
            if (TryNormalizeVersionValue(value, out var normalizedVersion))
            {
                version = normalizedVersion;
                return true;
            }
        }
        catch (TargetInvocationException)
        {
        }
        catch (MethodAccessException)
        {
        }

        return false;
    }

    private static bool TryGetVersionFieldValue(object instance, string fieldName, out object version)
    {
        version = 0;
        var field = instance.GetType().GetField(fieldName, MemberFlags);
        if (field == null)
        {
            return false;
        }

        var value = field.GetValue(instance);
        if (!TryNormalizeVersionValue(value, out var normalizedVersion))
        {
            return false;
        }

        version = normalizedVersion;
        return true;
    }

    private static bool TryNormalizeVersionValue(object? value, out object version)
    {
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
        {
            version = value;
            return true;
        }

        version = 0;
        return false;
    }

    private static bool TryReadVisualStateSnapshot(object source, out VisualStateSnapshot snapshot)
    {
        var builder = new VisualStateSnapshotBuilder();

        if (TryReadVectorLikeProperty(source, "Offset", out var offsetX, out var offsetY) ||
            TryReadVectorLikeProperty(source, "VisualOffset", out offsetX, out offsetY) ||
            TryReadVectorLikeField(source, "_offset", out offsetX, out offsetY))
        {
            builder.SetOffset(offsetX, offsetY);
        }

        if (TryGetVisualClip(source, out var clip))
        {
            builder.SetClip(clip);
        }

        if (TryGetPropertyValue(source, "Transform", out var transform) ||
            TryGetPropertyValue(source, "VisualTransform", out transform) ||
            TryGetFieldValue(source, "_transform", out transform))
        {
            builder.SetTransform(transform);
        }

        if (TryGetScrollableAreaClip(source, out var scrollClip))
        {
            builder.SetScrollableAreaClip(scrollClip);
        }

        if (TryReadDoubleProperty(source, "Opacity", out var opacity))
        {
            builder.SetOpacity(opacity);
        }

        if (TryReadSizeProperty(source, "RenderSize", out var width, out var height) ||
            (TryReadDoubleProperty(source, "ActualWidth", out width) &&
             TryReadDoubleProperty(source, "ActualHeight", out height)))
        {
            builder.SetRenderSize(width, height);
        }

        snapshot = builder.ToSnapshot();
        return builder.HasState;
    }

    private static bool TryGetScrollableAreaClip(object source, out object? value)
    {
        if (TryGetPropertyValue(source, "ScrollableAreaClip", out value))
        {
            return true;
        }

        return TryGetPropertyValue(source, "VisualScrollableAreaClip", out value);
    }

    private static bool TryGetVisualClip(object source, out object? value)
    {
        if (TryGetPropertyValue(source, "VisualClip", out value) && value != null)
        {
            return true;
        }

        return TryGetPropertyValue(source, "Clip", out value);
    }

    private static bool TryReadVectorLikeProperty(object instance, string propertyName, out double x, out double y)
    {
        x = 0;
        y = 0;

        return TryGetPropertyValue(instance, propertyName, out var value)
            && value != null
            && TryReadDoubleProperty(value, "X", out x)
            && TryReadDoubleProperty(value, "Y", out y);
    }

    private static bool TryReadVectorLikeField(object instance, string fieldName, out double x, out double y)
    {
        x = 0;
        y = 0;

        return TryGetFieldValue(instance, fieldName, out var value)
            && value != null
            && TryReadDoubleProperty(value, "X", out x)
            && TryReadDoubleProperty(value, "Y", out y);
    }

    private static bool TryReadSizeProperty(object instance, string propertyName, out double width, out double height)
    {
        width = 0;
        height = 0;

        return TryGetPropertyValue(instance, propertyName, out var value)
            && value != null
            && TryReadDoubleProperty(value, "Width", out width)
            && TryReadDoubleProperty(value, "Height", out height);
    }

    private static bool TryReadRect(object value, out double x, out double y, out double width, out double height)
    {
        x = 0;
        y = 0;
        width = 0;
        height = 0;

        return TryReadDoubleProperty(value, "X", out x)
            && TryReadDoubleProperty(value, "Y", out y)
            && TryReadDoubleProperty(value, "Width", out width)
            && TryReadDoubleProperty(value, "Height", out height);
    }

    private static bool TryReadDoubleProperty(object instance, string propertyName, out double value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        if (propertyValue is IConvertible convertible)
        {
            try
            {
                value = convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException)
            {
            }
            catch (InvalidCastException)
            {
            }
            catch (OverflowException)
            {
            }
        }

        return false;
    }

    private void SubscribeInvalidationEvents(object source)
    {
        if (source is INotifyPropertyChanged propertyChanged)
        {
            PropertyChangedEventHandler handler = (_, _) => MarkDirtyAndRefresh(source);
            TrySubscribeInvalidationCallback(
                () => propertyChanged.PropertyChanged += handler,
                () => propertyChanged.PropertyChanged -= handler);
        }

        foreach (var eventName in s_eventNames)
        {
            var eventInfo = source.GetType().GetEvent(eventName, MemberFlags);
            if (eventInfo?.EventHandlerType != typeof(EventHandler))
            {
                continue;
            }

            EventHandler handler = (_, _) => MarkDirtyAndRefresh(source);
            TrySubscribeInvalidationCallback(
                () => eventInfo.AddEventHandler(source, handler),
                () => eventInfo.RemoveEventHandler(source, handler));
        }
    }

    private bool TrySubscribeInvalidationCallback(Action subscribe, Action unsubscribe)
    {
        if (!TryRunInvalidationSubscriptionAction(subscribe))
        {
            return false;
        }

        _unsubscribeActions.Add(() => TryRunInvalidationSubscriptionAction(unsubscribe));
        return true;
    }

    private static bool TryRunInvalidationSubscriptionAction(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (TargetInvocationException ex) when (IsIgnorableInvalidationSubscriptionFailure(ex.InnerException))
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (MethodAccessException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return false;
    }

    private static bool IsIgnorableInvalidationSubscriptionFailure(Exception? exception)
    {
        return exception is InvalidOperationException
            or ArgumentException
            or MethodAccessException
            or NotSupportedException;
    }

    private static IReadOnlyList<object?> EnumerateCollection(object source)
    {
        if (!TryReadIntProperty(source, "Count", out var count) || count <= 0)
        {
            return Array.Empty<object?>();
        }

        var indexer = FindIndexer(source.GetType());
        if (indexer == null)
        {
            return Array.Empty<object?>();
        }

        var result = new object?[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = indexer(source, i);
        }

        return result;
    }

    private static bool TryGetPropertyValue(object instance, string propertyName, out object? value)
    {
        var property = instance.GetType().GetProperty(propertyName, MemberFlags);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            value = null;
            return false;
        }

        value = property.GetValue(instance);
        return true;
    }

    private static bool TryGetFieldValue(object instance, string fieldName, out object? value)
    {
        var field = instance.GetType().GetField(fieldName, MemberFlags);
        if (field == null)
        {
            value = null;
            return false;
        }

        value = field.GetValue(instance);
        return true;
    }

    private static bool TryReadIntProperty(object instance, string propertyName, out int value)
    {
        value = 0;
        if (!TryGetPropertyValue(instance, propertyName, out var propertyValue))
        {
            return false;
        }

        if (propertyValue is int intValue)
        {
            value = intValue;
            return true;
        }

        return false;
    }

    private static Func<object, int, object?>? FindIndexer(Type type)
    {
        var indexer = type.GetProperty("Item", MemberFlags, binder: null, returnType: null, types: new[] { typeof(int) }, modifiers: null);
        if (indexer != null)
        {
            return (instance, index) => indexer.GetValue(instance, new object[] { index });
        }

        var getter = type.GetMethod("get_Item", MemberFlags, binder: null, types: new[] { typeof(int) }, modifiers: null);
        if (getter != null)
        {
            return (instance, index) => getter.Invoke(instance, new object[] { index });
        }

        return null;
    }

    private static bool IsTerminalValue(object value)
    {
        var type = value.GetType();
        return type.IsPrimitive
            || type.IsEnum
            || value is string
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(TimeSpan);
    }

    private readonly struct VisualStateSnapshot : IEquatable<VisualStateSnapshot>
    {
        public VisualStateSnapshot(
            bool hasOffset,
            double offsetX,
            double offsetY,
            bool hasClipProperty,
            object? clipReference,
            bool hasTransformProperty,
            object? transformReference,
            bool hasScrollableAreaClipProperty,
            bool hasScrollableAreaClipRect,
            double scrollClipX,
            double scrollClipY,
            double scrollClipWidth,
            double scrollClipHeight,
            object? scrollClipReference,
            bool hasOpacity,
            double opacity,
            bool hasRenderSize,
            double renderWidth,
            double renderHeight)
        {
            HasOffset = hasOffset;
            OffsetX = offsetX;
            OffsetY = offsetY;
            HasClipProperty = hasClipProperty;
            ClipReference = clipReference;
            HasTransformProperty = hasTransformProperty;
            TransformReference = transformReference;
            HasScrollableAreaClipProperty = hasScrollableAreaClipProperty;
            HasScrollableAreaClipRect = hasScrollableAreaClipRect;
            ScrollClipX = scrollClipX;
            ScrollClipY = scrollClipY;
            ScrollClipWidth = scrollClipWidth;
            ScrollClipHeight = scrollClipHeight;
            ScrollClipReference = scrollClipReference;
            HasOpacity = hasOpacity;
            Opacity = opacity;
            HasRenderSize = hasRenderSize;
            RenderWidth = renderWidth;
            RenderHeight = renderHeight;
        }

        private bool HasOffset { get; }

        private double OffsetX { get; }

        private double OffsetY { get; }

        private bool HasClipProperty { get; }

        private object? ClipReference { get; }

        private bool HasTransformProperty { get; }

        private object? TransformReference { get; }

        private bool HasScrollableAreaClipProperty { get; }

        private bool HasScrollableAreaClipRect { get; }

        private double ScrollClipX { get; }

        private double ScrollClipY { get; }

        private double ScrollClipWidth { get; }

        private double ScrollClipHeight { get; }

        private object? ScrollClipReference { get; }

        private bool HasOpacity { get; }

        private double Opacity { get; }

        private bool HasRenderSize { get; }

        private double RenderWidth { get; }

        private double RenderHeight { get; }

        public bool Equals(VisualStateSnapshot other)
        {
            return HasOffset == other.HasOffset &&
                OffsetX.Equals(other.OffsetX) &&
                OffsetY.Equals(other.OffsetY) &&
                HasClipProperty == other.HasClipProperty &&
                ReferenceEquals(ClipReference, other.ClipReference) &&
                HasTransformProperty == other.HasTransformProperty &&
                ReferenceEquals(TransformReference, other.TransformReference) &&
                HasScrollableAreaClipProperty == other.HasScrollableAreaClipProperty &&
                HasScrollableAreaClipRect == other.HasScrollableAreaClipRect &&
                ScrollClipX.Equals(other.ScrollClipX) &&
                ScrollClipY.Equals(other.ScrollClipY) &&
                ScrollClipWidth.Equals(other.ScrollClipWidth) &&
                ScrollClipHeight.Equals(other.ScrollClipHeight) &&
                ReferenceEquals(ScrollClipReference, other.ScrollClipReference) &&
                HasOpacity == other.HasOpacity &&
                Opacity.Equals(other.Opacity) &&
                HasRenderSize == other.HasRenderSize &&
                RenderWidth.Equals(other.RenderWidth) &&
                RenderHeight.Equals(other.RenderHeight);
        }

        public override bool Equals(object? obj)
        {
            return obj is VisualStateSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(HasOffset);
            hash.Add(OffsetX);
            hash.Add(OffsetY);
            hash.Add(HasClipProperty);
            hash.Add(GetReferenceHashCode(ClipReference));
            hash.Add(HasTransformProperty);
            hash.Add(GetReferenceHashCode(TransformReference));
            hash.Add(HasScrollableAreaClipProperty);
            hash.Add(HasScrollableAreaClipRect);
            hash.Add(ScrollClipX);
            hash.Add(ScrollClipY);
            hash.Add(ScrollClipWidth);
            hash.Add(ScrollClipHeight);
            hash.Add(GetReferenceHashCode(ScrollClipReference));
            hash.Add(HasOpacity);
            hash.Add(Opacity);
            hash.Add(HasRenderSize);
            hash.Add(RenderWidth);
            hash.Add(RenderHeight);
            return hash.ToHashCode();
        }

        private static int GetReferenceHashCode(object? value)
        {
            return value == null ? 0 : RuntimeHelpers.GetHashCode(value);
        }
    }

    private struct VisualStateSnapshotBuilder
    {
        private bool _hasOffset;
        private double _offsetX;
        private double _offsetY;
        private bool _hasClipProperty;
        private object? _clipReference;
        private bool _hasTransformProperty;
        private object? _transformReference;
        private bool _hasScrollableAreaClipProperty;
        private bool _hasScrollableAreaClipRect;
        private double _scrollClipX;
        private double _scrollClipY;
        private double _scrollClipWidth;
        private double _scrollClipHeight;
        private object? _scrollClipReference;
        private bool _hasOpacity;
        private double _opacity;
        private bool _hasRenderSize;
        private double _renderWidth;
        private double _renderHeight;

        public bool HasState { get; private set; }

        public void SetOffset(double x, double y)
        {
            HasState = true;
            _hasOffset = true;
            _offsetX = x;
            _offsetY = y;
        }

        public void SetClip(object? clip)
        {
            HasState = true;
            _hasClipProperty = true;
            _clipReference = clip;
        }

        public void SetTransform(object? transform)
        {
            HasState = true;
            _hasTransformProperty = true;
            _transformReference = transform;
        }

        public void SetScrollableAreaClip(object? clip)
        {
            HasState = true;
            _hasScrollableAreaClipProperty = true;
            _scrollClipReference = clip;
            if (clip != null && TryReadRect(clip, out var x, out var y, out var width, out var height))
            {
                _hasScrollableAreaClipRect = true;
                _scrollClipX = x;
                _scrollClipY = y;
                _scrollClipWidth = width;
                _scrollClipHeight = height;
            }
        }

        public void SetOpacity(double opacity)
        {
            HasState = true;
            _hasOpacity = true;
            _opacity = opacity;
        }

        public void SetRenderSize(double width, double height)
        {
            HasState = true;
            _hasRenderSize = true;
            _renderWidth = width;
            _renderHeight = height;
        }

        public readonly VisualStateSnapshot ToSnapshot()
        {
            return new VisualStateSnapshot(
                _hasOffset,
                _offsetX,
                _offsetY,
                _hasClipProperty,
                _clipReference,
                _hasTransformProperty,
                _transformReference,
                _hasScrollableAreaClipProperty,
                _hasScrollableAreaClipRect,
                _scrollClipX,
                _scrollClipY,
                _scrollClipWidth,
                _scrollClipHeight,
                _scrollClipReference,
                _hasOpacity,
                _opacity,
                _hasRenderSize,
                _renderWidth,
                _renderHeight);
        }
    }

    private void ClearSubscriptions()
    {
        for (var i = _unsubscribeActions.Count - 1; i >= 0; i--)
        {
            _unsubscribeActions[i]();
        }

        _unsubscribeActions.Clear();
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
