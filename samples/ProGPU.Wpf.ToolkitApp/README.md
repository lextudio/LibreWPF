# ProGPU WPF Toolkit App

This sample exercises the public Xceed `Extended.Wpf.Toolkit` package through the custom ProGPU WPF SDK. The app project keeps the normal WPF migration shape:

- `Project Sdk="ProGPU.Wpf.Sdk/11.0.0-dev"`
- `TargetFramework=net11.0-windows`
- `UseWPF=true`
- one `PackageReference` to `Extended.Wpf.Toolkit`

The XAML uses regular `http://schemas.xceed.com/wpf/xaml/toolkit` and `http://schemas.xceed.com/wpf/xaml/avalondock` namespaces plus the package `Zoombox` CLR namespace. It validates `Xceed.Wpf.Toolkit` controls, popup-backed editors (`CheckComboBox`, `TimePicker`, `ColorPicker`, `CalculatorUpDown`, `DropDownButton`, and `SplitButton`), up/down and spinner controls (`DateTimeUpDown`, `TimeSpanUpDown`, `ByteUpDown`, `DoubleUpDown`, `LongUpDown`, `DecimalUpDown`, and `ButtonSpinner`), selector/range controls (`WatermarkComboBox` and `RangeSlider`), navigation controls (`Wizard` and `WizardPage`), text/editing controls (`WatermarkTextBox`, `AutoSelectTextBox`, `WatermarkPasswordBox`, `MaskedTextBox`, `MultiLineTextEditor`, and `RichTextBox` with `PlainTextFormatter`), visual interaction controls (`Zoombox`, `ZoomboxView`, and `MagnifierManager`), public panel layout (`WrapPanel`), collection editors (`CollectionControl` and `CollectionControlButton` dialog creation/bindings/OK persistence), `ColorCanvas`, `CheckListBox` selection, `PropertyGrid`, `BusyIndicator`, `WindowContainer`, `ChildWindow`, embedded `MessageBox`, direct `WindowControl` primitive template/events/input, `Xceed.Wpf.AvalonDock.DockingManager`, AvalonDock documents, `LayoutDocument.Close()`/reopen lifecycle, cancelable `DocumentClosing`, `LayoutDocument.Float()`/`DockAsDocument()` transitions, icon-backed document headers, `DocumentsSource`/`AnchorablesSource` MVVM generation, source-backed active-content binding, `ILayoutUpdateStrategy` placement hooks, generated layout-item metadata updates, document and anchorable title-template selectors, generated `LayoutItem` command bindings, generated document tab-group commands, runtime AvalonDock theme switching across Aero/Metro/VS2010 package theme assemblies, document context menus, active-content events, anchorables, left/right auto-hide side groups, layout replacement events, theme assembly loading, compiled BAML, bindings, code-behind event hookup, document activation, document closed events, floating-window model state, anchorable hide/show state, public `ToggleAutoHide()` transitions, and XML layout serialization/deserialization without app-side ProGPU APIs.

The AvalonDock floating-window path and Xceed popup-editor path intentionally exercise the SDK's portable `HwndSource` compatibility facade. Third-party WPF code can query `PresentationSource.FromVisual(...)`, install `HwndSourceHook` callbacks, and receive a stable synthetic handle, while the real root still runs through the ProGPU/Silk.NET portable presentation source.

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

Run the live ProGPU/Silk.NET apphost input probe:

```bash
PROGPU_WPF_TOOLKIT_LIVE_VALIDATE=1 ./eng/run-progpu-wpf-toolkit.sh
```

If the local `11.0.0-dev` SDK packages are stale or missing, rebuild the package feed first:

```bash
PROGPU_WPF_TOOLKIT_REBUILD_PACKAGES=1 ./eng/run-progpu-wpf-toolkit.sh
```
