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

## Which XAML works where

BAML and XSG are two separate compile paths; a project uses exactly one. Simpler XAML is a feature of the XSG path only.

| Project | XAML style | Compiled by | Result |
|---|---|---|---|
| 1. `UseLibreWpfXamlSourceGenerator` off (default) | old style (xmlns declared) | BAML (`PresentationBuildTasks`) | exactly upstream WPF |
| 2. XSG enabled | old style (xmlns declared) | the generator | same generated code as the Simpler XAML form of the same document |
| 3. XSG enabled | new style (no `xmlns`) | the generator | works: the generator supplies the default WPF presentation and `x` namespaces |
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
- NativeAOT/trimming implications not yet evaluated

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
