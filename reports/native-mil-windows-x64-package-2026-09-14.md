# Windows x64 native-MIL SDK package qualification — 2026-09-14

## Acceptance action

`ProGPU.Wpf.ShowcaseApp` is built unchanged from the LibreWPF SDK package feed
for `win-x64`, with `ProGpuWpfRendererMode=NativeMilWgpu` and native MIL hit
testing. Both its pre-display object check and displayed `Application.Run`
check must complete. Package outputs must match the selected x64 NuGet members;
an AnyCPU launch through Windows WPF's ordinary MIL does not exercise this
path.

## Windows 11 ARM64 x64-emulation evidence

The Parallels guest has the .NET 10 x64 runtime but no x64 SDK. Its ARM64 SDK
built the unchanged Showcase with `-r win-x64 -p:PlatformTarget=x64` from a
private 23-package feed in `C:\Temp\ProGpuWpfNativeX64Emulation`. The feed
came from LibreWPF #126 code head `adfbcd39f`, ProGPU `ff8bcbf4`, and that
head's CI-built Windows managed payload. This is package-only evidence for
those exact binaries, not for a later ProGPU or LibreWPF source pin.

The prerequisite `PresentationBuildTasks` and the Showcase build had zero
warnings and errors. The apphost PE machine is `0x8664` (AMD64). Guest output
matched the package's x64 `PresentationCore.dll`
(`d523814d5c5fc7f0cc8e9c334f692fc61fdcc2536932673dd365abaaa3b4a5de`)
and `progpu_native.dll`
(`c899cb55ed7ce71df523e2c65fdaebc3f53355f57cfc735ea395a2c2fdadc788`).
With `prlctl exec --current-user`, the pre-display check printed
`ProGPU WPF Showcase validation succeeded.` The displayed run reached
startup, resources, system commands, storyboards, resource controls, the
secondary window, editor and document, printed `ProGPU WPF Showcase
Application.Run validation succeeded.`, and exited zero.

A live x64-emulated Themes window showed five whole-word lines and readable
text at the VM's 2× scale. Direct `prlctl capture` produced a black frame while
the app was alive, whereas a guest `System.Drawing.Graphics.CopyFromScreen`
capture launched with hidden PowerShell showed the rendered app and desktop.
The capture-path discrepancy is unresolved; the black host image must not be
used as evidence of a rendering failure or success. The guest-side image is a
spot check, not native-WPF pixel parity. The app was closed and the VM returned
to its prior suspended state.

The guest's Windows PowerShell execution policy disables `.ps1` execution.
The new gate script parsed with zero errors under that interpreter; its
build/hash/run operations were exercised manually without changing or
overriding the guest policy. The script itself still requires hosted execution.

## True x64 hosted gate and boundaries

The existing `Windows AnyCPU package launch` CI job verifies ordinary Windows
WPF MIL and cannot admit `NativeMilWgpu`. The new
`eng/progpu-wpf-windows-native-mil-showcase.ps1` job consumes the exact CI
package bundle on `windows-2025`, rejects a non-x64 host, builds the unchanged
Showcase in private outputs, checks apphost architecture and package-member
hashes, then requires both bounded success markers. It leaves the renderer
default unchanged. This job is authored but its CI result is pending.

LibreWPF #126's exact PR-head [build](https://github.com/wieslawsoltes/LibreWPF/actions/runs/34883761758)
and its merged-branch [build](https://github.com/wieslawsoltes/LibreWPF/actions/runs/34884769187)
passed all jobs. The new gate branches from the latest
`progpu-rendering-port`, pins ProGPU `main` `c36cf91d`, and aligns
LibreWinForms with its merged `fced003a7` source pin. Its exact ProGPU Build
was still completing package consumer jobs when this record was written.
Do not transfer the older emulation result to that new source pin.

True x64 hosted runtime, native-WPF visual comparison, broader Windows
modal/popup and SDK admission, and the full DirectX/Direct2D/platform parity
matrix remain open until their own gates pass.
