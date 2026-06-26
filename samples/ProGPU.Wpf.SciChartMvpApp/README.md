# ProGPU WPF SciChart MVP App

This is the first executable SciChart-focused SDK smoke lane. It uses the normal `ProGPU.Wpf.Sdk` project shape and renders Visual Xccelerator-style 2D and 3D chart primitives through the reusable `ProGPU.DirectX` SciChart bridge into a WPF `WriteableBitmap`.

The sample intentionally does not reference the commercial SciChart packages yet. The local workspace does not include licensed SciChart assemblies, so the current MVP validates the ProGPU-side render context that a future binary adapter can call. Public package discovery for the real integration lane currently points at `SciChart` and `SciChart3D` version `9.0.0.29196`.

Run from the repository root:

```bash
./eng/run-progpu-wpf-scichart.sh
```

Run non-interactive renderer validation:

```bash
PROGPU_WPF_SCICHART_VALIDATE=1 ./eng/run-progpu-wpf-scichart.sh
```

Run `Application.Run` validation:

```bash
PROGPU_WPF_SCICHART_RUN_VALIDATE=1 ./eng/run-progpu-wpf-scichart.sh
```

Rebuild local SDK packages before running:

```bash
PROGPU_WPF_SCICHART_REBUILD_PACKAGES=1 ./eng/run-progpu-wpf-scichart.sh
```
