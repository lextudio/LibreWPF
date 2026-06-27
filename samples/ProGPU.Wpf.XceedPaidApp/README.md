# ProGPU WPF Paid Xceed Toolkit + DataGrid

This sample exercises the commercial Xceed Toolkit Plus and Xceed DataGrid packages through the custom `ProGPU.Wpf.Sdk` project shape. The app project stays a normal SDK-switched WPF app:

- `Project Sdk="ProGPU.Wpf.Sdk/11.0.0-dev"`
- `UseWPF`
- normal compiled `App.xaml` / `MainWindow.xaml`
- normal Xceed package references
- no app-side ProGPU APIs

The project references the paid Toolkit/AvalonDock packages directly plus the separate paid DataGrid product:

- `Xceed.Wpf.Toolkit` `5.2.26322.8434`
- `Xceed.Wpf.AvalonDock` `5.2.26322.8434`
- `Xceed.Wpf.AvalonDock.Themes.Windows10` `5.2.26322.8434`
- `Xceed.Wpf.Toolkit.Themes.MaterialDesign` `5.2.26322.8434`
- `Xceed.Products.Wpf.DataGrid.Full` `7.3.26322.8481`

The direct Toolkit references are intentional. The complete Toolkit metapackage also brings the Toolkit-era `Xceed.Wpf.DataGrid.Toolkit` assembly, which collides with the separate `Xceed.Wpf.DataGrid` product's `DataGridControl` type. This sample keeps the paid DataGrid surface on the 7.3 DataGrid product and uses Toolkit Plus/AvalonDock packages for the Toolkit side.

Runtime licensing is loaded only from environment variables:

- `XCEED_TOOLKIT_LICENSE_KEY`
- `XCEED_DATAGRID_LICENSE_KEY`

Do not put license values in this repository. `App.xaml.cs` sets `Xceed.Wpf.Toolkit.Licenser.LicenseKey` and `Xceed.Wpf.DataGrid.Licenser.LicenseKey` before constructing any paid controls.

Run:

```bash
./eng/run-progpu-wpf-xceed-paid.sh
```

Validation:

```bash
PROGPU_WPF_XCEED_PAID_VALIDATE=1 ./eng/run-progpu-wpf-xceed-paid.sh
PROGPU_WPF_XCEED_PAID_RUN_VALIDATE=1 ./eng/run-progpu-wpf-xceed-paid.sh
```

When the license env vars are missing, `PROGPU_WPF_XCEED_PAID_VALIDATE=1` still validates that the paid packages restore and the expected Toolkit Plus/DataGrid/Views3D/theme assemblies load. Set `PROGPU_WPF_XCEED_PAID_REQUIRE_LICENSE=1` to make validation fail unless both license variables are present.

The MVP window hosts an AvalonDock layout with a Toolkit Plus Material-control pane, a paid `Xceed.Wpf.DataGrid.DataGridControl` document backed by 100,000 rows, explicit `xcdg:Column` definitions, `DataGridCollectionViewSource`, `TableView`, fixed headers, row selection, `BringItemIntoView(...)` navigation, and package theme assembly loading. WPF remains responsible for the managed Xceed control tree, binding, collection views, and docking state; ProGPU owns windowing, input, invalidation, clipping, image/layer texture trimming, shaders, and final WebGPU rendering.
