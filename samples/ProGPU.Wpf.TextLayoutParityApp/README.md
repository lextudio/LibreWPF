# WPF text-layout comparison fixture

`ProGPU.Wpf.TextLayoutParityApp` uses the LibreWPF SDK and ProGPU native MIL.
The sibling `ProGPU.Wpf.TextLayoutParityApp.Windows` project links the same
`App.xaml`, `MainWindow.xaml`, and code-behind but uses the stock Windows WPF
SDK. Keep the content shared: the native Windows renderer is the same-machine
reference for line breaks, size, and caret geometry.

Set `PROGPU_WPF_TEXT_LAYOUT_REPORT=1` to query each insertion position,
including the last one, and print one `TEXT_LAYOUT` metrics line. Add
`PROGPU_WPF_TEXT_LAYOUT_EXIT_AFTER_REPORT=1` for unattended validation. An
invalid final caret rectangle writes `TEXT_LAYOUT_ERROR` and exits nonzero.
The Windows x64 package gate in
`eng/progpu-wpf-windows-native-mil-showcase.ps1` builds and compares both
variants; see `reports/native-mil-windows-text-layout-parity-2026-09-15.md`
for its first guest result and qualification limits.
