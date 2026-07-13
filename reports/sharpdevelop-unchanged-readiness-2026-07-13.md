# SharpDevelop on LibreWPF/LibreWinForms/ProGPU: unchanged-source readiness

Date: 2026-07-13

## Executive assessment

There are two different readiness questions, and they currently have different answers:

1. **Can the current LibreWPF SharpDevelop port build and run the full workbench?**
   Yes. The current port builds the full workbench and automated package-mode runs cover editor, project, menu/toolbar, popup, AvalonDock, resource, class-diagram, both WinForms and WPF designers, reporting-designer, save/reload, and shutdown paths. LibreWinForms preview.11 is immutably published, all three package IDs are repository-signed and downloadable, and the clean-cache public-only SharpDevelop preview.11 lane passes with 296 established warnings and 0 errors plus every requested workbench smoke.
2. **Can an unmodified upstream SharpDevelop checkout run by only selecting LibreWPF/LibreWinForms/ProGPU packages?**
   Not yet. The SharpDevelop fork currently contains 47 LibreWPF-labelled commits, 127 tracked source/project files that mention `LIBREWPF`, and 241 preprocessor conditionals that mention `LIBREWPF` (220 are exact `#if LIBREWPF`). Many changes are legitimate application portability work, but they prove that the package stack has not yet absorbed every host assumption needed by unchanged upstream source.

The nearest practical milestone—**full current-fork workbench reproducibility from public packages, including both designers**—has therefore been reached. The next objective is to deepen designer interaction coverage, then move reusable compatibility out of SharpDevelop and into typed LibreWPF/LibreWinForms/ProGPU seams until the application-only diff becomes small and declarative.

In concrete work units rather than a misleading single percentage:

- the **current ported fork** has a reproducible public preview baseline on SharpDevelop master, including both WinForms and WPF designers in the same exact clean-cache gate;
- the **WinForms designer** has a usable core and is roughly two to four focused interaction/metadata slices from a comfortable MVP;
- the **WPF designer** now works in the actual workbench for load, presentation, surface and outline selection, PropertyGrid synchronization, edit, undo/redo, and save, but remains roughly three to five interaction/toolbox/adorner slices from a comfortable MVP;
- **literal unchanged upstream SharpDevelop** remains a multi-phase compatibility extraction: the 241 conditional regions need classification and the reusable portions must move into typed framework seams before that claim can be made.

## Readiness matrix

| Area | Current evidence | Distance to a useful milestone | Remaining blockers |
|---|---|---|---|
| Public dependency closure | ProGPU `0.1.0-preview.12` is immutable at `3eb90de946e0df7ea242756bd172de66f46a2276`; LibreWPF preview.11 and LibreWinForms preview.11 are immutably published. LibreWinForms Release `29256570682` is green; all eight GitHub assets match the audited artifact; all three NuGet IDs are repository-signed, v3-indexed, and HTTP 200 downloadable. | Reached for the preview.11 application lane. | Repeat the same provenance/index/public-cache gate for future aligned previews. |
| Full current-fork workbench | Before WPF-designer integration, the clean-cache public-package build passed with 289 warnings and 0 errors. After integration it passes with 296 warnings and 0 errors; StartPage, Search/Replace, ClassDiagram, FormsDesigner, WPF designer, ResourceToolkit, HexEditor, and Reporting all pass and exit cleanly. | Reproducible preview baseline reached; not equivalent to all interactive IDE behavior. | Early-close determinism, IME, managed debugger backend, remaining native-window/dialog paths, and broader manual input. |
| Literal unchanged upstream source | Not achieved. SharpDevelop still owns wrapper projects and platform conditionals. | Multi-phase, not a one-commit package switch. | Convert application portability conditionals into reusable typed framework/platform behavior; keep only product composition and optional diagnostics in SharpDevelop. |
| WinForms designer | The real SharpDevelop FormsDesigner loads the 21-component LineCounter surface, shows PropertyGrid state, persists CodeDOM, creates/removes controls, moves/resizes them, and passes undo/redo. Nested services, lifecycle, attributed designers, selection adorners, eight resize handles, reflection-free UndoEngine coverage, typed scrolled-parent coordinates, grid/snap behavior, modifier bypass, toolbox placement, and native/fallback visual snap-line overlays are green. | A useful interactive core exists; a comfortable MVP is a few focused slices away. | Multi-selection/group movement, keyboard nudges, richer parent rules, extender providers, verbs/menu commands, inherited/read-only components, localized resources, and event-handler creation/navigation. |
| WPF/XAML designer | The core libraries, add-in, and full preview.11 aggregate build with 0 errors. In the actual SharpDevelop workbench a real project/XAML file attaches through the secondary display binding, presents a `DesignSurface`, selects a `Grid`, populates PropertyGrid, then selects the actual child through the typed outline model, synchronizes DesignContext and PropertyGrid, edits its `Tag`, exercises undo/redo and serialization, restores prior state, preserves the source checksum, and exits cleanly. | Basic designer and outline operation are proven; a comfortable interactive MVP remains several ordered slices. | Rendered Outline Pad click/focus and multi-selection, toolbox insertion, pointer hit-testing, selection adorners, move/resize, richer property-editor interaction, error recovery, resource/theme breadth, and save/reload with code-behind projects. |
| DataGridView-heavy designer/property UI | Typed hit testing and cell rectangles, validated current-cell/current-row state, read-only inheritance, real child TextBox/ComboBox editors, commit/cancel/Enter/Escape lifecycle, and hosted pointer/render routing are integrated on LibreWinForms `d550881789`; exact CI `29260747661` is green. | The core interaction slice is complete; keyboard/binding breadth follows. | Add tab/arrow traversal, validation/error flow, binding, virtual mode, accessibility, and large-data virtualization breadth. |
| ProGPU/System.Drawing | Preview.11/12 include the reconciled WinForms/System.Drawing work, Icon serialization, ClassDiagram named pens/brushes, and the full rendering/package gates. | Sufficient for current designer smokes; extend only for concrete missing primitives. | Continue exact drawing/text/image/print fidelity discovered by real designer documents; avoid host-specific workarounds and avoid overlapping SkiaSharp parity work. |

## What already works in the WinForms designer

The current result is beyond a static preview. Automated runs prove these application-level operations:

- load the unchanged LineCounter designer document into SharpDevelop's real `FormsDesignerViewContent`;
- site and enumerate 21 components through the managed design host;
- select components through the selection service and PropertyGrid;
- serialize normal property mutations back through CodeDOM and restore the source byte-for-byte;
- create a toolbox control through designer input, move and resize it, and remove it;
- undo and redo move/resize/removal through SharpDevelop's real undo surface;
- render custom paint and owner-draw content through LibreWinForms and ProGPU;
- run source-built Reporting designer primitives and its focused 103-test suite.

The layout-engine work now connects `WindowsFormsDesignerOptionService` values to typed placement/manipulation behavior. LibreWinForms commits `5903e6d24` and `b2943bdbd` add native-shaped padded/scrolled `DisplayRectangle` behavior, common-root coordinate translation, deterministic grid and candidate snap-line computation, nested/scrolled parent handling, toolbox placement, resize constraints, and modifier bypass without reflection. The integrated behavior executable reports `grid=12 toolbox=2 snap=9 alt=2 coordinates=1 transactions=8 sourceGuard=7`.

Visual snap-line feedback is now integrated on LibreWinForms exact `450b44c4e789b9edf95f31a1e012e87d9389cd66` in three granular commits. `b77665097` adds a typed `IPortableWinFormsAdornerSource`, exact matched-guide state, union invalidation, and post-child-tree overlay rendering through native ProGPU recording or an isolated retained fallback surface. `ec57d42d0` gates move, resize, toolbox placement, Alt bypass, commit/cancel/dispose, host dispatch, and package-mode behavior; `450b44c4e` documents the architecture. Local evidence is `adorners=15`, `snapLineAdorner=True`, full behavior/docs/reflection gates, three-package pack/bundle, and all 14 fresh-cache SDK smoke modes green. Exact branch CI `29269988347` is running.

The next completed slice is typed `DataGridView` interaction. LibreWinForms commits `d2161a997` and `d55088178` add shared cell geometry/hit testing, current-cell/current-row state, read-only inheritance, real TextBox/ComboBox editing children, commit/cancel/Enter/Escape behavior, and host pointer/render routing. Focused evidence is `geometry=14 current=9 edit=24 host=10`; full behavior, docs, reflection audit, three-package pack rehearsal, package-only SharpDevelop SDK smoke, and exact CI `29260747661` are green.

## WPF designer status and gap

SharpDevelop's WPF designer is a separate subsystem from the already-running WinForms designer. Its source graph is present under:

- `src/Libraries/WpfDesigner/WpfDesign`
- `src/Libraries/WpfDesigner/WpfDesign.XamlDom`
- `src/Libraries/WpfDesigner/WpfDesign.Designer`
- `src/Libraries/WpfDesigner/XamlDesigner`
- `src/AddIns/DisplayBindings/WpfDesign/WpfDesign.AddIn`

The first bootstrap, real-workbench gate, and typed outline interaction are now integrated and pushed on SharpDevelop master at exact `ecf4bfcf8186a4ad6d1fb2876ea0c18edd80b68c`:

- WpfDesigner `ec25886d9a` adds exact-identity LibreWPF projects for `WpfDesign`, `WpfDesign.XamlDom`, and `WpfDesign.Designer`.
- WpfDesigner `76aa9e0b85` adds a typed load/edit/save/unload smoke. It passes with `root=Grid child=Button edit=Updated save=True unload=True`.
- SharpDevelop commits through `58f9f3792d` build and compose the add-in, manifest, and three libraries into the coherent preview.11 full-workbench aggregate. The aggregate builds with 296 established warnings and 0 errors.
- SharpDevelop `907d22a7d0` exercises the registered secondary display binding in the actual workbench. The exact result is `root=System.Windows.Window selected=System.Windows.Controls.Grid propertyGrid=True presented=True edit=True undo=True redo=True save=True`; exit code is zero and the source XAML SHA-256 is unchanged before and after the run.
- SharpDevelop `139295d336` integrates the feature, `4a02d25422` adds it to the public-package gate, and `63cd4ae9c5` documents the resulting portable designer acceptance.
- SharpDevelop `6a96e13c33` selects the actual `Grid` child through `Outline.Root`/`IOutlineNode.IsSelected`, verifies the DesignContext primary/only selection and PropertyGrid synchronization, performs a typed `Tag` edit with undo/redo and in-memory XAML serialization, then restores the original selection and property grid. `ecf4bfcf81` adds exact public-gate assertions and a source guard against reflection-based field, method, or event discovery. The integrated real-workbench run reports every outline flag true, exits 0, and preserves the `MainWindow.xaml` checksum `3669670408`/`3596`.

The three libraries build with only three legacy serialization/CAS warnings. The add-in graph builds with 0 errors. Project acquisition and design-context activation are typed; the smoke waits for the loaded solution/startup project and for a stable design context rather than using runtime reflection or private-field probes.

The remaining implementation order is:

1. Exercise the rendered Outline Pad TreeView click/focus path, multi-selection, drag/reorder, lock/visibility toggles, and real property-editor controls.
2. Add toolbox insertion and verify generated XAML plus undo/redo in the real workbench.
3. Validate typed pointer hit testing, selection adorners, move/resize, multi-selection, and undo/redo.
4. Extend round-trip coverage to `UserControl`, merged resources, custom project controls, and code-behind event wiring while preserving source checksums when changes are rolled back.
5. Port the standalone `XamlDesigner` host only where it adds coverage not already exercised by SharpDevelop.

## Definition of “full unchanged SharpDevelop”

The target should be considered reached only when all of the following are true:

- a clean upstream-equivalent SharpDevelop source checkout needs no platform-specific application patch to compile;
- its only configuration change is selecting supported LibreWPF/LibreWinForms packages and the portable desktop SDK/runtime entry point;
- the normal add-in graph, including both WinForms and WPF designers, loads without a curated reduced list;
- editor, project system, build, debugger, dialogs, docking, menus, resource tools, ClassDiagram, Reporting, WinForms designer, and WPF designer pass user-driven and automated workflows;
- the bridge remains reflection-free in product hot paths, with missing state supplied by typed framework contracts;
- no private feed, local package pin, source checkout path, or application-specific ProGPU workaround is required.

The current work meets much of the runtime-workbench criterion, most of the WinForms designer core criterion, and the public-package architecture criterion. It does not yet meet the unchanged-source, WPF-designer, debugger, IME, or complete interactive-platform criteria.

## Where the unchanged-source work is concentrated

The 241 preprocessor regions that mention `LIBREWPF` are not evenly distributed. The largest files are:

- `AvalonEdit.AddIn/Src/CodeEditor.cs`: 21 regions;
- `SharpDevelop/Project/ProjectService.cs`: 15 regions;
- `AvalonEdit.AddIn/Src/AvalonEditViewContent.cs`: 10 regions;
- `Main/Base/Project/Util/DotnetDetection.cs`: 8 regions;
- `SharpDevelop/Startup/SharpDevelopMain.cs`: 7 regions;
- `WorkbenchStartup.cs`, `MSBuildEngineWorker.cs`, `GlobalAssemblyCacheService.cs`, `CodeEditorAdapter.cs`, and `CSharpTextEditorExtension.cs`: 6 regions each.

This makes the next unchanged-source phase measurable. Editor/input behavior, project/build/runtime discovery, and startup/platform services should be audited as clusters. Each region must be classified as either deliberate portable application composition, an obsolete framework workaround, or a reusable typed LibreWPF/LibreWinForms seam. Only the last two classes reduce the unchanged-source distance; merely moving conditionals between SharpDevelop files does not.

The counts above were measured on SharpDevelop `ecf4bfcf8186a4ad6d1fb2876ea0c18edd80b68c`: `git rev-list --count master --grep=LibreWPF` reports 47 commits, recursive tracked-source search reports 127 files, and `rg '^#(?:if|elif).*LIBREWPF'` reports 241 conditional directives. The narrower exact `#if LIBREWPF` form occurs 220 times.

The broader branch delta is an intentionally conservative upper bound: relative to merge-base `3f3ae2a5d4ffafd98cdf1c09308b9b8d93b0356c` with `upstream/master`, this fork is 84 commits ahead and changes 225 paths (`15,206` insertions and `1,483` deletions). That includes tests, package wrappers, CI, reports, and deliberate product composition, so it is not a count of 225 framework blockers; it does show why “unchanged upstream” is a compatibility-extraction program rather than one final build fix.

## Immediate execution order

1. Let the exact aligned LibreWPF CI complete for WPF `3b306e9fd57d13e98393cf2a0a84c1db57f3c987`, ProGPU `bd4b770106df65986b229634ba9b1a9eb06fd6bc`, and LibreWinForms `d5508817892da193293302501cdb397e4b0ecc25`; keep immutable preview tags unchanged.
2. Add WinForms designer multi-selection/group manipulation, keyboard nudging, and the remaining parent/verb/extender/event design-time contracts now that visual snap-line adorners and attributed designer activation are green.
3. Continue WPF designer rendered-outline/toolbox/adorner/pointer manipulation and custom-control/resource coverage now that typed outline-to-surface selection is integrated.
4. Keep moving reusable fixes into LibreWPF, LibreWinForms, or ProGPU and reduce SharpDevelop-only `LIBREWPF` conditionals after each proven seam.

This report should be updated with exact commits, CI runs, package indexes, and smoke output after each slice rather than replacing evidence with a single subjective completion percentage.
