namespace System.Windows.Media.ProGPU.Composition;

// Inline failure-only ownership, serialized by the containing paragraph gate.
// This primitive owns cleanup only; it does not manufacture a paragraph/font.
internal struct WpfHintedTextRetirement
{
    private IDisposable? _owner;
    private bool _disposing;

    internal void Capture(IDisposable owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner is not null)
            throw new InvalidOperationException("A failed hinted resource must retire before replacement.");
        _owner = owner;
    }

    internal void Dispose()
    {
        if (_owner is null || _disposing) return;
        _disposing = true;
        try
        {
            _owner.Dispose();
            _owner = null;
        }
        finally { _disposing = false; }
    }

    internal void DisposePreservingFailure(Exception failure)
    {
        try { Dispose(); }
        catch (Exception cleanup)
        {
            try { failure.Data["HintedReflowCleanupFailure"] = cleanup; }
            catch { /* Keep the original error and exact cleanup owner. */ }
        }
    }
}
