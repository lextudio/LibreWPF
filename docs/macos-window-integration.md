# macOS window integration: title bar fusion, backdrop, appearance, native menu

Status: phase A implemented (section 7); phase B is design only. Written after reading the LibreWPF/ProGPU
sources, the Avalonia and Uno Platform documentation, and Apple's HIG.

## 1. Why

A Mac app that looks native fuses its content with the title bar (the traffic lights float over
the window content), uses the system material behind the window (vibrancy), follows the user's
appearance (Light/Dark), and puts commands in the menu bar. The first Pulse showcase needs all
four; any LibreWPF app on macOS does.

## 2. What exists today (measured, not assumed)

| Layer | State |
| --- | --- |
| ProGPU backend (`MacOsNativeWindowPlatform`, `SilkWindowController`) | **Has it all**: `SetClientAreaExtension` (`NSWindowStyleMaskFullSizeContentView`, `titlebarAppearsTransparent`, title visibility), `ExtendedTitleBarHeight`, `FrameInsets`, `SetTheme` (`NSAppearance`), `SetBackdrop` (`NSVisualEffectView`: `Blur`, `Acrylic`, `Mica`, `MicaAlt` mapped to window/content/underWindow/underPage materials), `ChromeHints` including `MacOsThickTitleBar` (an `NSToolbar` that makes the bar taller). |
| ProGPU.Wpf host (`ProGpuWpfWindowHost`) | Uses only `SetDecorations`, `SetCanResize/Minimize/Maximize`, and `SetBackdrop(Transparent)` when `TransparentFramebuffer` is set. Nothing reaches client-area extension, theme or the materials. |
| WPF layer (`Window`, `PortableWindowState`) | `AllowsTransparency` -> `TransparentFramebuffer`; WPF requires it to come with `WindowStyle.None` (no traffic lights), and the fork enforces that. `WindowChrome` is wired as **full custom chrome**: `SetPortableCustomChrome` forces `WindowStyle.None` and `TryBeginPortableChromeDrag` drags from `CaptionHeight`. `Window.WindowBackdropType` exists but is internal and Windows-only (DWM). |

Consequence: the platform capability exists; the missing piece is a faithful, testable WPF-level
API and the state plumbing from `Window` to the host.

## 3. What Avalonia and Uno do

Sources: Avalonia docs (window management, how-to windows, macOS guide, v12 notes) and Uno
Platform docs (Windowing). Statements about Uno on macOS beyond those pages are unverified.

### Avalonia

- `ExtendClientAreaToDecorationsHint` extends content under the title bar; v12 replaced
  `ExtendClientAreaChromeHints` with `WindowDecorations` (`Full`/`BorderOnly`/`None`).
- `WindowDecorationProperties.ElementRole` (`TitleBar`, `MinimizeButton`, ...) marks drag and
  button regions in the content.
- `TransparencyLevelHint` is a prioritised list (`None`/`Transparent`/`AcrylicBlur`/`Mica`); the
  window reports `ActualTransparencyLevel`; `TransparencyBackgroundFallback` is the solid brush
  used when the request cannot be honoured. On macOS only `Transparent` is supported (no vibrancy
  materials); v12 had a regression where `Transparent` rendered black.
- `NativeMenu.Menu` attaches to `Window`/`Application`, gestures use `Meta` for Command, items
  without `Click`/`Command` are disabled, a menu headed "Edit" gets the system text-editing
  items, and `NativeDock.Menu` defines the Dock menu.

### Uno Platform

- Implements the WinUI API: `AppWindowTitleBar.ExtendsContentIntoTitleBar`, `Window.SetTitleBar`,
  `SetDragRectangles`, `PreferredHeightOption` (Standard/Tall/Collapsed),
  `OverlappedPresenter.SetBorderAndTitleBar`, `InputNonClientPointerSource` regions
  (`Caption`/`Passthrough`), and `AppWindowTitleBar.IsCustomizationSupported()`.
- Its documentation lists the macOS behaviour explicitly: the native traffic lights are kept and
  vertically centred; the title bar is transparent and its text hidden; `LeftInset` reports the
  space the traffic lights reserve so content stays clear of them; pressing a `Caption` region
  drags and a double-click follows the user's "double-click a window's title bar" setting;
  disabling minimise/maximise greys the buttons instead of hiding them; title bar colours can be
  set but have no effect.
- Gaps: drag rectangles took over a year (issue #19302, 2025-01 to 2025-11); the macOS native
  window type is internal; a community package (`UnoMacOSTitleBar`) adds the `NSToolbar` thick
  title bar that Uno does not expose.

### What we take

1. **Give layout a way to read the inset** the traffic lights take (Uno `LeftInset`); never
   hard-code 78 px.
2. **Declare drag and pass-through regions explicitly** (Uno `Caption`/`Passthrough`, Avalonia
   `ElementRole`); WPF already has `WindowChrome.CaptionHeight` and `IsHitTestVisibleInChrome`.
3. **Request, actual, fallback** for backdrop (Avalonia): the app asks, reads what it got, and
   supplies a fallback brush, so no platform checks leak into app code.
4. **Capability probing and a per-platform behaviour table** in the docs (Uno).
5. **Attached-property native menu** modelled on Avalonia `NativeMenu`.
6. **Do not churn the API** (Avalonia v12 removed `ExtendClientAreaChromeHints`) and **pin every
   behaviour with a test and a screenshot** (Avalonia `Transparent` regression).

## 4. Design

### 4.1 Principle and a correction

Principle: use WPF concepts where their meaning already matches; add an explicit,
macOS-namespaced, opt-in layer where WPF has no concept; never change what an existing
WPF-shaped program does.

Correction to the first plan: `WindowChrome` cannot simply mean "extend into the title bar" on
macOS. In LibreWPF it already means *replace the system frame with app-drawn chrome*
(`WindowStyle.None`, app-drawn buttons), and apps such as AvalonDock windows rely on that.
Reinterpreting `UseAeroCaptionButtons=true` (the default) as "keep the traffic lights" would
change their windows. So fusion is **explicit**, and `WindowChrome` keeps its meaning. Where its
properties describe the same thing they are reused: `CaptionHeight` sizes the drag region of a
fused window, `IsHitTestVisibleInChrome` marks controls that must receive clicks inside it.

### 4.2 API (phase A) - namespace `System.Windows.Platform.MacOS`, static class `MacWindow`

All are attached properties on `Window`; every one is a no-op off macOS and absent from the
render path unless set.

| Property | Type / default | Meaning |
| --- | --- | --- |
| `ExtendsContentIntoTitleBar` | bool / false | `NSWindowStyleMaskFullSizeContentView`, transparent title bar, traffic lights kept. Content is laid out from the top of the window. |
| `TitleBarHeight` | double / NaN (system default) | Height of the fused bar the traffic lights are centred in (`ExtendedTitleBarHeight`); also the drag height when `WindowChrome` is absent. |
| `CaptionInsets` | Thickness, read-only | `Left` = space the traffic lights occupy, `Top` = bar height. Updated on fullscreen and appearance changes. Layout binds to it. |
| `Backdrop` | `MacBackdrop` / `None` | Requested material: `None`, `Transparent`, `WindowBackground`, `ContentBackground`, `UnderWindowBackground`, `UnderPageBackground`. |
| `ActualBackdrop` | `MacBackdrop`, read-only | What the host applied (falls back to `None` when unsupported). |
| `Appearance` | `System` / `Light` / `Dark` | `NSWindow.appearance`; `System` follows macOS. |

Deferred from the first cut (they can be added without breaking anything): `ShowsTitle` (a fused
bar always hides the system title text, as Uno does) and `BackdropFallback` (an app that wants a
solid colour when `ActualBackdrop` is `None` sets `Window.Background` from that property).

`MacBackdrop` names are the macOS materials, not Windows names: Mica and Acrylic describe
Windows materials and map to different macOS ones. The four non-trivial values are exactly what
the backend's `NativeWindowBackdrop` offers today (`Blur` -> window background, `Acrylic` ->
content background, `Mica` -> under window, `MicaAlt` -> under page). More materials (sidebar,
title bar, HUD, popover) need a backend enum extension in the ProGPU repository and are phase B.

`Backdrop` other than `None` implies a transparent framebuffer. Today that is only reachable via
`AllowsTransparency`, which forces `WindowStyle.None`; the host must derive the framebuffer
transparency from `Backdrop` before the native window is created and must not require
`WindowStyle.None`.

### 4.3 Windows-shaped aliases (phase A.2, where meaning matches)

- `Window.WindowBackdropType` (already in the fork, internal): make it public and map
  `MainWindow` -> `UnderWindowBackground`, `TransientWindow` -> `ContentBackground`,
  `TabbedWindow` -> `UnderPageBackground` on macOS. `MacWindow.Backdrop` wins when both are set.
- `ThemeMode` (.NET 9, `Light`/`Dark`/`System`): map to `MacWindow.Appearance` if the fork
  exposes it. Skipped when absent; not invented.

### 4.4 Behaviour (modelled on Uno's documented macOS rules)

- A fused window keeps close/minimise/zoom; `ResizeMode.NoResize`/`CanMinimize` grey the buttons.
- Mouse press in the caption region (`CaptionHeight`, else `TitleBarHeight`) starts a window drag
  unless the element under the pointer has `WindowChrome.IsHitTestVisibleInChrome`; a double
  click performs the user's system action (zoom or minimise). The existing
  `TryBeginPortableChromeDrag` gesture is reused rather than duplicated.
- Full screen: the bar and traffic lights auto-hide; `CaptionInsets` becomes zero.
- Colours: title bar colours are never configurable (macOS draws it); document it.

### 4.5 Plumbing

```text
MacWindow attached properties (WPF layer, PresentationFramework)
  -> Window : IPortableWindowStateSource.TryGetPortableWindowState
  -> PortableWindowState { HasMacWindow, MacBackdrop }          (creation: framebuffer transparency)
  -> PortableWindowActivationService.TryApplyMacWindow(PortableMacWindowRequest)   (live updates)
  -> WpfPortableWindowActivation.ApplyPortableWindowState (options: TransparentFramebuffer from Backdrop)
  -> ProGpuWpfWindowHost (after the native window exists)
       controller.SetClientAreaExtension / SetTitleBarHeight / SetTheme / SetBackdrop
  <- host reports applied backdrop and caption insets
  <- IPortableWindowChromeSink.OnPortableMacWindowChanged(actualBackdrop, insets)
  -> Window sets ActualBackdrop and CaptionInsets (read-only DPs)
```

Changes after the window exists go through the same synchronisation call that already updates
title, state and border. Left inset comes from the standard window buttons' frames
(`standardWindowButton:` for close/miniaturize/zoom) plus the standard 8 pt gap; it is queried
after each style change, not hard-coded.

### 4.6 Phase B: native menu bar (design only, nothing built)

Same principles as phase A: reuse WPF concepts where they match, an explicit macOS-namespaced
opt-in where they do not, request and actual, no platform checks in app code.

**Model.** The menu is an ordinary WPF `Menu`/`MenuItem` tree. Nothing new to learn and the same
XAML renders as a normal in-window menu on Windows and Linux.

```xml
<Menu x:Name="MainMenu" macos:MacMenu.UsesNativeMenuBar="True">
  <MenuItem Header="_View">
    <MenuItem Header="Vitals"  Command="{Binding ShowVitals}"  InputGestureText="Cmd+1"/>
    <MenuItem Header="Silicon" Command="{Binding ShowSilicon}" />
    <Separator/>
    <MenuItem Header="Appearance"> ... IsCheckable, grouped ... </MenuItem>
  </MenuItem>
</Menu>
```

**API** (`System.Windows.Platform.MacOS`, static class `MacMenu`):

- `MacMenu.UsesNativeMenuBar` (attached, on `Menu`, default false): on macOS the tree becomes the
  application menu bar and the in-window `Menu` collapses (takes no layout); elsewhere it is
  ignored. Read-only `MacMenu.IsNativeMenuBarActive` is per `Menu` instance (a read-only attached
  property, like `ActualBackdrop` in phase A) and reports whether that menu is the one currently
  installed as the application menu bar.
- `MacMenu.Role` (attached enum `MacMenuRole` on `MenuItem`: `None`, `Edit`, `Window`, `Help`):
  the primary way a top-level item is recognised; the English header is only a
  fallback when the role is `None`.
- Application-wide settings are static members of `MacMenu`, not attached properties, because
  WPF's `Application` is not a `DependencyObject`: `MacMenu.AboutCommand` (static `ICommand`, run
  by "About <app>").
  Without it, the standard About panel (`orderFrontStandardAboutPanelWithOptions:`) is shown with
  options filled from the entry assembly (name, version, copyright); that mapping is an open
  question below until measured, since the panel otherwise reads bundle metadata.
- Standard application menu is generated, not authored: About, Services, Hide, Hide Others, Show
  All, Quit. Quit does not shut down immediately (`Application.Shutdown` tears the dispatcher
  down with no cancellable handshake): it calls `Close()` on each live window in turn, non-main windows first and
  `Application.MainWindow` last, and stops at the first window that cancels (`Close()` is void, so a cancel is detected by the
  window still being in `Application.Windows` after the call), leaving the
  application running. Once every window has closed without cancellation it calls
  `Application.Shutdown()` itself, so the result does not depend on `ShutdownMode`
  (`OnMainWindowClose` would otherwise shut down before later windows could cancel, and
  `OnExplicitShutdown` would never exit). Authored items only add to the generated menu
  through `MacMenu.ApplicationMenuItems` (static collection of `MenuItem`, for example Preferences,
  inserted after About).
- Recognised by header, as in Avalonia: "Edit" (system selectors `undo:`, `cut:`, `copy:`,
  `paste:`, `selectAll:` are appended when the menu has none of its own), "Window" (handed to
  `setWindowsMenu:` so macOS adds minimise, zoom and the window list), "Help" (`setHelpMenu:`,
  search field). Recognition uses `MacMenu.Role` first and the English header only as a fallback,
  so localisation does not break it.
- `MacMenu.DockMenu` (static `Menu`): used for the Dock icon menu.

**Item mapping** (pure functions, tested without a window):

| WPF | NSMenuItem |
|---|---|
| `Header` with `_` access key | title with the underscore removed (macOS has no access keys) |
| `Command` / `CommandParameter` | enabled state from `CanExecute` evaluated when the menu opens (`menuNeedsUpdate:`), not polled |
| activation | invokes the item through its public `MenuItemAutomationPeer` `IInvokeProvider`, which runs the command and raises `Click`. Activation is asynchronous and not user-initiated:
`MenuItem` queues both through the dispatcher at Render priority and passes `userInitiated=false`
(a mouse click passes true), so anything that gates on user initiation sees a programmatic
invoke, and tests must pump the dispatcher before asserting. There is no handler detection (WPF has no public API for it), so a command-less, handler-less item is enabled and does nothing, as in WPF |
| `IsCheckable`/`IsChecked` | `state` on/off; `GroupName` radio behaviour is the app's, as in WPF |
| `Command.InputGestures` or `InputGestureText` key gesture | key equivalent; `ModifierKeys.Windows` is Command, `Control` stays Control, `Alt` is Option |
| `Separator` | separator |
| `Icon` | ignored in the first cut |
| nested `MenuItem` | submenu |
| `Visibility.Collapsed` | omitted; `IsEnabled=false` greyed |

**Where the Cocoa code lives.** The preferred home is the ProGPU backend, as a neutral model
(`NativeApplicationMenu` with a tree of id, title, key, modifiers, enabled, checked, children, and a
single activation callback by id) so WinUI and Avalonia hosts can reuse it; this is what the
repository guidance asks for popups and system menus. That needs a ProGPU commit and a pointer
bump, so it follows the publish order. A first cut may live in `ProGPU.Wpf` next to
`WpfMacWindowIntegration` (as phase A did), as a temporary placement owned by the same change: it
moves to ProGPU when the backend commit lands, and the exit condition is that the next published
ProGPU pointer contains `NativeApplicationMenu`. The WPF-side model, mapping and tests do not
change. The action target is a native class registered once with an `[UnmanagedCallersOnly]`
action and validate function (no reflection, no managed delegate marshalling per item);
activation is posted to the UI dispatcher and runs the WPF command there.

**Behaviour rules.**

- One menu bar for the application. Every `Menu` with `UsesNativeMenuBar` registers while it is
  loaded in a window and unregisters on unload. The installed bar is the registered menu of the
  key window; a key window with none keeps the menu of `Application.MainWindow` (else the most
  recently registered one), and with no registered menu only the generated application menu
  exists. The bar is swapped when the key window changes, never rebuilt just to switch.
- Items are rebuilt when the `Items` tree, `Header`, `Command`, `CommandParameter`,
  `InputGestureText`, the command's `InputGestures`, `IsEnabled`, `IsChecked` or `Visibility`
  change, coalesced to one rebuild per dispatcher turn; unchanged state is not re-applied.
- Requirement, not yet a rule: a text-editing key equivalent (Edit items) must not steal the key
  from a focused WPF text element, so a `TextBox` keeps its own undo. Cocoa normally offers the
  main menu a key equivalent before the responder chain, so how to honour this is open
  question 3.
- Quit and Hide always exist, even with no `Menu`.

**Measured (spike on a running window, macOS with the GLFW host).**

- The host already installs a menu bar: an application menu (About, Services, Hide `h`, Hide
  Others, Show All, Quit `q`, all titled with the process name) and a Window menu (Minimize `m`,
  Zoom, Bring All to Front, Enter Full Screen `f` listed twice, then the window list).
- `[NSApp setMainMenu:]` after the window is shown is honoured, and it replaces the whole bar:
  the default Quit, Hide and window list disappear. So the generated application menu is
  required, titled with the app's own name, and `setWindowsMenu:` must be called again.

**Open questions, still to measure before building the host.**

0. What the standard About panel shows for an unbundled .NET process, and which option keys
   (`ApplicationName`, `ApplicationVersion`, `Copyright`) are needed to make it correct.
1. How `performKeyEquivalent:` reaches the menu when the key window's content view is the
   GLFW layer-hosting view (Command+Q and Command+1 must work with focus in any WPF element).
2. The Dock menu needs `applicationDockMenu:` on the application delegate, which GLFW owns; if it
   cannot be added without replacing the delegate it is deferred.

**Built so far (step 1, no public API yet).** `PortableMenuItem` (neutral model, in
`ProGPU.Wpf.Interop`) and the internal pure `MacMenuMapper` that turns a `Menu` tree into it:
access-key stripping, collapsed items omitted, separators kept, `IsEnabled` and `IsChecked`
(only when checkable), key equivalents from the command's `KeyGesture` first and from
`Cmd+Shift+1` style `InputGestureText` second, header-based role fallback, unique ids that
resolve back to the WPF item. Covered by `MacMenuMapperTests`.

**Built so far (step 2, the Cocoa host, not wired to WPF yet).** `WpfMacMenuHost` in `ProGPU.Wpf`
(temporary placement, see above): builds the generated application menu (About, Services, Hide
`h`, Hide Others, Show All, Quit `q`, all titled with the app's name) plus the mapped model as
`NSMenu`/`NSMenuItem` (titles, key equivalents and modifier masks, enabled, checked state,
separators, submenus, `setWindowsMenu:`/`setHelpMenu:` for the Window and Help roles) and
installs it with `setMainMenu:`. Items target one native class registered once with an
`[UnmanagedCallersOnly]` action; activation reports the item id through a single callback and
Quit reports a reserved id so the WPF layer can close windows cancellably. Covered by
`WpfMacMenuHostTests`, which build the bar headlessly, read it back through the Objective-C
runtime and trigger items with `performActionForItemAtIndex:`. Not built: the public `MacMenu`
class and attached properties, registration, the dispatcher hop for activation, the Quit
sequence, rebuild on change, key-window switching, the Edit items and the Dock menu.

**Built so far (step 3, wired to WPF).** Public `MacMenu` (`System.Windows.Platform.MacOS`):
`UsesNativeMenuBar` (attached, on `Menu`), read-only `IsNativeMenuBarActive`, `ApplicationName`
(default the entry assembly name) and `Refresh()`. A menu registers while loaded; the installed
bar is the active window's menu, else the main window's, else the most recent; the in-window
menu collapses while installed (restored when it is not, and left alone on a host with no native
bar). The host gets the model through the new `InstallMenuBar` callback; activation hops to the
menu's dispatcher and clicks the item through its public `MenuItemAutomationPeer` (asynchronous,
as documented); Quit closes non-main windows first and the main window last, stops at the first
cancel, then calls `Application.Shutdown()`. Rebuilds are coalesced to one per dispatcher turn at
Normal priority (Background priority was starved by the render loop for seconds) and also run on
`Window.Activated`, so the bar follows the active window.

Measured on a real window (a fused Pulse dashboard plus a second window hosting the menu): the
bar is installed automatically with the app's name and View menu and stays installed; a real
Command+1 (synthetic HID key events) raised the item's `Click`; a real Command+Q raised the
window's `Closing` and the process exited. This answers open question 1 only for a plain window:
Command shortcuts reached the menu with a WPF window key, not yet with a focused text element.

**Rebuild triggers (step 4).** Each registered menu is watched at every level: the item
collections, and per item `Header`, `Command`, `CommandParameter`, `InputGestureText`,
`IsEnabled`, `IsChecked`, `IsCheckable` and `Visibility` (separators included). A collection
change re-walks the tree so new items are watched too, and any change asks for one coalesced
rebuild (the pending rebuild is the live dispatcher operation, so a dispatcher that shut down with
one queued cannot block later ones). Gestures added to or removed from a `RoutedCommand` are not
observable; `MacMenu.Refresh()` covers that. Covered by a unit test over header, checked,
enabled, gesture text, separator visibility, a nested item added later, coalescing and detaching.

Not built: `MacMenu.Role` and `MacMenu.AboutCommand`/`ApplicationMenuItems`/`DockMenu`, the Edit
items and the text-key requirement, the Dock menu.

**Verification plan.** Pure mapping tests (table above, gestures, access keys, recognised
roles, collapsed and disabled items). A recording platform double for the rebuild coalescing
and the activation path. On a real window: read `NSApp.mainMenu`, check titles, key equivalents
and enabled state, then run `performActionForItemAtIndex:` and send a real Command+1 with
synthetic HID events, asserting the bound command ran. Pulse adopts it with a View menu
(Vitals, Silicon, Proof on Command+1/2/3) and an Appearance submenu (Auto, Light, Dark),
only after a published closure contains it.

- `MacWindow.UnifiedToolbar` (`NSToolbar` thick bar) and more materials (backend enum extension in
  ProGPU) remain separate and later.

## 5. Risks

- Transparent framebuffer plus vibrancy is the most fragile combination (Avalonia regression);
  every backdrop gets a screenshot test and a fallback.
- `PortableWindowState` is a pull-based snapshot; live changes need the existing synchronisation
  hook and must not re-apply identical values (avoid flicker).
- Left inset depends on AppKit geometry that changes between macOS releases; it is measured.
- NativeMilWgpu renderer mode may handle transparency differently; phase A targets the managed
  renderer first and states the NativeMil status in the tests.
- The native backend is a git submodule; phase A touches only LibreWPF so it can be committed
  without a ProGPU change or a pointer bump.

## 6. Verification plan

- Unit tests (portable, no window): state mapping from `MacWindow` to `PortableWindowState`,
  backdrop to framebuffer-transparency derivation, material mapping, fallback behaviour.
- Contract tests against the controller with a recording platform double.
- macOS acceptance: Pulse screenshots with and without fusion, with each backdrop, Light and
  Dark, plus a drag/double-click check; `CaptionInsets` compared with the real button frames.
- Pulse consumes it only after a published closure contains it; until then an overlay build
  demonstrates it in JIT.

## 7. Phase A: what was built and measured

Built: `MacWindow` (`System.Windows.Platform.MacOS`, public, in the reference assembly),
`PortableMacWindowRequest`/`Result` and `PortableWindowActivationCallbacks.ApplyMacWindow`
(Interop), `PortableWindowActivationService.TryApplyMacWindow`, `Window.UpdatePortableMacWindow`
(called on property change and once after the activation exists), and
`WpfMacWindowIntegration` + `ProGpuWpfWindowHost.ApplyMacWindow` (ProGPU.Wpf). The activation
makes the framebuffer transparent for any backdrop other than `None` without forcing
`WindowStyle.None`.

Measured on macOS (Apple silicon, managed renderer, Pulse overlay build):
`ExtendsContentIntoTitleBar` + `UnderWindowBackground` fused the title bar with floating traffic
lights over the content and drew the material behind it; `ActualBackdrop` reported
`UnderWindowBackground` and `CaptionInsets` was `71, 22` (left = zoom button right edge + 8 pt,
top = the system default bar height). Tests: 4 `PortableMacWindowTests` (WPF layer through the
activation service) and 27 `WpfMacWindowIntegrationTests` (mapping, no-controller, plumbing
contract).

Finding (a backend defect, deliberately not fixed here): the backend's `SetBackdrop` adds its
`NSVisualEffectView` as a subview of the content view. The content view layer-hosts the rendered
frame, so the material covered all window content in a first attempt (a blank grey window). The
host therefore asks the controller only for `Transparent` and adds its own effect view to the
frame view, **below** the content view. The proper fix belongs in the ProGPU repository
(`MacOsNativeWindowPlatform.SetBackdrop`); it needs a ProGPU commit and pointer bump, which is
held until it can be published in order.

Added after the first cut: **window drag from the fused bar**. A fused window has no system title
bar to drag by and, measured with `hitTest:` on the frame view, the whole bar except the traffic
lights is the layer-hosting content view, so clicks reach WPF content. `Window` now starts a drag
(`TryBeginPortableMacCaptionDrag`, called from the existing `TryBeginPortableChromeDrag`) for a
left press inside `CaptionInsets.Top` unless the element under the pointer or one of its
ancestors has `WindowChrome.IsHitTestVisibleInChrome`; custom-chrome windows keep their own
path. Verified end to end with synthetic HID events on a fused window: a press at the bar and a
drag moved the native window by the dragged distance (frame x 50 to 199, y down 99). Unit tests:
`PressInTheFusedBarStartsAWindowDragOnlyInsideTheBar` (WPF layer) and a source contract for the
ancestor walk (hit testing itself needs the ProGPU geometry provider, which the WPF-layer test
host does not register).

Double click on the fused bar zooms the window (verified on a real window: frame 900x560 to the
screen size, `isZoomed` 1, `WindowState` Maximized). The count comes from the window system:
`PortableWindowActivationCallbacks.GetPointerClickCount` reads `NSApplication.currentEvent.clickCount`,
which honours the user's double-click interval and distance and does not count a drag. Verified: a
real double click zooms, and a drag followed at once by a single click does not. A host without
that callback falls back to a second press within 500 ms and 4 px. The user's title-bar double-click
setting (minimise or nothing) is not read yet, and AppKit logs a harmless "Window move completed
without beginning" because the first press of the pair already began a native drag.

Full screen: the host used to read the full-screen state only when a request was applied, so
`CaptionInsets` stayed 71,22 inside a full-screen window. `Window.ArrangeOverride` now re-sends the
unchanged request for a fused window (entering or leaving full screen resizes it), and
`ProGpuWpfWindowHost.ApplyMacWindow` answers an unchanged request on an unchanged full-screen state
from its cached result, so the material is not rebuilt on every arrange. Verified on a real window
(`styleMask` read back): windowed 71,22; full screen 0,0; restored 71,22.

Not verified yet:
live appearance changes, the NativeMilWgpu renderer,
`CaptionInsets` after a bar-height change, and a `Backdrop` change after creation from `None`
(reports `None`, as designed).
