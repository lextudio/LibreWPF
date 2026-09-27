# Multi-window rendering on Linux

Opening a second top-level `Window` on a headless X server with Mesa software rendering aborted
the process inside wgpu's GLES/EGL backend
([issue #102](https://github.com/wieslawsoltes/LibreWPF/issues/102)):

```
thread '<unnamed>' panicked at wgpu-hal/src/gles/egl.rs:300:14:
called `Result::unwrap()` on an `Err` value: BadAccess
   12: wgpuInstanceCreateSurface
thread caused non-unwinding panic. aborting.
```

An EGL context is thread-affine, and wgpu's GLES backend binds and unbinds one around its work,
`unwrap()`ing the result. Two things in LibreWPF put a foreign context in the way.

**A GLFW client context left current.** Transparent-framebuffer windows
(`AllowsTransparency="True"`, and the native popups created for one) ask GLFW for a client API so
its X11 backend picks a visual with an alpha channel. GLFW makes that context current on the
creating thread; WebGPU never uses it, but `create_surface` releases whatever context the thread
holds and Mesa answers `EGL_BAD_ACCESS` because the bound context is GLFW's. `ProGpuWpfWindowHost`
now drops it on window load, before any surface exists. This is what AvalonDock hit when its
drop-target overlay appeared during a docking drag.

**A WebGPU instance per window.** Requesting an adapter enumerates every backend, GLES included,
even where wgpu ends up selecting Vulkan, and `enumerate_adapters` makes the GLES context current -
colliding with the contexts other live instances hold. Native popup hosts always borrowed their
owner window's device; top-level windows now do the same through a process-wide render device, so
one instance serves every window. Set `PROGPU_WPF_DISABLE_RENDER_DEVICE_SHARING=1` to opt out (it
is off on Windows, which presents through D3D12). Sharing is best-effort: if the owner tears its
device down first, the next window retires it and creates one itself, and any window still using
the device can hand it on.

## GLES/EGL follow-up: repeated client-context binding

[Issue #187](https://github.com/wieslawsoltes/LibreWPF/issues/187) reports a remaining
GLES/EGL abort in `wgpuDeviceCreateBuffer`, including with shared devices. The
previous explanation that the host could not influence it was premature.
Source tracing found a concrete missing host setting: Silk.NET's pinned
[`DoRender` contract](https://github.com/dotnet/Silk.NET/blob/v2.23.0/src/Windowing/Silk.NET.Windowing.Common/Internals/ViewImplementationBase.cs)
can bind the client context before each render callback independently of automatic
buffer swapping. Clearing it once at Load is therefore insufficient.

The WebGPU host now sets `IsContextControlDisabled = true` before creating every
Silk window, in both managed and native MIL modes. It retains
`ShouldSwapAutomatically = false`, the transparent X11 alpha-capable visual, and
the initial Load-time detach (Silk initialization binds before Load independently
of its render-loop setting). No per-frame GL/EGL call, driver filtering, backend
default change, foreign renderer patch or exception suppression is introduced.

This repairs the source-backed rebinding path; it does not yet prove the reported
native abort resolved. The actual forced-GL multi-window/docking application pass,
including close/reopen and transparent pixels, remains required. A Vulkan pass
cannot qualify EGL, and native non-unwinding panics still cannot be caught by a
managed exception handler.

## Selecting a backend

The updated source dependency includes merged ProGPU
[#205](https://github.com/wieslawsoltes/ProGPU/pull/205), providing native instance
selection through `PROGPU_WGPU_BACKEND` and the `WGPU_BACKEND` alias. Typed
`ProGpuWpfWindowOptions.NativeBackendOptions` also carries an explicit choice
through source activation and owned/shared device creation. See
[window backend configuration](native-backend-window-options.md) for inheritance
and the independent forced-GL/Vulkan gates. Exact-package qualification and a
new release are still required; preview.65 does not acquire these changes merely
because source was updated. Hiding EGL drivers is not a safe
selection mechanism: the issue reports an earlier instance-creation panic too.
Report the actual selected backend for every run with
`ProGpuWpfDiagnostics.TryGetWindowHost(window, out var host)`, then
`host.CompositionTarget.Context.AdapterName` and `.AdapterBackendType`.

## Test coverage

`src/ProGPU.Wpf.MultiWindowSmokeHarness` opens three top-level windows with transparent secondaries,
presents frames on all of them, closes the render device owner and opens another, asserting that
one device serves them all. `eng/progpu-wpf-linux-multi-window-smoke.sh` runs it under `Xvfb` with
Mesa software rendering, and the `Linux headless multi-window render device smoke` job in
`.github/workflows/progpu-wpf-sdk.yml` runs that in CI.
The harness also checks the actual window context-control/swap flags and verifies
that the unused client context is not current after Show or any event/render turn.
Source integration regressions guard the setup, initial detach and live
assertion wiring. These guards are not native EGL or pixel qualification.

The smoke also rasterizes `Ag09` from the dependency-pinned Inter Regular font
on each window's actual device, including after closing the original owner. Its
private atlas explicitly captures native compute and restores the host preference
before rasterization; this does not change compositor or backend defaults. The
probe requires real compute passes/submissions and reads back R8 atlas coverage
inside **each** glyph's region, with an untouched guard texel. An empty frame,
coverage from another glyph, or a raster/CPU fallback cannot qualify this probe.
The font and its original license travel with the harness. This adds the missing
runtime exercise for LibreWPF #186: lazy atlas initialization means the earlier
empty-window pass did not compile `Compute_GlyphRasterizer`. Local coverage-reader
controls are not a Mesa pass; the explicit GL CI run must execute this new probe.

The unchanged source initially failed two of these guards and passed the existing
initial-detach control. All three pass after this change; shell syntax and diff
checks pass. Local logs are in `artifacts/client-context/`. Full host compilation,
the native multi-window run and exact-package platform validation remain CI/final
qualification requirements; no local VM or GPU run was used for these results.
