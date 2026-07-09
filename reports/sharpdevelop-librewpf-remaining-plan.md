# SharpDevelop LibreWPF Remaining Plan

Date: 2026-07-09

## Current finalized state

The current SharpDevelop slice is closed at package-mode build parity plus broad popup/hosted-control/AvalonDock smoke coverage. `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj` builds in Release from a fresh NuGet cache against `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal` with `287` warnings and `0` errors after refreshing the local SharpDevelop feed; the focused post-restore rebuild currently reports `39` warnings and `0` errors.

Reusable fixes now landed in LibreWPF, LibreWinForms, and ProGPU include menu/context/combo popup coverage, Core.Presentation toolbar dropdown smoke coverage, hosted WinForms `ContextMenuStrip` popup validation, AvalonEdit completion popup smoke support, AvalonDock dockable floating-window/context-menu/mode-toggle/redock/auto-hide/flyout smoke coverage, hosted WinForms TreeView/ListView/ComboBox owner drawing, DrawItem background/focus helpers, dialog/clipboard services, ImageList/TreeView icon rendering, typed portable `HwndSource` hook dispatch for activation, mouse activation, show/hide, basic window move/resize geometry messages, non-client/title-bar mouse hook dispatch, exact ProGPU scene geometry clipping for portable window regions, plus LibreWinForms preview package/release workflows on the default `librewinforms-progpu-port` branch. LibreWPF now tracks LibreWinForms submodule commit `2810c93a8`, which hardens package cleanup, expected-artifact validation, NuGet README metadata, release bundle contents, missing-artifact workflow behavior, and isolated current-version restore caches so stale user/global `LibreWPF.Interop` packages cannot shadow the bridge feed.

The latest popup/AvalonDock pass fixes the hosted WinForms context-menu crash by guarding source-less portable popup menu-mode pushes in `MenuBase.PushMenuMode(...)`, adds a real AvalonDock pad state smoke, and fixes portable `Window.Activate()` so shown non-Windows LibreWPF windows set activation through `PortableWindowActivationService` instead of falling into the HWND path. The broad SharpDevelop smoke now validates File menu, real AddInTree context menu, ComboBox popup, Core.Presentation toolbar dropdown, hosted WinForms `ContextMenuStrip`, AvalonEdit completion popup, AvalonDock `ProjectBrowserPad` float, floating-window context menu, docked pane options menu, dockable/floating mode toggle, redock, auto-hide/flyout creation, restore, property pad, ResX conversion, sample build, FormsDesigner load, and FormsDesigner mutation in one package-mode run. The only runtime log noise in this lane is the existing SharpDevelop `log4net` configuration warning.

The latest AvalonDock pass also adds a typed handle-based `PortableWindowRegion` route from SharpDevelop flyout windows into ProGPU. Direct `SetWindowRgn`/`CreateRectRgn`/`CombineRgn` use is now avoided on non-Windows for the active flyout update and open/close animation paths, and SharpDevelop startup remains alive after the change. The region DTO exclusion rectangles now feed a ProGPU vector path difference assigned to the scene root `GeometryClip`, so the same base-minus-exclusion region is enforced for rendering and GPU hit testing. The SharpDevelop-local AvalonDock smoke uses typed `LIBREWPF` helpers to observe flyout creation and source attachment; it does not use private-field reflection.

LibreWinForms repository packaging/default-branch status was rechecked on 2026-07-09. `wieslawsoltes/winforms` already reports `librewinforms-progpu-port` as default branch with the LibreWinForms description/topics, and `./eng/librewinforms-verify-docs.sh` succeeds, so no new WinForms commit was needed for that request.

The latest SharpDevelop crash pass fixed a LibreWPF host-loop shutdown bug instead of adding a SharpDevelop workaround. A macOS crash report showed the failed thread inside `glfwWindowShouldClose` after the broad smoke run finished. `ProGpuWpfWindowHost.Run()` now owns the portable native loop, pumps `DoEvents()`, exits on a LibreWPF-owned close-start flag, and avoids `IWindow.Run()`, `IWindow.IsClosing`, or trace-time native window property reads during shutdown. The follow-up package-coherence pass also refreshed every ProGPU package in the SharpDevelop-local feed after a stale same-version `ProGPU.Scene.dll` hid the current `Visual.GeometryClip` API and caused a runtime `MissingMethodException`. The refreshed ProGPU package set plus refreshed `LibreWPF.ProGPU` local package rebuild `SharpDevelop.Full.LibreWpf`, and the final owner-loop smoke exits with code `0` while validating menu/context/ComboBox/toolbar popups, hosted WinForms `ContextMenuStrip`, AvalonDock floating/context menu/redock/auto-hide/flyout, ProjectBrowser, property pad, FormsDesigner load/mutation, ResX, LineCounter build, and editor completion. No new ProGPU or LibreWinForms source changes were required for this slice.

The latest ResourceToolkit pass enables the LibreWPF add-in's `${res:...}` Find References, Rename Resource, Find Missing Resource Keys, and unused-key detection path against current SharpDevelop document/project/search-result APIs. This replaces the previous user-facing "not enabled" messages for that resolver family without reintroducing the old NRefactory v3 AST/refactoring path. The follow-up pass adds a generic typed `/SharpDevelop/LibreWpf/SmokeHooks` workbench extension point and a ResourceToolkit smoke hook enabled by `LIBREWPF_SHARPDEVELOP_RESOURCE_TOOLKIT_SMOKE`. The ResourceToolkit add-in now also keeps its historical NRefactory resolver and C#/VB resource-completion identities loadable through explicit LibreWPF no-op implementations while the unsupported AST-backed path remains disabled. `ResourceToolkit.LibreWpf.csproj` builds in Release with `1` warning and `0` errors, the ResourceToolkit-included `SharpDevelop.Full.LibreWpf.csproj` rebuilds with `39` warnings and `0` errors, the ResourceToolkit smoke reports `Success mode=WholeSolution files=8 references=0 missing=0 unused=0`, and editor completion still opens with `8` bindings and `12` items when ResourceToolkit is loaded.

The latest save/runtime pass covers SharpDevelop's normal workbench save command on LibreWPF/macOS. The `LIBREWPF` build now avoids the Windows-only `SetFileTime` creation-time preservation call on non-Windows while leaving the Windows path intact, and `LIBREWPF_SHARPDEVELOP_SAVE_SMOKE` validates a real AvalonEdit-backed file through dirty-state marking, `SaveFile.Save(content)`, temporary-file safe saving, disk marker verification, and byte-for-byte restore. The follow-up `LIBREWPF_SHARPDEVELOP_SAVE_ALL_SMOKE` path now validates the real `SaveAllFiles.SaveAll()` workbench-wide dirty-content/opened-file route against the same restore harness. `LIBREWPF_SHARPDEVELOP_RELOAD_SMOKE` now validates an external disk edit through the real `ReloadFile` command and restores the sample file byte-for-byte. `SharpDevelop.Full.LibreWpf.csproj` with ResourceToolkit included builds with `103` warnings and `0` errors after these smoke extensions, the LineCounter save-all smoke reports `Success command=SaveAll file=LineCounterBrowser.cs markedDirty=True saveClearedDirty=True diskContainsMarker=True diskRestored=True safeSaving=True`, and the reload smoke reports `Success command=Reload file=LineCounterBrowser.cs diskChanged=True commandReloaded=True dirtyCleared=True diskRestored=True editorRestored=True`, both with no sample-file diff after exit.

SharpDevelop is not yet fully runnable as an IDE. The remaining work below is the plan for the next implementation phase.

## Remaining implementation work

1. Expand portable HWND hook coverage.
   - Covered now: activation, mouse activation, show/hide, basic move/resize, portable window regions, and non-client/title-bar mouse hook dispatch for `WM_NCMOUSEMOVE`, `WM_NCLBUTTONDOWN`, `WM_NCLBUTTONDBLCLK`, and `WM_NCRBUTTONDOWN`/`WM_NCRBUTTONUP` when a platform service supplies typed non-client events.
   - Covered now: the WPF package host no longer delegates lifetime to Silk.NET `IWindow.Run()` and no longer polls `IWindow.IsClosing` after close begins.
   - Remaining: add native host event generation for title-bar/non-client mouse events on supported platform backends, add exact window-position data when consumers need native `WINDOWPOS` details, and cover any additional region/floating-window events used by AvalonDock and SharpDevelop.
   - Keep the existing activation hook path reflection-free and extend it with neutral DTOs rather than passing native Win32 structs through package APIs.

2. Finish AvalonDock floating/flyout parity.
   - Replace the remaining `WindowInteropWrapper` and `FloatingWindow` Win32 assumptions with portable window ownership, activation, and placement services.
   - Covered now: live full-shell smoke for `ProjectBrowserPad` dockable floating window creation, floating-window context menu, dockable/floating mode toggle, redock, auto-hide, flyout creation, and final dock restore.
   - Remaining: broaden live validation to restore-layout persistence, focus restoration, tab switching, drag docking overlays, platform-raised floating-window title/non-client interactions, and user-driven mouse/keyboard flows.

3. Complete remaining popup runtime validation.
   - Covered now: main menu, real AddInTree context menu, ComboBox, Core.Presentation toolbar drop-down button, hosted WinForms `ContextMenuStrip`, AvalonEdit completion popup, AvalonDock floating-window context menu, and AvalonDock auto-hide flyout through package-mode SharpDevelop flows.
   - Remaining: validate AvalonDock tab/dropdown context-menu interactions and manual user-driven menu/context-menu flows beyond the in-process smoke harness.
   - Add more in-process validation helpers only where macOS automation permissions block reliable external driving.

4. Add AvalonEdit IME/text-input seam.
   - Replace native IME calls and `HwndSource` assumptions with a typed LibreWPF text-composition service backed by ProGPU/Silk input events and local OS APIs.
   - Validate composition start/update/commit/cancel, caret placement, and candidate-window positioning.

5. Broaden workbench service/runtime coverage.
   - Covered now: source-file save flows through the normal workbench save command, workbench-wide `SaveAllFiles.SaveAll()`, dirty-state transitions, temporary-file safe saving, explicit reload command coverage, external disk-change reload validation, and restore validation on the LineCounter sample.
   - Remaining: exercise debug commands, templates, add-in loading, command routing, broader workbench pads, toolbar updates, editor navigation, additional project loading cases, save-as/new-file flows, external-change prompt paths for dirty files, and shutdown persistence.
   - Promote each validated runtime feature into a package-mode smoke or focused unit test.

6. Continue LibreWinForms parity for designer surfaces.
   - Finish any missing FormsDesigner runtime and property-grid behavior with source-owned LibreWinForms APIs mirrored into the temporary LibreWPF compatibility package only when needed for current package consumers.
   - Keep source-owned LibreWinForms as the long-term implementation and remove compatibility mirrors once SharpDevelop consumes LibreWinForms directly.
   - Keep LibreWinForms package validation on the matching bridge version and a local LibreWPF/ProGPU feed when testing unpublished bridge bits. The package lane now uses `artifacts/nuget/librewinforms-pack` by default and evicts same-version bridge packages before restore, so stale user/global packages no longer hide missing local bridge content.

7. Complete ResourceToolkit feature parity.
   - The ResourceToolkit package-mode wrapper now builds and can be included in `SharpDevelop.Full.LibreWpf` with `LibreWpfSharpDevelopIncludeResourceToolkit=true`.
   - Covered now: `${res:...}` Find References, Rename Resource, Find Missing Resource Keys, and unused-key detection use current SharpDevelop text/project/search-result APIs in the LibreWPF build.
   - Covered now: a generic typed workbench smoke-hook extension point exercises ResourceToolkit without coupling the main workbench assembly to the add-in, and the smoke validates solution-file scanning, reference detection, missing-key detection, and unused-key detection.
   - Remaining: port BCL/strongly typed resource references that used the old NRefactory v3 AST resolvers and restore an interactive unused-key cleanup view or equivalent typed UI.
   - Keep completion, tooltip, and resource resolver cache behavior on current SharpDevelop text/project APIs.

8. Clean package-mode wrapper warnings.
   - Split the SharpDevelop LibreWPF wrapper into typed facade projects or explicit source excludes so duplicate source/version/native-helper warnings disappear without changing upstream SharpDevelop source behavior.

## Validation gates for the next phase

- `SharpDevelop.Full.LibreWpf.csproj` Release fresh-cache build succeeds from the local LibreWPF SDK feed.
- Focused `ProGPU.Wpf.Tests` popup, activation, host, and LibreWinForms compatibility sets pass.
- Focused `ProGPU.Wpf.Tests` window geometry hook coverage remains green when AvalonDock hook behavior changes.
- A runtime smoke opens the workbench, loads a C# project, opens a source file, opens/uses the File menu, editor context menu, toolbar dropdown, completion popup, hosted WinForms context menu, AvalonDock float/auto-hide/flyout paths, and at least one hosted WinForms combo/tree/list owner-draw surface.
- The save and save-all smokes remain green for a source file with temporary-file safe saving enabled and leave the sample tree byte-for-byte clean after restore.
- The reload smoke remains green for an external disk edit and leaves the sample tree byte-for-byte clean after restore.
- A ResourceToolkit-included full wrapper build remains green, `${res:...}` resource refactoring remains enabled, the ResourceToolkit smoke remains green, and feature smokes are added as BCL/strongly typed resource support and cleanup UI parity are restored.
- AvalonDock float/auto-hide/dock restore smoke remains green, and any broader floating-window hook work keeps the ProGPU/LibreWPF path reflection-free.
- Reports stay updated after every slice with exact commands, warning/error counts, and any remaining blocker.
