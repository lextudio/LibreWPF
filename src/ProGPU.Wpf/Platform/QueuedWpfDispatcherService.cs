using System;
using System.Collections.Generic;
using System.Threading;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class QueuedWpfDispatcherService : IWpfDispatcherService
{
    private readonly object _gate = new();
    private readonly int _dispatcherThreadId;
    private readonly List<QueuedOperation> _queue = new();
    private long _nextSequence;

    public QueuedWpfDispatcherService()
        : this(Environment.CurrentManagedThreadId)
    {
    }

    internal QueuedWpfDispatcherService(int dispatcherThreadId)
    {
        if (dispatcherThreadId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dispatcherThreadId), dispatcherThreadId, "Dispatcher thread id must be greater than zero.");
        }

        _dispatcherThreadId = dispatcherThreadId;
    }

    public bool CheckAccess()
    {
        return Environment.CurrentManagedThreadId == _dispatcherThreadId;
    }

    public IWpfDispatcherOperation Post(Action callback, WpfDispatcherPriority priority = WpfDispatcherPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (!Enum.IsDefined(typeof(WpfDispatcherPriority), priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unsupported dispatcher priority.");
        }

        lock (_gate)
        {
            var operation = new QueuedOperation(callback, priority, _nextSequence++);
            _queue.Add(operation);
            return operation;
        }
    }

    public bool ProcessPending()
    {
        if (!CheckAccess())
        {
            throw new InvalidOperationException("The queued WPF dispatcher must be processed on its owning thread.");
        }

        var processed = false;

        while (true)
        {
            QueuedOperation? operation;
            lock (_gate)
            {
                operation = DequeueNextOperation();
            }

            if (operation == null)
            {
                return processed;
            }

            if (!operation.TryStart())
            {
                continue;
            }

            try
            {
                operation.Invoke();
                processed = true;
            }
            finally
            {
                operation.MarkCompleted();
            }
        }
    }

    private QueuedOperation? DequeueNextOperation()
    {
        if (_queue.Count == 0)
        {
            return null;
        }

        var selectedIndex = 0;
        var selected = _queue[0];

        for (var i = 1; i < _queue.Count; i++)
        {
            var candidate = _queue[i];
            if (candidate.Priority > selected.Priority ||
                candidate.Priority == selected.Priority && candidate.Sequence < selected.Sequence)
            {
                selectedIndex = i;
                selected = candidate;
            }
        }

        _queue.RemoveAt(selectedIndex);
        return selected;
    }

    private sealed class QueuedOperation : IWpfDispatcherOperation
    {
        private const int Pending = 0;
        private const int Running = 1;
        private const int Completed = 2;
        private const int Canceled = 3;

        private readonly Action _callback;
        private int _state;

        public QueuedOperation(Action callback, WpfDispatcherPriority priority, long sequence)
        {
            _callback = callback;
            Priority = priority;
            Sequence = sequence;
        }

        public WpfDispatcherPriority Priority { get; }

        public long Sequence { get; }

        public bool IsCanceled => Volatile.Read(ref _state) == Canceled;

        public bool IsCompleted => Volatile.Read(ref _state) == Completed;

        public bool Cancel()
        {
            return Interlocked.CompareExchange(ref _state, Canceled, Pending) == Pending;
        }

        public void Dispose()
        {
            Cancel();
        }

        public bool TryStart()
        {
            return Interlocked.CompareExchange(ref _state, Running, Pending) == Pending;
        }

        public void Invoke()
        {
            _callback();
        }

        public void MarkCompleted()
        {
            Volatile.Write(ref _state, Completed);
        }
    }
}
