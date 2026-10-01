namespace System.Windows.Media.ProGPU.Composition;

/// <summary>Shared retirement for the original native generation, separate from any source handle.</summary>
internal sealed class WpfHintedTextLifetime(IDisposable owner)
{
    private readonly object _gate = new();
    private IDisposable? _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private int _uses;
    private bool _closed, _retiring;

    internal Lease Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            int next = checked(_uses + 1);
            var lease = new Lease(this);
            _uses = next;
            return lease;
        }
    }

    private void EndUse()
    {
        lock (_gate)
        {
            if (_uses <= 0) throw new InvalidOperationException("A hinted generation use ended twice.");
            if (--_uses == 0) _closed = true;
            Release();
        }
    }

    private void RetryRelease() { lock (_gate) Release(); }

    private void Release()
    {
        if (!_closed || _uses != 0 || _owner is null || _retiring) return;
        _retiring = true;
        try { _owner.Dispose(); _owner = null; }
        finally { _retiring = false; }
    }

    internal sealed class Lease(WpfHintedTextLifetime owner) : IDisposable
    {
        private readonly object _gate = new();
        private WpfHintedTextLifetime? _owner = owner;
        private bool _ended, _disposing;
        internal bool IsDisposed { get { lock (_gate) return _ended; } }

        internal Lease Retain()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_ended, this);
                return _owner!.Acquire();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                var value = _owner;
                if (value is null || _disposing) return;
                _disposing = true;
                // Publication precedes native teardown and any reentrant call.
                // A failed teardown retries only retirement, never this use end.
                try
                {
                    if (!_ended) { _ended = true; value.EndUse(); }
                    else value.RetryRelease();
                    _owner = null;
                    GC.SuppressFinalize(this);
                }
                finally { _disposing = false; }
            }
        }

        ~Lease() { try { Dispose(); } catch { /* The native owner also retains finalization. */ } }
    }
}
