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

Real WPF `MediaContextNotificationWindow` now keeps its hidden HWND and DUCE window-message notification setup Windows-only. On non-Windows it preserves construction/disposal shape but skips `RegisterWindowMessage`, hidden `HwndWrapper` creation, MILCore DWM content attach/detach, and channel `SetNotificationWindow(...)`. Real WPF `MediaContext` also routes performance-counter access through a local clock boundary: Windows keeps QPC, while non-Windows uses `Stopwatch` counts and frequency. Real WPF `MediaSystem` keeps media-context lifetime bookkeeping on non-Windows but returns disconnected before MILCore version checks, partition-manager startup, DUCE transport/channel creation, RDP hardware toggle setup, redirection notifications, and partition-manager shutdown. This is a transition boundary: ProGPU/Silk.NET scheduling uses `DispatcherWpfRenderScheduler` and host render wakeups today, while full in-assembly `MediaContext` integration still needs a portable channel notification source instead of DUCE HWND messages.

Real WPF `HwndTarget` remains the Win32 composition target, but its static window-message registration is now Windows-only and non-Windows construction fails before session, HWND, or MIL initialization. This keeps the public type loadable while making the remaining replacement boundary explicit: non-Windows window ownership must come from a ProGPU/Silk.NET composition target rather than an HWND-backed target. Real WPF `PortableCompositionTarget` is the first in-assembly non-HWND target boundary for that replacement: it owns a root visual and device transforms without DUCE resource creation, and `CompositionTarget` now gates DUCE root cleanup behind `UsesDuceComposition`. Real WPF `PortablePresentationSource` wraps that target with `PresentationSource` root/source tracking and render requests, and `ProGpuWpfWindowHost` now owns `WpfPortablePresentationSourceBridge` attachment through `TryCreatePortablePresentationSource(...)`/`TryBindPortablePresentationSource(...)` until `ProGPU.Wpf` and real `PresentationCore` type identity are unified. `WpfPortableWindowActivation` is the current bootstrap layer above that bridge: it derives host title/size options from WPF-shaped `Window` properties and binds the window object itself as the portable source root visual until real WPF lifetime code can call the path directly.

## Mapping

- HWND creation, render target ownership, resize, frame coalescing, and present: Silk.NET + `ProGpuWpfWindowHost`.
- Real WPF `HwndTarget`: Windows-only HWND composition target; non-Windows type loading is safe, but construction fails fast until a Silk.NET-backed `CompositionTarget` replaces it.
- Real WPF `PortableCompositionTarget`: non-HWND managed root/device-transform target; no DUCE resource creation; intended base for the Silk.NET/ProGPU-backed presentation source.
- Real WPF `PortablePresentationSource`: non-HWND `PresentationSource` that owns `PortableCompositionTarget`, participates in current-source tracking, raises root/source changes, and emits render requests without entering the DUCE render loop.
- `WpfPortablePresentationSourceBridge`: reflection adapter that creates or binds the real portable source, mirrors its root into `ProGpuWpfWindowHost.WpfRootVisual`, forwards device scale, and maps source render requests into the Silk.NET host scheduler. The host owns bridge lifetime, replacement, root sync, and per-frame DPI updates.
- `WpfPortableWindowActivation`: transition bootstrap that derives `ProGpuWpfWindowOptions` from WPF-shaped window `Title`/size properties, attaches the host-owned portable source, and uses the top-level window as the root visual.
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
- MediaContext notifications: Windows keeps the original hidden HWND notification window; non-Windows `MediaContextNotificationWindow` is a no-op until DUCE channel messages are replaced by a ProGPU/Silk.NET notification source.
- MediaContext clock: Windows keeps `QueryPerformanceFrequency`/`QueryPerformanceCounter`; non-Windows uses `Stopwatch.Frequency`/`Stopwatch.GetTimestamp()` behind the same WPF count-to-tick conversion helpers.
- MediaSystem/MIL transport: Windows keeps MILCore partition management and DUCE transport/channel creation; non-Windows keeps lifetime bookkeeping but skips MIL startup/shutdown and channel creation until a ProGPU/Silk.NET transport is wired.
- Timers: `ThreadPoolWpfTimerService` for portable one-shot and repeating timers used by render scheduling, future MediaContext animation ticks, hover, and dispatcher scheduling bridges.
- Shell launch: `ProcessWpfLauncher`, using local OS shell association through `ProcessStartInfo.UseShellExecute`.

## Next Steps

1. Harden the process-backed clipboard and file dialog services or replace them with native per-OS implementations behind the same interfaces.
2. Wire `WpfPortableWindowActivation` into the real non-Windows `Window.Show` and `Application.Run` startup path so Silk.NET size, DPI, input, and ProGPU presentation are created by WPF lifetime rather than test or host bootstrap code.
3. Add WPF input manager routing, IME/stylus handling, full routed drag/drop over `IWpfDragDropService`, map real WPF `Dispatcher`/`DispatcherTimer`/`MediaContext` channel notifications and animation ticks onto the queued dispatcher and timer boundaries, deepen native event-loop wakeups beyond the current guarded host `DoRender()` bridge, and deepen activation integration behind local interfaces.
4. Replace `MS.Win32` usages in rendering-adjacent code with the service interfaces.
5. Keep Windows-specific compatibility behavior isolated in the Windows implementation.
