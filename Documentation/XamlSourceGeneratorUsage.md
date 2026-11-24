# Using the LibreWPF XAML Source Generator (XSG)

## Status

Preview. Default remains the BAML/`PresentationBuildTasks` path. Opt in per project.

## Opt-in

```xml
<PropertyGroup>
  <UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>
</PropertyGroup>
```

Or via environment variable (no project edit):

```bash
LIBREWPF_USE_XSG=1 dotnet build
```

## Switches

| Property | Default | Meaning |
|---|---|---|
| `UseLibreWpfXamlSourceGenerator` | `false` | **Overall switch.** Off: the project is compiled by the BAML compiler, exactly as upstream WPF, and the feature switches below do nothing. On: XAML is compiled by the generator. (`LIBREWPF_USE_XSG=1` sets it without a project edit.) |
| `LibreWpfSimplerXaml` | follows the overall switch | Simpler XAML: a root element without `xmlns` gets the default WPF presentation and `x` namespaces. Off: such a document is an error, `LWXSG0003`. |
| `LibreWpfCSharpExpressions` | follows the overall switch | C# expressions, `{cs: <expression>}`. Off: a `{cs:}` value is an error, `LWXSG0004`, reported at its line. |
| `LibreWpfCompiledBindings` | follows the overall switch | Compiled bindings: under an `x:DataType`, `{Binding Path}` is compiled to typed accessors (no reflection, trim/NativeAOT safe) and a wrong path is a build error. Off: every binding is the reflective stock `Binding` and `x:DataType` is ignored. See below. |

`UseLibreWpfXamlSourceGenerator=true` alone turns every feature on; any feature can then be turned off on its own:

```xml
<PropertyGroup>
  <UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>
  <LibreWpfCSharpExpressions>false</LibreWpfCSharpExpressions>   <!-- XSG, but no {cs:} -->
</PropertyGroup>
```

A feature set to `true` while the overall switch is off does nothing, and the build says so (warning `LWXSG5001`). A document that
declares its namespaces needs no switch; only the feature a document actually uses has to be on. The switches reach the generator as
`build_property.*` (`CompilerVisibleProperty`, from the SDK's `ProGPU.Wpf.Sdk.XamlSourceGeneratorFeatures.targets`); a host that passes
none keeps every feature on. A new XSG feature is one more property, one more `CompilerVisibleProperty` and one more row in that file.

| Diagnostic | Severity | When |
|---|---|---|
| `LWXSG0003` | error | the root declares no `xmlns` and `LibreWpfSimplerXaml` is off |
| `LWXSG0004` | error | a `{cs:}` value is present and `LibreWpfCSharpExpressions` is off |
| `LWXSG0005` | error | an `x:DataType` names no type the compilation can see |
| `LWXSG0006` | error | a compiled binding path names a property its type does not have (or cannot be read from the generated code) |
| `LWXSG0007` | warning | a binding that stays reflective at run time: inside an `x:DataType` scope or behind a `RelativeSource` / `ElementName` it could not be compiled (an indexer, a type that cannot be named, a member that exists only on the run-time type of a base-typed value such as `TemplatedParent.View`) |
| `LWXSG0008` | warning | a construct the generator cannot generate is left out, or a value is left as text: an `EventSetter`, a resource without `x:Key` or `TargetType`/`DataType`, a binding property it cannot write (`Source` that is not `{StaticResource}`/`{x:Static}`, an unparsable `Delay`, an unknown property), or a text for a value whose type has a `TypeConverter` the generator does not know |
| `LWXSG0009` | info | a top-level resource of a loose dictionary is left out because `LibreWpfXsgExcludeTargetTypes` names its type |
| `LWXSG5001` | warning | a feature switch is `true` while `UseLibreWpfXamlSourceGenerator` is off |

## Compiled bindings

```xml
<Window x:Class="Sample.MainWindow" xmlns:vm="clr-namespace:Sample.Vm" x:DataType="vm:MainVm">
  <TextBox Text="{Binding Person.Name, Mode=TwoWay}" />
</Window>
```

- **Scope.** `x:DataType` on any element types the DataContext its bindings and its descendants' bindings read. A `DataTemplate`
  (any `*Template`) starts a new scope from its own `DataType` (or `x:DataType`); a template without one ends the scope. An element
  that sets its own `DataContext` without an `x:DataType` ends the scope too; its `DataContext="{Binding ...}"` itself still reads the
  parent's type. `x:CompileBindings="False"` turns compilation off for an element and its subtree.
- **What compiles.** A dotted path of instance properties (`A.B.C`), with no `ElementName`, `RelativeSource`, `Source` or `XPath`
  (those do not read the DataContext). Indexers and attached-property steps stay reflective with `LWXSG0007`.
- **How.** Each step becomes a generated `PropertyDescriptor` with typed getter/setter delegates, passed to a stock `Binding` as an
  early-bound `PropertyPath("(0).(1)", ...)` parameter. WPF's binding engine already accepts a descriptor there: it reads and writes
  through it, listens to `INotifyPropertyChanged` by its name, and treats a get-only (or init-only) property as read-only, exactly as
  for reflection. Mode, converters, validation, `StringFormat` and templates are unchanged, and no runtime change was needed.
- `Binding` markup properties now generated: `Path`, `Mode`, `UpdateSourceTrigger`, `StringFormat`, `ElementName`, `RelativeSource`
  (`Self`, `TemplatedParent`, `FindAncestor` with `AncestorType`/`AncestorLevel`), `Converter`/`ConverterParameter`/`FallbackValue`/
  `TargetNullValue` (`{StaticResource}` or text), `Delay` and the `Validates*`/`Notify*`/`IsAsync` flags.

## Themes and other loose ResourceDictionaries

The XSG path produces no BAML, so a `ResourceDictionary` file without `x:Class` (`Themes/Generic.xaml`, `Themes/Aero2.NormalColor.xaml`,
any loose dictionary) is always compiled to a `ResourceDictionary` subclass and registered, from a module initializer, with
`System.Windows.Markup.XamlSourceGeneratedResources` under its resource path: project-relative (or the item's `Link`), lower-case,
`/`-separated, e.g. `themes/generic.xaml`. WPF's theme lookup (`SystemResources`) asks that registry before it looks for BAML, so
`ThemeInfo`/`DefaultStyleKey` styles of a control library built with XSG load with neither BAML nor System.Xaml. There is no switch:
before this, such a file was passed to the generator and silently produced nothing.

What a generated theme does not contain yet is reported, not skipped quietly (`LWXSG0008`, a warning with the file and line): `MultiTrigger`,
`DataTrigger`, `MultiDataTrigger`, `EventTrigger`, `EventSetter`, `Style.Resources`, template resources, storyboards and `VisualStateManager`
groups. The in-box themes use all of them, which is why a full `PresentationFramework.Aero2` built by the generator is not possible yet; see
[NativeAotPlan.md](NativeAotPlan.md).

Registration is by **assembly name**. `LibreWpfXsgResourceAssemblyName` makes a project register its dictionaries under another assembly's
name, so an XSG-compiled theme can stand in for `PresentationFramework.Aero2` (`themes/aero2.normalcolor.xaml`): `SystemResources`
asks the registry by name before it loads the theme assembly, which is what makes themes work under NativeAOT, where that load fails.

`LibreWpfXsgExcludeTargetTypes` (comma-separated type names, simple or full; a `;` would start an `.editorconfig` comment and truncate the list)
leaves out the top-level resources whose `TargetType` / `DataType` / `x:Key="{x:Type ...}"` names one of them (info `LWXSG0009`). A theme for a
platform that lacks a control does not style it: the document viewers (`DocumentViewer`, `FlowDocumentReader`, `FlowDocumentScrollViewer`,
`FlowDocumentPageViewer`, and the `FlowDocument` / `FixedDocument` / `FixedDocumentSequence` templates that create them) need `System.Printing`,
which is Windows-only, and NativeAOT cannot even compile a method that needs a type the assembly does not have. Empty (the default): nothing is left out.

## NativeAOT

A prototype that publishes and runs with `PublishAot=true` on macOS is in openavalon's `samples/XsgNativeAot` (README there). It needs
compiled bindings (no reflection over view models), an XSG theme registered as `PresentationFramework.Aero2` and rooted with
`TrimmerRootAssembly` (NativeAOT runs module initializers only of assemblies in the image), and `_SuppressWpfTrimError=true`. Since `XamlSourceGeneratorNativeAot.targets` the SDK does both for an XSG project with `PublishAot` (it roots every project reference); only a package that carries XSG output must root itself.

What a NativeAOT application has to know (found with a gallery of Menu, ToolBar, TabControl, TreeView, ListView, DataGrid, Calendar, ...):

- Bindings without a compiled path are reflection over the run-time type, and NativeAOT removes members nothing references. Give every
  scope its `x:DataType`: the root, and the row type for a `GridViewColumn`, `DataGridTextColumn` and so on (`x:DataType` is accepted on any element).
- The SDK targets add an rd.xml (`ProGPU.Wpf.Sdk.XamlSourceGeneratorNativeAotRoots.rd.xml`) that keeps the public members of `ListView` and
  `GridView`, which the in-box themes bind by name (`TemplatedParent.View.Columns`, reported as LWXSG0007).
- A `DataGrid` over a collection does not offer the new-row placeholder under NativeAOT (it needs the item type's default constructor by reflection):
  set `CanUserAddRows="False"` for the same behaviour in JIT and AOT.
- `CollectionView` sorts, groups and filters by property name (`SortDescription.PropertyName`, `GroupDescription`) with reflection over the item type, which
  NativeAOT has trimmed: without help the list is silently unsorted. Keep the item type with an rd.xml entry (`<Type Name="App.Item" Dynamic="Required All" />` in an
  `RdXmlFile`), as `samples/XsgNativeAot/rd.xml` does. A `ValidationRule` needs nothing (it is called through its virtual method). `<Binding.ValidationRules>` of a `<Binding>` element is generated; inside a data condition (`DataTrigger.Binding`) it is still reported as LWXSG0008.
- A parenthesized path to an internal type of another assembly (`(base:AccessibilitySwitches.X)`, what the in-box themes read) is resolved by the type's name when
  the binding is created (`__XsgStaticPath`), because the run-time path parser knows no XML prefix. The SDK's rd.xml keeps the two such types the themes use.
- A member that exists only on a subclass of the declared type (`View.Columns`: `ViewBase` has no `Columns`, `GridView` does) is compiled against the
  one class of that assembly that has it.

### Application, `StartupUri` and `Source`

With no BAML, nothing can be loaded from a resource at run time, so what BAML resolves at run time is resolved at build time:

- **`<ResourceDictionary Source="Styles/Brushes.xaml"/>`** (also inside `MergedDictionaries`): the dictionary file is a project item compiled by the
  generator and registered under its resource path, so the reference becomes a lookup of that path in the generated registry
  (`XamlSourceGeneratedResources.TryCreateDictionary`). The path is resolved at build time: relative to the referencing file's folder,
  rooted (`/Styles/a.xaml`), `pack://application:,,,/a.xaml`, or `pack://application:,,,/Other;component/a.xaml` (another assembly, a version in
  the assembly part is ignored). A dictionary that is not compiled in fails when it is created with a `FileNotFoundException` that names it.
- **The root object of every `x:Class` XAML file** with a (public or internal) parameterless constructor in its code-behind is registered under its
  assembly name and resource path by a module initializer (`XamlSourceGeneratedResources.RegisterComponent`); the `Application` is not. At run time
  `Application.LoadComponent(Uri)` asks that registry first, and with it `Application.StartupUri="MainWindow.xaml"` creates the window with no
  BAML resource. The registry is empty in an application that does not use the generator: `LoadComponent` then returns to the resource path at once.
- A `Uri`-typed value is generated as `new Uri("...", UriKind.RelativeOrAbsolute)`; an `ImageSource`-typed text (`<Image Source="Assets/logo.png"/>`)
  as `new BitmapImage(new Uri(...))`.
- **`<Resource Include="..."/>` items** (images, fonts, any file read through `pack://application:,,,/path`): the BAML path embeds them in
  `<AssemblyName>.g.resources` (`Microsoft.WinFX.targets`), which the XSG path does not import, so a `Resource` item was silently not in the assembly.
  `ProGPU.Wpf.Sdk.XamlSourceGeneratorResources.targets` writes the same `.g.resources` (an inline `ResourceWriter` task: no `GenerateResource`, no BAML;
  keys are the lower-case relative paths with `/`, a `Link` metadata overrides the path) and embeds it, when `UseLibreWpfXamlSourceGenerator` is on.
- **Parenthesized binding paths**, `(Owner.Member)` and `(prefix:Owner.Member)`, are resolved at build time whatever the source of the binding: a
  dependency property is passed as the `DependencyProperty`, a static property as a static `PropertyInfo` (`typeof(T).GetProperty("M", Public | Static)`, which
  ILC keeps). At run time such a text is resolved with the default XAML namespace only, by reflection, and a prefix (`base:`, `framework:`) cannot be
  resolved at all. A type or member the generated code cannot reach (an `internal` type of another assembly, such as
  `AccessibilitySwitches` / `FrameworkAppContextSwitches`, visible only to the in-box theme assemblies) is reported as `LWXSG0008`.

Not yet: the in-box theme assemblies (`PresentationFramework.Aero2`, `.Fluent`, ...) are still built with the BAML compiler; `Frame.Source` /
`NavigationService` navigation to a XAML file and `Image Source` pack URIs are not resolved at build time.

## Which XAML works where

BAML and XSG are two separate compile paths; a project uses exactly one. Simpler XAML is a feature of the XSG path only.

| Project | XAML style | Compiled by | Result |
|---|---|---|---|
| 1. `UseLibreWpfXamlSourceGenerator` off (default) | old style (xmlns declared) | BAML (`PresentationBuildTasks`) | exactly upstream WPF |
| 2. XSG enabled | old style (xmlns declared) | the generator | same generated code as the Simpler XAML form of the same document |
| 3. XSG enabled | new style (no `xmlns`) | the generator | works while `LibreWpfSimplerXaml` is on (the default): the generator supplies the default WPF presentation and `x` namespaces |
| (not supported) XSG off | new style | BAML | the BAML compiler does not accept a root without `xmlns` |

Run-time XAML (`XamlReader.Load/Parse`, `ResourceDictionary.Source`) is the same stock `System.Xaml` for every project: it never
adds implicit namespaces, and the `XmlReader` a caller passes to `XamlXmlReader` is used as given.

## What works (B2–B3)

- Element construction, property assignment, nested content
- `x:Name` typed fields
- Simpler XAML (implicit namespaces) — no `xmlns` declarations required
- C# expressions: `{cs: <expression>}`
- Common markup extensions: `{Binding}`, `{StaticResource}`, `{DynamicResource}`,
  `{x:Type}`, `{x:Static}`, `{x:Null}`, `{x:Reference}`

## Limitations (B5–B6 pending)

- Hot Reload: not yet supported on the source-gen path; use BAML path for
  iterative UI development
- Visual Studio / OpenDevelop XAML designer: expects BAML; source-gen projects may
  show limited designer support
- `ResourceDictionary` `MergedDictionaries`, `DataTemplate`/`ControlTemplate` inner
  graphs, and complex markup extensions are partially supported
- Localization (`x:Uid`) not yet supported
- Generation is not incremental across the compilation: the generator combines the whole `Compilation` with every XAML file (it needs the
  compilation for type resolution and compiled bindings), so any edit of the C# code regenerates all XAML. Acceptable for builds, costly for
  the IDE with many XAML files; resolving only the symbols a document names, and caching them, is future work.
- NativeAOT/trimming: compiled bindings and generated dictionaries remove the reflection the generator itself introduces; the framework assemblies are not yet annotated or AOT-tested

## Generated code

Generated files appear under `obj/<config>/<tfm>/generated/.../*.g.cs` when
`EmitCompilerGeneratedFiles` is enabled:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

## Switching back to BAML

Set `UseLibreWpfXamlSourceGenerator=false` (or unset `LIBREWPF_USE_XSG`) and rebuild.
No source changes required.

## Debugging

- Generated code is compiled directly; C# compiler errors point at the generated
  `.g.cs` file. Use `#line` directives (planned) to improve mapping.
- Set `LIBREWPF_USE_XSG=0` to fall back to BAML for comparison.
