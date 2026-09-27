namespace System.Windows.Media.ProGPU;

internal static class ProGpuWpfResizePolicy
{
    internal static bool ShouldRenderInline(bool isRendering, bool isSettingNativeSize,
        bool isWindows, bool isInteractiveMoveResize)
    {
        // Do not nest presentation inside an acquired frame or native size
        // setter. Win32 exposes its actual modal size/move loop through the
        // shared controller; ordinary WM_SIZE does not imply that loop. Other
        // platforms retain their existing native live-resize callback behavior.
        return !isRendering && !isSettingNativeSize &&
            (!isWindows || isInteractiveMoveResize);
    }
}
