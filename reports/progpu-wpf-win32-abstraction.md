# ProGPU WPF Win32 Abstraction Plan

## Rule

The ProGPU port lane should not add new direct Win32 calls. Existing WPF Win32 calls must either move to Silk.NET/ProGPU where they are rendering or windowing concerns, or move behind local platform interfaces where they are OS services.

## Initial Interfaces

`src/ProGPU.Wpf/Platform` introduces:

- `IWpfClipboard`
- `IWpfCursorService`
- `IWpfDispatcherService`
- `IWpfDispatcherOperation`
- `IWpfDragDropService`
- `IWpfFileDialogService`
- `IWpfInputService`
- `IWpfLauncher`
- `IWpfMonitorService`
- `IWpfPlatformServices`
- `IWpfTimerService`
- `IWpfTimer`
- `IWpfWindowEventService`
- `CrossPlatformWpfPlatformServices`
- `ProcessWpfFileDialogService`
- `ProcessWpfLauncher`
- `QueuedWpfDispatcherService`
- `SilkNetWpfCursorService`
- `SilkNetWpfDragDropService`
- `SilkNetWpfInputService`
- `SilkNetWpfMonitorService`
- `SilkNetWpfWindowEventService`
- `ThreadPoolWpfTimerService`

The default host implementation uses `CrossPlatformWpfPlatformServices`. Shell launching is implemented with `ProcessStartInfo.UseShellExecute`, which is the portable .NET replacement for simple `ShellExecute`-style URI and file open requests. Clipboard text access is implemented by `ProcessWpfClipboard` behind `IWpfClipboard`, selecting `pbcopy`/`pbpaste` on macOS, PowerShell `Set-Clipboard`/`Get-Clipboard` on Windows, and `wl-copy`/`wl-paste` with `xclip`/`xsel` fallbacks on Linux. File open, file save, and folder pickers are implemented by `ProcessWpfFileDialogService` behind `IWpfFileDialogService`, selecting `osascript` on macOS, PowerShell Windows Forms dialogs on Windows, and `zenity` on Linux. Low-level key, text, mouse, and wheel input is implemented by `SilkNetWpfInputService` behind `IWpfInputService`, with `ProGpuWpfWindowHost` forwarding normalized events through `InputReceived`. Cursor updates are implemented by `SilkNetWpfCursorService` behind `IWpfCursorService`, mapping WPF-shaped cursor names to Silk.NET `StandardCursor` values. Dispatcher posting is implemented by `QueuedWpfDispatcherService` behind `IWpfDispatcherService`, preserving owner-thread processing and WPF-shaped priority ordering while letting the Silk.NET host pump pending work explicitly. `DispatcherWpfRenderScheduler` maps coalesced render requests onto a one-shot timer plus `Render` dispatcher-priority callback, and `IWpfRenderScheduler` feeds the host's guarded scheduler wakeup path and `ProGpuWpfFrameState` gate so unchanged retained frames can be skipped at the Silk.NET render boundary when no explicit draw callback or pending render request requires work. Portable drop payload intake is implemented by `SilkNetWpfDragDropService` behind `IWpfDragDropService`, mapping Silk.NET file drops into `WpfDragDropEventArgs` and forwarding them through `ProGpuWpfWindowHost.DragDropReceived`. Activation/deactivation and external file-drop intake is implemented by `SilkNetWpfWindowEventService` behind `IWpfWindowEventService`, with the host forwarding normalized events through `WindowEventReceived`. Monitor enumeration is implemented through Silk.NET `IWindowPlatform.GetMonitors`, mapping `IMonitor` bounds, primary identity, reflected DPI/content-scale metadata, and video-mode-to-bounds scale into `WpfMonitorInfo`. Timer creation is implemented by `ThreadPoolWpfTimerService` behind `IWpfTimerService`, using `System.Threading.Timer` for portable one-shot and repeating timers.

## Mapping

- HWND creation, render target ownership, resize, frame coalescing, and present: Silk.NET + `ProGpuWpfWindowHost`.
- D3D device/swapchain: ProGPU `WgpuContext`.
- MIL composition target: `ProGpuWpfCompositionTarget`.
- Drawing commands: `IWpfCompositionCommandSink`, backed by `ProGpuCompositionCommandSink`.
- WPF-shaped Viewport3D offscreen rendering: `WpfViewport3DReflectionBridge` compiles reflected 3D visual/model/mesh data into ProGPU's `Mesh3D` extension and composites the cached offscreen texture through the normal 2D command stream.
- File dialogs: `ProcessWpfFileDialogService`, with a later option to replace process-backed commands with native per-OS APIs behind the same `IWpfFileDialogService` boundary.
- Clipboard: `ProcessWpfClipboard`, with a later option to replace process-backed commands with native per-OS APIs behind the same `IWpfClipboard` boundary.
- Input: `SilkNetWpfInputService` for low-level keyboard, text, mouse, and wheel events; WPF input manager routing remains a higher-level porting step.
- Drag/drop: `SilkNetWpfDragDropService` for external file-drop payloads surfaced as WPF-shaped drop events; full WPF routed drag/drop, drag enter/over/leave, data-object negotiation, and effects remain higher-level porting work.
- Activation and file drop: `SilkNetWpfWindowEventService` for focus-derived activation/deactivation and external file-drop window events.
- Monitor/DPI: `SilkNetWpfMonitorService` for monitor bounds, primary monitor identity, reflected per-monitor scale, and video-mode-to-bounds DPI inference; Silk.NET framebuffer/logical-size data remains the active frame DPI scale.
- Cursor: `SilkNetWpfCursorService` maps WPF-shaped cursors onto Silk.NET `StandardCursor` values and applies them through Silk.NET mouse cursor objects.
- Dispatcher posting and render scheduling: `QueuedWpfDispatcherService` queues callbacks by WPF-shaped priority and processes them on the owning thread from `ProGpuWpfWindowHost.ProcessDispatcherQueue`, `DoEvents`, and render callbacks; `DispatcherWpfRenderScheduler` uses `IWpfTimerService` plus `WpfDispatcherPriority.Render` to preserve WPF-style invalidation requests until the host renders or its unchanged-frame gate can safely skip work, and the host observes scheduler wakeups to request a non-reentrant Silk.NET render pass when it is already on the dispatcher owner thread.
- Timers: `ThreadPoolWpfTimerService` for portable one-shot and repeating timers used by render scheduling, future MediaContext animation ticks, hover, and dispatcher scheduling bridges.
- Shell launch: `ProcessWpfLauncher`, using local OS shell association through `ProcessStartInfo.UseShellExecute`.

## Next Steps

1. Harden the process-backed clipboard and file dialog services or replace them with native per-OS implementations behind the same interfaces.
2. Add WPF input manager routing, IME/stylus handling, full routed drag/drop over `IWpfDragDropService`, map real WPF `Dispatcher`/`DispatcherTimer`/`MediaContext` onto the queued dispatcher and timer boundaries, deepen native event-loop wakeups beyond the current guarded host `DoRender()` bridge, and deepen activation integration behind local interfaces.
3. Replace `MS.Win32` usages in rendering-adjacent code with the service interfaces.
4. Keep Windows-specific compatibility behavior isolated in the Windows implementation.
