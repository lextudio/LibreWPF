# SharpDevelop LibreWPF Remaining Plan

Date: 2026-07-09

## Current finalized state

The current SharpDevelop slice is closed at package-mode build parity plus broad popup/hosted-control/AvalonDock smoke coverage. `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj` builds in Release from a fresh NuGet cache against `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal` with `287` warnings and `0` errors after refreshing the local SharpDevelop feed; the focused post-restore rebuild currently reports `39` warnings and `0` errors.

Reusable fixes now landed in LibreWPF, LibreWinForms, and ProGPU include menu/context/combo popup coverage, Core.Presentation toolbar dropdown smoke coverage, hosted WinForms `ContextMenuStrip` popup validation, AvalonEdit completion popup smoke support, AvalonDock dockable floating-window/context-menu/mode-toggle/redock/auto-hide/flyout smoke coverage, hosted WinForms TreeView/ListView/ComboBox owner drawing, DrawItem background/focus helpers, dialog/clipboard services, ImageList/TreeView icon rendering, typed portable `HwndSource` hook dispatch for activation, mouse activation, show/hide, basic window move/resize geometry messages, exact ProGPU scene geometry clipping for portable window regions, plus LibreWinForms preview package/release workflows on the default `librewinforms-progpu-port` branch. LibreWPF now tracks LibreWinForms submodule commit `2810c93a8`, which hardens package cleanup, expected-artifact validation, NuGet README metadata, release bundle contents, missing-artifact workflow behavior, and isolated current-version restore caches so stale user/global `LibreWPF.Interop` packages cannot shadow the bridge feed.

The latest popup/AvalonDock pass fixes the hosted WinForms context-menu crash by guarding source-less portable popup menu-mode pushes in `MenuBase.PushMenuMode(...)`, adds a real AvalonDock pad state smoke, and fixes portable `Window.Activate()` so shown non-Windows LibreWPF windows set activation through `PortableWindowActivationService` instead of falling into the HWND path. The broad SharpDevelop smoke now validates File menu, real AddInTree context menu, ComboBox popup, Core.Presentation toolbar dropdown, hosted WinForms `ContextMenuStrip`, AvalonEdit completion popup, AvalonDock `ProjectBrowserPad` float, floating-window context menu, docked pane options menu, dockable/floating mode toggle, redock, auto-hide/flyout creation, restore, property pad, ResX conversion, sample build, FormsDesigner load, and FormsDesigner mutation in one package-mode run. The only runtime log noise in this lane is the existing SharpDevelop `log4net` configuration warning.

The latest AvalonDock pass also adds a typed handle-based `PortableWindowRegion` route from SharpDevelop flyout windows into ProGPU. Direct `SetWindowRgn`/`CreateRectRgn`/`CombineRgn` use is now avoided on non-Windows for the active flyout update and open/close animation paths, and SharpDevelop startup remains alive after the change. The region DTO exclusion rectangles now feed a ProGPU vector path difference assigned to the scene root `GeometryClip`, so the same base-minus-exclusion region is enforced for rendering and GPU hit testing. The SharpDevelop-local AvalonDock smoke uses typed `LIBREWPF` helpers to observe flyout creation and source attachment; it does not use private-field reflection.

LibreWinForms repository packaging/default-branch status was rechecked on 2026-07-09. `wieslawsoltes/winforms` already reports `librewinforms-progpu-port` as default branch with the LibreWinForms description/topics, and `./eng/librewinforms-verify-docs.sh` succeeds, so no new WinForms commit was needed for that request.

SharpDevelop is not yet fully runnable as an IDE. The remaining work below is the plan for the next implementation phase.

## Remaining implementation work

1. Expand portable HWND hook coverage.
   - Add typed ProGPU/LibreWPF contracts for non-client activation/title-bar messages, exact window-position data when consumers need native `WINDOWPOS` details, and region/floating-window events used by AvalonDock and SharpDevelop.
   - Keep the existing activation hook path reflection-free and extend it with neutral DTOs rather than passing native Win32 structs through package APIs.

2. Finish AvalonDock floating/flyout parity.
   - Replace the remaining `WindowInteropWrapper` and `FloatingWindow` Win32 assumptions with portable window ownership, activation, and placement services.
   - Covered now: live full-shell smoke for `ProjectBrowserPad` dockable floating window creation, floating-window context menu, dockable/floating mode toggle, redock, auto-hide, flyout creation, and final dock restore.
   - Remaining: broaden live validation to restore-layout persistence, focus restoration, tab switching, drag docking overlays, floating-window title/non-client interactions, and user-driven mouse/keyboard flows.

3. Complete remaining popup runtime validation.
   - Covered now: main menu, real AddInTree context menu, ComboBox, Core.Presentation toolbar drop-down button, hosted WinForms `ContextMenuStrip`, AvalonEdit completion popup, AvalonDock floating-window context menu, and AvalonDock auto-hide flyout through package-mode SharpDevelop flows.
   - Remaining: validate AvalonDock tab/dropdown context-menu interactions and manual user-driven menu/context-menu flows beyond the in-process smoke harness.
   - Add more in-process validation helpers only where macOS automation permissions block reliable external driving.

4. Add AvalonEdit IME/text-input seam.
   - Replace native IME calls and `HwndSource` assumptions with a typed LibreWPF text-composition service backed by ProGPU/Silk input events and local OS APIs.
   - Validate composition start/update/commit/cancel, caret placement, and candidate-window positioning.

5. Broaden workbench service/runtime coverage.
   - Exercise debug commands, templates, add-in loading, command routing, workbench pads, toolbar updates, editor navigation, project loading, save flows, and shutdown persistence.
   - Promote each validated runtime feature into a package-mode smoke or focused unit test.

6. Continue LibreWinForms parity for designer surfaces.
   - Finish any missing FormsDesigner runtime and property-grid behavior with source-owned LibreWinForms APIs mirrored into the temporary LibreWPF compatibility package only when needed for current package consumers.
   - Keep source-owned LibreWinForms as the long-term implementation and remove compatibility mirrors once SharpDevelop consumes LibreWinForms directly.
   - Keep LibreWinForms package validation on the matching bridge version and a local LibreWPF/ProGPU feed when testing unpublished bridge bits. The package lane now uses `artifacts/nuget/librewinforms-pack` by default and evicts same-version bridge packages before restore, so stale user/global packages no longer hide missing local bridge content.

7. Complete ResourceToolkit feature parity.
   - The ResourceToolkit package-mode wrapper now builds and can be included in `SharpDevelop.Full.LibreWpf` with `LibreWpfSharpDevelopIncludeResourceToolkit=true`.
   - Keep the compile-time shell, completion, tooltip, and resource resolver cache on current SharpDevelop text/project APIs.
   - Rewrite Find References, Rename Resource, Find Unused Resources, and Find Missing Resources against the current parser/project model. The old NRefactory v3 AST resolver/refactoring paths remain disabled under `LIBREWPF` until that rewrite is implemented.

8. Clean package-mode wrapper warnings.
   - Split the SharpDevelop LibreWPF wrapper into typed facade projects or explicit source excludes so duplicate source/version/native-helper warnings disappear without changing upstream SharpDevelop source behavior.

## Validation gates for the next phase

- `SharpDevelop.Full.LibreWpf.csproj` Release fresh-cache build succeeds from the local LibreWPF SDK feed.
- Focused `ProGPU.Wpf.Tests` popup, activation, host, and LibreWinForms compatibility sets pass.
- Focused `ProGPU.Wpf.Tests` window geometry hook coverage remains green when AvalonDock hook behavior changes.
- A runtime smoke opens the workbench, loads a C# project, opens a source file, opens/uses the File menu, editor context menu, toolbar dropdown, completion popup, hosted WinForms context menu, and at least one hosted WinForms combo/tree/list owner-draw surface.
- A ResourceToolkit-included full wrapper build remains green, and feature smokes are added as each disabled legacy ResourceToolkit command is replaced with a current typed implementation.
- AvalonDock float/auto-hide/dock restore smoke remains green, and any broader floating-window hook work keeps the ProGPU/LibreWPF path reflection-free.
- Reports stay updated after every slice with exact commands, warning/error counts, and any remaining blocker.
