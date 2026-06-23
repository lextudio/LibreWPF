# ProGPU WPF MVP App

This is the first runnable no-source-change MVP app for the custom ProGPU WPF SDK path. It uses regular WPF XAML and code-behind with:

- `Project Sdk="ProGPU.Wpf.Sdk/11.0.0-dev"`
- `TargetFramework=net11.0-windows`, matching the normal WPF project shape
- normal `App.xaml` / `MainWindow.xaml` with compiled startup/exit handlers, `ShutdownMode`, `Application.Properties`, and compiled merged resource dictionaries
- WPF app resources, assembly `;component` merged dictionaries, `Application.LoadComponent`, `ComponentResourceKey`, localization metadata, `AccessText`, `ObjectDataProvider`, `XmlDataProvider`, `x:Array`, `x:Null`, `DynamicResource` invalidation, WPF `Resource` pack streams, `DrawingImage`, `Image`, `ImageBrush`, menus, context menus, secondary `Window` XAML, bindings, `PriorityBinding`, `FallbackValue`, `TargetNullValue`, `RelativeSource`, routed commands, custom routed events, routed-event class handlers, input bindings, value converters, `MultiBinding`, `CollectionViewSource` sorting/grouping/filtering, `SelectedValuePath`, multi-selection, `GroupBox`, `Expander`, `ScrollViewer`, `ToolBar`, `ToolTip`, `Popup`, `ToggleButton`, `RadioButton`, `RepeatButton`, `Calendar`, `DatePicker`, `FocusManager`, `KeyboardNavigation`, `DockPanel`, `WrapPanel`, `UniformGrid`, `GridSplitter`, `Viewbox`, explicit and implicit `DataTemplate`s, `DataTemplateSelector`, `ItemContainerStyle`, `ItemsPanelTemplate`, implicit and `BasedOn` styles, property/data style triggers, `EventSetter`, compiled `ControlTemplate` styles, `VisualStateManager`, `ValidationRule`, `BindingGroup`, `Validation.ErrorTemplate`, `EventTrigger`/`BeginStoryboard` animations, reusable `UserControl`, custom `DependencyProperty` binding, inherited attached properties, coercion callbacks, `AddOwner`, metadata overrides, `SetCurrentValue`, `Frame`/`Page` navigation, `PasswordBox`, `RichTextBox`, `TextRange`, `EditingCommands`, `Hyperlink.RequestNavigate`, `ListView`/`GridView`, list/table/tree controls, and a basic `FlowDocument`
- no app-side ProGPU APIs

Run from the repository root:

```bash
./eng/run-progpu-wpf-mvp.sh
```

Run the same app through a bounded `Application.Run()` validation that opens via `StartupUri`, validates the WPF object graph, application manager state, startup event resources/properties, and shuts down automatically:

```bash
PROGPU_WPF_MVP_RUN_VALIDATE=1 ./eng/run-progpu-wpf-mvp.sh
```

If the local `11.0.0-dev` packages are stale or missing, rebuild the SDK package feed first:

```bash
PROGPU_WPF_MVP_REBUILD_PACKAGES=1 ./eng/run-progpu-wpf-mvp.sh
```
