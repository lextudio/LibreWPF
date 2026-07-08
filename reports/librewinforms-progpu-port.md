# LibreWinForms ProGPU Port

## Decision

Adding `wieslawsoltes/winforms` as a submodule helps the SharpDevelop and broader compatibility work. The correct long-term path is to reuse upstream WinForms managed code in a LibreWinForms port rather than keep adding application-specific WinForms stubs inside LibreWPF.

The submodule is mounted at:

`/Users/wieslawsoltes/GitHub/wpf/external/LibreWinForms`

The active port branch inside the submodule is:

`librewinforms-progpu-port`

## Package lane

Initial `LibreWinForms.*` package identities were added in the submodule:

- `LibreWinForms.Sdk`
- `LibreWinForms.System.Windows.Forms`
- `LibreWinForms.WindowsFormsIntegration`

The LibreWPF SDK now has `ProGpuWpfUseLibreWinForms`. When a package-mode app sets both `ProGpuWpfUsePortableWinFormsCompat=true` and `ProGpuWpfUseLibreWinForms=true`, the SDK prefers the `LibreWinForms.*` package identities instead of the old `LibreWPF.WinFormsCompat.*` package identities.

`LibreWinForms.System.Windows.Forms` and `LibreWinForms.WindowsFormsIntegration` are now source-owned in the submodule. They build the portable compatibility implementation directly into framework-identity assemblies (`System.Windows.Forms.dll` and `WindowsFormsIntegration.dll`) instead of depending on `LibreWPF.WinFormsCompat.*` alias packages. The SDK package remains the stable package identity for future no-source-change WinForms app switching.

The local SharpDevelop feed also has refreshed `LibreWPF.Sdk` package content so `ProGpuWpfUseLibreWinForms=true` resolves the source-owned `LibreWinForms.*` packages. A normal SDK repack still needs the repository private restore feeds configured for the WPF pack project; the local validation feed was refreshed from the checked-in SDK target files.

## Reuse plan

Move reusable managed code from upstream WinForms into LibreWinForms packages in this order:

1. Resource and design-time APIs needed by SharpDevelop: `ResXResourceReader`, `ResXResourceWriter`, `System.Drawing.Design`, `System.Windows.Forms.Design`, and `System.ComponentModel.Design` types.
2. Control/data models used by SharpDevelop pads and FormsDesigner: `Control`, `ContainerControl`, `UserControl`, `Form`, `TreeView`, `ListView`, `PropertyGrid`, menu/context-menu, dialogs, data binding, and image lists.
3. `WindowsFormsIntegration` host contracts so WPF-hosted WinForms surfaces use the same popup/input/render path as LibreWPF.
4. Platform services for Silk.NET windows/input, ProGPU painting/composition/text/clipping/hit testing, local OS dialogs/clipboard/drag-drop, and Win32 compatibility shims where APIs need Win32 semantics.

## SharpDevelop validation

This should help SharpDevelop because many of its projects reference `System.Windows.Forms` and `WindowsFormsIntegration` directly. A branded SDK/package lane lets us keep those references stable while moving implementation from a WPF-local shim into a WinForms source-reuse port.

The local SharpDevelop LibreWPF build has now been switched to:

```xml
<ProGpuWpfUseLibreWinForms>true</ProGpuWpfUseLibreWinForms>
```

Local source-owned packages were packed into:

`/Users/wieslawsoltes/GitHub/wpf/artifacts/packages/SharpDevelopLocal`

Fresh-cache package restore confirms the SharpDevelop full-workbench restore graph now contains:

- `LibreWinForms.System.Windows.Forms/0.1.0-preview.sharpdevelop.1`
- `LibreWinForms.WindowsFormsIntegration/0.1.0-preview.sharpdevelop.1`

Validated results against `/Users/wieslawsoltes/GitHub/SharpDevelop/samples/LineCounter/LineCounter.sln`:

- `SharpDevelop.Full.LibreWpf` Release build succeeds with warnings only.
- WPF workbench main menu popup opens.
- WPF workbench AddInTree context menu opens with 27 items.
- WPF ComboBox drop-down opens.
- Hosted WinForms `PropertyGrid` smoke succeeds with 22 rows.
- Hosted WinForms `ContextMenuStrip.Show(...)` opens with 3 items.
- FormsDesigner add-in compiles, is included in the full workbench output, attaches `ICSharpCode.FormsDesigner.FormsDesignerViewContent` to `LineCounterBrowser.cs`, and loads a replayed `System.Windows.Forms.UserControl` design surface with 21 components.

The FormsDesigner load fix is split between SharpDevelop and LibreWinForms compatibility code. SharpDevelop's LibreWPF parser path now adds `System.ComponentModel.TypeConverter`, `System.Drawing.Primitives`, portable `System.Windows.Forms.dll`, and ProGPU `System.Drawing.Common.dll` assemblies by typed runtime identity when classic .NET Framework projects cannot resolve WinForms/Drawing references on macOS. The existing SharpDevelop designer rule still checks normal `System.Windows.Forms.Form`/`UserControl` base types. The portable `CodeDomDesignerLoader` then replays common CodeDOM designer statements into typed controls/components and modeled collections, which is enough for the LineCounter designer surface to load beyond attachment.

Regression coverage:

- `ProGPU.Wpf.Tests.Platform.PortableWinFormsCodeDomDesignerLoaderTests.BeginLoadReplaysCodeDomControlTree` validates `DesignSurface` CodeDOM replay for `UserControl -> Panel -> Button`.
- The adjacent portable WinForms compatibility tests still pass under the repository `vstest` path.

## 2026-07-08 source-owned package validation

The copied implementation in `external/LibreWinForms/src/LibreWinForms.Portable` now builds as source-owned packages:

```text
LibreWinForms.System.Windows.Forms build        -> succeeds, 26 warnings, 0 errors
LibreWinForms.WindowsFormsIntegration build     -> succeeds, 4 warnings, 0 errors
LibreWinForms.System.Windows.Forms pack         -> creates System.Windows.Forms.dll package
LibreWinForms.WindowsFormsIntegration pack      -> creates WindowsFormsIntegration.dll package
SharpDevelop.Full.LibreWpf fresh-cache build    -> succeeds, 286 warnings, 0 errors
```

Runtime smoke through the source-owned package lane:

```text
FormsDesigner smoke -> Attached, surface=Loaded, root=System.Windows.Forms.UserControl, components=21, selectable=21
Designer mutation   -> Success, selected component reaches PropertyGrid and changed Text value is visible
Main menu popup     -> opened
Context menu popup  -> opened, 27 items
ComboBox popup      -> opened
PropertyGrid smoke  -> Success, selected=CSharpProject, rows=22
ContextMenuStrip    -> Opened, 3 items
```

## 2026-07-08 ProGPU drawing shim update

The next SharpDevelop fidelity gap was in the shared ProGPU drawing layer rather than LibreWPF or SharpDevelop code. `System.Drawing.Graphics.DrawImage(...)` now supports source-rectangle sprite extraction and `ImageAttributes.ColorMatrix` through the ProGPU image-effect shader path. This covers SharpDevelop ghost icons, grayscale code-coverage bitmaps, and version-control overlay images without app-specific fallbacks.

The local validation feed was refreshed for:

- `ProGPU.Scene`
- `ProGPU.System.Drawing.Common`
- `ProGPU.Dxf`

`ProGPU.Dxf` now suppresses the transitive Microsoft `System.Drawing.Common` asset from `netDxf.netstandard`, so ProGPU samples/tests and LibreWinForms consumers see one source-owned drawing shim assembly.

Validated results:

```text
ProGPU.Tests build                                  -> succeeds, 4 warnings, 0 errors
GdiBitmapTests + ImageEffectRenderTests             -> 14 passed, 0 failed
SharpDevelop.Full.LibreWpf fresh-cache Release build -> succeeds, 286 warnings, 0 errors
SharpDevelop combined smoke                         -> menus/popups/build/resx/forms designer/property grid/context menu/completion all pass
```

The fresh-cache validation used:

```text
NUGET_PACKAGES=/tmp/sharpdevelop-librewpf-nuget-librewinforms-source-owned-1
```

This is important because earlier same-version preview packages in the global NuGet cache did not contain the newer ProGPU interop dialog DTOs or System.Drawing types. Package-mode CI should keep using a clean package cache or unique preview versions for this lane.

## 2026-07-08 FormsDesigner CodeDOM flush and ToolStripContainer serialization

The portable LibreWinForms design surface now retains its loader and dispatches `DesignSurface.Flush()` into `CodeDomDesignerLoader.Flush()`. The loader serializes the live design-surface component graph back into a `CodeCompileUnit` and calls the SharpDevelop C# designer generator's normal `Write(...)` path. This keeps SharpDevelop on its existing managed designer pipeline instead of adding app-specific save logic.

The serializer also handles `ToolStripContainer` intrinsic child panels through typed expressions such as `this.tscMain.BottomToolStripPanel`, rather than expecting `TopToolStripPanel`, `BottomToolStripPanel`, `ContentPanel`, and related panels to exist as named fields. This fixes the LineCounter designer flush path where a named `ToolStrip` is hosted inside `BottomToolStripPanel`.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 3 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 26 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 286 warnings, 0 errors
FormsDesigner smoke                                         -> Attached, surface=Loaded, components=21, selectable=21
FormsDesigner mutation smoke                                -> Success, selectedByService=True, selectedByContainer=True, selectedByGrid=True, valueVisible=True, flushPersisted=True, rows=54
```

## 2026-07-08 FormsDesigner event hookup preservation

The portable `CodeDomDesignerLoader` now captures existing `CodeAttachEventStatement` and `CodeRemoveEventStatement` entries from the parsed `InitializeComponent()` method and appends them to the generated CodeDOM during `Flush()`. This prevents designer round trips from dropping existing handler hookups such as `button.Click += ...` or SharpDevelop sample handlers like `ListView.ColumnClick += ...` while the serializer still rebuilds the mutable component/property/child-control graph from typed WinForms objects.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 4 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 26 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 286 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner event property editing

The portable design host now publishes a typed `IEventBindingService`. `CodeDomDesignerLoader.BeginLoad()` seeds that service from parsed `CodeAttachEventStatement`/`CodeRemoveEventStatement` entries after the design surface has replayed the component tree, and `PortableCodeDomDesignSurfaceSerializer` emits the current event-binding service state during `Flush()`. This means designer event-property edits can replace existing handler hookups instead of the loader blindly replaying stale parsed statements. The host also prefers an app-provided event service over the fallback portable service, which lets SharpDevelop's active C# FormsDesigner service keep owning compatible-method lookup, handler generation, and source navigation.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 6 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 26 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 115 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner component lifecycle

The portable `IDesignerHost` now also owns the basic component lifecycle expected by toolbox and delete flows. Unnamed `CreateComponent(...)` calls get a stable `INameCreationService` name such as `label1`, the name is published through the component site and `IDesignerSerializationManager`, and the generated CodeDOM can serialize the new component field, properties, and parent `Controls.Add(...)` relationship. `DestroyComponent(...)` now detaches controls from their parent collection, removes name/instance mappings, clears portable event bindings for the component, disposes it, and prevents stale fields, child-add statements, or event hookups from being emitted on the next flush.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 8 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 27 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 286 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner resources and localization

The portable CodeDOM replay now handles the resource patterns emitted by WinForms designers. `ComponentResourceManager.GetObject(...)`, `GetString(...)`, `GetStream(...)`, and `ApplyResources(...)` are evaluated through typed resource-manager APIs, and `ApplyResources(...)` has a property-descriptor fallback over the resolved `ResourceSet` for portable controls whose platform implementation does not mutate all properties itself. CodeDOM type names are normalized before type-resolution service and known-type lookup, so assembly-qualified designer type names resolve consistently.

The serializer also reads the active `CodeDomLocalizationProvider` from the portable design host and emits `resources.ApplyResources(this, "$this")` plus per-component `resources.ApplyResources(...)` calls when `CodeDomLocalizationModel.PropertyReflection` is requested. This preserves the reflection-localization shape that SharpDevelop detects in existing designer files while the remaining resource-file writer path is brought over from upstream WinForms.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 10 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 27 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 286 warnings, 0 errors
SharpDevelop broad smoke                                    -> popups/build/resx/forms designer/property grid/context menu/completion all pass, exit code 0
```

## 2026-07-08 FormsDesigner handler generation contract

The portable `EventBindingService` now follows the standard designer flow for `IEventBindingService.ShowCode(component, event)`: when no handler is assigned, it creates a unique method name, publishes it through the event-property binding map, calls the app-provided code navigation/generation override, and rolls the new binding back if navigation fails. This lets SharpDevelop's `CSharpEventBindingService` keep owning real handler insertion and source navigation while the portable host supplies the expected WinForms service contract.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsCodeDomDesignerLoaderTests -> Passed, 11 total
LibreWinForms.System.Windows.Forms build                    -> succeeds, 27 warnings, 0 errors
SharpDevelop.Full.LibreWpf fresh-cache Release build        -> succeeds, 286 warnings, 0 errors
```

## 2026-07-08 FormsDesigner ResX resource file support

LibreWinForms now owns a portable `System.Resources` ResX compatibility slice in the source-owned `System.Windows.Forms.dll` package: `ResXFileRef`, `ResXDataNode`, `ResXResourceReader`, and `ResXResourceWriter`. The implementation covers the designer resource-file API surface SharpDevelop expects without depending on Windows WinForms assemblies: string and byte resources, metadata enumeration, `UseResXDataNodes`, comments, node round trips, relative file references through `BasePath`, and standard type-name converter hooks.

Focused validation:

```text
ProGPU.Wpf.Tests PortableWinFormsResXResourceTests + designer resource tests -> Passed, 5 total
LibreWinForms.System.Windows.Forms package-mode build                         -> succeeds, 28 warnings, 0 errors
```

The package-mode build used a refreshed `LibreWPF.Interop/0.1.0-preview.sharpdevelop.1` package and an explicit `LibreWinFormsBridgePackageVersion=0.1.0-preview.sharpdevelop.1` so the source-owned LibreWinForms project resolved the current ProGPU dialog and service DTO contracts. The next SharpDevelop validation step is to remove or disable its transitional local `System.Resources.ResX*` shim in the LibreWPF wrapper, because those types now correctly belong to LibreWinForms.

SharpDevelop package-mode validation after disabling that transitional local shim:

```text
SharpDevelop.Full.LibreWpf fresh-cache Release build -> succeeds, 286 warnings, 0 errors
SharpDevelop broad smoke                             -> popups/resx/build/forms designer/property grid/context menu/completion all pass, exit code 0
```

The broad smoke used the refreshed local `LibreWinForms.System.Windows.Forms/0.1.0-preview.sharpdevelop.1` package and verified the package-owned ResX API through `LIBREWPF_SHARPDEVELOP_RESX_SMOKE=1`.

## Remaining work

- Replace the copied compatibility implementation with progressively reused upstream WinForms managed code from the submodule, keeping the same typed ProGPU/Silk.NET platform seams.
- Replace the remaining compatibility-only controls with upstream managed WinForms implementations behind typed ProGPU/Silk.NET platform services.
- Expand FormsDesigner runtime validation from the current load/selection/property mutation/minimal CodeDOM flush/event-preservation/event-property editing/component create-remove/resource-replay/localization-shape/handler-generation-contract/ResX-reader-writer coverage to real SharpDevelop source-navigation smoke, resource-file save/load round trips, toolbox placement details, and broader generated-code round trips.
- Make the standard WPF SDK pack workflow restore from the required private WPF feeds so local validation does not need generated package artifact refreshes.
