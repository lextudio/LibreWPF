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
        var changedSources = CollectVersionChanges(_versionSnapshots, currentSnapshots);
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

    private static Dictionary<object, object> CaptureVersionSnapshots(object root)
    {
        var snapshots = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CaptureObjectVersions(root, snapshots, visited);
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
