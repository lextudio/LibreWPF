# ProGPU WPF MVP App

This is the first runnable no-source-change MVP app for the custom ProGPU WPF SDK path. It uses regular WPF XAML and code-behind with:

- `Project Sdk="ProGPU.Wpf.Sdk/11.0.0-dev"`
- `TargetFramework=net11.0-windows`, matching the normal WPF project shape
- normal `App.xaml` / `MainWindow.xaml` with a compiled merged resource dictionary
- WPF app resources, menus, context menus, bindings, `PriorityBinding`, `FallbackValue`, `TargetNullValue`, `RelativeSource`, routed commands, input bindings, value converters, `MultiBinding`, `CollectionViewSource` sorting/grouping/filtering, resource `DataTemplate`s, `DataTemplateSelector`, `ItemContainerStyle`, `ItemsPanelTemplate`, compiled `ControlTemplate` styles, `ValidationRule`, `BindingGroup`, `Validation.ErrorTemplate`, `EventTrigger`/`BeginStoryboard` animations, reusable `UserControl`, `Frame`/`Page` navigation, `PasswordBox`, `RichTextBox`, `TextRange`, `EditingCommands`, list/table/tree controls, and a basic `FlowDocument`
- no app-side ProGPU APIs

Run from the repository root:

```bash
./eng/run-progpu-wpf-mvp.sh
```

If the local `11.0.0-dev` packages are stale or missing, rebuild the SDK package feed first:

```bash
PROGPU_WPF_MVP_REBUILD_PACKAGES=1 ./eng/run-progpu-wpf-mvp.sh
```
