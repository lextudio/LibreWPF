using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Silk.NET.Core.Contexts;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class SilkNetWpfWindowDecorationService : IWpfWindowDecorationService
{
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

        return false;
    }

    private static IntPtr GetWin32Hwnd(IView view)
    {
        if (view is not INativeWindowSource nativeWindowSource)
        {
            return IntPtr.Zero;
        }

        var nativeWindow = nativeWindowSource.Native;
        if (nativeWindow == null)
        {
            return IntPtr.Zero;
        }

        var win32 = nativeWindow.Win32;
        return win32.HasValue ? win32.Value.Item2 : IntPtr.Zero;
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
