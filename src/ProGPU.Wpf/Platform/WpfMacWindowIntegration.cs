using System.Runtime.InteropServices;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Platform;

/// <summary>
/// Applies the macOS window integration request (title bar fusion, backdrop material, appearance)
/// to a live native window through its window controller. The mapping functions are pure so they
/// are tested without a window; see docs/macos-window-integration.md.
/// </summary>
internal static partial class WpfMacWindowIntegration
{
    private const int BackdropNone = 0;
    private const ulong StyleMaskFullScreen = 1UL << 14;
    private const ulong ViewWidthSizable = 2UL;
    private const ulong ViewHeightSizable = 16UL;
    private const long WindowBelow = -1;
    private const long ZoomButton = 2;
    // The standard gap AppKit leaves between the zoom button and a toolbar item.
    private const double TrafficLightGap = 8d;

    /// <summary>Whether a backdrop request needs a transparent framebuffer at window creation.</summary>
    internal static bool RequiresTransparentFramebuffer(int backdrop) => backdrop > BackdropNone;

    internal static NativeWindowBackdrop MapBackdrop(int backdrop) => backdrop switch
    {
        1 => NativeWindowBackdrop.Transparent,
        2 => NativeWindowBackdrop.Blur,
        3 => NativeWindowBackdrop.Acrylic,
        4 => NativeWindowBackdrop.Mica,
        5 => NativeWindowBackdrop.MicaAlt,
        _ => NativeWindowBackdrop.None,
    };

    /// <summary>NSVisualEffectMaterial raw value for a backdrop code (same materials as the backend enum).</summary>
    internal static long MapMaterial(int backdrop) => backdrop switch
    {
        2 => 12, // windowBackground
        3 => 18, // contentBackground
        4 => 21, // underWindowBackground
        5 => 22, // underPageBackground
        _ => 0,
    };

    internal static NativeWindowTheme MapAppearance(int appearance) => appearance switch
    {
        1 => NativeWindowTheme.Light,
        2 => NativeWindowTheme.Dark,
        _ => NativeWindowTheme.Default,
    };

    /// <summary>NaN means "system default"; the controller uses a negative height for that.</summary>
    internal static double MapTitleBarHeight(double height) =>
        double.IsFinite(height) && height > 0d ? height : -1d;

    /// <summary>
    /// Applies the request. <paramref name="appliedBackdrop"/> is the backdrop code last applied by
    /// this method (0 initially) so a window created transparent by AllowsTransparency is not made
    /// opaque by a request that never asked for a backdrop.
    /// </summary>
    internal static PortableMacWindowResult? Apply(
        SilkWindowController? controller,
        bool transparentFramebuffer,
        PortableMacWindowRequest request,
        ref int appliedBackdrop,
        ref nint effectView)
    {
        if (!OperatingSystem.IsMacOS() ||
            controller == null ||
            controller.Handle.Kind != NativeWindowKind.Cocoa ||
            !controller.Handle.IsValid)
        {
            return null;
        }

        controller.SetClientAreaExtension(
            request.ExtendsContentIntoTitleBar,
            MapTitleBarHeight(request.TitleBarHeight));
        controller.SetTheme(MapAppearance(request.Appearance));

        int actualBackdrop = BackdropNone;
        if (request.Backdrop != BackdropNone || appliedBackdrop != BackdropNone)
        {
            // A material needs the framebuffer to have been created transparent.
            bool canApply = request.Backdrop == BackdropNone || transparentFramebuffer;
            if (canApply)
            {
                RemoveEffectView(ref effectView);

                // The controller makes the window non-opaque with a clear background (Transparent) or
                // opaque again (None). The material itself is added here: the controller's own
                // NSVisualEffectView is a subview of the content view, which is layer-hosting the
                // rendered frame, so it would cover the window content.
                var controllerBackdrop = request.Backdrop >= 2 ? NativeWindowBackdrop.Transparent : MapBackdrop(request.Backdrop);
                if (controller.SetBackdrop(controllerBackdrop))
                {
                    if (request.Backdrop >= 2)
                    {
                        if (TryAddEffectView(controller.Handle.Handle, MapMaterial(request.Backdrop), out effectView))
                        {
                            actualBackdrop = request.Backdrop;
                        }
                        else
                        {
                            controller.SetBackdrop(NativeWindowBackdrop.Transparent);
                        }
                    }
                    else
                    {
                        actualBackdrop = request.Backdrop;
                    }

                    appliedBackdrop = actualBackdrop;
                }
            }
        }

        double left = 0d;
        double top = 0d;
        if (request.ExtendsContentIntoTitleBar && !IsFullScreen(controller.Handle.Handle))
        {
            top = controller.ExtendedTitleBarHeight;
            left = TryGetTrafficLightInset(controller.Handle.Handle, out double inset) ? inset : 0d;
        }

        return new PortableMacWindowResult(actualBackdrop, left, top);
    }

    /// <summary>
    /// Adds a behind-window NSVisualEffectView to the window's frame view, below the content view,
    /// so the rendered content stays on top of the material.
    /// </summary>
    private static bool TryAddEffectView(nint nsWindow, long material, out nint effectView)
    {
        effectView = 0;
        if (nsWindow == 0)
        {
            return false;
        }

        nint contentView = SendObject(nsWindow, Selector("contentView"));
        nint frameView = contentView == 0 ? 0 : SendObject(contentView, Selector("superview"));
        nint effectClass = GetClass("NSVisualEffectView");
        if (contentView == 0 || frameView == 0 || effectClass == 0)
        {
            return false;
        }

        CGRect bounds = SendRect(frameView, Selector("bounds"));
        nint view = SendObjectRect(SendObject(effectClass, Selector("alloc")), Selector("initWithFrame:"), bounds);
        if (view == 0)
        {
            return false;
        }

        SendVoidUnsigned(view, Selector("setAutoresizingMask:"), ViewWidthSizable | ViewHeightSizable);
        SendVoidLong(view, Selector("setBlendingMode:"), 0);
        SendVoidLong(view, Selector("setState:"), 1);
        SendVoidLong(view, Selector("setMaterial:"), material);
        SendVoidObjectLongObject(frameView, Selector("addSubview:positioned:relativeTo:"), view, WindowBelow, contentView);
        // addSubview retained it; drop the allocation reference. The frame view now owns the view.
        SendVoid(view, Selector("release"));
        effectView = view;
        return true;
    }

    private static void RemoveEffectView(ref nint effectView)
    {
        if (effectView != 0)
        {
            SendVoid(effectView, Selector("removeFromSuperview"));
            effectView = 0;
        }
    }

    /// <summary>
    /// The click count AppKit assigned to the mouse event being dispatched (the system double-click
    /// interval and distance apply, and a drag does not count), or 0 off macOS or without an event.
    /// </summary>
    internal static int GetCurrentClickCount()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return 0;
        }

        nint application = SendObject(GetClass("NSApplication"), Selector("sharedApplication"));
        nint current = application == 0 ? 0 : SendObject(application, Selector("currentEvent"));
        return current == 0 ? 0 : (int)Math.Clamp((long)SendUnsigned(current, Selector("clickCount")), 0L, int.MaxValue);
    }

    private static bool IsFullScreen(nint nsWindow) =>
        nsWindow != 0 && (SendUnsigned(nsWindow, Selector("styleMask")) & StyleMaskFullScreen) != 0;

    /// <summary>Whether the controller's Cocoa window is in macOS full screen (false off macOS).</summary>
    internal static bool IsFullScreen(SilkWindowController? controller) =>
        OperatingSystem.IsMacOS() &&
        controller != null &&
        controller.Handle.Kind == NativeWindowKind.Cocoa &&
        controller.Handle.IsValid &&
        IsFullScreen(controller.Handle.Handle);

    /// <summary>
    /// The right edge of the zoom button plus the standard gap, measured from the live button frame
    /// rather than assumed (the buttons moved between macOS releases).
    /// </summary>
    private static bool TryGetTrafficLightInset(nint nsWindow, out double inset)
    {
        inset = 0d;
        if (nsWindow == 0)
        {
            return false;
        }

        nint zoom = SendObjectLong(nsWindow, Selector("standardWindowButton:"), ZoomButton);
        if (zoom == 0)
        {
            return false;
        }

        CGRect frame = SendRect(zoom, Selector("frame"));
        double right = frame.X + frame.Width;
        if (!double.IsFinite(right) || right <= 0d)
        {
            return false;
        }

        inset = right + TrafficLightGap;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    private static nint Selector(string name) => RegisterName(name);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RegisterName(string name);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nuint SendUnsigned(nint receiver, nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint SendObject(nint receiver, nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint SendObjectRect(nint receiver, nint selector, CGRect rect);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoid(nint receiver, nint selector);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoidLong(nint receiver, nint selector, long value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoidUnsigned(nint receiver, nint selector, ulong value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial void SendVoidObjectLongObject(nint receiver, nint selector, nint first, long second, nint third);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial nint SendObjectLong(nint receiver, nint selector, long value);

    [LibraryImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static partial CGRect SendRect(nint receiver, nint selector);
}
