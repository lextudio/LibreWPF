using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Silk.NET.Core.Contexts;
using Silk.NET.GLFW;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

public sealed unsafe class SilkNetWpfWindowDecorationService : IWpfWindowDecorationService
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string X11Library = "libX11.so.6";
    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_LBUTTONUP = 0x0202;
    private const int SC_MOUSEMOVE = 0xF012;
    private const int ClientMessage = 33;
    private const int NetWmMoveresizeMove = 8;
    private const int NormalApplicationSource = 1;
    private const int PropModeReplace = 0;
    private const nuint XaAtom = 4;
    private const long SubstructureNotifyMask = 1L << 19;
    private const long SubstructureRedirectMask = 1L << 20;
    private const nuint CWOverrideRedirect = 1u << 9;

    public bool TryBeginDragMove(object window)
    {
        if (window is not IView view || view.Handle == IntPtr.Zero)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return TryBeginWin32DragMove(GetWin32Hwnd(view));
        }

        if (OperatingSystem.IsMacOS())
        {
            return TryBeginCocoaDragMove(GetCocoaWindow(view));
        }

        if (OperatingSystem.IsLinux())
        {
            var x11 = GetX11Window(view);
            return TryBeginX11DragMove(x11.Display, x11.Window);
        }

        return false;
    }

    public bool TryShowWithoutActivation(object window)
    {
        if (window is not IWindow view)
        {
            return false;
        }

        if (OperatingSystem.IsMacOS())
        {
            return TryShowCocoaWithoutActivation(GetCocoaWindow(view)) ||
                TryShowGlfwWithoutActivation(view);
        }

        return TryShowGlfwWithoutActivation(view);
    }

    public bool TryActivate(object window)
    {
        if (window is not IWindow view)
        {
            return false;
        }

        if (OperatingSystem.IsMacOS())
        {
            // Cocoa ordering and GLFW focus are complementary here. Ordering
            // makes the NSWindow key while GLFW updates its cross-platform
            // focused-window state and emits the matching focus callback.
            bool cocoaActivated = TryActivateCocoaWindow(GetCocoaWindow(view));
            bool glfwActivated = TryActivateGlfwWindow(view);
            return cocoaActivated || glfwActivated;
        }

        if (OperatingSystem.IsWindows() && TryActivateWin32Window(GetWin32Hwnd(view)))
        {
            return true;
        }

        return TryActivateGlfwWindow(view);
    }

    public bool TryConfigurePopupOwner(object ownerWindow, object popupWindow)
    {
        if (ownerWindow is not IView ownerView || popupWindow is not IView popupView)
        {
            return false;
        }

        if (OperatingSystem.IsMacOS())
        {
            return TryConfigureCocoaPopupOwner(GetCocoaWindow(ownerView), GetCocoaWindow(popupView));
        }

        if (OperatingSystem.IsLinux())
        {
            var owner = GetX11Window(ownerView);
            var popup = GetX11Window(popupView);
            return TryConfigureX11PopupOwner(owner, popup);
        }

        return false;
    }

    private static INativeWindow? GetNativeWindow(IView view)
    {
        if (view is not INativeWindowSource nativeWindowSource)
        {
            return null;
        }

        return nativeWindowSource.Native;
    }

    private static IntPtr GetWin32Hwnd(IView view)
    {
        var nativeWindow = GetNativeWindow(view);
        if (nativeWindow == null)
        {
            return IntPtr.Zero;
        }

        var win32 = nativeWindow.Win32;
        return win32.HasValue ? win32.Value.Item2 : IntPtr.Zero;
    }

    private static IntPtr GetCocoaWindow(IView view)
    {
        var nativeWindow = GetNativeWindow(view);
        if (nativeWindow == null)
        {
            return IntPtr.Zero;
        }

        var cocoa = nativeWindow.Cocoa;
        return cocoa.GetValueOrDefault();
    }

    private static bool TryShowGlfwWithoutActivation(IWindow view)
    {
        var nativeWindow = GetNativeWindow(view);
        var glfwWindow = (WindowHandle*)(nativeWindow?.Glfw ?? IntPtr.Zero);
        if (glfwWindow == null)
        {
            return false;
        }

        try
        {
            var glfw = Glfw.GetApi();
            glfw.SetWindowAttrib(
                glfwWindow,
                WindowAttributeSetter.FocusOnShow,
                false);
            try
            {
                view.IsVisible = true;
            }
            finally
            {
                glfw.SetWindowAttrib(
                    glfwWindow,
                    WindowAttributeSetter.FocusOnShow,
                    true);
            }

            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool TryActivateGlfwWindow(IWindow view)
    {
        var nativeWindow = GetNativeWindow(view);
        var glfwWindow = (WindowHandle*)(nativeWindow?.Glfw ?? IntPtr.Zero);
        if (glfwWindow == null)
        {
            return false;
        }

        try
        {
            Glfw.GetApi().FocusWindow(glfwWindow);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static X11WindowHandle GetX11Window(IView view)
    {
        var nativeWindow = GetNativeWindow(view);
        if (nativeWindow == null)
        {
            return default;
        }

        var x11 = nativeWindow.X11;
        return x11.HasValue
            ? new X11WindowHandle(x11.Value.Item1, x11.Value.Item2)
            : default;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryBeginWin32DragMove(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            ReleaseCapture();
            SendMessage(hwnd, WM_SYSCOMMAND, (IntPtr)SC_MOUSEMOVE, IntPtr.Zero);
            SendMessage(hwnd, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryActivateWin32Window(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return SetForegroundWindow(hwnd);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("macos")]
    private static bool TryShowCocoaWithoutActivation(IntPtr nsWindow)
    {
        if (nsWindow == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            IntPtr orderFront = SelRegisterName("orderFront:");
            if (orderFront == IntPtr.Zero)
            {
                return false;
            }

            ObjCMsgSend(nsWindow, orderFront, IntPtr.Zero);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("macos")]
    private static bool TryActivateCocoaWindow(IntPtr nsWindow)
    {
        if (nsWindow == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            IntPtr makeKeyAndOrderFront = SelRegisterName("makeKeyAndOrderFront:");
            if (makeKeyAndOrderFront == IntPtr.Zero)
            {
                return false;
            }

            ObjCMsgSend(nsWindow, makeKeyAndOrderFront, IntPtr.Zero);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("macos")]
    private static bool TryBeginCocoaDragMove(IntPtr nsWindow)
    {
        if (nsWindow == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var nsApplicationClass = ObjCGetClass("NSApplication");
            var sharedApplicationSelector = SelRegisterName("sharedApplication");
            var currentEventSelector = SelRegisterName("currentEvent");
            var performDragSelector = SelRegisterName("performWindowDragWithEvent:");
            if (nsApplicationClass == IntPtr.Zero ||
                sharedApplicationSelector == IntPtr.Zero ||
                currentEventSelector == IntPtr.Zero ||
                performDragSelector == IntPtr.Zero)
            {
                return false;
            }

            var nsApplication = ObjCMsgSend(nsApplicationClass, sharedApplicationSelector);
            if (nsApplication == IntPtr.Zero)
            {
                return false;
            }

            var currentEvent = ObjCMsgSend(nsApplication, currentEventSelector);
            if (currentEvent == IntPtr.Zero)
            {
                return false;
            }

            ObjCMsgSend(nsWindow, performDragSelector, currentEvent);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("macos")]
    private static bool TryConfigureCocoaPopupOwner(IntPtr ownerWindow, IntPtr popupWindow)
    {
        if (ownerWindow == IntPtr.Zero || popupWindow == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            IntPtr addChildWindow = SelRegisterName("addChildWindow:ordered:");
            IntPtr setHidesOnDeactivate = SelRegisterName("setHidesOnDeactivate:");
            if (addChildWindow == IntPtr.Zero || setHidesOnDeactivate == IntPtr.Zero)
            {
                return false;
            }

            ObjCMsgSend(popupWindow, setHidesOnDeactivate, false);
            ObjCMsgSend(ownerWindow, addChildWindow, popupWindow, 1);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("linux")]
    private static bool TryConfigureX11PopupOwner(X11WindowHandle owner, X11WindowHandle popup)
    {
        if (owner.Display == IntPtr.Zero || owner.Window == UIntPtr.Zero ||
            popup.Display == IntPtr.Zero || popup.Window == UIntPtr.Zero ||
            owner.Display != popup.Display)
        {
            return false;
        }

        try
        {
            bool configured = XSetTransientForHint(owner.Display, popup.Window, owner.Window) != 0;
            // WPF computes popup placement in device-screen coordinates. A managed
            // X11 toplevel lets Mutter/KWin/WSLg reposition a menu after mapping,
            // which makes its visual and input bounds diverge. Native X11 menus use
            // override-redirect for the same reason: the owner controls placement,
            // while the transient/type hints still describe lifetime and semantics.
            var attributes = new XSetWindowAttributes
            {
                OverrideRedirect = 1
            };
            configured |= XChangeWindowAttributes(
                owner.Display,
                popup.Window,
                CWOverrideRedirect,
                ref attributes) != 0;
            var windowType = XInternAtom(
                owner.Display,
                "_NET_WM_WINDOW_TYPE",
                onlyIfExists: false);
            var dropdownMenuType = XInternAtom(
                owner.Display,
                "_NET_WM_WINDOW_TYPE_DROPDOWN_MENU",
                onlyIfExists: false);
            var popupMenuType = XInternAtom(
                owner.Display,
                "_NET_WM_WINDOW_TYPE_POPUP_MENU",
                onlyIfExists: false);
            if (windowType != UIntPtr.Zero &&
                dropdownMenuType != UIntPtr.Zero &&
                popupMenuType != UIntPtr.Zero)
            {
                UIntPtr* popupTypes = stackalloc UIntPtr[2];
                popupTypes[0] = dropdownMenuType;
                popupTypes[1] = popupMenuType;
                _ = XChangeProperty(
                    owner.Display,
                    popup.Window,
                    windowType,
                    (UIntPtr)XaAtom,
                    format: 32,
                    PropModeReplace,
                    (byte*)popupTypes,
                    elementCount: 2);
            }

            XFlush(owner.Display);
            return configured;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("linux")]
    private static bool TryBeginX11DragMove(IntPtr display, UIntPtr window)
    {
        if (display == IntPtr.Zero || window == UIntPtr.Zero)
        {
            return false;
        }

        try
        {
            var root = XDefaultRootWindow(display);
            if (root == UIntPtr.Zero)
            {
                return false;
            }

            if (XQueryPointer(
                    display,
                    root,
                    out _,
                    out _,
                    out var rootX,
                    out var rootY,
                    out _,
                    out _,
                    out _) == 0)
            {
                return false;
            }

            var moveresizeAtom = XInternAtom(display, "_NET_WM_MOVERESIZE", onlyIfExists: false);
            if (moveresizeAtom == UIntPtr.Zero)
            {
                return false;
            }

            XUngrabPointer(display, UIntPtr.Zero);

            var message = new XClientMessageEvent
            {
                Type = ClientMessage,
                SendEvent = true,
                Display = display,
                Window = window,
                MessageType = moveresizeAtom,
                Format = 32,
                Data0 = rootX,
                Data1 = rootY,
                Data2 = NetWmMoveresizeMove,
                Data3 = 1,
                Data4 = NormalApplicationSource
            };

            var sent = XSendEvent(
                display,
                root,
                propagate: false,
                SubstructureRedirectMask | SubstructureNotifyMask,
                ref message) != 0;

            XFlush(display);
            return sent;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport(ObjCLibrary, EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjCGetClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjCLibrary, EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjCMsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void ObjCMsgSend(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void ObjCMsgSend(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.Bool)] bool argument);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void ObjCMsgSend(IntPtr receiver, IntPtr selector, IntPtr argument, long orderingMode);

    [DllImport(X11Library)]
    private static extern UIntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(X11Library, CharSet = CharSet.Ansi)]
    private static extern UIntPtr XInternAtom(
        IntPtr display,
        string atomName,
        [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [DllImport(X11Library)]
    private static extern int XQueryPointer(
        IntPtr display,
        UIntPtr window,
        out UIntPtr rootReturn,
        out UIntPtr childReturn,
        out int rootXReturn,
        out int rootYReturn,
        out int winXReturn,
        out int winYReturn,
        out uint maskReturn);

    [DllImport(X11Library)]
    private static extern int XUngrabPointer(IntPtr display, UIntPtr time);

    [DllImport(X11Library)]
    private static extern int XSendEvent(
        IntPtr display,
        UIntPtr window,
        [MarshalAs(UnmanagedType.Bool)] bool propagate,
        long eventMask,
        ref XClientMessageEvent eventSend);

    [DllImport(X11Library)]
    private static extern int XFlush(IntPtr display);

    [DllImport(X11Library)]
    private static extern int XSetTransientForHint(IntPtr display, UIntPtr window, UIntPtr ownerWindow);

    [DllImport(X11Library)]
    private static extern int XChangeWindowAttributes(
        IntPtr display,
        UIntPtr window,
        nuint valueMask,
        ref XSetWindowAttributes attributes);

    [DllImport(X11Library)]
    private static extern int XChangeProperty(
        IntPtr display,
        UIntPtr window,
        UIntPtr property,
        UIntPtr type,
        int format,
        int mode,
        byte* data,
        int elementCount);

    private readonly record struct X11WindowHandle(IntPtr Display, UIntPtr Window);

    [StructLayout(LayoutKind.Sequential)]
    private struct XSetWindowAttributes
    {
        public UIntPtr BackgroundPixmap;
        public UIntPtr BackgroundPixel;
        public UIntPtr BorderPixmap;
        public UIntPtr BorderPixel;
        public int BitGravity;
        public int WinGravity;
        public int BackingStore;
        public UIntPtr BackingPlanes;
        public UIntPtr BackingPixel;
        public int SaveUnder;
        public nint EventMask;
        public nint DoNotPropagateMask;
        public int OverrideRedirect;
        public UIntPtr Colormap;
        public UIntPtr Cursor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XClientMessageEvent
    {
        public int Type;
        public IntPtr Serial;

        [MarshalAs(UnmanagedType.Bool)]
        public bool SendEvent;

        public IntPtr Display;
        public UIntPtr Window;
        public UIntPtr MessageType;
        public int Format;
        public long Data0;
        public long Data1;
        public long Data2;
        public long Data3;
        public long Data4;
    }
}
