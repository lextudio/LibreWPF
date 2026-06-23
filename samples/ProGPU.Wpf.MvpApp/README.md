# ProGPU WPF MVP App

This is the first runnable no-source-change MVP app for the custom ProGPU WPF SDK path. It uses regular WPF XAML and code-behind with:

- `Project Sdk="ProGPU.Wpf.Sdk/11.0.0-dev"`
- `TargetFramework=net11.0-windows`, matching the normal WPF project shape
- normal `App.xaml` / `MainWindow.xaml` with a compiled merged resource dictionary
- WPF app resources, `ComponentResourceKey`, localization metadata, `AccessText`, `ObjectDataProvider`, `XmlDataProvider`, `x:Array`, `x:Null`, `DynamicResource` invalidation, WPF `Resource` pack streams, menus, context menus, secondary `Window` XAML, bindings, `PriorityBinding`, `FallbackValue`, `TargetNullValue`, `RelativeSource`, routed commands, input bindings, value converters, `MultiBinding`, `CollectionViewSource` sorting/grouping/filtering, `SelectedValuePath`, multi-selection, `GroupBox`, `Expander`, `ScrollViewer`, `ToolBar`, `ToolTip`, `Popup`, `ToggleButton`, `RadioButton`, `RepeatButton`, `Calendar`, `DatePicker`, `FocusManager`, `KeyboardNavigation`, `DockPanel`, `WrapPanel`, `UniformGrid`, `GridSplitter`, `Viewbox`, resource `DataTemplate`s, `DataTemplateSelector`, `ItemContainerStyle`, `ItemsPanelTemplate`, compiled `ControlTemplate` styles, `VisualStateManager`, `ValidationRule`, `BindingGroup`, `Validation.ErrorTemplate`, `EventTrigger`/`BeginStoryboard` animations, reusable `UserControl`, custom `DependencyProperty` binding, `SetCurrentValue`, `Frame`/`Page` navigation, `PasswordBox`, `RichTextBox`, `TextRange`, `EditingCommands`, `ListView`/`GridView`, list/table/tree controls, and a basic `FlowDocument`
- no app-side ProGPU APIs

Run from the repository root:

```bash
./eng/run-progpu-wpf-mvp.sh
```

If the local `11.0.0-dev` packages are stale or missing, rebuild the SDK package feed first:

```bash
PROGPU_WPF_MVP_REBUILD_PACKAGES=1 ./eng/run-progpu-wpf-mvp.sh
```
