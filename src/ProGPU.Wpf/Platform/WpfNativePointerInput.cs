using System;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Platform;

internal static class WpfNativePointerInput
{
    internal static bool RequiresNativeDispatch(WpfInputEventArgs input) =>
        input.NativePointer != null || input.Kind is WpfInputEventKind.MouseLeave or WpfInputEventKind.MouseCancel;

    internal static bool Forward(IPortableWindowActivationServiceRegistrar? service,
        object target, WpfInputEventArgs input, bool presentationSource)
    {
        if (input.NativePointer is not { } native)
            throw new ArgumentException("The event does not contain native pointer input.", nameof(input));
        if (service is not IPortableNativePointerInputService pointerService)
            throw new PlatformNotSupportedException("The source provider does not support native pointer input.");

        bool accepted = presentationSource
            ? pointerService.TryProcessPresentationSourceNativePointerInputEvent(target, native, (int)input.Modifiers, out bool handled)
            : pointerService.TryProcessNativePointerInputEvent(target, native, (int)input.Modifiers, out handled);
        if (!accepted)
            throw new InvalidOperationException("The source provider rejected native pointer input.");
        input.Handled = handled;
        return true;
    }
}
