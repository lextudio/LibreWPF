# ProGPU WPF Toolkit App

This sample exercises the public Xceed `Extended.Wpf.Toolkit` package through the custom ProGPU WPF SDK. The app project keeps the normal WPF migration shape:

- `Project Sdk="ProGPU.Wpf.Sdk/11.0.0-dev"`
- `TargetFramework=net11.0-windows`
- `UseWPF=true`
- one `PackageReference` to `Extended.Wpf.Toolkit`

The XAML uses regular `http://schemas.xceed.com/wpf/xaml/toolkit` and `http://schemas.xceed.com/wpf/xaml/avalondock` namespaces. It validates `Xceed.Wpf.Toolkit` controls, `Xceed.Wpf.AvalonDock.DockingManager`, AvalonDock documents, anchorables, theme assembly loading, compiled BAML, bindings, and code-behind event hookup without app-side ProGPU APIs.

Build and launch the SDK-produced apphost from the repository root:

```bash
./eng/run-progpu-wpf-toolkit.sh
```

Run a bounded apphost validation:

```bash
PROGPU_WPF_TOOLKIT_VALIDATE=1 ./eng/run-progpu-wpf-toolkit.sh
```

Run through `Application.Run()` and `StartupUri`:

```bash
PROGPU_WPF_TOOLKIT_RUN_VALIDATE=1 ./eng/run-progpu-wpf-toolkit.sh
```

If the local `11.0.0-dev` SDK packages are stale or missing, rebuild the package feed first:

```bash
PROGPU_WPF_TOOLKIT_REBUILD_PACKAGES=1 ./eng/run-progpu-wpf-toolkit.sh
```
