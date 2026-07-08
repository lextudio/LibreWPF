# SharpDevelop LibreWPF Remaining Plan

Date: 2026-07-08

## Current finalized state

The current SharpDevelop slice is closed at package-mode build parity plus broad popup/hosted-control smoke coverage. `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj` builds in Release from a fresh NuGet cache against `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal` with `286` warnings and `0` errors.

Reusable fixes now landed in LibreWPF, LibreWinForms, and ProGPU include menu/context/combo popup coverage, AvalonEdit completion popup smoke support, hosted WinForms TreeView/ListView/ComboBox owner drawing, DrawItem background/focus helpers, dialog/clipboard services, ImageList/TreeView icon rendering, and the first typed portable `HwndSource` hook dispatch path for activation messages.

SharpDevelop is not yet fully runnable as an IDE. The remaining work below is the plan for the next implementation phase.

## Remaining implementation work

1. Expand portable HWND hook coverage.
   - Add typed ProGPU/LibreWPF contracts for window position, mouse activation, non-client activation, show/hide, and region/floating-window events used by AvalonDock and SharpDevelop.
   - Keep the existing activation hook path reflection-free and extend it with neutral DTOs rather than passing native Win32 structs through package APIs.

2. Finish AvalonDock floating/flyout parity.
   - Replace `WindowInteropWrapper`, `FloatingWindow`, and `FlyoutPaneWindow` Win32 assumptions with portable window ownership, region, activation, and placement services.
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

7. Decide ResourceToolkit strategy.
   - Either add a compatibility parser layer for the legacy NRefactory/SharpDevelop.Dom dependencies or keep ResourceToolkit disabled with a documented unsupported add-in list for the preview.

8. Clean package-mode wrapper warnings.
   - Split the SharpDevelop LibreWPF wrapper into typed facade projects or explicit source excludes so duplicate source/version/native-helper warnings disappear without changing upstream SharpDevelop source behavior.

## Validation gates for the next phase

- `SharpDevelop.Full.LibreWpf.csproj` Release fresh-cache build succeeds from the local LibreWPF SDK feed.
- Focused `ProGPU.Wpf.Tests` popup, activation, host, and LibreWinForms compatibility sets pass.
- A runtime smoke opens the workbench, loads a C# project, opens a source file, opens/uses the File menu, editor context menu, completion popup, and at least one hosted WinForms combo/tree/list owner-draw surface.
- AvalonDock float/auto-hide/dock restore flows run without Win32-only fallbacks.
- Reports stay updated after every slice with exact commands, warning/error counts, and any remaining blocker.
