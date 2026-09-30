using System.Collections.Generic;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU;

public sealed partial class WpfPortableWindowActivation
{
    // A native callback may enter from another thread while the source dispatcher
    // is draining Input/Render. Protect transport ownership, never source callbacks.
    private readonly object _deferredHostInputGate = new();
    private Queue<DeferredHostInput>? _deferredHostInput;
    private bool _isDrainingDeferredHostInput;
    private object? _deferredHostInputOperation;
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
            // Layout-dependent pointer packets must wait for the active flush.
            // Keyboard delivery needs no such boundary: retain the original
            // synchronous source path unless older deferred work owns the FIFO.
            bool keyboard = input.Kind is WpfInputEventKind.KeyDown or WpfInputEventKind.KeyUp or WpfInputEventKind.TextInput;
            if ((!_isFlushingWpfDispatcher || keyboard) && !_isDrainingDeferredHostInput &&
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
            _deferredHostInputOperation = null;
        }
    }

    private void ScheduleDeferredHostInput()
    {
        object operation;
        lock (_deferredHostInputGate)
        {
            if (_isDisposed || _isDrainingDeferredHostInput || _deferredHostInputOperation != null ||
                _deferredHostInput is not { Count: > 0 })
            {
                return;
            }

            operation = new object();
            _deferredHostInputOperation = operation;
        }

        try
        {
            if (!TryGetWindowActivationService(out var service) || service is not IPortableWindowInputDispatcher dispatcher)
            {
                throw new PlatformNotSupportedException("Deferred host input requires the source window's Input-priority dispatcher.");
            }

            if (!dispatcher.TryPostInput(Window, () => ProcessDeferredHostInput(operation)))
            {
                throw new InvalidOperationException("The source window rejected deferred input dispatch.");
            }
        }
        catch
        {
            lock (_deferredHostInputGate)
            {
                // A post can invoke source hooks before returning. Retire only
                // this operation, never a replacement established by those hooks.
                if (ReferenceEquals(_deferredHostInputOperation, operation))
                {
                    unchecked { ++_deferredHostInputGeneration; }
                    _deferredHostInput?.Clear();
                    _deferredHostInputOperation = null;
                }
            }

            throw;
        }
    }

    private void ProcessDeferredHostInput(object operation)
    {
        DeferredHostInput next;
        lock (_deferredHostInputGate)
        {
            if (!ReferenceEquals(_deferredHostInputOperation, operation)) return;
            _deferredHostInputOperation = null;
            if (_isDisposed || _deferredHostInput is not { Count: > 0 }) return;
            next = _deferredHostInput.Dequeue();
            _isDrainingDeferredHostInput = true;
        }

        try
        {
            // One packet per real source Input operation: pending Render work
            // runs before the next packet, while Background barriers cannot pass
            // accepted input even inside an ApplicationIdle flush frame.
            DispatchHostInput(next.Input, next);
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
        ScheduleDeferredHostInput();
    }
}
