using System.Threading;

namespace ProGPU.Wpf.ShowcaseApp;

// A driver's completion file proves submission, not delivery of native input.
// Observe source button events on the dispatcher; read completion on the probe task.
internal sealed class NativeDragInputReceipt
{
    private bool _pressed;
    private int _released;

    internal bool HasSourceRelease => Volatile.Read(ref _released) != 0;

    internal void ObserveLeftButton(bool pressed)
    {
        if (pressed)
        {
            _pressed = true;
            Volatile.Write(ref _released, 0);
        }
        else if (_pressed)
        {
            _pressed = false;
            Volatile.Write(ref _released, 1);
        }
    }

    internal bool IsComplete(bool driverCompleted) => driverCompleted && HasSourceRelease;
}
