# LibreWPF XAML Source Generator (XSG) Plan

## Goal

Introduce a Roslyn XAML Source Generator that runs **in parallel** with the
existing BAML/`PresentationBuildTasks` path, mirroring the MAUI/WXSG approach,
so that LibreWPF projects can opt into:

- typed `x:Name` fields
- C# expressions (`{cs: ...}`)
- Simpler XAML (implicit namespaces) at compile time

without abandoning the BAML path (backward compatibility, designers, Hot Reload).

## Opt-in

```xml
<PropertyGroup>
  <UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>
</PropertyGroup>
```

Default: `false` (BAML path). The two paths are mutually exclusive per project.

## Phases

### B1 — Skeleton + pipeline (done, proof of concept)
- Project: `src/LibreWPF.Xaml.SourceGenerator/` (netstandard2.0, IIncrementalGenerator)
- Discovers `AdditionalFiles` (`*.xaml`), extracts `x:Class` + `x:Name`
- Emits a stub partial class with typed fields + empty `InitializeComponent()`
- Targets/props under `packaging/LibreWPF.Xaml.SourceGenerator/`

### B2 — XAML object-graph code generation
- Parse XAML into an AST (reuse `WinUiXaml.Xaml` parser or a LibreWPF-local one)
- Generate `InitializeComponent()` body: element construction, property assignment,
  resource lookup, bindings, event wiring
- Typed `x:Name` fields assigned during construction
- Support `x:Class`, `x:Name`, `x:Key`, `x:Type`, `x:Static`, `x:Null`, `x:Reference`
- Support `{Binding}`, `{StaticResource}`, `{DynamicResource}`, `{TemplateBinding}`

### B3 — C# expressions
- Recognize `{cs: <expr>}` markup extension
- Embed expression text verbatim into generated property assignment
- Ensure generated file has correct `using` directives for the expression's types
- Compile-time validation via C# compiler (errors point at generated file; improve
  with `#line` directives where possible)

### B4 — Build integration
- Conditionally skip `MarkupCompilePass1/2` when `UseLibreWpfXamlSourceGenerator=true`
- Feed XAML files as `AdditionalFiles`
- Handle `ApplicationDefinition` (entry point) vs `Page`
- Generate BAML? No — source-gen path produces no BAML; runtime uses pure code
- Update `LibreWPF.Sdk` / `ProGPU.Wpf.Sdk` to import the new targets conditionally

### B5 — Runtime / designer / tooling parity
- Hot Reload: generated code must be reloadable (source-gen friendly)
- WpfDesigner: must consume generated code or fall back to BAML designer
- XAML language service: completion/validation for implicit namespaces (Phase 3 done)
- Incremental build: only regenerate changed XAML files

### B6 — Migration & documentation
- Migrate a sample app (e.g. `RealXamlCompilerHarness`) to source-gen path
- Document opt-in, limitations, debugging generated code
- Decide default: keep BAML as default for compatibility

## Parallel-path constraints

- BAML and XSG are **mutually exclusive per project**: a project is compiled by one or the other. XSG-only features (Simpler XAML,
  `{cs: ...}`) live in the generator; the BAML compiler (`PresentationBuildTasks`, `MarkupCompiler`) and the shared run-time
  `System.Xaml` carry no XSG feature and behave exactly as upstream WPF. (An attempt to also teach `MarkupCompiler` and
  `XamlXmlReader` about implicit namespaces was reverted: rebuilding the reader a caller hands to `XamlXmlReader` loses its
  namespace context, which broke every `XamlReader.Parse(text, ParserContext)` template without `xmlns`, e.g. TomsToolbox's.
  `XamlXmlReaderNamespaceContextTests` pins this.)
- BAML path remains the default and is never removed
- Source-gen path must produce identical runtime behavior for supported XAML subset
- Designers that require BAML (legacy) must still work; source-gen path may have
  reduced designer support initially
- Tests must cover both paths (old WPF style and Simpler XAML style)

## Open questions

1. Reuse `WinUiXaml.Xaml` parser vs. a new lightweight parser?
2. How to handle `ResourceDictionary` MergedDictionaries at code-gen time?
3. Localization (Uids) support in source-gen path?
4. NativeAOT/trimming implications of generated code?

## Status

- B1: skeleton created, builds successfully
- B2: functional — element construction, properties, nesting, x:Name, content
  property inference, Simpler XAML (implicit namespaces) support. Verified via
  end-to-end compile of a test project.
- B3: functional — `{cs: ...}` expressions embedded verbatim into generated code.
  Verified via end-to-end compile.
- B4: partial — env var / MSBuild property switch works; BAML path skipped via
  `ImportFrameworkWinFXTargets`. AdditionalFiles feeding works.
- B5: partial — XAML language service parity done (Phase 3); Hot Reload and
  designer support documented as limitations (see XamlSourceGeneratorUsage.md)
- B6: partial — usage docs added; migration of a sample app pending; default
  strategy: BAML remains default
