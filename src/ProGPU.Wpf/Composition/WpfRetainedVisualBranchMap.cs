using System;
using System.Collections.Generic;
using ProGpuVisual = global::ProGPU.Scene.Visual;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class WpfRetainedVisualBranchMap
{
    private readonly Dictionary<object, List<ProGpuVisual>> _visualsBySource = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProGpuVisual, HashSet<object>> _sourcesByVisual = new(ReferenceEqualityComparer.Instance);

    public int SourceCount => _visualsBySource.Count;

    public int VisualCount { get; private set; }

    public object? LastSource { get; private set; }

    public ProGpuVisual? LastVisual { get; private set; }

    public IReadOnlyCollection<object> Sources => _visualsBySource.Keys;

    public void Clear()
    {
        _visualsBySource.Clear();
        _sourcesByVisual.Clear();
        VisualCount = 0;
        LastSource = null;
        LastVisual = null;
    }

    public void Register(object? source, ProGpuVisual? visual)
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

    public WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForSources(IEnumerable<object> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var visitedSources = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var invalidatedVisuals = new HashSet<ProGpuVisual>(ReferenceEqualityComparer.Instance);
        var dirtySourceCount = 0;
        var mappedSourceCount = 0;
        var sharedWithCleanSourceVisualCount = 0;

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
            if (!_sourcesByVisual.TryGetValue(visual, out var visualSources))
            {
                continue;
            }

            foreach (var visualSource in visualSources)
            {
                if (!visitedSources.Contains(visualSource))
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
            sharedWithCleanSourceVisualCount);
    }
}

public readonly struct WpfRetainedVisualBranchInvalidationResult
{
    public WpfRetainedVisualBranchInvalidationResult(
        int dirtySourceCount,
        int mappedSourceCount,
        int invalidatedVisualCount,
        int sharedWithCleanSourceVisualCount = 0)
    {
        DirtySourceCount = dirtySourceCount;
        MappedSourceCount = mappedSourceCount;
        InvalidatedVisualCount = invalidatedVisualCount;
        SharedWithCleanSourceVisualCount = sharedWithCleanSourceVisualCount;
    }

    public int DirtySourceCount { get; }

    public int MappedSourceCount { get; }

    public int UnmappedSourceCount => DirtySourceCount - MappedSourceCount;

    public int InvalidatedVisualCount { get; }

    public int SharedWithCleanSourceVisualCount { get; }

    public bool CanTargetAllDirtySources =>
        DirtySourceCount > 0 &&
        UnmappedSourceCount == 0 &&
        InvalidatedVisualCount > 0 &&
        SharedWithCleanSourceVisualCount == 0;
}

internal interface IWpfRetainedVisualBranchSink
{
    void RegisterVisualOwner(object sourceVisual);
}
