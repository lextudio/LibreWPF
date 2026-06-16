using System;
using System.Collections.Generic;
using ProGpuVisual = global::ProGPU.Scene.Visual;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class WpfRetainedVisualBranchMap
{
    private readonly Dictionary<object, List<ProGpuVisual>> _visualsBySource = new(ReferenceEqualityComparer.Instance);

    public int SourceCount => _visualsBySource.Count;

    public int VisualCount { get; private set; }

    public object? LastSource { get; private set; }

    public ProGpuVisual? LastVisual { get; private set; }

    public IReadOnlyCollection<object> Sources => _visualsBySource.Keys;

    public void Clear()
    {
        _visualsBySource.Clear();
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
}

internal interface IWpfRetainedVisualBranchSink
{
    void RegisterVisualOwner(object sourceVisual);
}
