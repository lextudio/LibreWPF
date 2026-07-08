# SharpDevelop LibreWPF Remaining Plan

Date: 2026-07-08

## Current finalized state

The current SharpDevelop slice is closed at package-mode build parity plus broad popup/hosted-control smoke coverage. `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj` builds in Release from a fresh NuGet cache against `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal` with `286` warnings and `0` errors.

Reusable fixes now landed in LibreWPF, LibreWinForms, and ProGPU include menu/context/combo popup coverage, AvalonEdit completion popup smoke support, hosted WinForms TreeView/ListView/ComboBox owner drawing, DrawItem background/focus helpers, dialog/clipboard services, ImageList/TreeView icon rendering, typed portable `HwndSource` hook dispatch for activation, mouse activation, show/hide, basic window move/resize geometry messages, exact ProGPU scene geometry clipping for portable window regions, plus LibreWinForms preview package/release workflows on the default `librewinforms-progpu-port` branch. LibreWPF now tracks LibreWinForms submodule commit `108952950`, which hardens package cleanup, expected-artifact validation, NuGet README metadata, release bundle contents, and missing-artifact workflow behavior.

The latest AvalonDock pass also adds a typed handle-based `PortableWindowRegion` route from SharpDevelop flyout windows into ProGPU. Direct `SetWindowRgn`/`CreateRectRgn`/`CombineRgn` use is now avoided on non-Windows for the active flyout update and open/close animation paths, and SharpDevelop startup remains alive after the change. The region DTO exclusion rectangles now feed a ProGPU vector path difference assigned to the scene root `GeometryClip`, so the same base-minus-exclusion region is enforced for rendering and GPU hit testing.

SharpDevelop is not yet fully runnable as an IDE. The remaining work below is the plan for the next implementation phase.

## Remaining implementation work

1. Expand portable HWND hook coverage.
   - Add typed ProGPU/LibreWPF contracts for non-client activation/title-bar messages, exact window-position data when consumers need native `WINDOWPOS` details, and region/floating-window events used by AvalonDock and SharpDevelop.
   - Keep the existing activation hook path reflection-free and extend it with neutral DTOs rather than passing native Win32 structs through package APIs.

2. Finish AvalonDock floating/flyout parity.
   - Replace the remaining `WindowInteropWrapper` and `FloatingWindow` Win32 assumptions with portable window ownership, activation, and placement services.
   - Validate the new ProGPU-native `PortableWindowRegion.Bounds` minus `ExcludedRects` composition in live AvalonDock float/auto-hide flows, then broaden the contract if AvalonDock exposes non-rectangular native regions.
   - Validate dock, float, auto-hide, restore layout, focus restoration, and tab switching in the full SharpDevelop shell.

3. Complete popup runtime validation.
   - Validate menu, context menu, ComboBox, toolbar drop-down, Core.Presentation drop-down buttons, AvalonDock tabs, and AvalonEdit completion popups through package-mode app flows.
   - Add in-process validation helpers for interactions that cannot be driven by macOS automation permissions on this machine.

4. Add AvalonEdit IME/text-input seam.
   - Replace native IME calls and `HwndSource` assumptions with a typed LibreWPF text-composition service backed by ProGPU/Silk input events and local OS APIs.
   - Validate composition start/update/commit/cancel, caret placement, and candidate-window positioning.

5. Broaden workbench service/runtime coverage.
   - Exercise debug commands, templates, add-in loading, command routing, workbench pads, toolbar updates, editor navigation, project loading, save flows, and shutdown persistence.
   - Promote each validated runtime feature into a package-mode smoke or focused unit test.

6. Continue LibreWinForms parity for designer surfaces.
   - Finish any missing FormsDesigner runtime and property-grid behavior with source-owned LibreWinForms APIs mirrored into the temporary LibreWPF compatibility package only when needed for current package consumers.
   - Keep source-owned LibreWinForms as the long-term implementation and remove compatibility mirrors once SharpDevelop consumes LibreWinForms directly.
   - Keep LibreWinForms package validation on `LIBREWINFORMS_BRIDGE_PACKAGE_VERSION=0.1.0-preview.sharpdevelop.1` and the local SharpDevelop feed until the next public LibreWPF/ProGPU preview ships the newer portable dialog DTOs and `ProGPU.System.Drawing.Common` package.

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
- A runtime smoke opens the workbench, loads a C# project, opens a source file, opens/uses the File menu, editor context menu, completion popup, and at least one hosted WinForms combo/tree/list owner-draw surface.
- A ResourceToolkit-included full wrapper build remains green, and feature smokes are added as each disabled legacy ResourceToolkit command is replaced with a current typed implementation.
- AvalonDock float/auto-hide/dock restore flows run without Win32-only fallbacks.
- Reports stay updated after every slice with exact commands, warning/error counts, and any remaining blocker.
