# SharpDevelop LibreWPF Integration

Date: 2026-07-08

## Current status

- Added a package-mode `SharpDevelop.LibreWpf` executable shell:
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.LibreWpf.csproj`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/LibreWpf/LibreWpfSharpDevelopMain.cs`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/LibreWpf/LibreWpfWorkbenchShell.xaml`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/LibreWpf/LibreWpfWorkbenchShell.xaml.cs`
- The shell consumes `LibreWPF.Sdk/0.1.0-preview.sharpdevelop.1` from `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal` and now references the source-built `ICSharpCode.SharpDevelop.LibreWpf` Base wrapper so package-mode runs carry the SharpDevelop core assembly graph.
- Added/kept LibreWPF wrapper projects for SharpDevelop source libraries:
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Libraries/AvalonDock/AvalonDock/AvalonDock.LibreWpf.csproj`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Libraries/AvalonEdit/ICSharpCode.AvalonEdit/ICSharpCode.AvalonEdit.LibreWpf.csproj`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Libraries/SharpTreeView/ICSharpCode.TreeView/ICSharpCode.TreeView.LibreWpf.csproj`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Core/Project/ICSharpCode.Core.LibreWpf.csproj`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/ICSharpCode.Core.Presentation/ICSharpCode.Core.Presentation.LibreWpf.csproj`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/ICSharpCode.Core.WinForms/ICSharpCode.Core.WinForms.LibreWpf.csproj`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/ICSharpCode.SharpDevelop.Widgets/Project/ICSharpCode.SharpDevelop.Widgets.LibreWpf.csproj`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Base/Project/ICSharpCode.SharpDevelop.LibreWpf.csproj`
- Added SharpDevelop-local package configuration:
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/NuGet.config`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/Directory.Build.props`
- Added/expanded LibreWPF WinForms compatibility packages so SharpDevelop-era projects can resolve common `System.Windows.Forms`, design-time, drag/drop, list, and hosted-control APIs when `ProGpuWpfUsePortableWinFormsCompat=true`.
- Added ProGPU-backed portable drawing package coverage:
  - `ProGPU.System.Drawing.Common` packages the existing ProGPU `System.Drawing.Common` assembly shim.
  - `ProGPU.SkiaSharp` packages the ProGPU SkiaSharp-compatible assembly shim used by bitmap decoding.
  - The WinFormsCompat package now builds against the ProGPU drawing shim instead of Microsoft `System.Drawing.Common`.
- The LibreWPF `ICSharpCode.Core.Presentation` wrapper now also builds against `ProGPU.System.Drawing.Common` and defines `LIBREWPF` so resource images use an in-memory PNG stream decoded by `BitmapImage` instead of Windows GDI `Bitmap.GetHbitmap()`.
- The full SharpDevelop workbench no longer loses toolbar/pad image resources on modern .NET. The historical `.resources` image payloads require BinaryFormatter deserialization, which is disabled at runtime; the LibreWPF `ICSharpCode.Core` wrapper now falls back to the existing `data/resources/image/BitmapResources/BitmapResources.res` manifest and loads PNG/ICO files directly through the ProGPU drawing shim.
- The full workbench now has a LibreWPF-only in-process validation hook for popup testing. `LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=Menu` opens the real AddInTree-built File menu after startup, and `LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=<ms>` closes the workbench for unattended captures.
- Added a LibreWPF AvalonEdit add-in wrapper and wired it into the full workbench package-mode build:
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/AddIns/DisplayBindings/AvalonEdit.AddIn/AvalonEdit.AddIn.LibreWpf.csproj`
  - `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj`
- The full workbench now accepts absolute Unix file paths on the command line and opens `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Startup/SharpDevelopMain.cs` through the real `AvalonEditDisplayBinding`.
- Source-tree add-in assembly resolution now falls back to `AppContext.BaseDirectory` under `LIBREWPF`, so built add-in assemblies copied beside `SharpDevelop.dll` can be found without installing the historical Windows add-in layout.
- The current LibreWPF AvalonEdit path embeds the stock AvalonEdit `.xshd`/`.xsd` highlighting resources in the SDK-style wrapper and registers the source-tree syntax definitions during editor startup.
- The full workbench now opens a `.cs` file through the real AvalonEdit display binding, loads the CSharpBinding add-in MVP, attaches `CSharpTextEditorExtension`, resolves `CSharpBinding.CSharpLanguageBinding`, and loads legacy C# projects as `CSharpProject`.
- The Windows Forms designer slice is now included in the package-mode CSharpBinding wrapper through the LibreWinForms package lane. `FormsDesigner.LibreWpf` builds, the add-in descriptor is copied beside the full workbench, and `CSharpFormsDesigner` attaches a `FormsDesignerViewContent` to the LineCounter sample's `LineCounterBrowser.cs` with a replayed portable `UserControl` design surface.

The current validation path includes both the controlled `SharpDevelop.LibreWpf` smoke shell and the fuller `SharpDevelop.Full.LibreWpf` wrapper. The full wrapper builds the historical SharpDevelop workbench entry point through LibreWPF package mode and now starts/renders the main IDE shell on macOS. It is not yet full IDE parity: legacy Win32 hooks, project-system services, editor IME, and several Windows-only integration points still need typed portable seams.

## 2026-07-09 Portable activation and SharpDevelop smoke refresh

SharpDevelop's AvalonDock auto-hide smoke exposed a real LibreWPF port issue: on non-Windows, `Window.Activate()` could return `false` for a shown portable window because it fell through to the HWND `HwndSource` activation path. `PresentationFramework.Window.Activate()` now routes shown portable windows through the existing typed `PortableWindowActivationService.SetActivationState(this, true)` boundary before any Win32/source-window check. This keeps activation state in the source-built WPF portable service and avoids adding bridge-local reflection or AvalonDock-specific workarounds.

The SharpDevelop-local AvalonDock smoke also gained typed `LIBREWPF` helpers to observe flyout creation without private-field reflection. The harness now records `flyoutShowResult`, `flyoutCreated`, `flyoutHasPresentationSource`, and `flyoutWindowCount`, which separates a failed portable activation contract from the normal AvalonDock auto-hide close timer behavior.

LibreWinForms repo state was rechecked as part of the same pass. `wieslawsoltes/winforms` already uses `librewinforms-progpu-port` as the default branch, has the LibreWinForms/ProGPU/Silk.NET description and topics, and `./eng/librewinforms-verify-docs.sh` succeeds, so no empty LibreWinForms commit was needed.

Validation:

```text
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build src/Microsoft.DotNet.Wpf/src/PresentationFramework/PresentationFramework.csproj -c Release -v:minimal /nr:false
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release -v:minimal /nr:false
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet vstest src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll --Tests:ProGPU.Wpf.Tests.Composition.WpfManagedProjectGraphTests.PresentationFrameworkHasPortableWindowActivationBoundary
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet pack packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj -c Release -o artifacts/packages/SharpDevelopLocal -v:minimal -p:Version=0.1.0-preview.sharpdevelop.1 -p:PackageVersion=0.1.0-preview.sharpdevelop.1 /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-goal-nuget-activate DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Release -v:minimal -clp:ErrorsOnly /p:GenerateFullPaths=true /p:LibreWpfSharpDevelopIncludeResourceToolkit=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-goal-nuget-activate DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 LIBREWPF_SHARPDEVELOP_AVALONDOCK_SMOKE=ProjectBrowser LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=30000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Release/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-goal-nuget-activate DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All LIBREWPF_SHARPDEVELOP_AVALONDOCK_SMOKE=ProjectBrowser LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=1 LIBREWPF_SHARPDEVELOP_FORMS_DESIGNER_SMOKE=/Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/LineCounterBrowser.cs LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=45000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Release/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln
gh repo view wieslawsoltes/winforms --json defaultBranchRef,description,repositoryTopics
./eng/librewinforms-verify-docs.sh
```

Results:

```text
PresentationFramework Release build          -> succeeds, 2 existing ReachFramework version-conflict warnings, 0 errors
ProGPU.Wpf.Tests Release build               -> succeeds, existing warning set, 0 errors
Portable activation boundary test            -> 1 passed, 0 failed
LibreWPF.Transport SharpDevelopLocal pack    -> succeeds, 0.1.0-preview.sharpdevelop.1 refreshed
SharpDevelop.Full.LibreWpf fresh-cache build -> succeeds, 287 warnings, 0 errors
SharpDevelop.Full.LibreWpf focused rebuild   -> succeeds, 39 warnings, 0 errors
Focused AvalonDock smoke                     -> Success, ownerActivateResult=True, ownerActiveAfterActivate=True, flyoutShowResult=True, flyoutCreated=True, flyoutHasPresentationSource=True, flyoutWindowCount=1, flyoutVisible=True
Broad SharpDevelop smoke                     -> main menu, AddInTree context menu, ComboBox popup, toolbar dropdown, WinForms ContextMenuStrip, AvalonDock float/context menu/redock/auto-hide/flyout, ResX, build, property pad, FormsDesigner load/mutation, and editor completion all pass; exit code 0
LibreWinForms GitHub metadata                -> defaultBranch=librewinforms-progpu-port, LibreWinForms description/topics present
LibreWinForms docs verification              -> succeeds
```

## 2026-07-09 Owner-loop package coherence pass

The broad SharpDevelop smoke later exposed a packaging failure rather than a SharpDevelop runtime failure: `LibreWPF.ProGPU` had been packed against the current ProGPU submodule, but the SharpDevelop-local feed still contained stale same-version ProGPU packages. At runtime this loaded an older `ProGPU.Scene.dll` and failed with `MissingMethodException` for `ProGPU.Scene.Visual.GeometryClip`.

The local SharpDevelop feed was refreshed with a coherent `0.1.0-preview.sharpdevelop.1` ProGPU package set from `/Users/wieslawsoltes/GitHub/wpf/external/ProGPU`, then `LibreWPF.ProGPU` was repacked after tightening the native owner-loop diagnostics. The host loop now treats close/dispose exceptions as normal only after LibreWPF has started shutdown, while unexpected `ObjectDisposedException` or other event-pump failures are traced and rethrown instead of making the package-mode app exit silently. The temporary per-iteration trace was removed; `PROGPU_WPF_TRACE_NATIVE_LOOP=1` now records lifecycle transitions and owner-loop counters without flooding the log.

Validation:

```text
PROGPU_PACKAGE_VERSION=0.1.0-preview.sharpdevelop.1 PROGPU_PACKAGE_OUTPUT=/tmp/progpu-sharpdevelop-packages external/ProGPU/eng/progpu-pack.sh
cp /tmp/progpu-sharpdevelop-packages/*.0.1.0-preview.sharpdevelop.1.nupkg /tmp/progpu-sharpdevelop-packages/*.0.1.0-preview.sharpdevelop.1.snupkg artifacts/packages/SharpDevelopLocal/
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release -v:minimal /nr:false
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet vstest src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll --Tests:ProGPU.Wpf.Tests.ProGpuWpfWindowHostTests.NativeRunUsesOwnerDrivenPortableLoop
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet pack src/ProGPU.Wpf/ProGPU.Wpf.csproj -c Release -o artifacts/packages/SharpDevelopLocal -v:minimal -p:Version=0.1.0-preview.sharpdevelop.1 -p:PackageVersion=0.1.0-preview.sharpdevelop.1 /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-final-nuget DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Release -v:minimal -clp:ErrorsOnly /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-final-nuget DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 PROGPU_WPF_TRACE_NATIVE_LOOP=1 LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All LIBREWPF_SHARPDEVELOP_AVALONDOCK_SMOKE=ProjectBrowser LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=1 LIBREWPF_SHARPDEVELOP_FORMS_DESIGNER_SMOKE=/Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/LineCounterBrowser.cs LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=45000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Release/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln
```

Results:

```text
ProGPU SharpDevelopLocal package refresh       -> succeeds, coherent ProGPU/LibreWPF.Interop 0.1.0-preview.sharpdevelop.1 packages produced
ProGPU.Wpf.Tests Release build                 -> succeeds, existing warning set, 0 errors
Owner-loop regression test                     -> 1 passed, 0 failed
LibreWPF.ProGPU SharpDevelopLocal pack         -> succeeds, 0.1.0-preview.sharpdevelop.1 refreshed
SharpDevelop.Full.LibreWpf fresh-cache build   -> succeeds, 286 warnings, 0 errors
Broad SharpDevelop smoke                       -> main menu, context menu, ComboBox, toolbar popup, hosted WinForms ContextMenuStrip, AvalonDock float/context menu/redock/auto-hide/flyout, ResX, LineCounter build, property pad, FormsDesigner load/mutation, and editor completion all pass; exit code 0
Native owner-loop trace                         -> no MissingMethodException; closes through expected smoke timer after 1203 DoEvents calls / 1202 owner-loop iterations
```

## 2026-07-09 AvalonDock non-client hook contract

AvalonDock floating windows still consume legacy title-bar/non-client messages through `WindowInteropWrapper.FilterMessage(...)`. The concrete messages used by the SharpDevelop copy are `WM_NCLBUTTONDOWN` for starting dock drags, `WM_NCLBUTTONDBLCLK` for dock/maximize toggles, and `WM_NCRBUTTONDOWN`/`WM_NCRBUTTONUP` for floating-window title context menus.

LibreWPF now has a typed ProGPU window-event contract for those messages:

- `WpfWindowEventKind.NonClientMouseMove`
- `WpfWindowEventKind.NonClientMouseDown`
- `WpfWindowEventKind.NonClientMouseUp`
- `WpfWindowEventKind.NonClientMouseDoubleClick`

`WpfWindowEventArgs` carries the neutral button, hit-test code, and screen coordinate payload. `WpfPortableWindowActivation` maps those events back to the legacy `HwndSource` hook messages with the same `wParam` hit-test code and packed screen-coordinate `lParam` shape that AvalonDock expects. The current default Silk.NET window event service exposes factory helpers for these DTOs; native title-bar event generation is still a platform-service follow-up because the current Silk.NET `IWindow` event set does not surface non-client title-bar mouse events directly.

Validation:

```text
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release -v:minimal /nr:false
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet vstest src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll --Tests:ProGPU.Wpf.Tests.WpfPortableWindowActivationTests.HostNonClientMouseEventsDispatchLegacyHwndSourceHooks,ProGPU.Wpf.Tests.WpfPortableWindowActivationTests.HostNonClientMouseMoveDefaultsToCaptionHitTest,ProGPU.Wpf.Tests.Platform.SilkNetWpfWindowEventServiceTests.CreateNonClientMouseMoveEventStoresHitTestAndScreenCoordinates,ProGPU.Wpf.Tests.Platform.SilkNetWpfWindowEventServiceTests.CreateNonClientMouseButtonEventStoresButtonHitTestAndScreenCoordinates,ProGPU.Wpf.Tests.Platform.SilkNetWpfWindowEventServiceTests.CreateNonClientMouseButtonEventRejectsNonButtonKinds
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet vstest src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll --Tests:ProGPU.Wpf.Tests.WpfPortableWindowActivationTests,ProGPU.Wpf.Tests.Platform.SilkNetWpfWindowEventServiceTests
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet pack src/ProGPU.Wpf/ProGPU.Wpf.csproj -c Release -o artifacts/packages/SharpDevelopLocal -v:minimal -p:Version=0.1.0-preview.sharpdevelop.1 -p:PackageVersion=0.1.0-preview.sharpdevelop.1 /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-goal-nuget-nonclient DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Release -v:minimal -clp:ErrorsOnly /p:GenerateFullPaths=true /p:LibreWpfSharpDevelopIncludeResourceToolkit=true /nr:false
```

Results:

```text
ProGPU.Wpf.Tests Release build                 -> succeeds, existing warning set, 0 errors
Focused non-client hook/factory tests          -> 5 passed, 0 failed
Activation/window-event test group             -> 67 passed, 0 failed
LibreWPF.ProGPU SharpDevelopLocal pack         -> succeeds, 0.1.0-preview.sharpdevelop.1 refreshed
SharpDevelop.Full.LibreWpf fresh-cache build   -> succeeds, 287 warnings, 0 errors
```

## 2026-07-08 SharpDevelop popup/menu mode pass

The SharpDevelop full-workbench popup lane exposed a portable popup ordering issue in `ContextMenu`/`MenuBase`. The hosted WinForms `ContextMenuStrip` path opened a WPF `ContextMenu` through `WindowsFormsHost`, but `MenuBase.PushMenuMode(...)` could run before `PresentationSource.CriticalFromVisual(this)` returned a source on the portable popup path. The release build then passed `null` into `InputManager.PushMenuMode(...)` and threw `ArgumentNullException("menuSite")`.

LibreWPF now guards that source-less transient in `MenuBase.PushMenuMode(...)`: normal menus still push menu mode when a source is present, while portable popups that are not source-attached yet fail closed for that push instead of throwing. A focused source-level regression was added in `PortableWinFormsControlCompatibilityTests`.

SharpDevelop's local full-workbench smoke harness was also extended to cover real Core.Presentation toolbar dropdown menus, and the popup checks now drain earlier smoke popups before validating hosted WinForms context menus. That avoids overlap between independent popup checks while preserving a strict `ContextMenuStrip.Visible` result.

Validation:

```text
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -v:minimal /nr:false
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet vstest src/ProGPU.Wpf.Tests/bin/Debug/net10.0/ProGPU.Wpf.Tests.dll --Tests:ProGPU.Wpf.Tests.Platform.PortableWinFormsControlCompatibilityTests.MenuBaseDefersMenuModePushUntilPortablePopupHasPresentationSource
PROGPU_WPF_DEV_PACKAGE_VERSION=0.1.0-preview.1 DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 ./eng/progpu-wpf-sdk-ci.sh
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet pack packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj -c Release -o artifacts/packages/SharpDevelopLocal -v:minimal -p:Version=0.1.0-preview.sharpdevelop.1 -p:PackageVersion=0.1.0-preview.sharpdevelop.1
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-menu-fix-nuget DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Release -v:minimal -clp:ErrorsOnly /p:GenerateFullPaths=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-menu-fix-nuget DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=1 LIBREWPF_SHARPDEVELOP_FORMS_DESIGNER_SMOKE=/Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/LineCounterBrowser.cs LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=19000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Release/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln
```

Results:

```text
MenuBase portable popup regression test -> 1 passed, 0 failed
LibreWPF SDK CI/package gate             -> succeeds, including Hello/MVP/Toolkit/paid Xceed/SciChart lanes
SharpDevelop.Full.LibreWpf fresh build   -> succeeds, 193 warnings, 0 errors
Main menu popup                          -> opened
Context menu popup                       -> opened, 27 items
ComboBox popup                           -> opened
Core.Presentation toolbar dropdown       -> opened, 3 items
WinForms ContextMenuStrip smoke           -> Opened, 3 items
ResX smoke                               -> Success, files=1
LineCounter build smoke                  -> Success, 0 errors, 4 existing sample warnings
FormsDesigner smoke                      -> Attached, root=System.Windows.Forms.UserControl, components=21, selectable=21
FormsDesigner mutation smoke             -> Success, selected component reaches PropertyGrid and changed Text value is visible
PropertyGrid smoke                       -> Success, selected=ToolStripContainer, rows=54
Editor completion smoke                  -> Opened, bindings=7, items=12
```

## 2026-07-09 SharpDevelop AvalonDock smoke pass

The full SharpDevelop package-mode smoke now includes a real AvalonDock pad flow instead of only standalone menu/popup controls. The local SharpDevelop harness exposes the existing typed `PadDescriptor -> AvalonPadContent` map through an internal lookup and validates a real `ProjectBrowserPad` through:

- docked pad activation
- dockable floating window creation
- floating-window context-menu creation through the real AvalonDock menu path
- dockable/floating mode toggle while the pad is hosted by the floating window
- redock through AvalonDock state restoration
- auto-hide transition
- flyout window creation through the portable popup/window path
- final docked-state restoration

No private-field reflection or object-shape probing was added for this validation path.

Validation:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-goal-nuget DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Release -v:minimal -clp:ErrorsOnly /p:GenerateFullPaths=true /p:LibreWpfSharpDevelopIncludeResourceToolkit=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-goal-nuget DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All LIBREWPF_SHARPDEVELOP_AVALONDOCK_SMOKE=ProjectBrowser LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=1 LIBREWPF_SHARPDEVELOP_FORMS_DESIGNER_SMOKE=/Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/LineCounterBrowser.cs LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=30000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Release/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln
```

Results:

```text
SharpDevelop.Full.LibreWpf fresh build   -> succeeds, 39 warnings, 0 errors
Main menu popup                          -> opened
Context menu popup                       -> opened, 27 items
ComboBox popup                           -> opened
Core.Presentation toolbar dropdown       -> opened, 3 items
Hosted WinForms ContextMenuStrip smoke   -> Opened, 3 items
ResX smoke                               -> Success, files=1
LineCounter build smoke                  -> Success, 0 errors, 4 existing sample warnings
FormsDesigner smoke                      -> Attached, root=System.Windows.Forms.UserControl, components=21, selectable=21
FormsDesigner mutation smoke             -> Success, selected component reaches PropertyGrid and changed Text value is visible
PropertyGrid smoke                       -> Success, selected=ToolStripContainer, rows=54
Editor completion smoke                  -> Opened, bindings=8, items=12
AvalonDock smoke                         -> Success, floatingContextMenuOpened=True, floatingContextMenuItems=6, floatingModeToggled=True, dockableModeRestored=True, DockableWindow -> Docked -> AutoHide/Flyout -> Docked
Application exit                         -> code 0 through smoke exit timer
```

## 2026-07-08 AvalonDock portable window-region pass

AvalonDock flyout windows used direct `CreateRectRgn`, `CombineRgn`, and `SetWindowRgn` calls while opening, closing, and updating auto-hide flyout regions. That path is now routed through a typed LibreWPF/ProGPU service on non-Windows:

- `PortableWindowRegion` was added to `ProGPU.Wpf.Interop` with a base `PortableRect` and preserved exclusion rectangles.
- `IPortableWindowActivationServiceRegistrar.TrySetWindowRegion(IntPtr, PortableWindowRegion)` was added as the public typed entry point for legacy handle-based interop.
- Source-built `PortableWindowActivationService` now stores a typed `SetWindowRegion` callback and forwards registrar calls without reflection.
- `WpfPortableWindowActivation` keeps a weak handle-to-activation map and applies regions to `ProGpuWpfWindowHost.WindowRegion`.
- SharpDevelop AvalonDock `FlyoutPaneWindow` now calls `InteropHelper.TrySetPortableWindowRegion(...)` on non-Windows before the Win32 fallback and no longer calls `GetCursorPos` for the auto-close mouse-over check on non-Windows.

Validation:

```text
dotnet build external/ProGPU/src/ProGPU.Wpf.Interop/ProGPU.Wpf.Interop.csproj -c Release --no-restore
dotnet build src/ProGPU.Wpf/ProGPU.Wpf.csproj -c Release --no-restore
dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release --no-restore
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 ./.dotnet/dotnet .dotnet/sdk/11.0.100-preview.4.26210.111/vstest.console.dll src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll --TestCaseFilter:"FullyQualifiedName~WpfPortableWindowActivationTests|FullyQualifiedName~SilkNetWpfWindowEventServiceTests"
dotnet pack external/ProGPU/src/ProGPU.Wpf.Interop/ProGPU.Wpf.Interop.csproj -c Release --no-build -o artifacts/packages/SharpDevelopLocal -p:Version=0.1.0-preview.sharpdevelop.1 -p:PackageVersion=0.1.0-preview.sharpdevelop.1
dotnet pack src/ProGPU.Wpf/ProGPU.Wpf.csproj -c Release --no-build -o artifacts/packages/SharpDevelopLocal -p:Version=0.1.0-preview.sharpdevelop.1 -p:PackageVersion=0.1.0-preview.sharpdevelop.1
rm -rf /tmp/sharpdevelop-librewpf-region-nuget-1
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-region-nuget-1 dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Release -p:RestoreSources='/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal;https://api.nuget.org/v3/index.json'
DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-region-nuget-1 dotnet run --project /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Release --no-build
```

Results:

```text
ProGPU.Wpf.Interop Release build -> succeeds, 0 warnings, 0 errors
ProGPU.Wpf Release build         -> succeeds, 0 warnings, 0 errors
ProGPU.Wpf.Tests Release build   -> succeeds, existing warning set, 0 errors
Activation/window-event tests    -> 62 passed, 0 failed
SharpDevelop.Full.LibreWpf       -> succeeds, 286 warnings, 0 errors
SharpDevelop startup             -> process stayed alive until interrupted; only the known log4net config warning was printed
```

This pass removes another macOS crash path and keeps the data in ProGPU-owned region state. The follow-up compositor slice now enforces the region as a native ProGPU scene geometry clip:

- `ProGPU.Scene.Visual.GeometryClip` is a first-class retained visual state property that invalidates the visual and parent branch.
- `Compositor` applies `GeometryClip` through the existing vector mask path for rendering and pushes the same `PushGeometryClip` state into GPU hit-test cache construction.
- `ProGpuWpfWindowHost` converts `PortableWindowRegion.Bounds` plus `ExcludedRects` into an exact vector path difference (`bounds - clipped exclusions`) and assigns it to the scene root visual.

Focused validation:

```text
dotnet restore external/ProGPU/src/ProGPU.Tests/ProGPU.Tests.csproj
dotnet test external/ProGPU/src/ProGPU.Tests/ProGPU.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~VisualChangeVersionTests"
dotnet restore src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj
dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release --no-restore
ln -sfn /Users/wieslawsoltes/.dotnet/shared/Microsoft.NETCore.App/10.0.5 .dotnet/shared/Microsoft.NETCore.App/10.0.5
dotnet vstest src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll --Tests:ProGPU.Wpf.Tests.ProGpuWpfWindowHostTests.TryCreateWindowRegionClipBuildsExactDifferencePath
dotnet vstest src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll --Tests:ProGPU.Wpf.Tests.ProGpuWpfWindowHostTests.TryCreateWindowRegionClipFailsClosedForEmptyRegion
```

Results:

```text
ProGPU VisualChangeVersionTests -> 15 passed, 0 failed
ProGPU.Wpf.Tests Release build  -> succeeds, existing warning set, 0 errors
Window-region clip tests        -> 2 passed, 0 failed
```

## 2026-07-08 LibreWinForms and FormsDesigner pass

The WPF repo now mounts `/Users/wieslawsoltes/GitHub/wpf/external/LibreWinForms` from `wieslawsoltes/winforms` on branch `librewinforms-progpu-port`. Initial `LibreWinForms.Sdk`, `LibreWinForms.System.Windows.Forms`, and `LibreWinForms.WindowsFormsIntegration` package identities are present as transitional aliases over the currently working compatibility assemblies. SharpDevelop is switched to `ProGpuWpfUseLibreWinForms=true` so future WinForms source-reuse work can land behind stable package names.

SharpDevelop full-workbench build:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-librewinforms-full-10 DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Release -v:minimal -clp:ErrorsOnly /p:RestoreSources=/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal%3Bhttps://api.nuget.org/v3/index.json /p:GenerateFullPaths=true /nr:false
```

Result:

```text
SharpDevelop.Full.LibreWpf -> Build succeeded, 182 warnings, 0 errors
```

FormsDesigner smoke:

```text
LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1
LIBREWPF_SHARPDEVELOP_FORMS_DESIGNER_SMOKE=/Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/LineCounterBrowser.cs
LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=30000
```

Result:

```text
LibreWPF AssemblyParserService loading .../System.ComponentModel.TypeConverter.dll
LibreWPF AssemblyParserService loading .../System.Drawing.Primitives.dll
LibreWPF AssemblyParserService loading .../System.Drawing.Common.dll
LibreWPF AssemblyParserService loading .../System.Windows.Forms.dll
LibreWPF ProjectContentContainer portable compatibility references added=4
LibreWPF ProjectContentContainer default references added=9
LibreWPF DisplayBindingService secondary attached id=CSharpFormsDesigner count=1
LibreWPF FormsDesigner smoke result=Attached file=LineCounterBrowser.cs secondary=1 designer=ICSharpCode.FormsDesigner.FormsDesignerViewContent surface=Loaded root=System.Windows.Forms.UserControl components=21 selectable=21
```

The first attachment blocker was design-time reference resolution, not the XAML compiler. Classic .NET Framework sample projects ask for `System.Drawing` and `System.Windows.Forms`; on macOS those references can resolve to nothing. The LibreWPF SharpDevelop parser fallback now adds `System.ComponentModel.TypeConverter`, `System.Drawing.Primitives`, portable ProGPU `System.Drawing.Common`, and portable `System.Windows.Forms` assemblies by typed runtime identity when those compatibility references are needed, preserving the existing FormsDesigner `System.Windows.Forms.Form`/`UserControl` base-type rule.

The next blocker was the old Windows Fusion/GAC path. `GlobalAssemblyCacheService` now keeps Fusion on Windows but uses app/runtime/reference-pack assembly directories on non-Windows. This removes the `fusion.dll` failure while still giving the parser concrete assembly identities.

The current LibreWinForms compatibility package now also provides a typed `DesignSurface`, `IDesignerLoaderHost`, `IDesignerHost`, `IComponentChangeService`, `ISelectionService`, and `IDesignerSerializationManager` implementation. The portable `CodeDomDesignerLoader` now parses SharpDevelop's `CodeCompileUnit` and replays common WinForms designer statements into typed controls/components, public properties, and modeled collections such as `Controls`, `Items`, `Columns`, and `Groups`. The LineCounter sample now loads a replayed `UserControl` surface with 21 components instead of the earlier blank fallback panel.

## 2026-07-08 ProGPU System.Drawing image path

SharpDevelop uses `System.Drawing.Graphics.DrawImage(...)` for disabled/ghosted icons, grayscale code-coverage bitmaps, and version-control overlay sprite extraction. The ProGPU drawing shim now handles the relevant GDI+ surface directly:

- `Graphics.DrawImage(Image, Rectangle, Rectangle, GraphicsUnit)` records the requested source rectangle instead of drawing the whole source bitmap.
- `Graphics.DrawImage(..., ImageAttributes)` routes `ColorMatrix` through the ProGPU image-effect compositor extension.
- `System.Drawing.Imaging.ColorMatrix` now exposes the common mutable `Matrix00` through `Matrix44` properties and indexer used by SharpDevelop add-ins.
- The ProGPU image-effect shader now has a typed source-rect, sampler-mode, and 4x5 color-matrix payload, so this stays a backend/shader feature instead of a SharpDevelop workaround.
- `ProGPU.Dxf` suppresses the transitive Microsoft `System.Drawing.Common` package asset from `netDxf.netstandard`, avoiding duplicate `System.Drawing.Common` assembly identities when samples/tests also reference the ProGPU drawing shim.

Validated ProGPU slice:

```text
ProGPU.Tests build -> succeeds, 4 warnings, 0 errors
GdiBitmapTests + ImageEffectRenderTests -> 14 passed, 0 failed
```

Refreshed local SharpDevelop feed:

```text
ProGPU.Scene.0.1.0-preview.sharpdevelop.1.nupkg
ProGPU.System.Drawing.Common.0.1.0-preview.sharpdevelop.1.nupkg
ProGPU.Dxf.0.1.0-preview.sharpdevelop.1.nupkg
```

Fresh-cache package-mode SharpDevelop validation used:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-librewinforms-source-owned-2
```

Result:

```text
SharpDevelop.Full.LibreWpf Release build -> succeeds, 286 warnings, 0 errors
Main menu popup                         -> opened
Context menu popup                      -> opened, 27 items
ComboBox popup                          -> opened
ResX smoke                              -> Success, files=1
LineCounter build smoke                 -> Success, 0 errors, 4 existing sample warnings
FormsDesigner smoke                     -> Attached, root=System.Windows.Forms.UserControl, components=21, selectable=21
FormsDesigner mutation smoke            -> Success, selected component reaches PropertyGrid and changed Text value is visible
PropertyGrid smoke                      -> Success, selected=ToolStripContainer, rows=54
WinForms ContextMenuStrip smoke          -> Opened, 3 items
Editor completion smoke                 -> Opened, bindings=7, items=39
```

Popup and hosted WinForms smoke:

```text
LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All
LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1
LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1
LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=12000
```

Result:

```text
LibreWPF full workbench menu popup opened
LibreWPF full workbench context menu Opened event
LibreWPF full workbench context menu popup opened items=27
LibreWPF full workbench ComboBox popup opened
LibreWPF property pad smoke result=Success selected=CSharpProject rows=22
LibreWPF WinForms context menu smoke result=Opened items=3
```

Focused regression coverage was added in `ProGPU.Wpf.Tests.Platform.PortableWinFormsCodeDomDesignerLoaderTests.BeginLoadReplaysCodeDomControlTree`. Local validation used the repository `vstest` path and passed the new test plus 27 adjacent portable WinForms compatibility tests.

The remaining FormsDesigner work is now inside richer CodeDOM/component behavior: broaden designer statement coverage, paint selection/adorners, mutate properties through the hosted grid, serialize changes, and validate generated-code round trip. The current milestone is replayed loaded-surface package-mode inclusion, not full designer parity.

## 2026-07-08 LibreWinForms ResX ownership update

The WinForms ResX resource-file API is now implemented in the source-owned LibreWinForms `System.Windows.Forms.dll` package instead of being a SharpDevelop-local LibreWPF compatibility shim. The new package slice provides `System.Resources.ResXFileRef`, `ResXDataNode`, `ResXResourceReader`, and `ResXResourceWriter` with the designer-facing behavior SharpDevelop needs for localized WinForms resources: values, metadata, comments, data nodes, `BasePath` file references, and type-name converter hooks.

Focused validation in the LibreWPF repo:

```text
ProGPU.Wpf.Tests PortableWinFormsResXResourceTests + designer resource tests -> 5 passed, 0 failed
LibreWinForms.System.Windows.Forms package-mode build                       -> succeeds, 28 warnings, 0 errors
```

This is a port/library ownership fix, not a XAML compiler change. The current SharpDevelop local LibreWPF wrapper still has a transitional duplicate `System.Resources.ResX*` shim under `src/Main/Base/Project/LibreWpf/Compat/SystemResourcesResXCompat.cs`; package-mode SharpDevelop validation should disable that shim now that LibreWinForms owns the API, otherwise the compiler sees duplicate `ResXResourceReader`/`ResXResourceWriter` definitions.

Validated after disabling the transitional local shim and repacking `LibreWinForms.System.Windows.Forms/0.1.0-preview.sharpdevelop.1`:

```text
SharpDevelop.Full.LibreWpf fresh-cache Release build -> succeeds, 286 warnings, 0 errors
Main menu popup                                      -> opened
AddInTree context menu popup                         -> opened, 27 items
ComboBox popup                                       -> opened
ResX smoke                                           -> Success, files=1
LineCounter build smoke                              -> Success, 0 errors, 4 existing sample warnings
FormsDesigner smoke                                  -> Attached, root=System.Windows.Forms.UserControl, components=21, selectable=21
FormsDesigner mutation smoke                         -> Success, selected ToolStripContainer, flushPersisted=True
Hosted PropertyGrid smoke                            -> Success, rows=54
WinForms ContextMenuStrip smoke                      -> Opened, 3 items
Editor completion smoke                              -> Opened, bindings=7, items=39
Application exit                                     -> code 0
```

## 2026-07-08 WinForms-hosted pad and popup pass

The current pass focused on real SharpDevelop workbench surfaces that combine popups with WinForms-hosted content:

- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/WinFormsCompatTypes.cs` now gives the portable `PropertyGrid` a typed `DisplayRows` model built through `TypeDescriptor`, plus stable selected-object count and refresh behavior. This is the correct compatibility surface for WinForms `PropertyGrid`; it is not a SharpDevelop object-shape probe.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsHost.cs` now clips each hosted control to its arranged bounds and renders portable `ComboBox`, `ListBox`/`CheckedListBox`, `ListView`, and `PropertyGrid` controls. This makes SharpDevelop pads such as Projects and Properties visibly useful under LibreWPF package mode.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsHost.cs` also has a typed right-click bridge for hosted WinForms controls. It finds the nearest `ContextMenuStrip`, runs its normal `Opening`/`Opened` lifecycle, converts visible `ToolStripMenuItem`/separator entries to a WPF `ContextMenu`, forwards leaf clicks through `PerformClick()`, and closes the backing strip when the WPF popup closes.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/WinFormsCompatTypes.cs` now publishes a typed `ContextMenuStrip.ShowRequested` event after `ContextMenuStrip.Show(control, point)` completes the normal `Opening`/`Opened` lifecycle. `WindowsFormsHost` registers weakly and converts direct WinForms show requests into WPF/ProGPU popups for the host that owns the source control. This covers SharpDevelop code paths that call `ContextMenuStrip.Show(...)` directly, not only pointer-triggered context menus.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Workbench/WpfWorkbench.cs` now has `LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1`, which brings the Project and Property pads forward, selects the loaded project, refreshes the hosted property grid, and logs the generated row count.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Workbench/WpfWorkbench.cs` now also has `LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1`, which calls `PropertyPad.Grid.ContextMenuStrip.Show(...)` and validates the direct hosted WinForms menu path.

Local package feed refresh:

```text
dotnet pack /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/System.Windows.Forms.csproj -c Debug -o /Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal /p:Version=0.1.0-preview.sharpdevelop.1 /p:PackageVersion=0.1.0-preview.sharpdevelop.1 /nr:false
dotnet pack /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsIntegration.csproj -c Debug -o /Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal /p:Version=0.1.0-preview.sharpdevelop.1 /p:PackageVersion=0.1.0-preview.sharpdevelop.1 /nr:false
```

Package-mode validation:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Debug -v:minimal /p:GenerateFullPaths=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=10000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln
```

Results:

```text
SharpDevelop.Full.LibreWpf             -> succeeds, 88 warnings, 0 errors
Main menu popup                         -> opened and visible in desktop capture
AddInTree solution context menu popup   -> opened with 27 items and visible in desktop capture
ComboBox popup                          -> opened and visible in desktop capture
Property pad smoke                      -> Success, selected=LibreWpfCSharpProject, rows=19
WinForms PropertyPad ContextMenuStrip    -> Opened with 3 items and visible in desktop capture
LineCounter build smoke                 -> Success, 0 errors, 4 existing sample warnings
ResX smoke                              -> Success, files=1, original file restored
```

Latest artifacts:

```text
Combined smoke log          -> /tmp/sharpdevelop-librewpf-winforms-contextmenu-smoke.log
Direct WinForms screenshot  -> /tmp/sharpdevelop-librewpf-winforms-contextmenu-visual.png
Direct WinForms visual log  -> /tmp/sharpdevelop-librewpf-winforms-contextmenu-visual.log
ComboBox visual screenshot  -> /tmp/sharpdevelop-librewpf-contextmenu-host-visual.png
ComboBox visual log         -> /tmp/sharpdevelop-librewpf-contextmenu-host-visual.log
Context menu screenshot     -> /tmp/sharpdevelop-librewpf-contextmenu-only-visual.png
Context menu visual log     -> /tmp/sharpdevelop-librewpf-contextmenu-only-visual.log
Main menu screenshot        -> /tmp/sharpdevelop-librewpf-menu-only-visual.png
Main menu visual log        -> /tmp/sharpdevelop-librewpf-menu-only-visual.log
```

The screenshots confirm a real SharpDevelop window with the main File menu, AddInTree solution context menu, smoke `ComboBox` dropdown, and hosted WinForms PropertyPad context menu visibly painted. The Properties pad renders project properties through `WindowsFormsHost`, and direct `ContextMenuStrip.Show(...)` now reaches the same WPF/ProGPU popup path. AppleScript input is still blocked by macOS automation permissions, so real pointer right-click validation remains a manual desktop check, but direct WinForms menu-show code paths are now covered by an in-app smoke.

## SharpDevelop full-workbench validation update

The current focus pass validated the full SharpDevelop workbench wrapper against the LineCounter sample solution:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Debug --no-restore -v:quiet /p:GenerateFullPaths=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=45000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln
```

Results:

```text
SharpDevelop.Full.LibreWpf build     -> succeeds with existing warning set only
LineCounter.sln load                 -> succeeds, 1 normal project node
SharpDevelop build smoke             -> Success, 0 errors, 4 existing LineCounter warnings
Produced add-in files                -> AddIns/Samples/LineCounter/LineCounter.addin, LineCounter.dll, LineCounter.pdb
```

The full-workbench popup hook now validates real SharpDevelop menu surfaces:

```text
LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All
```

Validated popup results:

```text
Main menu popup       -> ProGPU portable popup created and shown
ContextMenu popup     -> ProGPU portable popup created and shown with AddInTree-built solution context items
ComboBox drop-down    -> ProGPU portable popup created and shown
```

The earlier context-menu smoke failure was caused by opening the smoke `ContextMenu` before its placement target was attached to a `PresentationSource`; WPF correctly deferred the popup and then closed it when the target was still disconnected. The smoke harness now waits for a source-connected target before setting `ContextMenu.IsOpen`, matching the state a user-triggered context menu has after the workbench is visible. A macOS CoreGraphics synthetic right-click did not reach the app in this environment, so real pointer validation remains manual, but the in-app hook exercises the same WPF `ContextMenu` to `PopupRoot` to ProGPU popup path.

Latest captures/logs from this pass:

```text
Combined menu/context/combo popup smoke -> /tmp/sharpdevelop-popup-smoke-all-final.png
Combined popup trace log                -> /tmp/sharpdevelop-popup-smoke-all-final.log
LineCounter build smoke capture         -> /tmp/sharpdevelop-linecounter-build-smoke-final.png
LineCounter build smoke log             -> /tmp/sharpdevelop-linecounter-build-smoke-final.log
```

The local package feed was refreshed after rebuilding `PresentationFramework` in Release:

```text
/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal/LibreWPF.Transport.0.1.0-preview.sharpdevelop.1.nupkg
/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal/LibreWPF.Sdk.0.1.0-preview.sharpdevelop.1.nupkg
```

Fresh-cache package-mode validation used `NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final` with no manual copied WPF binaries:

```text
SharpDevelop.Full.LibreWpf restore/build -> succeeds, warnings only
All popup smoke from package output       -> /tmp/sharpdevelop-package-popup-smoke-all.png
All popup trace from package output       -> /tmp/sharpdevelop-package-popup-smoke-all.log
LineCounter package build smoke           -> /tmp/sharpdevelop-package-linecounter-build-smoke.png
LineCounter package build smoke log       -> /tmp/sharpdevelop-package-linecounter-build-smoke.log
```

### 2026-07-08 portability cleanup and validation

This pass removed the remaining high-value Windows-only calls that were still reachable in the LibreWPF SharpDevelop path:

- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Core/Project/Src/Services/FileUtility/FileUtility.cs` now skips Windows registry framework/SDK probes on non-Windows and searches `PATH`, `DOTNET_ROOT`, and installed dotnet SDK folders for portable tools.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Base/Project/Src/Project/Items/TypeLibrary.cs` now treats COM type library enumeration as a Windows-only feature and returns no libraries on non-Windows instead of walking `HKCR\TypeLib`.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Base/Project/Designer/TypeResolutionService.cs` skips the Visual Studio designer registry workaround on non-Windows.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Base/Project/Src/Gui/Dialogs/ReferenceDialog/AddWebReferenceDialog.cs` skips Internet Explorer typed-URL MRU import on non-Windows.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Base/Project/Src/Gui/Dialogs/ReferenceDialog/ServiceReference/SvcUtilPath.cs` now looks for `svcutil.exe` or `dotnet-svcutil` through the portable SDK/PATH resolver before failing closed.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Base/Project/Src/Gui/Dialogs/SharpDevelopAboutPanels.cs` falls back to `Environment.Version` outside Windows.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Base/Project/Src/Commands/ProjectMenuCommands.cs` keeps Sandcastle registry discovery Windows-only.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Logging/SDTraceListener.cs`, `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Sda/CallHelper.cs`, and `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Logging/ExceptionBox.cs` now avoid `Thread.SetApartmentState(...)` on non-Windows.

Validated commands:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Core/Project/ICSharpCode.Core.LibreWpf.csproj -c Debug --no-restore -v:minimal /p:GenerateFullPaths=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Debug --no-restore -v:minimal /p:GenerateFullPaths=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 LIBREWPF_TRACE_POPUP=1 LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=26000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=45000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln
```

Results:

```text
ICSharpCode.Core.LibreWpf              -> succeeds, 1 existing serialization warning
SharpDevelop.Full.LibreWpf             -> succeeds, 90 warnings, 0 errors before package-reference cleanup
Popup smoke                            -> main menu, AddInTree context menu, and ComboBox popup all opened
LineCounter package build smoke        -> Success, 0 errors, 4 existing sample warnings
```

Artifacts:

```text
Popup screenshot -> /tmp/sharpdevelop-package-popup-smoke-all-after-portable-guards.png
Popup log        -> /tmp/sharpdevelop-package-popup-smoke-all-after-portable-guards.log
Build screenshot -> /tmp/sharpdevelop-package-linecounter-build-smoke-after-portable-guards.png
Build log        -> /tmp/sharpdevelop-package-linecounter-build-smoke-after-portable-guards.log
```

At this intermediate checkpoint, the remaining warnings were mostly known package advisories/version-resolution warnings, legacy nullable/obsolete API warnings, duplicate compatibility type warnings in the temporary full wrapper, and intentional fail-closed compatibility stubs. The previous Windows registry and STA-thread analyzer clusters from Core/Base/SharpDevelop had been removed from the package-mode path.

After removing duplicate local `System.Configuration.ConfigurationManager` references and pinning the WCF packages to the resolved `10.0.652802` build, package restore/build still succeeds:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final dotnet restore /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -v:minimal /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Debug --no-restore -v:minimal /p:GenerateFullPaths=true /nr:false
```

Result:

```text
SharpDevelop.Full.LibreWpf             -> succeeds, 72 warnings, 0 errors
Duplicate ConfigurationManager warning -> removed
System.ServiceModel 10.0.0 resolution  -> removed
```

After source-warning cleanup in the LibreWPF compatibility path, the same build now reports:

```text
SharpDevelop.Full.LibreWpf             -> succeeds, 66 warnings, 0 errors
LineCounter package build smoke        -> /tmp/sharpdevelop-package-linecounter-build-smoke-after-warning-cleanup.log
LineCounter result                     -> Success, 0 errors, 4 existing sample warnings
```

After updating the SharpDevelop LibreWPF wrapper package graph to `Microsoft.Build`/`Microsoft.Build.Framework`/`Microsoft.Build.Tasks.Core`/`Microsoft.Build.Utilities.Core` `17.14.28` and explicitly overriding `System.Security.Cryptography.Xml` to `10.0.9`, the NuGet advisory warnings are gone:

```text
SharpDevelop.Full.LibreWpf             -> succeeds, 44 warnings, 0 errors
LineCounter package build smoke        -> /tmp/sharpdevelop-package-linecounter-build-smoke-after-package-advisory-cleanup.log
LineCounter result                     -> Success, 0 errors, 4 existing sample warnings
Popup package smoke                    -> /tmp/sharpdevelop-package-popup-smoke-all-after-package-advisory-cleanup.log
Popup result                           -> main menu, AddInTree context menu, and ComboBox popup all opened
```

## Latest SharpDevelop integration pass

Validated on 2026-07-07 against the local `LibreWPF.Sdk/0.1.0-preview.sharpdevelop.1` package cache.

Code changes exercised in this pass:

- `/Users/wieslawsoltes/GitHub/wpf/packaging/ProGPU.Wpf.Sdk/targets/ProGPU.Wpf.Sdk.targets` now copies the standalone `LibreWPF.WinFormsCompat.WindowsFormsIntegration` runtime assembly from the package cache deterministically, so package-mode SharpDevelop does not accidentally load the stale duplicate assembly carried by `LibreWPF.Transport`.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Project/ProjectService.cs` now supplies a LibreWPF C# MSBuild project fallback binding for legacy `.csproj`/C# project GUIDs when the historical CSharpBinding add-in descriptor cannot instantiate yet. This makes legacy C# projects load as `MSBuildBasedProject` instead of `ErrorProject`.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Base/Project/Src/Project/MSBuildInternals.cs` now normalizes unavailable legacy toolsets such as `ToolsVersion="4.0"` to the current MSBuild toolset under `LIBREWPF`.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Startup/SharpDevelopMain.cs` now configures `MSBUILD_EXE_PATH` and `MSBuildSDKsPath` from the installed dotnet SDK before SharpDevelop touches MSBuild. This fixes old projects importing `$(MSBuildBinPath)\Microsoft.CSharp.Targets` on macOS without requiring run-script environment variables.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Templates/Project/ProjectTemplateImpl.cs` now fails closed when a project-template `RunCommand` action resolves to no command instead of dereferencing a null command and terminating the workbench.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Base/Project/Project/ErrorProject.cs` logs guarded LibreWPF project-load diagnostics to stderr, which was used to isolate the project binding, toolset, and target import blockers.

Latest validation commands:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Debug --no-restore -v:quiet /p:GenerateFullPaths=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LibreWpfSharpDevelopSmoke/LibreWpfSharpDevelopSmoke.csproj -c Debug --no-restore -v:quiet /p:GenerateFullPaths=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=10000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=10000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/Command.cs
```

Current results:

```text
SharpDevelop.Full.LibreWpf       -> builds with the existing warning set only
LibreWpfSharpDevelopSmoke        -> builds cleanly
LineCounter.sln                  -> loads as a normal project node, not LineCounter (Error)
LineCounter/Src/Command.cs        -> opens through AvalonEdit in the full workbench
template RunCommand null action   -> no longer crashes project-template loading
```

Latest captures:

```text
LineCounter project loaded        -> /tmp/sharpdevelop-linecounter-auto-msbuild.png
LineCounter Command.cs editor      -> /tmp/sharpdevelop-linecounter-editor.png
Full workbench File menu popup     -> /tmp/sharpdevelop-full-menu-popup-auto.png
Smoke menu popup                   -> /tmp/sharpdevelop-popup-capture-Menu.png
Smoke context menu popup           -> /tmp/sharpdevelop-popup-capture-Context.png
Smoke ComboBox dropdown            -> /tmp/sharpdevelop-popup-capture-Combo.png
Smoke Core DropDownButton popup    -> /tmp/sharpdevelop-popup-capture-CoreDropDown.png
```

The LineCounter run no longer reports:

```text
No backend for project type installed.
The tools version "4.0" is unrecognized.
The imported project ".../Microsoft.CSharp.Targets" was not found.
```

The fallback C# project binding is an MVP bridge for project loading. Full SharpDevelop CSharpBinding parity still requires porting the language binding/add-in services, semantic editor services, build/debug commands, and project-template workflows through typed portable seams.

## Build validation

Validated commands:

```text
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/Microsoft.DotNet.Wpf/src/PresentationFramework/PresentationFramework.csproj -c Debug --no-cache -v:minimal
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf/ProGPU.Wpf.csproj -c Debug --no-cache -v:minimal
dotnet pack /Users/wieslawsoltes/GitHub/wpf/external/ProGPU/src/SkiaSharp/SkiaSharp.csproj -c Release -p:PackageVersion=0.1.0-preview.sharpdevelop.1
dotnet pack /Users/wieslawsoltes/GitHub/wpf/external/ProGPU/src/System.Drawing.Common/System.Drawing.Common.csproj -c Release -p:PackageVersion=0.1.0-preview.sharpdevelop.1
dotnet pack /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/System.Windows.Forms.csproj -c Release -p:PackageVersion=0.1.0-preview.sharpdevelop.1
dotnet pack /Users/wieslawsoltes/GitHub/wpf/external/ProGPU/src/ProGPU.Wpf.Interop/ProGPU.Wpf.Interop.csproj -c Release -p:PackageVersion=0.1.0-preview.sharpdevelop.1
dotnet pack /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf/ProGPU.Wpf.csproj -c Release -p:PackageVersion=0.1.0-preview.sharpdevelop.1
dotnet pack /Users/wieslawsoltes/GitHub/wpf/packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj -c Release -p:PackageVersion=0.1.0-preview.sharpdevelop.1
dotnet pack /Users/wieslawsoltes/GitHub/wpf/packaging/ProGPU.Wpf.Sdk/ProGPU.Wpf.Sdk.ArchNeutral.csproj -c Release -p:PackageVersion=0.1.0-preview.sharpdevelop.1
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.LibreWpf.csproj -c Debug --no-cache -v:minimal
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final /Users/wieslawsoltes/GitHub/wpf/.dotnet/dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/Base/Project/ICSharpCode.SharpDevelop.LibreWpf.csproj -c Debug --no-cache -v:quiet
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final /Users/wieslawsoltes/GitHub/wpf/.dotnet/dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.LibreWpf.csproj -c Debug --no-cache -v:quiet
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final /Users/wieslawsoltes/GitHub/wpf/.dotnet/dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Debug --no-cache -v:quiet
dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.LibreWpf.csproj -c Debug --no-cache -v:minimal
dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/ICSharpCode.SharpDevelop.Widgets/Project/ICSharpCode.SharpDevelop.Widgets.LibreWpf.csproj -c Debug --no-restore -v:minimal
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Debug --no-cache -v:minimal /p:GenerateFullPaths=true
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LibreWpfSharpDevelopSmoke/LibreWpfSharpDevelopSmoke.csproj -c Debug --no-cache -v:quiet /p:GenerateFullPaths=true /nr:false
```

Current results:

```text
AvalonDock.LibreWpf                            -> builds
ICSharpCode.AvalonEdit.LibreWpf                -> builds with obsolete CAS/serialization warnings from legacy AvalonEdit code
ICSharpCode.TreeView.LibreWpf                  -> builds
ICSharpCode.Core.LibreWpf                      -> builds; transitive builds still report Windows registry warnings when analyzers see the legacy framework-path probe
ICSharpCode.Core.Presentation.LibreWpf         -> builds without the previous Windows-only GetHbitmap warning
ICSharpCode.Core.WinForms.LibreWpf             -> builds
ICSharpCode.SharpDevelop.LibreWpf Base wrapper -> builds; ASMX/WCF compatibility remains fail-closed, while ResX read/write now has a portable XML-backed implementation
SharpDevelop.LibreWpf                         -> builds
SharpDevelop.Full.LibreWpf                    -> builds; starts and renders the full workbench shell with real toolbar/pad icons; opens a real .cs file through AvalonEdit with built-in C# lexical highlighting
ICSharpCode.SharpDevelop.Widgets.LibreWpf     -> builds with CLS-compliance warnings from System.Drawing-shaped public APIs
ProGPU.Wpf                                    -> builds warning-free
PresentationFramework                         -> builds with existing ReachFramework System.Collections.Immutable/System.Reflection.Metadata version-conflict warnings
LibreWPF.Sdk package                          -> packs in Release package mode
LibreWPF.Transport package                    -> packs in Release package mode
LibreWPF.ProGPU package                       -> packs in Release package mode
LibreWPF.Interop package                      -> packs in Release package mode
ProGPU.System.Drawing.Common package           -> packs
ProGPU.SkiaSharp package                       -> packs
LibreWPF.WinFormsCompat.System.Windows.Forms   -> packs
```

The final SharpDevelop validation was run from a fresh NuGet cache against `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal`, so the output is using the local `LibreWPF.Sdk`/`LibreWPF.Transport`/`LibreWPF.ProGPU` packages instead of manually copied WPF or ProGPU binaries.

After replacing the GDI image conversion, the fresh-cache package-mode build still succeeds. Remaining build warnings are from legacy AvalonEdit CAS/serialization APIs and Windows registry framework-path probes in `ICSharpCode.Core`.

After replacing the BinaryFormatter-backed image resource path, the full package-mode workbench was launched with:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll nologo noExceptionBox
```

The main IDE shell rendered successfully with actual resource icons instead of placeholder squares:

```text
Full IDE after image fallback -> /tmp/sharpdevelop-full-after-resource-fallback.png
```

The real full-workbench File menu was validated with:

```text
LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=Menu LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=12000 NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll nologo noExceptionBox
```

The latest capture shows the actual AddInTree-built File menu rendering over the full IDE:

```text
Full IDE File menu after lazy-menu event fix -> /tmp/sharpdevelop-full-menu-popup-after-menu-event-fix.png
```

The full workbench source-file open path was validated with:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-final LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=20000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Startup/SharpDevelopMain.cs
```

The latest capture shows the historical full workbench with an AvalonEdit document tab and visible source text:

```text
Full IDE source file through AvalonEdit built-in C# highlighting -> /tmp/sharpdevelop-full-csharp-highlighting-language-service.png
```

The same run reaches the expected workbench boundary:

```text
LibreWPF CodeEditorAdapter.FileNameChanged skipping source-tree text editor extensions for /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Startup/SharpDevelopMain.cs
LibreWPF CodeEditorAdapter.FileNameChanged language binding=ICSharpCode.SharpDevelop.DefaultLanguageBinding
LibreWPF CodeEditor.UpdateSyntaxHighlighting built-in highlighting=C#
LibreWPF AvalonEditDisplayBinding created ICSharpCode.AvalonEdit.AddIn.AvalonEditViewContent
LibreWPF FileService content for /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Startup/SharpDevelopMain.cs: ICSharpCode.AvalonEdit.AddIn.AvalonEditViewContent
LibreWPF WpfWorkbench.ShowView content=ICSharpCode.AvalonEdit.AddIn.AvalonEditViewContent windows=1 active=
```

`ICSharpCode.SharpDevelop.Widgets.LibreWpf` previously failed on broad WinForms API gaps. It now compiles after adding the typed compatibility surface for `System.Windows.Forms.Design`, `UITypeEditor`, `IWindowsFormsEditorService`, `NativeWindow`, `ContainerControl`, `UserControl`, `ListBox`, `ListView`, WinForms drag/drop/event types, `ImageList`, `Control.CreateGraphics()`, and the ProGPU drawing APIs needed by the sidebar code.

`ICSharpCode.SharpDevelop.LibreWpf` previously failed on AvalonEdit/NRefactory mode mismatches, missing ASMX/WCF/ResX compatibility surfaces, WinForms property-grid/list/tree APIs, and ProGPU `System.Drawing` gaps. It now builds against the local LibreWPF package cache. Legacy ASMX discovery and WCF metadata exchange remain intentionally fail-closed on LibreWPF until portable service implementations are designed; ResX read/write is now covered by the portable XML-backed compatibility layer and the LineCounter resource-update smoke.

## Popup validation

The `SharpDevelop.LibreWpf` shell can open one popup mode on startup via `LIBREWPF_SHARPDEVELOP_POPUP_SMOKE`:

```text
Menu         -> main File menu
Context      -> SharpTreeView context menu
Combo        -> ComboBox drop-down
CoreDropDown -> ICSharpCode.Core.Presentation.DropDownButton menu
AvalonEditCompletion -> AvalonEdit completion window
```

The SharpDevelop popup smoke app validates menu, context-menu, ComboBox, and `ICSharpCode.Core.Presentation.DropDownButton` popups against local package-mode LibreWPF output. A regression caused popup backing rectangles to be replayed at the top-left of the client area for context/combo/drop-down modes. The root cause was a ProGPU-side popup sizing feedback loop: WPF reported the popup root size, then the popup bridge inferred a different size from replayed GPU content bounds and fed that back into the portable presentation source every frame. Popup sizing is now source-driven through WPF's typed popup/client-size contract, and ProGPU replay bounds are not used as live popup window size.

Latest validated captures:

```text
Menu         -> /tmp/sharpdevelop-smoke-menu-traced-no-infer.png
Context      -> /tmp/sharpdevelop-smoke-context-no-infer.png
Combo        -> /tmp/sharpdevelop-smoke-combo-no-infer.png
CoreDropDown -> /tmp/sharpdevelop-smoke-core-dropdown-no-infer.png
Full IDE     -> /tmp/sharpdevelop-full-librewpf-popupfix.png
```

Latest SharpDevelop package-mode smoke captures after the image-resource fallback:

```text
Menu         -> /tmp/sharpdevelop-smoke-menu-after-resource-fallback.png
Context      -> /tmp/sharpdevelop-smoke-context-after-resource-fallback.png
Combo        -> /tmp/sharpdevelop-smoke-combo-after-resource-fallback.png
CoreDropDown -> /tmp/sharpdevelop-smoke-core-dropdown-after-resource-fallback.png
```

Latest full-IDE and focused smoke captures after the AvalonEdit open-path fixes:

```text
Full IDE source editor      -> /tmp/sharpdevelop-full-csharp-highlighting-language-service.png
Full IDE File menu          -> /tmp/sharpdevelop-full-menu-popup-after-highlighting.png
Smoke SharpTreeView context -> /tmp/sharpdevelop-smoke-context-current.png
Smoke ComboBox dropdown     -> /tmp/sharpdevelop-smoke-combo-current-foreground.png
Smoke Core drop-down button -> /tmp/sharpdevelop-smoke-core-dropdown-current.png
```

The real full-workbench File menu initially reported `IsSubmenuOpen=true` but did not render its popup. That path uses lazy AddInTree submenu expansion in `ICSharpCode.Core.Presentation.MenuService`; in LibreWPF, marking the `SubmenuOpened` routed event handled after replacing the dummy `ItemsSource` suppressed the popup path. The LibreWPF wrapper now leaves the event unhandled while keeping the Windows behavior unchanged.

Validated with `PROGPU_WPF_TRACE_POPUP=1`, package-built SharpDevelop output, and in-process WPF `Window.Close()` shutdown:

```text
Menu         -> /tmp/sharpdevelop-librewpf-package-Menu.png, status 0
Context      -> /tmp/sharpdevelop-librewpf-package-Context.png, status 0
Combo        -> /tmp/sharpdevelop-librewpf-package-Combo.png, status 0
CoreDropDown -> /tmp/sharpdevelop-librewpf-package-CoreDropDown.png, status 0
Shell        -> /tmp/sharpdevelop-librewpf-package-shell.png, status 0
```

The current console-traced app runs were also validated through the machine .NET 10 runtime because the repository-local `.dotnet` currently contains only .NET 11 preview runtime packs:

```text
LIBREWPF_SHARPDEVELOP_POPUP_SMOKE=Menu                 -> "Menu popup opened", exit 0
LIBREWPF_SHARPDEVELOP_POPUP_SMOKE=Context              -> "Project context popup opened", exit 0
LIBREWPF_SHARPDEVELOP_POPUP_SMOKE=Combo                -> "Configuration combo popup opened", exit 0
LIBREWPF_SHARPDEVELOP_POPUP_SMOKE=CoreDropDown         -> "Build dropdown popup opened", exit 0
LIBREWPF_SHARPDEVELOP_POPUP_SMOKE=AvalonEditCompletion -> "AvalonEdit completion opened", exit 0
no popup mode                                           -> clean auto-exit, exit 0
```

Representative traces:

```text
Menu:
ProGPU WPF popup: replay visible=True logical=(0,31.5) size=162x99 root=System.Windows.Controls.Primitives.PopupRoot visuals=62 content=25 renderData=52/52

Context:
ProGPU WPF popup: replay visible=True logical=(140,285) size=178x99 root=System.Windows.Controls.Primitives.PopupRoot visuals=60 content=26 renderData=47/47

Combo:
ProGPU WPF popup: replay visible=True logical=(372,73.5) size=140x55 root=System.Windows.Controls.Primitives.PopupRoot visuals=30 content=9 renderData=19/19

CoreDropDown:
ProGPU WPF popup: replay visible=True logical=(51,27.5) size=180x72 root=System.Windows.Controls.Primitives.PopupRoot visuals=49 content=20 renderData=36/36
```

The plain `SharpDevelop.LibreWpf` startup path also exits cleanly with `LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=1500`.

Package-mode validation on 2026-07-08 now uses the local LibreWPF package feed at `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal` and an isolated cache at `/tmp/sharpdevelop-librewpf-nuget-package-final`.

Latest full wrapper build:

```text
SharpDevelop.Full.LibreWpf.csproj -> Build succeeded, 38 warnings, 0 errors
```

The remaining warnings are not LibreWPF package advisories or Windows registry warnings. They are mostly duplicate-type conflicts from the temporary full-wrapper project composition (`RevisionClass` and `NativeMethods`) plus the existing `RunWorkbenchException` serialization warning.

Latest package-mode popup smoke:

```text
/tmp/sharpdevelop-package-popup-smoke-all-after-package-advisory-cleanup.log

LibreWPF full workbench menu popup opened
LibreWPF full workbench context menu Opened event
LibreWPF full workbench context menu popup opened items=27
LibreWPF full workbench ComboBox popup opened
```

Latest package-mode LineCounter solution smoke:

```text
/tmp/sharpdevelop-package-linecounter-build-smoke-after-host-cleanup.log

LibreWPF WorkbenchStartup opening: /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln
LibreWPF ProjectBrowserControl ViewSolution done rootNodes=1 solutionChildren=1 selected=<null>
LibreWPF build smoke result=Success errors=0 warnings=4
```

Latest combined full-workbench smoke after the portable ResX compatibility pass:

```text
/tmp/sharpdevelop-package-linecounter-combined-smoke-after-resx-compat.log

LibreWPF full workbench menu popup opened
LibreWPF ResX smoke result=Success files=1
LibreWPF full workbench context menu Opened event
LibreWPF full workbench context menu popup opened items=27
LibreWPF build smoke result=Success errors=0 warnings=4
LibreWPF full workbench ComboBox popup opened
```

The ResX smoke snapshots and restores the project resource files around `ResXConverter.UpdateResourceFiles(project)`, so validation does not leave `samples/LineCounter/Src/LineCounterBrowser.resx` dirty.

## ProGPU and LibreWPF changes exercised

- The SharpDevelop AvalonEdit SDK-style LibreWPF wrapper now embeds `Highlighting/Resources/*.xshd` and `Highlighting/Resources/*.xsd`, matching the legacy project resource behavior required by AvalonEdit's built-in highlighting manager.
- Popup retained visuals are positioned with the ProGPU scene visual `Offset` instead of pushing popup placement into every command transform.
- Source-built `PopupRoot` publishes a typed `IPortablePopupRootSource` marker. Normal retained main-tree replay skips portable popup roots, while the popup bridge explicitly includes them when replaying the dedicated popup layer.
- Popup sizing is driven by `IPortablePresentationSourceHost.TryUpdateRootVisualClientSize(...)` and WPF `PopupRoot` layout. The popup bridge no longer feeds ProGPU replay content bounds back into the portable presentation source, avoiding stale origin rectangles and frame-to-frame size oscillation.
- `IPortablePresentationSourceHost.TryUpdateRootVisualClientSize(...)` lets source-built WPF publish popup/client root sizing to the ProGPU host.
- Non-Windows popup screen restrictions now prefer primary screen bounds instead of the popup source's initial root rect.
- `ProGpuWpfWindowHost.Dispose()` now defers native Silk window disposal while the native run loop is active, avoiding the previous `Reset inside of the render loop` crash when WPF closes from dispatcher work.
- `ProGpuWpfWindowHost.Close()` and deferred disposal now call the Silk close path and actively wake the native event loop so WPF `Window.Close()` unwinds `IWindow.Run()` without external process termination.
- `PortableWindowActivationService` exposes typed dispatcher-timer promotion and window-disposed checks, allowing the ProGPU host to pump WPF `DispatcherTimer` shutdown work and fail closed when an older source assembly does not provide the seam.
- ProGPU `System.Drawing.Bitmap` now supports `Save(Stream, ImageFormat.Png)`, and the internal PNG encoder can write to caller-owned streams. SharpDevelop uses this for portable resource bitmap conversion in its LibreWPF wrapper.
- SharpDevelop's LibreWPF C# fallback project binding now returns a small `CompilableProject` implementation instead of a plain `MSBuildBasedProject`, restoring normal project behavior surfaces such as resource conversion for loaded `.csproj` files before the full CSharpBinding add-in is available.
- The LibreWPF ResX compatibility layer now implements the `System.Resources.ResXResourceReader`/`ResXResourceWriter` API shape SharpDevelop uses, including `IResourceReader`, `IResourceWriter`, metadata enumeration, file/stream constructors, and XML preservation for typed or binary entries. This removes the previous `PlatformNotSupportedException` from normal project resource-update paths.

## 2026-07-08 CSharpBinding and popup validation update

This pass enabled a fuller C# editor/project slice instead of keeping SharpDevelop on the earlier fallback language binding:

- Added `/Users/wieslawsoltes/GitHub/SharpDevelop/src/AddIns/BackendBindings/CSharpBinding/Project/CSharpBinding.LibreWpf.csproj`.
- Added `/Users/wieslawsoltes/GitHub/SharpDevelop/src/AddIns/BackendBindings/CSharpBinding/Project/CSharpBinding.LibreWpf.addin`.
- Wired `CSharpBinding.dll` and `CSharpBinding.addin` into `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj`.
- Re-enabled AvalonEdit source-tree syntax registration, text-editor extension creation, and source-created highlighter setup for the LibreWPF path.
- Fixed the old vendored Mono.Cecil reader so it can metadata-load .NET 10 runtime assemblies on this machine, including ARM64 PE images and the current `System.Private.CoreLib.dll` machine value `0xec20`, by allowing an `Unknown` metadata-only architecture instead of throwing before CLR metadata is read.
- Kept the smoke `DispatcherTimer` instances rooted on `WpfWorkbench`, avoiding lost delayed popup steps when testing menu, context menu, and ComboBox popups.

Validated command:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Debug -v:quiet /p:GenerateFullPaths=true /nr:false
```

Result:

```text
SharpDevelop.Full.LibreWpf             -> succeeds, 63 warnings, 0 errors
```

The warning count is from legacy Cecil/platform analyzer warnings plus temporary full-wrapper duplicate type conflicts. There are no build errors.

Full package-mode runtime smoke:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final \
LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 \
LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All \
LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution \
LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=15000 \
dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln \
  /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Startup/SharpDevelopMain.cs
```

Runtime results:

```text
Source file open                       -> AvalonEditDisplayBinding
Syntax definitions                     -> source-tree definitions registered=6
Editor extension                       -> CSharpTextEditorExtension attached
Language binding                       -> CSharpBinding.CSharpLanguageBinding
Project binding                        -> CSharpProject
Default parser references              -> System.Private.CoreLib, System.Private.Uri, System.Linq loaded through Cecil
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
WinForms PropertyGrid ContextMenuStrip -> Opened, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1, original file restored
Shutdown                               -> normal WPF close through smoke timer
```

No-smoke startup was also validated by launching the same solution/source file without `LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS`; the process remained alive after 10 seconds and was then terminated by the test harness. This confirms the CSharpBinding parser no longer terminates the dispatcher after startup.

## 2026-07-08 editor completion and cursor/popup validation

This pass added deterministic full-workbench editor-completion validation and fixed a native cursor/input-context edge hit by SharpDevelop popup/completion smoke runs.

- Added `LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE` to the SharpDevelop LibreWPF workbench smoke harness.
- `Source` completion uses `LineCounterBrowser.` and opens the source-local completion window with 4 items.
- `Framework` completion now waits for parser/project loading, selects a live marker in the active editor, and opens completion on `extensions.` with 39 items. The parser fallback adds 5 default .NET runtime references when the macOS SDK-style wrapper has no design-time references, and `System.Console` resolves as a class through Cecil/project content.
- `Console.` in `LineCounterBrowser.cs` was not used as the final framework marker because it lands in a disabled `#else` branch under the LineCounter sample's `IMPR2` define; that produced zero completion items despite valid framework references.
- `SilkNetWpfCursorService.SetCursor(...)` now returns `false` for uninitialized `IView` instances and catches `InvalidOperationException` from `CreateInput(...)`. Cursor updates are advisory; the normal input attachment path owns live Silk.NET input-context creation.
- The local package used by SharpDevelop was refreshed as `LibreWPF.ProGPU.0.1.0-preview.sharpdevelop.1`.

Validated SharpDevelop package-mode command:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final \
LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 \
LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All \
LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=Framework \
LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution \
LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=26000 \
dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/Extensibility.cs
```

Runtime results:

```text
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
WinForms PropertyGrid ContextMenuStrip -> Opened, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1
AvalonEdit completion popup            -> Opened, bindings=7, items=39, marker=extensions.
Shutdown                               -> normal WPF close through smoke timer
Cursor/input-context exception         -> not reproduced after SilkNetWpfCursorService guard
```

Focused WPF validation:

```text
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
DOTNET_ROLL_FORWARD=Major /Users/wieslawsoltes/GitHub/wpf/.dotnet/dotnet \
  /Users/wieslawsoltes/GitHub/wpf/.dotnet/sdk/11.0.100-preview.4.26210.111/vstest.console.dll \
  /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll \
  /TestCaseFilter:FullyQualifiedName~SilkNetWpfCursorServiceTests
```

Result:

```text
ProGPU.Wpf.Tests build                 -> succeeds, 0 warnings, 0 errors
SilkNetWpfCursorServiceTests           -> Passed, 14 total
```

## 2026-07-08 WinForms file/folder dialog service bridge

This pass added a typed portable dialog bridge for SharpDevelop code paths that still use WinForms `OpenFileDialog`, `SaveFileDialog`, and `FolderBrowserDialog`.

- `/Users/wieslawsoltes/GitHub/wpf/external/ProGPU/src/ProGPU.Wpf.Interop/PortableWpfServiceRegistry.cs` now exposes a neutral `PortableWpfServiceKey.WinForms` key and a typed `FileDialogServiceRegistered` notification so late-loaded compatibility assemblies can attach to the existing portable file-dialog service without reflection.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/PortableWinFormsDialogService.cs` registers a WinForms dialog registrar through a module initializer and stores the typed `Func<PortableFileDialogRequest, string?>` callback.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/WinFormsCompatTypes.cs` now routes `OpenFileDialog.ShowDialog()`, `SaveFileDialog.ShowDialog()`, and `FolderBrowserDialog.ShowDialog()` through `PortableWinFormsDialogService`, updates `FileName`/`FileNames`/`SelectedPath`, and returns `DialogResult.OK` only when the platform service returns a value.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf/WpfPortableWindowActivation.cs` registers the WinForms dialog service with the same `ShowPortableFileDialog` path used by source-built WPF `Microsoft.Win32` dialogs, including late registration through `PortableWpfServiceRegistry.FileDialogServiceRegistered`.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/Platform/PortableWinFormsDialogServiceTests.cs` covers open-file, save-file, and folder-pick requests against the typed `PortableFileDialogRequest` contract.

Focused validation:

```text
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/System.Windows.Forms.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf/ProGPU.Wpf.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
DOTNET_ROLL_FORWARD=Major /Users/wieslawsoltes/GitHub/wpf/.dotnet/dotnet \
  /Users/wieslawsoltes/GitHub/wpf/.dotnet/sdk/11.0.100-preview.4.26210.111/vstest.console.dll \
  /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll \
  /TestCaseFilter:FullyQualifiedName~PortableWinFormsDialogServiceTests
```

Result:

```text
System.Windows.Forms compat build      -> succeeds, existing compatibility warnings only
ProGPU.Wpf build                       -> succeeds, 0 warnings, 0 errors
ProGPU.Wpf.Tests build                 -> succeeds, existing compatibility warnings only
PortableWinFormsDialogServiceTests     -> Passed, 3 total
```

SharpDevelop package-mode validation after repacking `LibreWPF.Interop`, `LibreWPF.WinFormsCompat.System.Windows.Forms`, and `LibreWPF.ProGPU` into `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal`:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Debug -v:quiet /p:GenerateFullPaths=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final \
LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 \
LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All \
LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=Framework \
LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution \
LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=26000 \
dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/Extensibility.cs
```

Runtime result:

```text
SharpDevelop.Full.LibreWpf build       -> succeeds, 88 warnings, 0 errors
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
WinForms PropertyGrid ContextMenuStrip -> Opened, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1
AvalonEdit completion popup            -> Opened, bindings=7, items=39, marker=extensions.
Shutdown                               -> normal WPF close through smoke timer
```

Latest combined smoke log:

```text
/tmp/sharpdevelop-librewpf-combined-after-winforms-dialogs.log
```

The typed bridge proves the SharpDevelop/WinForms compatibility layer can now reach the existing ProGPU/Silk.NET platform file-dialog service. Native picker UI interaction was not manually clicked in this run; the underlying OS dialog implementation remains the existing `ProcessWpfFileDialogService` path and should be included in the next desktop manual pass.

## 2026-07-08 WinForms clipboard and hosted popup close pass

This pass moved WinForms clipboard support from process-local state to the same typed portable clipboard service used by LibreWPF `PresentationCore`, and fixed hosted WinForms context-menu close propagation.

- `/Users/wieslawsoltes/GitHub/wpf/external/ProGPU/src/ProGPU.Wpf.Interop/PortableWpfServiceRegistry.cs` now raises `ClipboardServiceRegistered` for typed clipboard registrars, mirroring the file-dialog late-registration path.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf/WpfPortableWindowActivation.cs` registers `PortableWpfServiceKey.WinForms` clipboard services with `GetPortableClipboardText`/`SetPortableClipboardText`, both during activation bootstrap and on late registration.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/PortableWinFormsClipboardService.cs` registers the WinForms clipboard service and keeps rich `IDataObject` payloads in managed state while mirroring text formats through the platform clipboard callback.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/WinFormsCompatTypes.cs` now routes WinForms `Clipboard.Clear()`, `SetDataObject()`, `GetDataObject()`, `SetText()`, `GetText()`, and `ContainsText()` through the portable service. `DataObject.SetData(object)` now publishes string payloads through the normal WinForms `Text`, `UnicodeText`, and `StringFormat` formats.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsHost.cs` now keeps the generated WPF `ContextMenu` and backing WinForms `ContextMenuStrip` closed state synchronized both ways. Direct `ContextMenuStrip.Close()` now closes the WPF/ProGPU popup, and WPF popup close still closes the backing strip.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Workbench/WpfWorkbench.cs` now closes the direct WinForms context-menu smoke after it verifies the menu opened, so later popup validations are not blocked by an intentionally open context menu.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/Platform/PortableWinFormsClipboardServiceTests.cs` covers WinForms text mirroring, native text reads, text `DataObject` mirroring, and string payload format publication.

Focused validation:

```text
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/System.Windows.Forms.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsIntegration.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
DOTNET_ROLL_FORWARD=Major /Users/wieslawsoltes/GitHub/wpf/.dotnet/dotnet \
  /Users/wieslawsoltes/GitHub/wpf/.dotnet/sdk/11.0.100-preview.4.26210.111/vstest.console.dll \
  /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll \
  /TestCaseFilter:"FullyQualifiedName~PortableWinFormsClipboardServiceTests|FullyQualifiedName~PortableWinFormsDialogServiceTests"
```

Result:

```text
System.Windows.Forms compat build      -> succeeds, existing compatibility warnings only
WindowsFormsIntegration build          -> succeeds, existing compatibility/version-resolution warnings only
ProGPU.Wpf.Tests build                 -> succeeds, existing compatibility warnings only
WinForms clipboard/dialog tests        -> Passed, 7 total
```

Package-mode validation after repacking `LibreWPF.Interop`, `LibreWPF.WinFormsCompat.System.Windows.Forms`, `LibreWPF.WinFormsCompat.WindowsFormsIntegration`, and `LibreWPF.ProGPU` into `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal`:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Debug -v:quiet /p:GenerateFullPaths=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final \
LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 \
LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All \
LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=Framework \
LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution \
LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=36000 \
dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/Extensibility.cs
```

Runtime result:

```text
SharpDevelop.Full.LibreWpf build       -> succeeds, 88 warnings, 0 errors
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
WinForms PropertyGrid ContextMenuStrip -> Opened and Closed, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1
AvalonEdit completion popup            -> Opened, bindings=7, items=39, marker=extensions.
Shutdown                               -> normal WPF close through smoke timer
```

Latest combined smoke log:

```text
/tmp/sharpdevelop-librewpf-combined-after-winforms-clipboard-and-close.log
```

## 2026-07-08 WinForms color dialog and refreshed SharpDevelop popup pass

This pass closed the next SharpDevelop WinForms compatibility gap found after the popup/file-dialog/clipboard work: `SharpDevelopColorDialog` derives from `System.Windows.Forms.ColorDialog`, but the LibreWPF WinFormsCompat `ColorDialog.RunDialog(...)` still returned cancel unconditionally.

- `/Users/wieslawsoltes/GitHub/wpf/external/ProGPU/src/ProGPU.Wpf.Interop/PortableWpfServiceRegistry.cs` now has the typed `PortableColorDialogRequest` DTO and `IPortableColorDialogServiceRegistrar` service registration.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/PortableWinFormsColorDialogService.cs` registers the WinForms color-dialog service under `PortableWpfServiceKey.WinForms`.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/WinFormsCompatTypes.cs` now routes `ColorDialog.RunDialog(...)` through the portable service and updates `Color` only when the platform picker returns a selected ARGB value.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf/Platform/IWpfPlatformServices.cs` adds the neutral `IWpfColorDialogService` contract and `WpfColorDialogOptions`.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf/Platform/ProcessWpfColorDialogService.cs` provides a process-backed native picker implementation for Windows PowerShell/WinForms, macOS `osascript choose color`, and Linux `zenity`/`kdialog`, with shared parsing for decimal ARGB, hex, `rgb(...)`, and comma-separated RGB output.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf/WpfPortableWindowActivation.cs` registers WinForms color dialogs during activation bootstrap and late service registration.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/Platform/PortableWinFormsColorDialogServiceTests.cs` and `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/Platform/ProcessWpfColorDialogServiceTests.cs` cover the WinForms registrar bridge, cancel semantics, native command generation, parsing, Linux fallback, and unsupported-platform failure.

Focused validation:

```text
dotnet build /Users/wieslawsoltes/GitHub/wpf/external/ProGPU/src/ProGPU.Wpf.Interop/ProGPU.Wpf.Interop.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/System.Windows.Forms.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf/ProGPU.Wpf.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
DOTNET_ROLL_FORWARD=Major /Users/wieslawsoltes/GitHub/wpf/.dotnet/dotnet \
  /Users/wieslawsoltes/GitHub/wpf/.dotnet/sdk/11.0.100-preview.4.26210.111/vstest.console.dll \
  /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll \
  /TestCaseFilter:"FullyQualifiedName~PortableWinFormsColorDialogServiceTests|FullyQualifiedName~ProcessWpfColorDialogServiceTests|FullyQualifiedName~PortableWinFormsClipboardServiceTests|FullyQualifiedName~PortableWinFormsDialogServiceTests"
```

Result:

```text
ProGPU.Wpf.Interop build               -> succeeds, 0 warnings, 0 errors
System.Windows.Forms compat build      -> succeeds, existing compatibility warnings only
ProGPU.Wpf build                       -> succeeds, 0 warnings, 0 errors
ProGPU.Wpf.Tests build                 -> succeeds, existing compatibility warnings only
WinForms color/clipboard/dialog tests  -> Passed, 20 total
```

Package-mode validation after repacking `LibreWPF.Interop`, `LibreWPF.WinFormsCompat.System.Windows.Forms`, and `LibreWPF.ProGPU` into `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal` and clearing the isolated package cache:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Debug -v:quiet /p:GenerateFullPaths=true /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final \
LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 \
LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All \
LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=Framework \
LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution \
LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=36000 \
dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/Extensibility.cs
```

Runtime result:

```text
SharpDevelop.Full.LibreWpf build       -> succeeds, 182 warnings, 0 errors
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
WinForms PropertyGrid ContextMenuStrip -> Opened and Closed, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1
AvalonEdit completion popup            -> Opened, bindings=7, items=39, marker=extensions.
Shutdown                               -> normal WPF close through smoke timer, exit code 0
```

Latest combined smoke log:

```text
/tmp/sharpdevelop-librewpf-combined-after-color-dialog.log
```

Native color picker UI was not manually clicked during the automated SharpDevelop smoke because it would block the run. The color-dialog bridge itself is covered by focused registrar tests and the process-backed platform service tests; a desktop manual pass should still click the real picker from SharpDevelop's editor command path.

## 2026-07-08 SharpDevelop popup and completion validation

This pass focused on the remaining popup class that was still regressing in the full SharpDevelop shell: AvalonEdit completion. The completion surface is not a `Popup`; it is a non-activating owned `Window` (`ShowActivated=false`) that closes itself when the owner loses focus. LibreWPF already exported `ShowActivated` and `Owner` through the typed `PortableWindowState` seam, and this pass fixed the ProGPU host behavior around that contract:

- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf/ProGpuWpfWindowHost.cs` now reports host visibility from the WPF-side show intent as well as the native Silk window visibility. That prevents owner-deactivation suppression from missing the brief interval where WPF has asked to show an owned non-activating window but the native window visibility has not caught up yet.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf/WpfPortableWindowActivation.cs` now routes host input for `ShowActivated=false` owned windows without marking the child WPF window active. Input still goes to the owned window, while the owner window remains the active WPF window, matching AvalonEdit completion behavior.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/WpfPortableWindowActivationTests.cs` adds `HostInputForNonActivatingOwnedWindowKeepsOwnerActive` so this behavior stays pinned.

Focused validation:

```text
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet /Users/wieslawsoltes/GitHub/wpf/.dotnet/sdk/11.0.100-preview.4.26210.111/vstest.console.dll \
  /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll \
  /TestCaseFilter:"FullyQualifiedName~WpfPortableWindowActivationTests|FullyQualifiedName~ProGpuWpfWindowHostTests"
```

Result:

```text
ProGPU.Wpf.Tests focused window/popup/input slice -> Passed, 139 total
```

The local `LibreWPF.ProGPU` package was refreshed into `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal`, SharpDevelop was rebuilt from the isolated package cache, and the full package-mode workbench smoke was run with the system .NET 10 host. Running with the repository-local .NET 11 preview host reproduced a .NET reference mismatch in SharpDevelop's in-app build service; the .NET 10 host is the correct validation host for the current `net10.0-windows` SharpDevelop package-mode output.

Validation command:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final \
LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 \
LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All \
LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=Framework \
LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution \
LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=36000 \
dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/Extensibility.cs
```

Runtime result:

```text
LineCounter.sln load                       -> succeeds as CSharpProject
Extensibility.cs AvalonEdit open           -> succeeds with CSharpTextEditorExtension attached
Main menu popup                            -> opened
AddInTree solution context menu popup      -> opened, 27 items
ComboBox popup                             -> opened
Property pad smoke                         -> Success, selected=CSharpProject, rows=22
WinForms PropertyGrid ContextMenuStrip     -> Opened and Closed, 3 items
LineCounter build smoke                    -> Success, 0 errors, 4 existing sample warnings
ResX smoke                                 -> Success, files=1
AvalonEdit completion popup                -> Opened, bindings=7, items=39, marker=extensions.
Shutdown                                   -> normal WPF close through smoke timer, exit code 0
```

Latest combined smoke log:

```text
/tmp/sharpdevelop-librewpf-combined-system-dotnet10.log
```

## 2026-07-08 WinForms layout/control compatibility pass

This pass focused on the next SharpDevelop surfaces that were reachable after popup and completion support: WinForms-hosted dialogs and pads that depend on `SplitContainer`, `TabControl`, and `DataGridView`.

- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/WinFormsCompatTypes.cs` now publishes `SplitContainer.Panel1` and `Panel2` through the normal `Controls` tree so `WindowsFormsHost` can traverse and render their hosted children.
- The portable `TabControl` now keeps `Controls` and `TabPages` synchronized without reflection or object-shape probing. This covers SharpDevelop-era code that adds pages either through `TabPages.Add(...)` or through the inherited controls collection.
- The portable `DataGridView` now has typed row add/remove events, `Rows.Add(params object?[] values)`, row-header width state, and per-column width state needed by SharpDevelop and Xceed-like WinForms grids.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsHost.cs` now lays out and renders `SplitContainer`, `TabControl`, `TabPage`, and `DataGridView` using typed WinFormsCompat APIs. This unblocks visible hosted content in SharpDevelop dialogs such as reference/project/package editors without adding WPF managed-code hacks.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/Platform/PortableWinFormsControlCompatibilityTests.cs` pins the new child-tree and event behavior for `SplitContainer`, `TabControl`, and `DataGridView`.
- `/Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/Workbench/WpfWorkbench.cs` now sequences the WinForms context-menu smoke before the AvalonEdit completion smoke. The previous combined smoke could overlap a hosted WinForms context menu with completion startup and report a false `NoWindow` result even though both surfaces passed independently.

Focused validation:

```text
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/System.Windows.Forms.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsIntegration.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet /Users/wieslawsoltes/GitHub/wpf/.dotnet/sdk/11.0.100-preview.4.26210.111/vstest.console.dll \
  /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll \
  /TestCaseFilter:"FullyQualifiedName~PortableWinFormsControlCompatibilityTests"
```

Result:

```text
System.Windows.Forms compat build      -> succeeds, existing compatibility warnings only
WindowsFormsIntegration compat build   -> succeeds, existing compatibility warnings only
ProGPU.Wpf.Tests build                 -> succeeds, existing compatibility warnings only
WinForms control compatibility tests   -> Passed, 3 total
```

After repacking `LibreWPF.WinFormsCompat.System.Windows.Forms` and `LibreWPF.WinFormsCompat.WindowsFormsIntegration` as `0.1.0-preview.sharpdevelop.1` into `/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal`, the full SharpDevelop package-mode wrapper was rebuilt and the combined runtime smoke was rerun with the system .NET 10 host.

Runtime validation command:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final \
LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 \
LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All \
LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=Framework \
LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution \
LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=38000 \
dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/Extensibility.cs
```

Runtime result:

```text
SharpDevelop.Full.LibreWpf build       -> succeeds, 182 warnings, 0 errors
LineCounter.sln load                   -> succeeds as CSharpProject
Extensibility.cs AvalonEdit open       -> succeeds with CSharpTextEditorExtension attached
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
WinForms PropertyGrid ContextMenuStrip -> Opened and Closed, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1
AvalonEdit completion popup            -> Opened, bindings=7, items=39, marker=extensions.
Shutdown                               -> normal WPF close through smoke timer, exit code 0
```

Latest combined smoke log:

```text
/tmp/sharpdevelop-librewpf-combined-after-winforms-layout-sequenced.log
```

## 2026-07-08 WinForms selection/tree compatibility pass

This pass followed up on SharpDevelop hosted dialogs and pads that need more than layout-only WinForms compatibility. The focus was typed selection and event behavior for controls used by New File/New Project/reference-style dialogs, solution trees, property pads, and hosted context-menu surfaces.

- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/WinFormsCompatTypes.cs` now keeps `ListView.Items`, `ListView.SelectedItems`, and `ListViewItem.Selected` synchronized through the owning `ListView` without reflection or object-shape probing. Selection changes raise `SelectedIndexChanged`, and add/remove/clear paths attach or detach the owner so item state remains coherent.
- `ListView.GetItemAt(...)` now performs details-view header and row hit testing, so `WindowsFormsHost` can select the item under the pointer instead of falling back to the first item.
- `TreeView.SelectedNode` now raises cancellable `BeforeSelect` and `AfterSelect` events, rejects nodes owned by another tree, and invalidates the host surface after a real selection change.
- `TreeNode.Expand()`, `Collapse()`, and `Toggle()` now route through typed `TreeView` event raisers for `BeforeExpand`, `AfterExpand`, and `BeforeCollapse`, including cancellation for the before events.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsHost.cs` now applies default mouse selection for hosted `ListView` controls and records rendered `TreeNode.Bounds` in client coordinates, matching the coordinates used by WinForms hit testing.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/Platform/PortableWinFormsControlCompatibilityTests.cs` now covers `ListView` selected-item synchronization, cancellable `TreeView.SelectedNode` changes, and `TreeNode` expand/collapse events in addition to the earlier split, tab, and grid coverage.

Focused validation:

```text
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/System.Windows.Forms.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsIntegration.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release -v:quiet /p:UseSharedCompilation=false
dotnet /Users/wieslawsoltes/GitHub/wpf/.dotnet/sdk/11.0.100-preview.4.26210.111/vstest.console.dll \
  /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll \
  /TestCaseFilter:"FullyQualifiedName~PortableWinFormsControlCompatibilityTests"
```

Result:

```text
System.Windows.Forms compat build      -> succeeds, existing compatibility warnings only
WindowsFormsIntegration compat build   -> succeeds, existing compatibility warnings only
ProGPU.Wpf.Tests build                 -> succeeds
WinForms control compatibility tests   -> Passed, 6 total
```

After repacking `LibreWPF.WinFormsCompat.System.Windows.Forms` and `LibreWPF.WinFormsCompat.WindowsFormsIntegration` as `0.1.0-preview.sharpdevelop.1`, the package-mode SharpDevelop wrapper was rebuilt with the system .NET 10 SDK/runtime.

Build result:

```text
SharpDevelop.Full.LibreWpf build       -> succeeds, 88 warnings, 0 errors
```

Runtime validation command:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-package-final \
LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 \
LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All \
LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=Framework \
LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution \
LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 \
LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=38000 \
dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Debug/net10.0-windows/SharpDevelop.dll \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln \
  /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/Extensibility.cs
```

Runtime result:

```text
LineCounter.sln load                   -> succeeds as CSharpProject
Extensibility.cs AvalonEdit open       -> succeeds with CSharpTextEditorExtension attached
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
WinForms PropertyGrid ContextMenuStrip -> Opened and Closed, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1
AvalonEdit completion popup            -> Opened, bindings=7, items=39, marker=extensions.
Shutdown                               -> normal WPF close through smoke timer, exit code 0
```

The only `ERROR` text in the latest log is SharpDevelop's existing missing `log4net` configuration message; no LibreWPF runtime exception was reported.

Latest combined smoke log:

```text
/tmp/sharpdevelop-librewpf-combined-after-winforms-selection.log
```

Additional popup regression coverage:

```text
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release -v:quiet /p:UseSharedCompilation=false
dotnet /Users/wieslawsoltes/GitHub/wpf/.dotnet/sdk/11.0.100-preview.4.26210.111/vstest.console.dll \
  /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll \
  /TestCaseFilter:"FullyQualifiedName~ProGpuWpfWindowHostTests.PortablePopup"
```

Result:

```text
ProGPU.Wpf.Tests build                 -> succeeds
Portable popup host tests              -> Passed, 4 total
```

The added popup coverage verifies that ProGPU-hosted popup input continues to route through the popup presentation source with local coordinates after a non-1.0 DPI scale is attached. This protects SharpDevelop menu, context-menu, and ComboBox popup interaction on HiDPI displays without adding SharpDevelop-specific workarounds.

## 2026-07-08 WinForms ListView activation pass

This pass addressed the next SharpDevelop WinForms compatibility gap found by source audit: many real dialogs and pads subscribe to `ListView.ItemActivate`, including reference panels, Attach to Process, FileScout, AddInScout, Wix setup dialog lists, and code-coverage/resource-toolkit list surfaces. The previous compat `ListView` declared the event but never raised it.

- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/WinFormsCompatTypes.cs` now exposes typed `ListView` activation through `RaiseItemActivate()` and `TryActivateItemAt(x, y)`. Activation selects the hit item before raising `ItemActivate`, matching the way SharpDevelop code expects `SelectedItems[0]` to be valid in activation handlers.
- The base compat `Control` now exposes `RaiseMouseDoubleClick(...)` so hosted controls can receive the normal double-click lifecycle without reflection or private event access.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsHost.cs` now routes WPF double-clicks on hosted `ListView` controls to the typed `ListView.TryActivateItemAt(...)` path after normal mouse up/click delivery.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/Platform/PortableWinFormsControlCompatibilityTests.cs` now covers item activation selection, `ItemActivate` delivery, and no-op behavior when activation misses every row.

Focused validation:

```text
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/System.Windows.Forms.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsIntegration.csproj -c Release --no-restore -v:minimal /p:UseSharedCompilation=false
dotnet build /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release -v:quiet /p:UseSharedCompilation=false
dotnet /Users/wieslawsoltes/GitHub/wpf/.dotnet/sdk/11.0.100-preview.4.26210.111/vstest.console.dll \
  /Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll \
  /TestCaseFilter:"FullyQualifiedName~PortableWinFormsControlCompatibilityTests"
```

Result:

```text
System.Windows.Forms compat build      -> succeeds, existing compatibility warnings only
WindowsFormsIntegration compat build   -> succeeds, existing compatibility warnings only
ProGPU.Wpf.Tests build                 -> succeeds
WinForms control compatibility tests   -> Passed, 7 total
```

After repacking `LibreWPF.WinFormsCompat.System.Windows.Forms` and `LibreWPF.WinFormsCompat.WindowsFormsIntegration` as `0.1.0-preview.sharpdevelop.1`, the package-mode SharpDevelop wrapper was rebuilt and the broad runtime smoke was rerun.

Runtime result:

```text
SharpDevelop.Full.LibreWpf build       -> succeeds, 88 warnings, 0 errors
LineCounter.sln load                   -> succeeds as CSharpProject
Extensibility.cs AvalonEdit open       -> succeeds with CSharpTextEditorExtension attached
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
WinForms PropertyGrid ContextMenuStrip -> Opened and Closed, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1
AvalonEdit completion popup            -> Opened, bindings=7, items=39, marker=extensions.
Shutdown                               -> normal WPF close through smoke timer, exit code 0
```

Latest combined smoke log:

```text
/tmp/sharpdevelop-librewpf-combined-after-listview-activation.log
```

## 2026-07-08 hosted WinForms keyboard and TreeView compatibility pass

This pass continued the SharpDevelop compatibility work in the reusable LibreWPF WinForms shim instead of adding SharpDevelop-local workarounds.

- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/System.Windows.Forms/WinFormsCompatTypes.cs` now exposes typed key dispatch helpers on `Control` (`RaiseKeyDown`, `RaiseKeyUp`, `RaiseKeyPress`) and routes them through the normal virtual `OnKeyDown`/`OnKeyUp`/`OnKeyPress` lifecycle.
- `TextBoxBase` now supports selection replacement, `AppendText`, `Select`, `SelectAll`, `Cut`, `Copy`, `Paste`, `ApplyTextInput`, and Back/Delete editing through typed key events. This gives hosted WinForms text controls enough behavior for SharpDevelop property/tool windows without private-field or reflection probes.
- `/Users/wieslawsoltes/GitHub/wpf/src/LibreWPF.WinFormsCompat/WindowsFormsIntegration/WindowsFormsHost.cs` now keeps a typed focused hosted control, forwards WPF key up/down/text input to the focused WinForms control, and preserves `ListView.ItemActivate` on Enter when a selected row is active.
- `TreeView` now publishes `AfterCollapse`, clears selection when selected nodes are removed, and lets root `TreeNode.Remove()` detach through the owning `TreeView.Nodes` collection. This closes a project-browser class of issues where SharpDevelop removes root-level project or solution tree nodes.
- `/Users/wieslawsoltes/GitHub/wpf/src/ProGPU.Wpf.Tests/Platform/PortableWinFormsControlCompatibilityTests.cs` now covers hosted text editing semantics, inherited key event delivery, `AfterCollapse`, and root-node removal.

Focused validation:

```text
System.Windows.Forms compat build      -> succeeds, existing compatibility warnings only
WindowsFormsIntegration compat build   -> succeeds, existing compatibility warnings only
ProGPU.Wpf.Tests build                 -> succeeds
WinForms control compatibility tests   -> Passed, 10 total
ProGPU WPF host/popup tests            -> Passed, 93 total
```

After repacking `LibreWPF.WinFormsCompat.System.Windows.Forms` and `LibreWPF.WinFormsCompat.WindowsFormsIntegration` as `0.1.0-preview.sharpdevelop.1`, the package-mode SharpDevelop wrapper was rebuilt and the broad runtime smoke was rerun.

Runtime result:

```text
SharpDevelop.Full.LibreWpf build       -> succeeds, 88 warnings, 0 errors
LineCounter.sln load                   -> succeeds as CSharpProject
Extensibility.cs AvalonEdit open       -> succeeds with CSharpTextEditorExtension attached
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
WinForms PropertyGrid ContextMenuStrip -> Opened and Closed, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1
AvalonEdit completion popup            -> Opened, bindings=7, items=39, marker=extensions.
Shutdown                               -> normal WPF close through smoke timer, exit code 0
```

Latest combined smoke logs:

```text
/tmp/sharpdevelop-librewpf-combined-after-winforms-keyboard.log
/tmp/sharpdevelop-librewpf-combined-after-treeview-compat.log
```

## 2026-07-08 ImageList key compatibility pass

SharpDevelop and several add-ins use keyed WinForms image lists for project browser nodes, XML tree nodes, template lists, sorted list headers, FileScout, AddInScout, and resource/tool windows. The compat `ImageList.ImageCollection` previously only stored images and key names; it did not expose normal keyed lookup behavior.

- `ImageList.ImageCollection` now supports `Empty`, `Keys`, writable integer indexing, keyed indexing, `Add(Icon)`, `Add(string, Icon)`, `AddRange(Image[])`, `ContainsKey`, `IndexOf`, `IndexOfKey`, `Remove`, `RemoveAt`, and `RemoveByKey`.
- `TreeNode` now supports `ImageKey` and `SelectedImageKey`, invalidates its owner tree on changes, and clears numeric image indices when keyed image properties are assigned. This matches the SharpDevelop XML editor/tree-node pattern that switches between normal and ghost image keys during cut/paste operations.
- `PortableWinFormsControlCompatibilityTests` now covers keyed image lookup, key enumeration, keyed removal, and keyed tree-node image assignment.

Focused validation:

```text
System.Windows.Forms compat build      -> succeeds, existing compatibility/CLS warnings only
ProGPU.Wpf.Tests build                 -> succeeds
WinForms control compatibility tests   -> Passed, 11 total
SharpDevelop.Full.LibreWpf build       -> succeeds, 88 warnings, 0 errors
```

## 2026-07-08 ListView header and sorting compatibility pass

SharpDevelop add-ins such as LineCounter use hosted WinForms `ListView` controls with clickable detail headers and `ListViewItemSorter` comparers. The compat shim already stored columns, items, groups, and sorters, but the hosted WPF surface did not translate header clicks into `ColumnClick`.

- `System.Windows.Forms.ListView` now exposes typed `RaiseColumnClick(int)` and `TryRaiseColumnClickAt(int, int)` helpers. Header hit testing uses the same detail-header geometry as `GetItemAt(...)` and the WPF renderer, respects `HeaderStyle.Clickable`, and emits normal `ColumnClickEventArgs` without reflection or SharpDevelop-local workarounds.
- `WindowsFormsHost` now routes left-button mouse-up on hosted `ListView` headers through that typed header-hit path before item activation. SharpDevelop's existing `lvFileList_ColumnClick(...)` handler can therefore choose and apply its comparer exactly as it does on Windows.
- `PortableWinFormsControlCompatibilityTests` now covers header-coordinate column dispatch and assigned-sorter ordering.

Focused validation:

```text
System.Windows.Forms compat build      -> succeeds, existing compatibility/CLS warnings only
WindowsFormsIntegration compat build   -> succeeds, existing compatibility warnings only
ProGPU.Wpf.Tests build                 -> succeeds
WinForms control compatibility tests   -> Passed, 13 total
```

After repacking `LibreWPF.WinFormsCompat.System.Windows.Forms` and `LibreWPF.WinFormsCompat.WindowsFormsIntegration` as `0.1.0-preview.sharpdevelop.1`, the package-mode SharpDevelop wrapper was rebuilt and the broad runtime smoke was rerun.

Runtime result:

```text
SharpDevelop.Full.LibreWpf build       -> succeeds, 88 warnings, 0 errors
LineCounter.sln load                   -> succeeds as CSharpProject
Extensibility.cs AvalonEdit open       -> succeeds with CSharpTextEditorExtension attached
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
WinForms PropertyGrid ContextMenuStrip -> Opened and Closed, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1
AvalonEdit completion popup            -> Opened, bindings=7, items=39, marker=extensions.
Shutdown                               -> normal WPF close through smoke timer, exit code 0
```

Latest combined smoke log:

```text
/tmp/sharpdevelop-librewpf-combined-after-listview-column.log
```

## 2026-07-08 CheckedListBox compatibility pass

SharpDevelop dialogs and add-ins use checked WinForms list boxes for member selection, file-type registration, and component-inspector option panels. The previous shim only tracked a private set of checked indices and did not expose the standard collections or item-check event flow those dialogs expect.

- `CheckedListBox` now tracks `CheckState` per item, exposes `CheckedIndices` and `CheckedItems`, implements `SetItemCheckState(...)`/`GetItemCheckState(...)`, and raises `ItemCheck` with mutable `NewValue` before committing state.
- `CheckedListBox.TryToggleItemAt(...)` provides a typed host-facing click helper that respects `CheckOnClick` and the checkbox glyph hit area.
- `WindowsFormsHost` now routes hosted checked-list mouse selection through that typed helper, so rendered checked-list rows can toggle without reflection or app-local event simulation.
- `PortableWinFormsControlCompatibilityTests` now covers checked-state collections, mutable `ItemCheck` state, and click toggling.

Focused validation:

```text
System.Windows.Forms compat build      -> succeeds, existing compatibility/CLS warnings only
WindowsFormsIntegration compat build   -> succeeds, existing compatibility warnings only
ProGPU.Wpf.Tests build                 -> succeeds
WinForms control compatibility tests   -> Passed, 14 total
SharpDevelop.Full.LibreWpf build       -> succeeds, 88 warnings, 0 errors
```

Runtime result:

```text
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
WinForms PropertyGrid ContextMenuStrip -> Opened and Closed, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
AvalonEdit completion popup            -> Opened, bindings=7, items=39
Shutdown                               -> normal WPF close through smoke timer, exit code 0
```

Latest combined smoke log:

```text
/tmp/sharpdevelop-librewpf-combined-after-checkedlistbox.log
```

## 2026-07-08 ListView checkbox compatibility pass

SharpDevelop FormsDesigner configuration surfaces use hosted WinForms `ListView` controls with checkbox state, `CheckedItems`/`CheckedIndices`, and `ItemCheck` handlers to enable or disable toolbox categories and components. The previous shim rendered/selectable `ListView` rows but did not model the checkbox contract.

- `System.Windows.Forms.ListView` now exposes `CheckBoxes`, `CheckedItems`, `CheckedIndices`, and `ItemCheck` through typed compatibility APIs.
- `ListViewItem.Checked` now stays synchronized with its owning `ListView`, including add/remove/sort paths, and raises mutable `ItemCheckEventArgs` before committing state.
- `ListView.TryToggleItemCheckAt(...)` provides the host-facing hit-test/toggle path for checkbox glyph clicks without reflection or SharpDevelop-specific event simulation.
- `WindowsFormsHost` now renders detail/non-detail checkbox glyphs for hosted `ListView` controls and routes click input through the typed toggle helper before ordinary row selection.
- `PortableWinFormsControlCompatibilityTests` now covers checked collection publication, mutable `ItemCheck` state, and checkbox glyph hit testing.

Focused validation:

```text
System.Windows.Forms compat build      -> succeeds, existing compatibility/CLS warnings only
WindowsFormsIntegration compat build   -> succeeds, existing compatibility warnings only
ProGPU.Wpf.Tests build                 -> succeeds
WinForms control compatibility tests   -> Passed, 15 total
SharpDevelop.Full.LibreWpf build       -> succeeds, 182 warnings, 0 errors
```

Runtime result after repacking `LibreWPF.WinFormsCompat.System.Windows.Forms` and `LibreWPF.WinFormsCompat.WindowsFormsIntegration` as `0.1.0-preview.sharpdevelop.1`:

```text
LineCounter.sln load                   -> succeeds as CSharpProject
Extensibility.cs AvalonEdit open       -> succeeds with CSharpTextEditorExtension attached
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
WinForms PropertyGrid ContextMenuStrip -> Opened and Closed, 3 items
LineCounter build smoke                -> Success, 0 errors, 4 existing sample warnings
ResX smoke                             -> Success, files=1
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
AvalonEdit completion popup            -> Opened, bindings=7, items=39
Shutdown                               -> normal WPF close through smoke timer, exit code 0
```

Latest combined smoke log:

```text
/tmp/sharpdevelop-librewpf-combined-after-listview-checkbox.log
```

## 2026-07-08 LibreWinForms source-owned package lane

SharpDevelop was rebuilt against a fresh package cache after the LibreWPF SDK package content was refreshed to select `LibreWinForms.*` when `ProGpuWpfUseLibreWinForms=true`.

Restore graph check:

```text
LibreWinForms.System.Windows.Forms/0.1.0-preview.sharpdevelop.1
LibreWinForms.WindowsFormsIntegration/0.1.0-preview.sharpdevelop.1
```

Build result:

```text
SharpDevelop.Full.LibreWpf Release -> succeeds, 286 warnings, 0 errors
```

FormsDesigner smoke against `/Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/LineCounterBrowser.cs`:

```text
LibreWPF FormsDesigner smoke result=Attached file=LineCounterBrowser.cs secondary=1 designer=ICSharpCode.FormsDesigner.FormsDesignerViewContent surface=Loaded root=System.Windows.Forms.UserControl components=21 selectable=21
LibreWPF FormsDesigner mutation smoke result=Success component=System.Windows.Forms.ToolStripContainer name=tscMain selectedByService=True selectedByContainer=True selectedByGrid=True valueVisible=True rows=54
```

The mutation smoke selects a replayed child component through `ISelectionService`, routes the selection through the designer `PropertyContainer`, verifies `PropertyPad.Grid` is bound to that selected component, sets a browsable `Text` property through `PropertyDescriptor`, verifies the updated value appears in the hosted WinForms `PropertyGrid`, then restores the old value without saving the sample.

Popup and hosted WinForms smoke:

```text
Main menu popup                        -> opened
AddInTree solution context menu popup  -> opened, 27 items
ComboBox popup                         -> opened
Property pad smoke                     -> Success, selected=CSharpProject, rows=22
WinForms ContextMenuStrip              -> Opened, 3 items
```

The only matched non-fatal log line in that smoke is SharpDevelop's existing `log4net` configuration-section warning. No `Exception`, `Stack overflow`, `DllNotFoundException`, `PlatformNotSupportedException`, `MissingMethodException`, or `NotImplementedException` markers appeared in the filtered smoke output.

## 2026-07-08 FormsDesigner flush persistence

SharpDevelop's LibreWPF FormsDesigner smoke now validates the full minimal mutation loop for the LineCounter sample:

```text
LibreWPF FormsDesigner smoke result=Attached file=LineCounterBrowser.cs secondary=1 designer=ICSharpCode.FormsDesigner.FormsDesignerViewContent surface=Loaded root=System.Windows.Forms.UserControl components=21 selectable=21
LibreWPF FormsDesigner mutation smoke result=Success component=System.Windows.Forms.ToolStripContainer name=tscMain selectedByService=True selectedByContainer=True selectedByGrid=True valueVisible=True flushPersisted=True rows=54
```

The fix is in LibreWinForms rather than SharpDevelop-specific save code. `DesignSurface.Flush()` now calls the retained `CodeDomDesignerLoader`, and the portable serializer can address named controls inside `ToolStripContainer` intrinsic panels with typed property expressions. SharpDevelop continues using the C# binding generator's existing `MergeFormChanges(CodeCompileUnit)` path, so this remains a managed-code reuse path with no app-specific reflection.

The clean rebuilt package lane also passes the broader smoke: main menu popup, AddInTree context menu, ComboBox popup, solution build, ResX scan, FormsDesigner load/mutation/flush, hosted `PropertyGrid`, direct WinForms `ContextMenuStrip`, and AvalonEdit completion all succeed, and the smoke timer closes the workbench with exit code 0.

## 2026-07-08 FormsDesigner event preservation

LibreWinForms now preserves existing CodeDOM event attach/remove statements from the parsed designer method when flushing a regenerated component graph. This keeps SharpDevelop designer round trips from deleting existing event hookups while broader event editing support is still being ported.

Validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> 4 passed
SharpDevelop.Full.LibreWpf Release build                    -> succeeds, 286 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner event property editing

LibreWinForms now provides `IEventBindingService` from the portable design host, seeds it from parsed designer event statements, and serializes the current event-service mappings during `Flush()`. The focused regression edits a replayed button `Click` event through the standard designer event property and verifies the generated CodeDOM contains the replacement handler rather than the original parsed hookup. A second regression verifies that an app-supplied `IEventBindingService` overrides the fallback, preserving SharpDevelop's active `CSharpEventBindingService` path for compatible methods, handler generation, and source navigation.

Validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> 6 passed
SharpDevelop.Full.LibreWpf Release build                    -> succeeds, 115 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner component lifecycle

LibreWinForms now covers the next designer lifecycle slice needed for real toolbox/delete flows. The portable design host provides `INameCreationService`, assigns stable names to unnamed created components, publishes those names through the component site and serialization manager, and serializes new controls added to existing parent collections. Destroyed controls are detached from their parent, removed from the container/name map, cleared from the portable event-binding service, disposed, and omitted from regenerated fields, `Controls.Add(...)` calls, and event hookups.

Validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> 8 passed
LibreWinForms.System.Windows.Forms Release build            -> succeeds, 27 warnings, 0 errors
SharpDevelop.Full.LibreWpf Release build                    -> succeeds, 286 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner resource replay and localization shape

LibreWinForms now replays the WinForms designer resource patterns used by localized designer files. The portable CodeDOM loader evaluates `ComponentResourceManager.GetObject(...)`, `GetString(...)`, `GetStream(...)`, and `ApplyResources(...)`; `ApplyResources(...)` also falls back to typed property descriptors over the resolved resource set so portable controls receive resource values even when the platform method is incomplete. The CodeDOM type resolver now normalizes assembly-qualified names before service and known-type lookup.

The portable serializer also preserves SharpDevelop's reflection-localization model by emitting `resources.ApplyResources(...)` for the root and named components when a `CodeDomLocalizationProvider` requests `CodeDomLocalizationModel.PropertyReflection`. This does not yet claim complete localized resource-file editing; the next remaining slice is writing/updating resource files through upstream WinForms resource services.

Validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> 10 passed
LibreWinForms.System.Windows.Forms Release build            -> succeeds, 27 warnings, 0 errors
SharpDevelop.Full.LibreWpf Release build                    -> succeeds, 286 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner handler generation contract

LibreWinForms now implements the standard `IEventBindingService.ShowCode(component, event)` flow for events that do not yet have a handler. The portable event service creates a unique method name, stores it through the same event-property map used by the serializer, delegates source navigation/generation to the active service override, and removes the newly-created binding if navigation fails. This is the missing host-side contract that SharpDevelop's `CSharpEventBindingService` expects before it inserts or navigates to event handlers.

Validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> 11 passed
LibreWinForms.System.Windows.Forms Release build            -> succeeds, 27 warnings, 0 errors
SharpDevelop.Full.LibreWpf Release build                    -> succeeds, 286 warnings, 0 errors
```

## 2026-07-08 SharpDevelop shutdown and WinForms PictureBox compatibility

The latest SharpDevelop package-mode smoke closed two shutdown blockers found after the ResX package lane started running through the real full workbench. `LibreWinForms.System.Windows.Forms.PictureBox` now implements the standard `ISupportInitialize` contract expected by generated WinForms designer code and SharpDevelop's own exception dialog. The ProGPU WPF host now defers native window disposal out of render/dispatcher callbacks and fails GPU hit testing closed after target or host disposal, including captured presentation-source hit-test delegates that can run from queued WPF input work during shutdown.

Validation:

```text
LibreWinForms.System.Windows.Forms package-mode build                 -> succeeds, 28 warnings, 0 errors
ProGPU.Wpf.Tests PictureBoxSupportsDesignerInitialization             -> passed
ProGPU.Wpf.Tests GpuHitTestingFailsClosedAfterHostDisposal            -> passed
ProGPU.Wpf.Tests CapturedGpuHitTestCallbacksFailClosedAfterHostDisposal -> passed
SharpDevelop.Full.LibreWpf fresh-cache Release build                  -> succeeds, 286 warnings, 0 errors
SharpDevelop full smoke                                               -> menus/context menu/combo/resx/forms designer/property grid/context menu/completion pass, exit code 0
```

The final smoke log contains no `Unhandled`, `dispatcher exception`, `Reset inside`, `NullReference`, `InvalidCast`, or `PictureBox` failure entries.

## 2026-07-08 ResourceToolkit integration checkpoint

The ResourceToolkit add-in was probed as the next SharpDevelop surface because it covers `.resx`/`.resources` content, resource tooltips, completion, context menus, and WinForms resource dialogs. A local SDK-style `ResourceToolkit.LibreWpf.csproj` can be created and made optional in `SharpDevelop.Full.LibreWpf`, but enabling it now fails before LibreWPF or ProGPU runtime code is reached. The add-in targets the older SharpDevelop/NRefactory parser stack and references legacy `ICSharpCode.NRefactory.Ast`, `ICSharpCode.NRefactory.PrettyPrinter`, `ICSharpCode.NRefactory.Visitors`, and `ICSharpCode.SharpDevelop.Dom.NRefactoryResolver` APIs that are not present in the current package-mode SharpDevelop graph.

Validation:

```text
SharpDevelop.Full.LibreWpf default Release build                         -> succeeds, 38 warnings, 0 errors
ResourceToolkit.LibreWpf optional Release build with add-in enabled       -> fails, 173 legacy parser/API errors
```

This is tracked as a SharpDevelop add-in compatibility task, not a ProGPU rendering or LibreWinForms runtime blocker. The default full-workbench wrapper should keep ResourceToolkit disabled until either a legacy NRefactory compatibility assembly is ported or the add-in's parser/completion/refactoring paths are rewritten against the current SharpDevelop parser services. Resource file read/write support remains covered independently by the LibreWinForms `System.Resources.ResX*` implementation and the existing `LIBREWPF_SHARPDEVELOP_RESX_SMOKE` lane.

## 2026-07-08 WinForms ErrorProvider compatibility

SharpDevelop XML/resource dialogs use the standard WinForms `ErrorProvider` pattern generated by the designer: construct with an `IContainer`, run `ISupportInitialize.BeginInit/EndInit`, and drive validation state with `SetError(...)`/`GetError(...)`. LibreWinForms now provides a typed `ErrorProvider` extender-provider surface with per-control error text, icon alignment, icon padding, blink settings, container ownership, and `Clear()`. The mirrored LibreWPF WinFormsCompat package carries the same API so package-mode apps see identical behavior while LibreWinForms remains the source-owned implementation.

Validation:

```text
ProGPU.Wpf.Tests ErrorProviderSupportsDesignerGeneratedErrorState        -> passed
ProGPU.Wpf.Tests Release build                                           -> succeeds, 93 warnings, 0 errors
LibreWinForms.System.Windows.Forms Release build                         -> succeeds, 28 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build                     -> succeeds, 286 warnings, 0 errors
```

## 2026-07-08 XmlEditor package-mode add-in build

The SharpDevelop XmlEditor add-in is now a local LibreWPF wrapper pressure test and can be included in the full workbench wrapper. The first optional build exposed missing reusable WinForms API rather than WPF rendering issues. LibreWinForms now covers the required typed surface:

- `ListBox.SelectionMode`, `SelectedItems`, `SetSelected(...)`, `GetSelected(...)`, and `Sorted` insertion.
- `DataGridView.MultiSelect` and `ShowEditingIcon`.
- `ListViewItem(string text, int imageIndex)` and `ListViewItem(string[] items, int imageIndex)`.
- `Control.BringToFront()` / `SendToBack()` using the host renderer's child-order model.
- `TextBoxBase.Clear()`, `TreeNode.ExpandAll()`, `TreeNode.Collapse(bool)`, and `SplitContainer.SplitterWidth`.

Validation:

```text
ProGPU.Wpf.Tests ListBoxSupportsSortedMultiSelection                    -> passed
ProGPU.Wpf.Tests XmlEditorWinFormsSurfaceApiIsAvailable                 -> passed
LibreWinForms.System.Windows.Forms Release build                         -> succeeds, 30 warnings, 0 errors
XmlEditor.LibreWpf fresh-cache Release build                             -> succeeds, 248 warnings, 0 errors
SharpDevelop.Full.LibreWpf with XmlEditor fresh-cache Release build      -> succeeds, 286 warnings, 0 errors
```

## 2026-07-08 XmlEditor runtime and TreeView icons

The package-mode full workbench now opens a real XML-like file through the included XmlEditor add-in. Opening `/Users/wieslawsoltes/GitHub/SharpDevelop/samples/SdaUser/app.config` selects `ICSharpCode.XmlEditor.XmlLanguageBinding`, loads XML syntax highlighting, and attaches the `XmlTreeView` secondary view:

```text
LibreWPF CodeEditorAdapter.FileNameChanged language binding=ICSharpCode.XmlEditor.XmlLanguageBinding
LibreWPF CodeEditor.UpdateSyntaxHighlighting highlighting=XML highlighter=ICSharpCode.AvalonEdit.AddIn.CustomizingHighlighter
LibreWPF DisplayBindingService secondary attached id=XmlTreeView count=1
LibreWPF WorkbenchStartup application exit code=0
```

The hosted WinForms TreeView renderer in both LibreWPF package compatibility and source-owned LibreWinForms now consumes the existing typed `ImageList`/`TreeNode.ImageKey`/`TreeNode.SelectedImageKey` model and draws node icons through a cached `WriteableBitmap` upload path backed by ProGPU `System.Drawing.Bitmap.LockBits(...)`. This keeps the SharpDevelop XmlEditor fix in the reusable host renderer rather than adding an app-specific workaround.

Validation:

```text
LibreWinForms.WindowsFormsIntegration Release build                      -> succeeds, 34 warnings, 0 errors
LibreWPF WindowsFormsIntegration Release build                           -> succeeds, 101 warnings, 0 errors
ProGPU.Wpf.Tests WinForms compatibility focused set                      -> 20 passed, 0 failed
SharpDevelop.Full.LibreWpf fresh-cache Release build                     -> succeeds, 286 warnings, 0 errors
SharpDevelop XmlEditor app.config open smoke                             -> XmlTreeView attached, exit code 0
```

## 2026-07-08 WinForms TreeView owner drawing

SharpDevelop's XML tree and project browser use WinForms `TreeViewDrawMode.OwnerDrawText`: `ExtTreeView.OnDrawNode(...)` decides whether default text rendering should run, custom `ExtTreeNode.Draw(...)` paints label backgrounds/foregrounds, and project-browser handlers draw overlay images through `DrawTreeNodeEventArgs.Graphics`. The portable WinForms host previously rendered only its built-in TreeView row path, so those application-owned owner-draw callbacks never executed.

LibreWinForms and the LibreWPF package mirror now expose a typed `TreeView.RaiseDrawNode(DrawTreeNodeEventArgs)` bridge and the WPF host composes owner-draw rows through an offscreen ProGPU `System.Drawing.Bitmap`/`Graphics.FromImage(...)` path. `OwnerDrawText` keeps the host-owned expander and `ImageList` icon rendering while letting the app draw the text region; `OwnerDrawAll` can replace the full row. The implementation uses typed WinForms and System.Drawing APIs only, with no reflection or SharpDevelop-specific special cases.

Validation:

```text
LibreWPF WindowsFormsIntegration Release build                           -> succeeds, 101 warnings, 0 errors
LibreWinForms.WindowsFormsIntegration Release build, fresh NuGet cache    -> succeeds, 34 warnings, 0 errors
ProGPU.Wpf.Tests WinForms compatibility focused set                      -> 21 passed, 0 failed
SharpDevelop.Full.LibreWpf fresh-cache Release build                     -> succeeds, 286 warnings, 0 errors
SharpDevelop XmlEditor app.config open smoke                             -> XmlTreeView attached; timed shutdown ended with code 143 in this run
```

## 2026-07-08 Hosted WinForms ComboBox drop-down and owner draw

SharpDevelop add-ins also use hosted WinForms combo boxes inside editor/designer surfaces. One concrete case is the IconEditor color picker, which sets `ComboBox.DrawMode = OwnerDrawFixed` and handles `DrawItem` to paint color swatches. The portable host previously rendered only the selected text/button and treated `ComboBox` clicks as inherited `ListBox` row selection, so hosted WinForms combo boxes had no drop-down popup path and owner-drawn selected cells were ignored.

LibreWinForms and the LibreWPF package mirror now expose typed `ListBox.RaiseDrawItem(...)` and `RaiseMeasureItem(...)` bridges. The WPF host uses those bridges for hosted owner-drawn `ListBox`/`ComboBox` rows through an offscreen ProGPU `System.Drawing.Bitmap`/`Graphics.FromImage(...)` replay path. Hosted WinForms `ComboBox` clicks now open a WPF/ProGPU popup menu positioned below the hosted control, keep `ComboBox.DroppedDown` synchronized, and select items through the normal `SelectedIndex`/`SelectedIndexChanged` path. The fix is generic host behavior with no SharpDevelop-specific code or reflection.

Validation:

```text
LibreWPF WindowsFormsIntegration Release build                           -> succeeds, 101 warnings, 0 errors
LibreWinForms.WindowsFormsIntegration Release build, fresh NuGet cache    -> succeeds, 4 warnings, 0 errors
ProGPU.Wpf.Tests WinForms compatibility focused set                      -> 22 passed, 0 failed
SharpDevelop.Full.LibreWpf fresh-cache Release build                     -> succeeds, 286 warnings, 0 errors
```

## 2026-07-08 WinForms owner-draw background and focus helpers

SharpDevelop owner-drawn WinForms controls call `DrawItemEventArgs.DrawBackground()` and `DrawFocusRectangle()` from their normal `DrawItem` handlers. Those helpers were previously empty in the portable compatibility layer, so hosted owner-drawn combo/list rows could paint text or custom swatches but still miss the stock selected-row background and focus indication.

LibreWinForms and the LibreWPF package mirror now implement those helpers through the existing ProGPU-backed `System.Drawing.Graphics` path. `DrawBackground()` fills selected rows with `SystemBrushes.Highlight`, disabled rows with `SystemBrushes.Control`, and normal rows with `SystemBrushes.Window`; `DrawFocusRectangle()` draws the standard focus outline when `DrawItemState.Focus` is present and `NoFocusRect` is absent. This keeps the behavior generic to the WinForms compatibility layer and avoids SharpDevelop-specific rendering code.

Validation:

```text
LibreWinForms.WindowsFormsIntegration Release build, fresh NuGet cache    -> succeeds, 34 warnings, 0 errors
ProGPU.Wpf.Tests WinForms compatibility focused set                      -> 23 passed, 0 failed
```

## 2026-07-08 Portable HwndSource hook dispatch checkpoint

The current SharpDevelop integration slice is finalized at the point where package-mode builds succeed and the most visible menu, context-menu, ComboBox, completion-popup, and hosted WinForms owner-draw surfaces have reusable LibreWPF/LibreWinForms coverage. SharpDevelop is not yet fully complete, but the remaining work is now narrowed to runtime parity and portable platform contracts rather than basic SDK/package integration.

LibreWPF now exposes a typed portable `IPortablePresentationSourceHost.DispatchHwndSourceHook(...)` contract, and the source-built `PortablePresentationSource` forwards it to its companion `HwndSource` public hook list. ProGPU window activation dispatches `WM_ACTIVATE` and `WM_ACTIVATEAPP` through that contract on host activation/deactivation, which covers the first SharpDevelop/AvalonDock `HwndSource.AddHook(...)` pressure point without reflection or duck-typed callback adapters.

Validation:

```text
ProGPU PresentationCore shim Release build                               -> succeeds, 0 warnings, 0 errors
LibreWPF PresentationCore Release build                                  -> succeeds, 0 warnings, 0 errors
LibreWPF.ProGPU Release pack into SharpDevelopLocal feed                 -> succeeds
LibreWPF.Transport Release pack into SharpDevelopLocal feed              -> succeeds
LibreWPF.Sdk Release pack into SharpDevelopLocal feed                    -> succeeds
ProGPU.Wpf.Tests focused activation/source/host/WinForms set             -> 174 passed, 0 failed
SharpDevelop.Full.LibreWpf fresh-cache Release package-mode build        -> succeeds, 286 warnings, 0 errors
```

## 2026-07-08 AvalonDock window geometry hook pass

AvalonDock's `WindowInteropWrapper` uses `WM_WINDOWPOSCHANGING` to keep auto-hide flyout windows aligned while the owner window moves, and floating windows route `WM_WINDOWPOSCHANGED`, `WM_MOVE`, and `WM_SIZE` through the same `HwndSource.AddHook(...)` filter surface. LibreWPF previously forwarded only activation messages, so those hooks stayed silent in package mode.

The ProGPU WPF platform event contract now carries typed window geometry events: `WindowPositionChanging`, `WindowPositionChanged`, and `WindowSizeChanged`, with neutral left/top/width/height fields on `WpfWindowEventArgs`. The Silk.NET window event service raises those events from native `Move` and `Resize`, and `WpfPortableWindowActivation` translates them into the legacy `HwndSource` hook messages expected by AvalonDock. `WM_WINDOWPOSCHANGING`/`WM_WINDOWPOSCHANGED` carry no fake native `WINDOWPOS` pointer; consumers that need exact native structure data still need a typed portable contract. `WM_MOVE` and `WM_SIZE` receive packed coordinate/size `lParam` values for existing filters that read those values.

Validation:

```text
LibreWPF.ProGPU Release build                                            -> succeeds, 0 warnings, 0 errors
ProGPU.Wpf.Tests Release build                                           -> succeeds, 95 warnings, 0 errors
ProGPU.Wpf.Tests window activation/event focused set                     -> 55 passed, 0 failed
LibreWPF.ProGPU Release pack into SharpDevelopLocal feed                 -> succeeds
SharpDevelop.Full.LibreWpf fresh-cache Release package-mode build        -> succeeds, 286 warnings, 0 errors
```

## 2026-07-08 AvalonDock mouse activation hook pass

AvalonDock also uses `WM_MOUSEACTIVATE` through `WindowInteropWrapper.WindowActivating` so floating/auto-hide windows can suppress activation without losing normal input handling. LibreWPF now dispatches `WM_MOUSEACTIVATE` from typed ProGPU mouse-down input before it applies normal portable window activation. If a hook returns `MA_NOACTIVATE` or `MA_NOACTIVATEANDEAT`, the activation state update is suppressed; `MA_ACTIVATEANDEAT` and `MA_NOACTIVATEANDEAT` mark the input handled so it is not forwarded to the WPF input service. The low word of `lParam` is `HTCLIENT`, and the high word is the mapped mouse-down message (`WM_LBUTTONDOWN`, `WM_RBUTTONDOWN`, `WM_MBUTTONDOWN`, or `WM_XBUTTONDOWN`).

Validation:

```text
LibreWPF.ProGPU Release build                                            -> succeeds, 0 warnings, 0 errors
ProGPU.Wpf.Tests Release build                                           -> succeeds, 95 warnings, 0 errors
ProGPU.Wpf.Tests window activation/event focused set                     -> 57 passed, 0 failed
LibreWPF.ProGPU Release pack into SharpDevelopLocal feed                 -> succeeds
SharpDevelop.Full.LibreWpf fresh-cache Release package-mode build        -> succeeds, 286 warnings, 0 errors
```

## 2026-07-08 LibreWinForms packaging/default-branch pass

The LibreWinForms submodule now has the same preview-shipping scaffolding expected from the LibreWPF lane. `/Users/wieslawsoltes/GitHub/wpf/external/LibreWinForms` gained a LibreWinForms README front matter section, package table with NuGet badges, `eng/librewinforms-pack.sh`, package manifest/release-bundle helpers, docs verification, release docs, and GitHub workflows for build, docs, and release. The GitHub repository `wieslawsoltes/winforms` now reports `librewinforms-progpu-port` as its default branch with LibreWinForms/ProGPU/Silk.NET topics and description.

The follow-up hardening pass is pushed to LibreWinForms commit `2810c93a8`. The package lane now cleans all current-version package artifacts before packing, fails if an unexpected `0.1.0-preview.1` package remains in the output directory, includes the root README in each package, verifies the release workflow fails on missing artifacts, publishes a public `nuget.org` source in the release bundle `NuGet.config`, and restores through an isolated repo-local NuGet cache so stale same-version bridge packages from the user/global cache cannot shadow the freshly built LibreWPF/ProGPU feed.

Validation:

```text
LibreWinForms docs verification                                           -> succeeds
LibreWinForms package lane, fresh NuGet cache + SharpDevelopLocal feed    -> succeeds; packages, manifest, bundle, checksum written
LibreWinForms release bundle checksum                                     -> succeeds
LibreWinForms SDK package README metadata                                 -> present
WPF superproject submodule update                                         -> points external/LibreWinForms at 2810c93a8
```

Local package-lane validation should keep using the matching bridge version and explicit LibreWPF/ProGPU feed when validating unpublished bridge bits. The pack script now owns `artifacts/nuget/librewinforms-pack` by default and evicts the current bridge package versions before restore, so package-mode validation is no longer sensitive to stale packages already present in `~/.nuget/packages`.

## 2026-07-09 SharpDevelop native loop crash fix

The broad SharpDevelop package-mode smoke exposed a macOS native-window crash after the validated popup/AvalonDock/FormsDesigner flows completed. The process exited with code `139`; the crash report at `/Users/wieslawsoltes/Library/Logs/DiagnosticReports/dotnet-2026-07-09-015852.ips` showed the faulting thread inside `glfwWindowShouldClose` with an invalid `0x20` address. That pointed at LibreWPF/ProGPU host-loop shutdown polling rather than a SharpDevelop app bug.

`ProGpuWpfWindowHost.Run()` now uses an owner-driven portable loop instead of `IWindow.Run()`. The loop pumps Silk.NET events through the existing `DoEvents()` path, tracks close-start state with `_hasNativeWindowCloseStarted`, wakes the native event queue on close requests, and exits without polling `IWindow.IsClosing` or other native window properties after close has begun. The optional `PROGPU_WPF_TRACE_NATIVE_LOOP=1` diagnostics also avoid native property reads and only report LibreWPF-owned state.

Validation:

```text
dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj -c Release -v:minimal /nr:false
dotnet vstest src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll --Tests:ProGPU.Wpf.Tests.ProGpuWpfWindowHostTests.NativeRunUsesOwnerDrivenPortableLoop
dotnet pack src/ProGPU.Wpf/ProGPU.Wpf.csproj -c Release -o artifacts/packages/SharpDevelopLocal -v:minimal -p:Version=0.1.0-preview.sharpdevelop.1 -p:PackageVersion=0.1.0-preview.sharpdevelop.1 /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-ownerloop-final-nuget DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/SharpDevelop.Full.LibreWpf.csproj -c Release -v:minimal /nr:false
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-ownerloop-final-nuget DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 PROGPU_WPF_TRACE_NATIVE_LOOP=1 LIBREWPF_SHARPDEVELOP_TRACE_OPEN=1 LIBREWPF_SHARPDEVELOP_FULL_POPUP_SMOKE=All LIBREWPF_SHARPDEVELOP_AVALONDOCK_SMOKE=ProjectBrowser LIBREWPF_SHARPDEVELOP_BUILD_SMOKE=Solution LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1 LIBREWPF_SHARPDEVELOP_PROPERTY_PAD_SMOKE=1 LIBREWPF_SHARPDEVELOP_WINFORMS_CONTEXT_MENU_SMOKE=1 LIBREWPF_SHARPDEVELOP_EDITOR_COMPLETION_SMOKE=1 LIBREWPF_SHARPDEVELOP_FORMS_DESIGNER_SMOKE=/Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/Src/LineCounterBrowser.cs LIBREWPF_SHARPDEVELOP_EXIT_AFTER_MS=45000 dotnet /Users/wieslawsoltes/GitHub/SharpDevelop/src/Main/SharpDevelop/bin/Release/net10.0-windows/SharpDevelop.dll /nologo /noExceptionBox /Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln
```

Results:

```text
ProGPU.Wpf.Tests Release build                         -> succeeds, 95 existing warnings, 0 errors
Owner-driven native loop guard test                    -> 1 passed, 0 failed
LibreWPF.ProGPU SharpDevelopLocal pack                 -> succeeds, 0.1.0-preview.sharpdevelop.1 refreshed
SharpDevelop.Full.LibreWpf fresh-cache build           -> succeeds, 286 warnings, 0 errors
SharpDevelop broad package-mode smoke                  -> exit code 0
Validated runtime markers                              -> menu/context/ComboBox/toolbar popups, ProjectBrowser, property pad, FormsDesigner load/mutation, LineCounter build
Optional smoke markers not emitted in this final run    -> AvalonDock ProjectBrowser detail, editor completion, hosted WinForms ContextMenuStrip
Native-loop trace                                      -> owner loop entered and exited without a new crash report
```

## 2026-07-08 AvalonDock show/hide hook pass

AvalonDock floating/flyout windows also observe visibility transitions through the legacy `HwndSource.AddHook(...)` path. LibreWPF now carries typed `Shown` and `Hidden` window event kinds and translates both platform-raised visibility events and direct WPF `Show()`/`Hide()` activation callbacks into `WM_SHOWWINDOW`. `wParam` is `1` for show and `0` for hide, with no fake native structures or reflected state.

Validation:

```text
LibreWPF.ProGPU Release build                                            -> succeeds, 0 warnings, 0 errors
ProGPU.Wpf.Tests Release build                                           -> succeeds, 95 warnings, 0 errors
ProGPU.Wpf.Tests window activation/event focused set                     -> 61 passed, 0 failed
LibreWPF.ProGPU Release pack into SharpDevelopLocal feed                 -> succeeds
SharpDevelop.Full.LibreWpf fresh-cache Release package-mode build        -> succeeds, 286 warnings, 0 errors
```

## Remaining issues

- The unmodified `SharpDevelop.sln` still fails before LibreWPF runtime is reached because it targets legacy .NET Framework versions and old Windows build tools:
  - Missing reference assemblies for `.NETFramework,Version=v3.5`, `v4.0`, `v4.0,Profile=Client`, `v4.5`, and `v4.5.1` on macOS.
  - `src/Tools/Tools.build` uses `ResGen.exe`, which .NET Core MSBuild reports as unsupported.
- `ICSharpCode.SharpDevelop.Workbench.WpfWorkbench` and AvalonDock still contain old Win32/HWND hook assumptions. LibreWPF now dispatches portable activation, mouse activation, show/hide, and basic geometry hooks for the package-mode `HwndSource.AddHook(...)` path, but non-client title-bar messages, IME composition, exact native `WINDOWPOS` structure needs, and region/floating-window behavior still need typed portable contracts.
- `SharpDevelop.Full.LibreWpf` now builds and starts the historical workbench shell through LibreWPF package mode, loads the legacy LineCounter C# project as `CSharpProject`, opens a real source file, attaches the CSharpBinding editor extension, and renders the real AddInTree-built menu/context/combo popup surfaces. The complete IDE still cannot yet be claimed as fully working: debug commands, full designer support including handler generation/source navigation, add-in workflows, broader tool windows, templates, completion/refactoring flows, and non-smoke user interaction still need systematic runtime validation and additional portable service seams.
- The ResourceToolkit add-in is currently disabled in the local package-mode wrapper because it depends on legacy NRefactory Ast/PrettyPrinter/SharpDevelop.Dom resolver APIs that are not part of the current LibreWPF SharpDevelop project graph. It needs a compatibility parser layer or a targeted rewrite before it can become part of the default full-workbench build.
- Full-workbench AvalonEdit can now open and display a real source file with source-tree C# syntax registration, `CSharpBinding.CSharpLanguageBinding`, `CSharpTextEditorExtension` attached, and the completion popup opened in the package-mode smoke. Remaining editor parity work includes completion commit/filter interaction, semantic issue update behavior, refactoring context actions, IME composition, and designer-specific editor flows.
- Full-workbench external click validation is currently blocked on this macOS machine by automation permissions: Apple Events/System Events is not authorized, and synthetic CoreGraphics clicks did not reliably reach the LibreWPF-hosted window. In-process popup validation now covers the real full-workbench File menu plus the smoke-shell menu, context menu, ComboBox, and Core drop-down surfaces; broader manual interaction still needs user-side validation.
- AvalonDock full-workbench floating/flyout windows still contain Win32 region and interop wrappers (`WindowInteropWrapper`, `FlyoutPaneWindow`, `FloatingWindow`) that need portable window-region/floating-window contracts before the full workbench can be treated as cross-platform.
- AvalonEdit IME support still uses `HwndSource` and native IME calls. The current shell validates AvalonEdit rendering/editing basics, but IME composition needs a LibreWPF portable text-input/IME seam before full editor parity.
- The LibreWPF wrapper still carries temporary project-shaping warnings from duplicate source inclusion. These should be removed by splitting the package-mode wrapper into typed facade projects or by excluding the duplicate generated/version/native-helper files explicitly instead of relying on conflict warnings.

## 2026-07-08 ResourceToolkit package-mode enablement

The ResourceToolkit add-in is no longer fully excluded from the local LibreWPF full-workbench wrapper. `ResourceToolkit.LibreWpf.csproj` now builds against the current SharpDevelop text editor and project APIs, and the full wrapper can include it with `LibreWpfSharpDevelopIncludeResourceToolkit=true`.

The port keeps this slice source-compatible and typed: old NRefactory v3 AST resolver/refactoring entry points that are not present in the current package-mode graph are disabled behind `LIBREWPF` with explicit unsupported messages rather than replaced by reflection or duck-typed shims. The remaining ResourceToolkit work is a current-parser/resource-analysis rewrite for Find References, Rename Resource, Find Unused Resources, and Find Missing Resources.

Validation:

```text
ResourceToolkit.LibreWpf Release build                                      -> succeeds, 155 warnings, 0 errors
SharpDevelop.Full.LibreWpf Release build with ResourceToolkit included       -> succeeds, 39 warnings, 0 errors
SharpDevelop broad package-mode smoke                                       -> popups/resx/build/forms designer/property grid/context menu/completion pass, exit code 0
```

The broad smoke covered the real full-workbench menu popup, AddInTree context menu popup with 27 items, ComboBox popup, ResX smoke, LineCounter build smoke, FormsDesigner load and mutation, hosted PropertyGrid, direct WinForms `ContextMenuStrip`, and editor completion popup. This closes the current SharpDevelop ResourceToolkit compile/packaging task, while full ResourceToolkit feature parity remains a planned compatibility-parser rewrite.

## 2026-07-08 LibreWinForms bridge workflow update

LibreWinForms packaging now treats matching LibreWPF/ProGPU bridge packages as first-class build inputs instead of relying on an already-populated local artifact directory. The LibreWinForms CI and release workflows check out `wieslawsoltes/wpf` branch `progpu-rendering-port`, build the matching LibreWPF bridge feed with `eng/progpu-wpf-sdk-ci.sh`, and then pack LibreWinForms against that feed plus NuGet.org.

The source-owned LibreWinForms `System.Windows.Forms.Control` compatibility surface now includes standard `Validating` and `Validated` events, and the WPF fallback compatibility package mirrors those APIs. This unblocks SharpDevelop dialogs such as ResourceToolkit's string-resource editor through normal typed WinForms event contracts.

Validation:

```text
LibreWinForms docs verification                                             -> succeeds
LibreWinForms package lane with fresh cache + local bridge feed              -> succeeds; runtime packages, SDK package, manifest, bundle, and checksum written
LibreWinForms public-feed-only package lane                                  -> blocked until ProGPU.System.Drawing.Common and matching LibreWPF bridge packages are public
GitHub repository metadata                                                   -> default branch librewinforms-progpu-port, description/topics updated
```

## Next steps

- Add the real workbench service bootstrap to `SharpDevelop.LibreWpf` incrementally, keeping the SDK-style shell buildable after every slice.
- Expand the new portable `HwndSource` hook path beyond activation, mouse activation, and basic geometry into typed non-client, region, floating-window, and IME contracts.
- Add portable contracts for AvalonDock floating-window regions and AvalonEdit IME composition rather than relying on HWND hooks in package-mode apps.
- Keep validating popup classes in package mode after each shell milestone: menu, context menu, ComboBox, Core.Presentation drop-down buttons, toolbars, AvalonDock tabs, and AvalonEdit completion popups.
