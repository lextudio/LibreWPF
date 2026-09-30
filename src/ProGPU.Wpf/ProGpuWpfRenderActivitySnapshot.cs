namespace System.Windows.Media.ProGPU;

/// <summary>
/// Owner-thread host activity, read without pumping, rendering or GPU polling.
/// This is not a guarantee of future quiescence: delayed scheduler callbacks and
/// subsequent native/source events remain free to request a presentation.
/// </summary>
public readonly record struct ProGpuWpfRenderActivitySnapshot(
    bool IsRendering,
    bool HasPendingPresentationRequest,
    bool HasPendingDeviceRecovery,
    bool HasPendingDpiChange,
    long PresentedFrameCount,
    long DeviceRecoveryCount)
{
    public bool HasImmediatePresentationWork => IsRendering || HasPendingPresentationRequest ||
        HasPendingDeviceRecovery || HasPendingDpiChange;
}
