using System.Collections.Generic;
using System.Windows.Media.ProGPU.Platform;

namespace System.Windows.Media.ProGPU;

public sealed partial class WpfPortableWindowActivation
{
    // A native callback may enter from another thread while the source dispatcher
    // is draining Input/Render. Protect transport ownership, never source callbacks.
    private readonly object _deferredHostInputGate = new();
    private Queue<DeferredHostInput>? _deferredHostInput;
    private bool _isDrainingDeferredHostInput;
    private ulong _deferredHostInputGeneration;

    private sealed record DeferredHostInput(
        WpfInputEventArgs Input,
        WpfPortablePresentationSourceBridge? Bridge,
        object? Root,
        ulong Generation);

    private bool TryDeferHostInput(WpfInputEventArgs input)
    {
        lock (_deferredHostInputGate)
        {
            if (!_isFlushingWpfDispatcher && !_isDrainingDeferredHostInput &&
                _deferredHostInput is not { Count: > 0 })
            {
                return false;
            }

            // The event's fields are immutable, except Handled. Own that mutable
            // result separately; WithPointerCoordinates retains native timestamp,
            // button, modifiers, phase/device metadata and the original coordinates.
            WpfInputEventArgs copy = input.WithPointerCoordinates(input.X, input.Y, input.DeltaX, input.DeltaY);
            (_deferredHostInput ??= new()).Enqueue(new DeferredHostInput(
                copy, Host.PortablePresentationSourceBridge, Host.WpfRootVisual, _deferredHostInputGeneration));
            input.Handled = true; // This activation owns delivery; no second host fallback.
            return true;
        }
    }

    private bool IsDeferredHostInputCurrent(DeferredHostInput input)
    {
        lock (_deferredHostInputGate)
        {
            return !_isDisposed && input.Generation == _deferredHostInputGeneration &&
                ReferenceEquals(input.Bridge, Host.PortablePresentationSourceBridge) &&
                ReferenceEquals(input.Root, Host.WpfRootVisual);
        }
    }

    private void RetireDeferredHostInput()
    {
        lock (_deferredHostInputGate)
        {
            unchecked { ++_deferredHostInputGeneration; }
            _deferredHostInput?.Clear();
        }
    }

    private void DrainDeferredHostInput()
    {
        lock (_deferredHostInputGate)
        {
            if (_isDisposed || _isFlushingWpfDispatcher || _isDrainingDeferredHostInput ||
                _deferredHostInput is not { Count: > 0 })
            {
                return;
            }

            _isDrainingDeferredHostInput = true;
        }

        try
        {
            while (true)
            {
                DeferredHostInput next;
                lock (_deferredHostInputGate)
                {
                    if (_isDisposed || _deferredHostInput is not { Count: > 0 }) return;
                    next = _deferredHostInput.Dequeue();
                }

                // Use the original dispatch/press cleanup and per-event layout
                // boundary. Reentry appends to this FIFO, not the current stack.
                DispatchHostInput(next.Input, next);
            }
        }
        finally
        {
            lock (_deferredHostInputGate)
            {
                _isDrainingDeferredHostInput = false;
            }
        }
        // A throwing packet is attempted exactly once. Unattempted packets stay
        // ahead of new ingress until an ordinary host turn resumes this queue;
        // cancellation/hide/deactivation/disposal can still retire them first.
    }
}
