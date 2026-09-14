# Windows native-MIL SDK Showcase startup — 2026-09-14

## Acceptance action and provenance

The acceptance application is `ProGPU.Wpf.ShowcaseApp`. The action is to build
the unchanged SDK application with `ProGpuWpfRendererMode=NativeMilWgpu` and
native hit testing enabled, then run both its pre-display object checks and
displayed `Application.Run` self-test in the Parallels Windows 11 ARM64 VM.

The source is the merged LibreWPF integration branch at `c99d0311`, with
LibreWinForms `9e2924e5` and latest ProGPU `main` `ff8bcbf4`. The local
23-package feed was already produced from those commits. The guest built
Showcase in a private `C:\Temp\ProGpuWpfNativeWindowsFinal` artifacts and
NuGet tree, with zero build warnings or errors; it did not write outputs to
the shared source checkout. Its `progpu_native.dll` SHA-256 is
`59ac5c42cef19b62c835497907a1df6a3d48567239dbdfbae40ed087dbb1cdd5`,
identical to the `win-arm64` DLL in the exact ProGPU package.

The original package failed the pre-display `SystemCommands.MaximizeWindow`
assertion: the Window remained Normal. Source inspection showed that Windows
commands used the HWND post path when the portable activation had not yet
been created, despite frozen portable media. The source change now updates
the Window state before Show, without querying or posting to an HWND. The
guest's pre-display Showcase self-test passed with this rebuilt
`PresentationFramework.dll` overlaid into its disposable output.

The displayed run next failed in `WindowBackdropManager`: it handed the
ProGPU-owned portable handle to WPF's `DwmExtendFrameIntoClientArea`. The
source backdrop manager now avoids WPF HWND/DWM frame manipulation for
portable presentation, while leaving Windows MIL backdrops unchanged. The
ProGPU native host remains responsible for any future native backdrop effect.

The same run later rejected apparent unequal DPI during a framebuffer resize.
The captured frame was 3592×1875 pixels at logical 1796×938; Windows reported
uniform 2× content scale. X was exactly 2, while the one-row-short Y ratio was
1.9989339019189765. The host now uses the reported uniform content scale only
when each physical extent differs by at most one pixel and both ratios are
close to it. It retains the actual framebuffer and viewport dimensions, and
keeps larger or genuinely unequal-axis differences under the existing guard.
Eight focused render-surface geometry tests passed, including new one-pixel and
beyond-rounding cases.

With the rebuilt `PresentationFramework.dll`, `PresentationCore.dll`, and
`ProGPU.Wpf.dll` overlaid into the guest-only package output, the displayed
`Application.Run` self-test completed startup, system commands, storyboards,
resource controls, secondary window, editor, document, and shutdown, printing
`ProGPU WPF Showcase Application.Run validation succeeded.` The intermediate
Windows run also encountered the existing explicit synthetic-font-simulation
rejection while formatting a DataGrid header. Its mapped-request diagnostic
was expanded, but synthetic simulation was not implemented or admitted; that
failure did not recur in the final source-overlay run and needs a repeated
clean-package test before it can be classified as resolved or intermittent.

## Qualification boundary

This is Windows ARM64 source-overlay application evidence, **not** a fresh
NuGet package built from the follow-up branch. The unmodified merged packages
still fail the demonstrated Windows startup path. Rebuild exact follow-up
packages, rerun without overlays, repeat the text-heavy action, inspect visible
text at Windows DPI, and qualify x64 as well as ARM64 before Windows native SDK
admission. The larger native text, modal/popup, DirectX/Direct2D and platform
parity requirements remain open. No default renderer or unsupported guard for
true anisotropic presentation was enabled.
