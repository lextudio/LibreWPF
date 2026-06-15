using System;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class DispatcherWpfRenderScheduler : IWpfRenderScheduler, IDisposable
{
    public static readonly TimeSpan DefaultRenderInterval = TimeSpan.FromMilliseconds(16);

    private readonly object _gate = new();
    private readonly IWpfDispatcherService _dispatcher;
    private readonly IWpfTimer _renderTimer;
    private IWpfDispatcherOperation? _renderOperation;
    private bool _hasPendingRenderRequest;
    private bool _isDisposed;

    public DispatcherWpfRenderScheduler(
        IWpfDispatcherService dispatcher,
        IWpfTimerService timers,
        TimeSpan? renderInterval = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(timers);

        RenderInterval = renderInterval ?? DefaultRenderInterval;
        if (RenderInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(renderInterval), renderInterval, "Render interval must be greater than zero.");
        }

        _dispatcher = dispatcher;
        _renderTimer = timers.CreateTimer(RenderInterval, OnRenderTimerTick, isRepeating: false);
    }

    public event EventHandler? RenderRequested;

    public TimeSpan RenderInterval { get; }

    public bool HasPendingRenderRequest
    {
        get
        {
            lock (_gate)
            {
                return _hasPendingRenderRequest;
            }
        }
    }

    public void RequestRender()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_hasPendingRenderRequest)
            {
                return;
            }

            _hasPendingRenderRequest = true;
            _renderTimer.Start();
        }
    }

    public bool ConsumeRenderRequest()
    {
        lock (_gate)
        {
            var hadPendingRequest = _hasPendingRenderRequest;
            _hasPendingRenderRequest = false;
            CancelRenderDispatchLocked();
            return hadPendingRequest;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _hasPendingRenderRequest = false;
            CancelRenderDispatchLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _hasPendingRenderRequest = false;
            CancelRenderDispatchLocked();
            _renderTimer.Dispose();
            _isDisposed = true;
        }
    }

    private void OnRenderTimerTick()
    {
        lock (_gate)
        {
            if (_isDisposed || !_hasPendingRenderRequest || _renderOperation != null)
            {
                return;
            }

            _renderOperation = _dispatcher.Post(RaiseRenderRequested, WpfDispatcherPriority.Render);
        }
    }

    private void RaiseRenderRequested()
    {
        var shouldRaise = false;

        lock (_gate)
        {
            _renderOperation = null;
            shouldRaise = !_isDisposed && _hasPendingRenderRequest;
        }

        if (shouldRaise)
        {
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CancelRenderDispatchLocked()
    {
        if (_renderTimer.IsEnabled)
        {
            _renderTimer.Stop();
        }

        _renderOperation?.Cancel();
        _renderOperation = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
