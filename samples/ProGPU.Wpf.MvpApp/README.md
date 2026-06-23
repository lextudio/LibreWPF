# ProGPU WPF MVP App

This is the first runnable no-source-change MVP app for the custom ProGPU WPF SDK path. It uses regular WPF XAML and code-behind with:

- `Project Sdk="ProGPU.Wpf.Sdk/11.0.0-dev"`
- normal `App.xaml` / `MainWindow.xaml`
- WPF bindings, commands, item templates, controls, and a basic `FlowDocument`
- no app-side ProGPU APIs

Run from the repository root:

```bash
./eng/run-progpu-wpf-mvp.sh
```

If the local `11.0.0-dev` packages are stale or missing, rebuild the SDK package feed first:

```bash
PROGPU_WPF_MVP_REBUILD_PACKAGES=1 ./eng/run-progpu-wpf-mvp.sh
```
