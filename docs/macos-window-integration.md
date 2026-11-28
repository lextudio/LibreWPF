# macOS window integration: title bar fusion, backdrop, appearance, native menu

Status: design, with phase A in implementation. Written after reading the LibreWPF/ProGPU
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
| `ShowsTitle` | bool / true | Whether the system title text is drawn when the bar is fused (`titleVisibility`). |
| `TitleBarHeight` | double / NaN (system default) | Height of the fused bar the traffic lights are centred in (`ExtendedTitleBarHeight`); also the drag height when `WindowChrome` is absent. |
| `CaptionInsets` | Thickness, read-only | `Left` = space the traffic lights occupy, `Top` = bar height. Updated on fullscreen and appearance changes. Layout binds to it. |
| `Backdrop` | `MacBackdrop` / `None` | Requested material: `None`, `Transparent`, `WindowBackground`, `ContentBackground`, `UnderWindowBackground`, `UnderPageBackground`. |
| `ActualBackdrop` | `MacBackdrop`, read-only | What the host applied (falls back to `None` when unsupported). |
| `BackdropFallback` | Brush | Painted over the window when `ActualBackdrop` is `None`; ignored otherwise. |
| `Appearance` | `System` / `Light` / `Dark` | `NSWindow.appearance`; `System` follows macOS. |

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
  -> PortableWindowState { HasMacWindow, ExtendsContentIntoTitleBar, ShowsTitle, TitleBarHeight,
                           Backdrop, Appearance }                 (ProGPU.Wpf.Interop)
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

### 4.6 Phase B (separate design, same principles)

- `MacMenu`: `NativeMenu`-shaped attached property (`MacMenu.Menu` on `Window`/`Application`,
  `MenuItem` tree and `InputBindings` reused, `Gesture` command key mapped to Command, disabled
  without a command, "Edit"/"Window"/"Help" recognised, application menu with About/Hide/Quit,
  Dock menu).
- `MacWindow.UnifiedToolbar` (`NSToolbar` thick bar) and more materials (backend enum
  extension in ProGPU).

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
