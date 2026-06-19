using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Silk.NET.Core.Contexts;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class SilkNetWpfWindowDecorationService : IWpfWindowDecorationService
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_LBUTTONUP = 0x0202;
    private const int SC_MOUSEMOVE = 0xF012;

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport(ObjCLibrary, EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjCGetClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjCLibrary, EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjCMsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void ObjCMsgSend(IntPtr receiver, IntPtr selector, IntPtr argument);
}
