using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using MediaBrush = System.Windows.Media.Brush;
using ProGpuVisual = global::ProGPU.Scene.Visual;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class WpfRetainedVisualBranchMap
{
    private readonly Dictionary<object, VisualSet> _visualsBySource = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProGpuVisual, ReferenceOwnerSet> _sourcesByVisual = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProGpuVisual, ReferenceOwnerSet> _sourceOwnersByVisual = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProGpuVisual, ReferenceOwnerSet> _dependenciesByVisual = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _scratchDistinctSources = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ProGpuVisual> _scratchVisitedVisuals = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ProGpuVisual> _scratchInvalidatedVisuals = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ProGpuVisual> _scratchTargetVisuals = new(ReferenceEqualityComparer.Instance);
    private readonly List<WpfRetainedVisualBranchReplayTarget> _scratchReplayTargets = new();
    private readonly List<WpfRetainedVisualBranchReplayTarget> _scratchTopLevelReplayTargets = new();
    private readonly SingleReplayTargetList _scratchSingleReplayTarget = new();
    private readonly ReplayTargetList _scratchReplayTargetSnapshot = new();

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
        _scratchDistinctSources.Clear();
        _scratchVisitedVisuals.Clear();
        _scratchInvalidatedVisuals.Clear();
        _scratchTargetVisuals.Clear();
        _scratchReplayTargets.Clear();
        _scratchTopLevelReplayTargets.Clear();
        _scratchSingleReplayTarget.Clear();
        _scratchReplayTargetSnapshot.Clear();
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

        ref var sources = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _sourcesByVisual,
            visual,
            out var hasSources);
        if (hasSources && sources.Contains(source))
        {
            RegisterOwnerKind(source, visual, ownerKind);
            LastSource = source;
            LastVisual = visual;
            return;
        }

        ref var visuals = ref CollectionsMarshal.GetValueRefOrAddDefault(_visualsBySource, source, out _);
        visuals.Add(visual);
        sources.Add(source);
        RegisterOwnerKind(source, visual, ownerKind);
        VisualCount++;
        LastSource = source;
        LastVisual = visual;
    }

    public bool TryGetVisuals(object source, out IReadOnlyList<ProGpuVisual> visuals)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (_visualsBySource.TryGetValue(source, out var mappedVisuals) &&
            mappedVisuals.Count > 0)
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

        var count = visuals.Count;
        for (var i = 0; i < count; i++)
        {
            visuals[i].Invalidate();
        }

        return count;
    }

    public int InvalidateVisuals(IEnumerable<object> sources)
    {
        return InvalidateVisualsForSources(sources).InvalidatedVisualCount;
    }

    public IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForSources(IEnumerable<object> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (TryGetReferenceEqualityHashSet(sources, out var referenceDirtySources))
        {
            return GetReplayTargetsForReferenceSourceSet(referenceDirtySources);
        }

        if (sources is IReadOnlyCollection<object> sourceCollection)
        {
            if (sourceCollection.Count == 0)
            {
                return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
            }

            if (sourceCollection.Count == 1 &&
                TryGetSingleSource(sourceCollection, out var singleSource))
            {
                return GetReplayTargetsForSingleSource(singleSource);
            }
        }

        _scratchDistinctSources.Clear();
        try
        {
            foreach (var source in sources)
            {
                _scratchDistinctSources.Add(source);
            }

            return GetReplayTargetsForDistinctSourceSet(_scratchDistinctSources);
        }
        finally
        {
            _scratchDistinctSources.Clear();
        }
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForReferenceSourceSet(
        HashSet<object> dirtySources)
    {
        if (dirtySources.Count == 0)
        {
            return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
        }

        if (dirtySources.Count == 1 &&
            TryGetSingleSource(dirtySources, out var singleSource))
        {
            return GetReplayTargetsForSingleSource(singleSource);
        }

        return GetReplayTargetsForDistinctSourceSet(dirtySources);
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForDistinctSourceSet(
        HashSet<object> dirtySources)
    {
        if (dirtySources.Count == 0)
        {
            return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
        }

        _scratchReplayTargets.Clear();
        _scratchVisitedVisuals.Clear();
        try
        {
            foreach (var source in dirtySources)
            {
                if (!_visualsBySource.TryGetValue(source, out var visuals))
                {
                    return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
                }

                var visualCount = visuals.Count;
                for (var i = 0; i < visualCount; i++)
                {
                    var visual = visuals[i];
                    if (!TryGetReplaySourceForVisual(visual, out var replaySource))
                    {
                        return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
                    }

                    if (_scratchVisitedVisuals.Add(visual))
                    {
                        _scratchReplayTargets.Add(new WpfRetainedVisualBranchReplayTarget(replaySource, visual));
                    }
                }
            }

            return _scratchReplayTargets.Count <= 1
                ? SnapshotReplayTargets(_scratchReplayTargets)
                : SelectTopLevelReplayTargets(_scratchReplayTargets);
        }
        finally
        {
            _scratchReplayTargets.Clear();
            _scratchVisitedVisuals.Clear();
        }
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForSingleSource(object source)
    {
        if (!_visualsBySource.TryGetValue(source, out var visuals))
        {
            return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
        }

        if (visuals.Count == 1)
        {
            var visual = visuals[0];
            return TryGetReplaySourceForVisual(visual, out var replaySource)
                ? CreateSingleReplayTarget(replaySource, visual)
                : Array.Empty<WpfRetainedVisualBranchReplayTarget>();
        }

        _scratchReplayTargets.Clear();
        try
        {
            var visualCount = visuals.Count;
            for (var i = 0; i < visualCount; i++)
            {
                var visual = visuals[i];
                if (!TryGetReplaySourceForVisual(visual, out var replaySource))
                {
                    return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
                }

                _scratchReplayTargets.Add(new WpfRetainedVisualBranchReplayTarget(replaySource, visual));
            }

            return _scratchReplayTargets.Count <= 1
                ? SnapshotReplayTargets(_scratchReplayTargets)
                : SelectTopLevelReplayTargets(_scratchReplayTargets);
        }
        finally
        {
            _scratchReplayTargets.Clear();
        }
    }

    private static bool TryGetSingleSource(
        IReadOnlyCollection<object> sources,
        out object source)
    {
        foreach (var candidate in sources)
        {
            source = candidate;
            return true;
        }

        source = null!;
        return false;
    }

    private static bool TryGetSingleSource(
        HashSet<object> sources,
        out object source)
    {
        foreach (var candidate in sources)
        {
            source = candidate;
            return true;
        }

        source = null!;
        return false;
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> CreateSingleReplayTarget(
        object source,
        ProGpuVisual visual)
    {
        _scratchSingleReplayTarget.Set(new WpfRetainedVisualBranchReplayTarget(source, visual));
        return _scratchSingleReplayTarget;
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> SnapshotReplayTargets(
        List<WpfRetainedVisualBranchReplayTarget> targets)
    {
        if (targets.Count == 0)
        {
            return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
        }

        if (targets.Count == 1)
        {
            _scratchSingleReplayTarget.Set(targets[0]);
            return _scratchSingleReplayTarget;
        }

        _scratchReplayTargetSnapshot.Set(targets);
        return _scratchReplayTargetSnapshot;
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> SelectTopLevelReplayTargets(
        List<WpfRetainedVisualBranchReplayTarget> targets)
    {
        _scratchTopLevelReplayTargets.Clear();
        _scratchTargetVisuals.Clear();
        try
        {
            var targetCount = targets.Count;
            for (var i = 0; i < targetCount; i++)
            {
                var target = targets[i];
                _scratchTargetVisuals.Add(target.Visual);
            }

            for (var i = 0; i < targetCount; i++)
            {
                var target = targets[i];
                if (!IsCoveredByTargetAncestor(target.Visual, _scratchTargetVisuals))
                {
                    _scratchTopLevelReplayTargets.Add(target);
                }
            }

            return SnapshotReplayTargets(_scratchTopLevelReplayTargets);
        }
        finally
        {
            _scratchTopLevelReplayTargets.Clear();
            _scratchTargetVisuals.Clear();
        }
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

        if (TryGetReferenceEqualityHashSet(sources, out var referenceVisitedSources))
        {
            return InvalidateVisualsForReferenceSourceSet(referenceVisitedSources);
        }

        if (sources is IReadOnlyCollection<object> sourceCollection)
        {
            if (sourceCollection.Count == 0)
            {
                return new WpfRetainedVisualBranchInvalidationResult(0, 0, 0);
            }

            if (sourceCollection.Count == 1 &&
                TryGetSingleSource(sourceCollection, out var singleSource))
            {
                return InvalidateVisualsForSingleSource(singleSource);
            }
        }

        _scratchDistinctSources.Clear();
        try
        {
            foreach (var source in sources)
            {
                _scratchDistinctSources.Add(source);
            }

            return InvalidateVisualsForDistinctSourceSet(_scratchDistinctSources);
        }
        finally
        {
            _scratchDistinctSources.Clear();
        }
    }

    private WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForReferenceSourceSet(
        HashSet<object> visitedSources)
    {
        if (visitedSources.Count == 0)
        {
            return new WpfRetainedVisualBranchInvalidationResult(0, 0, 0);
        }

        if (visitedSources.Count == 1 &&
            TryGetSingleSource(visitedSources, out var singleSource))
        {
            return InvalidateVisualsForSingleSource(singleSource);
        }

        return InvalidateVisualsForDistinctSourceSet(visitedSources);
    }

    private WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForSingleSource(object source)
    {
        if (!_visualsBySource.TryGetValue(source, out var visuals))
        {
            return new WpfRetainedVisualBranchInvalidationResult(1, 0, 0);
        }

        var invalidatedVisualCount = 0;
        var sharedWithCleanSourceVisualCount = 0;
        var replayTargetConflictCount = 0;

        var visualCount = visuals.Count;
        for (var i = 0; i < visualCount; i++)
        {
            var visual = visuals[i];
            visual.Invalidate();
            invalidatedVisualCount++;

            if (!_sourceOwnersByVisual.TryGetValue(visual, out var sourceOwners))
            {
                replayTargetConflictCount++;
                continue;
            }

            if (sourceOwners.Count == 1)
            {
                continue;
            }

            replayTargetConflictCount++;

            sourceOwners.ClassifyAgainst(
                source,
                out var hasDirtySourceOwner,
                out var hasCleanSourceOwner);

            if (hasDirtySourceOwner && hasCleanSourceOwner)
            {
                sharedWithCleanSourceVisualCount++;
            }
        }

        return new WpfRetainedVisualBranchInvalidationResult(
            1,
            1,
            invalidatedVisualCount,
            sharedWithCleanSourceVisualCount,
            replayTargetConflictCount);
    }

    private WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForDistinctSourceSet(
        HashSet<object> visitedSources)
    {
        if (visitedSources.Count == 0)
        {
            return new WpfRetainedVisualBranchInvalidationResult(0, 0, 0);
        }

        _scratchInvalidatedVisuals.Clear();
        var mappedSourceCount = 0;
        var sharedWithCleanSourceVisualCount = 0;
        var replayTargetConflictCount = 0;
        try
        {
            foreach (var source in visitedSources)
            {
                if (!_visualsBySource.TryGetValue(source, out var visuals))
                {
                    continue;
                }

                mappedSourceCount++;

                var visualCount = visuals.Count;
                for (var i = 0; i < visualCount; i++)
                {
                    var visual = visuals[i];
                    if (_scratchInvalidatedVisuals.Add(visual))
                    {
                        visual.Invalidate();
                    }
                }
            }

            foreach (var visual in _scratchInvalidatedVisuals)
            {
                if (!_sourceOwnersByVisual.TryGetValue(visual, out var sourceOwners))
                {
                    replayTargetConflictCount++;
                    continue;
                }

                if (sourceOwners.Count == 1)
                {
                    continue;
                }

                replayTargetConflictCount++;

                sourceOwners.ClassifyAgainst(
                    visitedSources,
                    out var hasDirtySourceOwner,
                    out var hasCleanSourceOwner);
                if (!hasDirtySourceOwner)
                {
                    continue;
                }

                if (hasCleanSourceOwner)
                {
                    sharedWithCleanSourceVisualCount++;
                }
            }

            return new WpfRetainedVisualBranchInvalidationResult(
                visitedSources.Count,
                mappedSourceCount,
                _scratchInvalidatedVisuals.Count,
                sharedWithCleanSourceVisualCount,
                replayTargetConflictCount);
        }
        finally
        {
            _scratchInvalidatedVisuals.Clear();
        }
    }

    private static bool TryGetReferenceEqualityHashSet(
        IEnumerable<object> sources,
        out HashSet<object> sourceSet)
    {
        if (sources is HashSet<object> candidate &&
            ReferenceEquals(candidate.Comparer, ReferenceEqualityComparer.Instance))
        {
            sourceSet = candidate;
            return true;
        }

        sourceSet = null!;
        return false;
    }

    private void UnregisterVisualTreeCore(ProGpuVisual visual)
    {
        if (_sourcesByVisual.Remove(visual, out var sources))
        {
            foreach (var source in sources)
            {
                if (_visualsBySource.TryGetValue(source, out var visuals)
                    && RemoveVisualForSource(source, visual, visuals))
                {
                    VisualCount--;
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

    private bool RemoveVisualForSource(
        object source,
        ProGpuVisual visual,
        VisualSet visuals)
    {
        if (!visuals.Remove(visual))
        {
            return false;
        }

        if (visuals.Count == 0)
        {
            _visualsBySource.Remove(source);
            return true;
        }

        _visualsBySource[source] = visuals;
        return true;
    }

    private void RegisterOwnerKind(
        object source,
        ProGpuVisual visual,
        WpfRetainedVisualBranchOwnerKind ownerKind)
    {
        var ownersByVisual = ownerKind == WpfRetainedVisualBranchOwnerKind.SourceOwner
            ? _sourceOwnersByVisual
            : _dependenciesByVisual;
        ref var owners = ref CollectionsMarshal.GetValueRefOrAddDefault(ownersByVisual, visual, out _);
        owners.Add(source);
    }

    private bool TryGetReplaySourceForVisual(
        ProGpuVisual visual,
        out object replaySource)
    {
        replaySource = null!;
        if (!_sourceOwnersByVisual.TryGetValue(visual, out var sourceOwners) ||
            !sourceOwners.TryGetSingle(out replaySource))
        {
            return false;
        }

        return true;
    }

    private static bool IsCoveredByTargetAncestor(
        ProGpuVisual visual,
        HashSet<ProGpuVisual> targetVisuals)
    {
        for (var current = visual.Parent; current != null; current = current.Parent)
        {
            if (targetVisuals.Contains(current))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class SingleReplayTargetList : IReadOnlyList<WpfRetainedVisualBranchReplayTarget>
    {
        private WpfRetainedVisualBranchReplayTarget _target;
        private bool _hasTarget;

        public int Count => _hasTarget ? 1 : 0;

        public WpfRetainedVisualBranchReplayTarget this[int index]
        {
            get
            {
                if (!_hasTarget || index != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _target;
            }
        }

        public void Set(WpfRetainedVisualBranchReplayTarget target)
        {
            _target = target;
            _hasTarget = true;
        }

        public void Clear()
        {
            _target = default;
            _hasTarget = false;
        }

        public IEnumerator<WpfRetainedVisualBranchReplayTarget> GetEnumerator()
        {
            if (_hasTarget)
            {
                yield return _target;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class ReplayTargetList : IReadOnlyList<WpfRetainedVisualBranchReplayTarget>
    {
        private readonly List<WpfRetainedVisualBranchReplayTarget> _targets = new();

        public int Count => _targets.Count;

        public WpfRetainedVisualBranchReplayTarget this[int index] => _targets[index];

        public void Set(List<WpfRetainedVisualBranchReplayTarget> targets)
        {
            _targets.Clear();
            for (var i = 0; i < targets.Count; i++)
            {
                _targets.Add(targets[i]);
            }
        }

        public void Clear()
        {
            _targets.Clear();
        }

        public IEnumerator<WpfRetainedVisualBranchReplayTarget> GetEnumerator()
        {
            return _targets.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private struct VisualSet : IReadOnlyList<ProGpuVisual>
    {
        private ProGpuVisual? _single;
        private List<ProGpuVisual>? _many;

        public int Count => _many?.Count ?? (_single == null ? 0 : 1);

        public ProGpuVisual this[int index]
        {
            get
            {
                if (_many != null)
                {
                    return _many[index];
                }

                if (index != 0 || _single == null)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _single;
            }
        }

        public void Add(ProGpuVisual visual)
        {
            ArgumentNullException.ThrowIfNull(visual);

            if (_many != null)
            {
                _many.Add(visual);
                return;
            }

            if (_single == null)
            {
                _single = visual;
                return;
            }

            _many = new List<ProGpuVisual>
            {
                _single,
                visual
            };
            _single = null;
        }

        public bool Remove(ProGpuVisual visual)
        {
            ArgumentNullException.ThrowIfNull(visual);

            if (_many != null)
            {
                if (!_many.Remove(visual))
                {
                    return false;
                }

                if (_many.Count == 1)
                {
                    _single = _many[0];
                    _many = null;
                }

                return true;
            }

            if (!ReferenceEquals(_single, visual))
            {
                return false;
            }

            _single = null;
            return true;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_single, _many);
        }

        IEnumerator<ProGpuVisual> IEnumerable<ProGpuVisual>.GetEnumerator()
        {
            return GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<ProGpuVisual>
        {
            private readonly ProGpuVisual? _single;
            private List<ProGpuVisual>.Enumerator _manyEnumerator;
            private readonly bool _hasMany;
            private bool _singleConsumed;

            internal Enumerator(ProGpuVisual? single, List<ProGpuVisual>? many)
            {
                _single = single;
                if (many != null)
                {
                    _manyEnumerator = many.GetEnumerator();
                    _hasMany = true;
                }
                else
                {
                    _manyEnumerator = default;
                    _hasMany = false;
                }

                _singleConsumed = false;
                Current = null!;
            }

            public ProGpuVisual Current { get; private set; }

            object System.Collections.IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (_hasMany)
                {
                    if (!_manyEnumerator.MoveNext())
                    {
                        return false;
                    }

                    Current = _manyEnumerator.Current;
                    return true;
                }

                if (_singleConsumed || _single == null)
                {
                    return false;
                }

                Current = _single;
                _singleConsumed = true;
                return true;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
            }
        }
    }

    private struct ReferenceOwnerSet
    {
        private object? _single;
        private HashSet<object>? _many;

        public int Count => _many?.Count ?? (_single == null ? 0 : 1);

        public bool Add(object source)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (_many != null)
            {
                return _many.Add(source);
            }

            if (_single == null)
            {
                _single = source;
                return true;
            }

            if (ReferenceEquals(_single, source))
            {
                return false;
            }

            _many = new HashSet<object>(ReferenceEqualityComparer.Instance)
            {
                _single,
                source
            };
            _single = null;
            return true;
        }

        public bool Contains(object source)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (_many != null)
            {
                return _many.Contains(source);
            }

            return ReferenceEquals(_single, source);
        }

        public bool TryGetSingle(out object source)
        {
            if (_many == null && _single != null)
            {
                source = _single;
                return true;
            }

            source = null!;
            return false;
        }

        public void ClassifyAgainst(
            object dirtySource,
            out bool hasDirtySourceOwner,
            out bool hasCleanSourceOwner)
        {
            ArgumentNullException.ThrowIfNull(dirtySource);

            hasDirtySourceOwner = false;
            hasCleanSourceOwner = false;
            if (_many != null)
            {
                hasDirtySourceOwner = _many.Contains(dirtySource);
                hasCleanSourceOwner = _many.Count > (hasDirtySourceOwner ? 1 : 0);
                return;
            }

            if (_single == null)
            {
                return;
            }

            hasDirtySourceOwner = ReferenceEquals(_single, dirtySource);
            hasCleanSourceOwner = !hasDirtySourceOwner;
        }

        public void ClassifyAgainst(
            HashSet<object> dirtySources,
            out bool hasDirtySourceOwner,
            out bool hasCleanSourceOwner)
        {
            ArgumentNullException.ThrowIfNull(dirtySources);

            hasDirtySourceOwner = false;
            hasCleanSourceOwner = false;
            if (_many != null)
            {
                foreach (var sourceOwner in _many)
                {
                    if (dirtySources.Contains(sourceOwner))
                    {
                        hasDirtySourceOwner = true;
                    }
                    else
                    {
                        hasCleanSourceOwner = true;
                    }

                    if (hasDirtySourceOwner && hasCleanSourceOwner)
                    {
                        return;
                    }
                }

                return;
            }

            if (_single == null)
            {
                return;
            }

            hasDirtySourceOwner = dirtySources.Contains(_single);
            hasCleanSourceOwner = !hasDirtySourceOwner;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_single, _many);
        }

        public struct Enumerator
        {
            private readonly object? _single;
            private HashSet<object>.Enumerator _manyEnumerator;
            private readonly bool _hasMany;
            private bool _singleConsumed;

            internal Enumerator(object? single, HashSet<object>? many)
            {
                _single = single;
                if (many != null)
                {
                    _manyEnumerator = many.GetEnumerator();
                    _hasMany = true;
                }
                else
                {
                    _manyEnumerator = default;
                    _hasMany = false;
                }

                _singleConsumed = false;
                Current = null!;
            }

            public object Current { get; private set; }

            public bool MoveNext()
            {
                if (_hasMany)
                {
                    if (!_manyEnumerator.MoveNext())
                    {
                        return false;
                    }

                    Current = _manyEnumerator.Current;
                    return true;
                }

                if (_singleConsumed || _single == null)
                {
                    return false;
                }

                Current = _single;
                _singleConsumed = true;
                return true;
            }
        }
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

internal readonly struct WpfReplayRect : IEquatable<WpfReplayRect>
{
    public WpfReplayRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static WpfReplayRect Empty { get; } = new(0, 0, 0, 0);

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public bool Equals(WpfReplayRect other)
    {
        return X.Equals(other.X)
            && Y.Equals(other.Y)
            && Width.Equals(other.Width)
            && Height.Equals(other.Height);
    }

    public override bool Equals(object? obj)
    {
        return obj is WpfReplayRect other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Width, Height);
    }
}

internal readonly struct WpfReplayPoint
{
    public WpfReplayPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}

internal readonly struct WpfRetainedVisualState
{
    public WpfRetainedVisualState(
        Vector2 offset,
        Matrix4x4 transform,
        float opacity,
        WpfReplayRect? clipBounds,
        Vector2? size = null,
        global::ProGPU.Scene.EffectBase? effect = null,
        bool cacheAsLayer = false,
        WpfReplayRect? contentBounds = null,
        MediaBrush? opacityMask = null,
        WpfReplayRect? opacityMaskBounds = null,
        WpfReplayRect? outerClipBounds = null)
    {
        Offset = offset;
        Transform = transform;
        Opacity = opacity;
        ClipBounds = clipBounds;
        OuterClipBounds = outerClipBounds;
        Size = size;
        Effect = effect;
        CacheAsLayer = cacheAsLayer;
        ContentBounds = contentBounds;
        OpacityMask = opacityMask;
        OpacityMaskBounds = opacityMaskBounds;
    }

    public Vector2 Offset { get; }

    public Vector2? Size { get; }

    public Matrix4x4 Transform { get; }

    public float Opacity { get; }

    public WpfReplayRect? ClipBounds { get; }

    public WpfReplayRect? OuterClipBounds { get; }

    public global::ProGPU.Scene.EffectBase? Effect { get; }

    public bool CacheAsLayer { get; }

    public WpfReplayRect? ContentBounds { get; }

    public MediaBrush? OpacityMask { get; }

    public WpfReplayRect? OpacityMaskBounds { get; }
}

internal interface IWpfRetainedVisualStateSink
{
    void ApplyVisualState(in WpfRetainedVisualState state);
}
