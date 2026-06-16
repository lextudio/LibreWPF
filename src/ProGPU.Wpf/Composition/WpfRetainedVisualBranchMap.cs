using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows;
using ProGpuVisual = global::ProGPU.Scene.Visual;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class WpfRetainedVisualBranchMap
{
    private readonly Dictionary<object, List<ProGpuVisual>> _visualsBySource = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProGpuVisual, HashSet<object>> _sourcesByVisual = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProGpuVisual, HashSet<object>> _sourceOwnersByVisual = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProGpuVisual, HashSet<object>> _dependenciesByVisual = new(ReferenceEqualityComparer.Instance);

    public int SourceCount => _visualsBySource.Count;

    public int VisualCount { get; private set; }

    public object? LastSource { get; private set; }

    public ProGpuVisual? LastVisual { get; private set; }

    public IReadOnlyCollection<object> Sources => _visualsBySource.Keys;

    public void Clear()
    {
        _visualsBySource.Clear();
        _sourcesByVisual.Clear();
        _sourceOwnersByVisual.Clear();
        _dependenciesByVisual.Clear();
        VisualCount = 0;
        LastSource = null;
        LastVisual = null;
    }

    public void Register(object? source, ProGpuVisual? visual)
    {
        RegisterCore(source, visual, WpfRetainedVisualBranchOwnerKind.SourceOwner);
    }

    public void RegisterDependency(object? dependency, ProGpuVisual? visual)
    {
        RegisterCore(dependency, visual, WpfRetainedVisualBranchOwnerKind.Dependency);
    }

    private void RegisterCore(
        object? source,
        ProGpuVisual? visual,
        WpfRetainedVisualBranchOwnerKind ownerKind)
    {
        if (source == null || visual == null)
        {
            return;
        }

        if (!_visualsBySource.TryGetValue(source, out var visuals))
        {
            visuals = new List<ProGpuVisual>();
            _visualsBySource.Add(source, visuals);
        }

        foreach (var existing in visuals)
        {
            if (ReferenceEquals(existing, visual))
            {
                RegisterOwnerKind(source, visual, ownerKind);
                LastSource = source;
                LastVisual = visual;
                return;
            }
        }

        visuals.Add(visual);
        if (!_sourcesByVisual.TryGetValue(visual, out var sources))
        {
            sources = new HashSet<object>(ReferenceEqualityComparer.Instance);
            _sourcesByVisual.Add(visual, sources);
        }

        sources.Add(source);
        RegisterOwnerKind(source, visual, ownerKind);
        VisualCount++;
        LastSource = source;
        LastVisual = visual;
    }

    public bool TryGetVisuals(object source, out IReadOnlyList<ProGpuVisual> visuals)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (_visualsBySource.TryGetValue(source, out var mappedVisuals))
        {
            visuals = mappedVisuals;
            return true;
        }

        visuals = Array.Empty<ProGpuVisual>();
        return false;
    }

    public int InvalidateVisuals(object source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!_visualsBySource.TryGetValue(source, out var visuals))
        {
            return 0;
        }

        foreach (var visual in visuals)
        {
            visual.Invalidate();
        }

        return visuals.Count;
    }

    public int InvalidateVisuals(IEnumerable<object> sources)
    {
        return InvalidateVisualsForSources(sources).InvalidatedVisualCount;
    }

    public IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForSources(IEnumerable<object> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var dirtySources = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var source in sources)
        {
            dirtySources.Add(source);
        }

        if (dirtySources.Count == 0)
        {
            return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
        }

        var targets = new List<WpfRetainedVisualBranchReplayTarget>();
        var visitedVisuals = new HashSet<ProGpuVisual>(ReferenceEqualityComparer.Instance);
        foreach (var source in dirtySources)
        {
            if (!_visualsBySource.TryGetValue(source, out var visuals))
            {
                return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
            }

            foreach (var visual in visuals)
            {
                if (!TryGetReplaySourceForVisual(visual, out var replaySource))
                {
                    return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
                }

                if (visitedVisuals.Add(visual))
                {
                    targets.Add(new WpfRetainedVisualBranchReplayTarget(replaySource, visual));
                }
            }
        }

        if (targets.Count <= 1)
        {
            return targets;
        }

        var topLevelTargets = new List<WpfRetainedVisualBranchReplayTarget>(targets.Count);
        foreach (var target in targets)
        {
            var isCoveredByAncestor = false;
            foreach (var candidateAncestor in targets)
            {
                if (ReferenceEquals(candidateAncestor.Visual, target.Visual))
                {
                    continue;
                }

                if (IsAncestorOf(candidateAncestor.Visual, target.Visual))
                {
                    isCoveredByAncestor = true;
                    break;
                }
            }

            if (!isCoveredByAncestor)
            {
                topLevelTargets.Add(target);
            }
        }

        return topLevelTargets;
    }

    public void UnregisterVisualTree(ProGpuVisual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        UnregisterVisualTreeCore(visual);
        LastSource = null;
        LastVisual = null;
    }

    public WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForSources(IEnumerable<object> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var visitedSources = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var invalidatedVisuals = new HashSet<ProGpuVisual>(ReferenceEqualityComparer.Instance);
        var dirtySourceCount = 0;
        var mappedSourceCount = 0;
        var sharedWithCleanSourceVisualCount = 0;
        var replayTargetConflictCount = 0;

        foreach (var source in sources)
        {
            if (!visitedSources.Add(source))
            {
                continue;
            }

            dirtySourceCount++;

            if (!_visualsBySource.TryGetValue(source, out var visuals))
            {
                continue;
            }

            mappedSourceCount++;

            foreach (var visual in visuals)
            {
                if (invalidatedVisuals.Add(visual))
                {
                    visual.Invalidate();
                }
            }
        }

        foreach (var visual in invalidatedVisuals)
        {
            if (!_sourceOwnersByVisual.TryGetValue(visual, out var sourceOwners))
            {
                replayTargetConflictCount++;
                continue;
            }

            if (sourceOwners.Count != 1)
            {
                replayTargetConflictCount++;
            }

            var hasDirtySourceOwner = false;
            foreach (var sourceOwner in sourceOwners)
            {
                if (visitedSources.Contains(sourceOwner))
                {
                    hasDirtySourceOwner = true;
                    break;
                }
            }

            if (!hasDirtySourceOwner)
            {
                continue;
            }

            foreach (var sourceOwner in sourceOwners)
            {
                if (!visitedSources.Contains(sourceOwner))
                {
                    sharedWithCleanSourceVisualCount++;
                    break;
                }
            }
        }

        return new WpfRetainedVisualBranchInvalidationResult(
            dirtySourceCount,
            mappedSourceCount,
            invalidatedVisuals.Count,
            sharedWithCleanSourceVisualCount,
            replayTargetConflictCount);
    }

    private void UnregisterVisualTreeCore(ProGpuVisual visual)
    {
        if (_sourcesByVisual.Remove(visual, out var sources))
        {
            foreach (var source in sources)
            {
                if (_visualsBySource.TryGetValue(source, out var visuals)
                    && visuals.Remove(visual))
                {
                    VisualCount--;
                    if (visuals.Count == 0)
                    {
                        _visualsBySource.Remove(source);
                    }
                }
            }
        }

        _sourceOwnersByVisual.Remove(visual);
        _dependenciesByVisual.Remove(visual);

        if (visual is global::ProGPU.Scene.ContainerVisual containerVisual)
        {
            var children = containerVisual.Children;
            for (var i = 0; i < children.Count; i++)
            {
                UnregisterVisualTreeCore(children[i]);
            }
        }
    }

    private void RegisterOwnerKind(
        object source,
        ProGpuVisual visual,
        WpfRetainedVisualBranchOwnerKind ownerKind)
    {
        var ownersByVisual = ownerKind == WpfRetainedVisualBranchOwnerKind.SourceOwner
            ? _sourceOwnersByVisual
            : _dependenciesByVisual;
        if (!ownersByVisual.TryGetValue(visual, out var owners))
        {
            owners = new HashSet<object>(ReferenceEqualityComparer.Instance);
            ownersByVisual.Add(visual, owners);
        }

        owners.Add(source);
    }

    private bool TryGetReplaySourceForVisual(
        ProGpuVisual visual,
        out object replaySource)
    {
        replaySource = null!;
        if (!_sourceOwnersByVisual.TryGetValue(visual, out var sourceOwners) ||
            sourceOwners.Count != 1)
        {
            return false;
        }

        foreach (var sourceOwner in sourceOwners)
        {
            replaySource = sourceOwner;
            return true;
        }

        return false;
    }

    private static bool IsAncestorOf(ProGpuVisual ancestor, ProGpuVisual visual)
    {
        for (var current = visual.Parent; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}

public readonly record struct WpfRetainedVisualBranchReplayTarget(
    object Source,
    ProGpuVisual Visual);

internal enum WpfRetainedVisualBranchOwnerKind
{
    SourceOwner,
    Dependency
}

public readonly struct WpfRetainedVisualBranchInvalidationResult
{
    public WpfRetainedVisualBranchInvalidationResult(
        int dirtySourceCount,
        int mappedSourceCount,
        int invalidatedVisualCount,
        int sharedWithCleanSourceVisualCount = 0,
        int replayTargetConflictCount = 0)
    {
        DirtySourceCount = dirtySourceCount;
        MappedSourceCount = mappedSourceCount;
        InvalidatedVisualCount = invalidatedVisualCount;
        SharedWithCleanSourceVisualCount = sharedWithCleanSourceVisualCount;
        ReplayTargetConflictCount = replayTargetConflictCount;
    }

    public int DirtySourceCount { get; }

    public int MappedSourceCount { get; }

    public int UnmappedSourceCount => DirtySourceCount - MappedSourceCount;

    public int InvalidatedVisualCount { get; }

    public int SharedWithCleanSourceVisualCount { get; }

    public int ReplayTargetConflictCount { get; }

    public bool CanTargetAllDirtySources =>
        DirtySourceCount > 0 &&
        UnmappedSourceCount == 0 &&
        InvalidatedVisualCount > 0 &&
        SharedWithCleanSourceVisualCount == 0 &&
        ReplayTargetConflictCount == 0;
}

internal interface IWpfRetainedVisualBranchSink
{
    void RegisterVisualOwner(object sourceVisual);

    void RegisterVisualDependency(object dependency);

    bool PushVisualOwner(object sourceVisual);

    void PopVisualOwner();
}

internal readonly struct WpfRetainedVisualState
{
    public WpfRetainedVisualState(
        Vector2 offset,
        Matrix4x4 transform,
        float opacity,
        Rect? clipBounds,
        Vector2? size = null,
        global::ProGPU.Scene.EffectBase? effect = null,
        bool cacheAsLayer = false,
        Rect? contentBounds = null)
    {
        Offset = offset;
        Transform = transform;
        Opacity = opacity;
        ClipBounds = clipBounds;
        Size = size;
        Effect = effect;
        CacheAsLayer = cacheAsLayer;
        ContentBounds = contentBounds;
    }

    public Vector2 Offset { get; }

    public Vector2? Size { get; }

    public Matrix4x4 Transform { get; }

    public float Opacity { get; }

    public Rect? ClipBounds { get; }

    public global::ProGPU.Scene.EffectBase? Effect { get; }

    public bool CacheAsLayer { get; }

    public Rect? ContentBounds { get; }
}

internal interface IWpfRetainedVisualStateSink
{
    void ApplyVisualState(in WpfRetainedVisualState state);
}
