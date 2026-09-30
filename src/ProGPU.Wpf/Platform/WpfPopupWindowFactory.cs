using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

internal static class WpfPopupWindowFactory
{
    internal static IWindow Create(bool isPopup, bool hasSource, NativeWindowHandle owner,
        IPortableWindowActivationServiceRegistrar? sourceService,
        Func<IWindow> createStandard, Func<IWindow> createOwnedCocoa)
    {
        // Source admission and actual owner identity are independent gates. A
        // Cocoa process alone does not prove that this source can consume the
        // owned view's native input. Ordinary windows never use the popup factory.
        if (!isPopup || !hasSource || sourceService is not IPortableNativePointerInputService ||
            owner.Kind != NativeWindowKind.Cocoa)
            return createStandard();

        if (!owner.IsValid)
            throw new InvalidOperationException("An owned Cocoa popup requires a live native owner identity.");

        // Preserve creation/initialization failures; an ordinary NSWindow is not
        // a replacement for a source-admitted owned NSPanel.
        return createOwnedCocoa();
    }
}
