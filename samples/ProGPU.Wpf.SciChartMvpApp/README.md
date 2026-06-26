# ProGPU WPF SciChart MVP App

This is the first executable SciChart-focused SDK smoke lane. It uses the normal `ProGPU.Wpf.Sdk` project shape and renders Visual Xccelerator-style 2D and 3D chart primitives through the reusable `ProGPU.DirectX` SciChart bridge into a WPF `WriteableBitmap`.

The 2D panel exercises native line, mountain, column, financial, shaped heatmap, and colored sprite marker paths. The colored marker path maps SciChart-style `DrawColoredSprites(...)` start/count semantics to ProGPU-owned instanced textured quad draws with active clipping and per-marker ARGB tinting.

The 3D panel exercises native surface-mesh, point-cloud, line-strip, and triangle-strip draw paths so future SciChart3D axes, grids, and line-series adapters can land in `ProGPU.DirectX` instead of managed WPF bitmap fallbacks.

By default the sample stays license-free and validates the ProGPU-side render context. Set `PROGPU_WPF_SCICHART_REAL_PACKAGES=1` or build with `-p:ProGpuWpfUseRealSciChartPackages=true` to reference the commercial `SciChart` and `SciChart3D` packages directly. The opt-in lane reads `SCICHART_RUNTIME_LICENSE_KEY` at startup, calls `SciChartSurface.SetRuntimeLicenseKey(...)`, uses real SciChart 2D/3D data-series APIs, and attempts to create real chart controls beside the ProGPU bridge output. Public package discovery for the real integration lane currently points at `SciChart` and `SciChart3D` version `9.0.0.29196`; on macOS the published NuGet packages do not include `AbtLicensingNative.dylib`, so real renderable-series/control construction is reported as a native runtime gap until the SciChart licensing/native payload is available.

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

Build and run with real SciChart packages:

```bash
PROGPU_WPF_SCICHART_REAL_PACKAGES=1 ./eng/run-progpu-wpf-scichart.sh
```
