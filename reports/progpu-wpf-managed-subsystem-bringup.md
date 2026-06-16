# ProGPU WPF Managed Subsystem Bring-up

## Porting Decision

The port should reuse upstream managed WPF code wherever it can compile and execute. ProGPU and Silk.NET are platform/rendering replacements at the boundary, not a reason to rewrite `System.Xaml`, `PresentationBuildTasks`, `PresentationCore`, `PresentationFramework`, controls, themes, resources, or the XAML compiler in the ProGPU bridge.

The current architecture keeps this boundary:

- `System.Xaml` remains the real WPF XAML reader, schema, object writer, and markup-extension implementation.
- `PresentationBuildTasks` remains the real WPF XAML/BAML compiler path (`MarkupCompilePass1`, `MarkupCompilePass2`, `MarkupCompiler`, BAML writer/records).
- `PresentationFramework` remains the real WPF application, window, resource, style/template, control, document, and BAML/XAML loader implementation.
- `PresentationUI` remains the real WPF document/application support assembly. Windows builds keep the native `System.Printing` C++/CLI implementation, while non-Windows bring-up uses the existing `System.Printing-ref` project so the managed theme/XAML graph can compile without Visual C++ targets.
- Theme assemblies remain real WPF theme projects with `Page` XAML and internal markup compilation.
- `ProGPU.Wpf` supplies adapter services for rendering, retained scene ownership, portable windowing, input, dispatcher wakeups, and platform services without adding ProGPU references to the managed WPF subsystem projects.

## Basic App Smoke Ladder

1. **Managed core build gate**
   - Build `WindowsBase`, `System.Xaml`, `PresentationCore`, `PresentationFramework`, `PresentationUI`, `PresentationBuildTasks`, and at least one theme assembly.
   - Purpose: prove the upstream managed subsystem graph remains reusable on non-Windows.

2. **Code-only app gate**
   - Instantiate `Application`, `Window`, layout controls, text controls, resource dictionaries, and drawing visuals without XAML.
   - Route `Window.Show` through `PortableWindowActivationService` into the ProGPU/Silk.NET host.

3. **XAML compiler gate**
   - Build a tiny app with `ApplicationDefinition` and `Page` items using the real `PresentationBuildTasks`.
   - Verify generated BAML and code-behind connector paths are produced by WPF's compiler, not a ProGPU-local compiler.

4. **XAML runtime gate**
   - Load the generated app resources through real `PresentationFramework` BAML/XAML loaders.
   - Verify `ResourceDictionary`, `Style`, `ControlTemplate`, `StaticResource`, `DynamicResource`, and namescope behavior.

5. **Theme gate**
   - Compile and load the Fluent theme project and representative style dictionaries (`Button`, `Window`, `RichTextBox`).
   - Verify themed controls render through the ProGPU retained scene bridge.

6. **Rich framework gate**
   - Exercise controls, routed input/commands, focus, selection, `TextBox`, `RichTextBox`, `FlowDocument`, bindings, resources, and templates.
   - Keep failures attached to the real WPF subsystem that produced them instead of replacing the subsystem.

7. **Rendering integration gate**
   - Continue lowering WPF drawing, effects, brushes, text, 3D, retained invalidation, and dirty-branch replay to native ProGPU primitives.
   - Managed WPF should own semantics; ProGPU should own GPU execution.

## Current Checked Gate

`WpfManagedProjectGraphTests.ManagedSubsystemBringupReusesRealWpfXamlFrameworkAndThemeProjects` now locks down the initial managed-subsystem graph:

- real `System.Xaml` source files for reader/object-writer/markup-extension support exist in the graph;
- real `PresentationBuildTasks` markup compiler and BAML writer sources remain in the graph;
- real `PresentationFramework` application/window/resource/style/template/BAML/rich-text sources remain in the graph;
- real Fluent theme XAML remains marked for internal markup compilation;
- the real `PresentationCore` compatibility harness references WPF as an external subsystem while `ProGPU.Wpf` stays outside the managed WPF projects.

`WpfManagedProjectGraphTests.RealPresentationFrameworkHarnessExercisesManagedFrameworkAndProGpuBridge` now locks down the first real `PresentationFramework` code-only smoke harness. `src/ProGPU.Wpf.RealPresentationFrameworkHarness` loads the built `PresentationFramework.dll` and `PresentationCore.dll` in an isolated assembly load context, constructs real `Application`, `Window`, `StackPanel`, `TextBox`, `RichTextBox`, `FlowDocument`, `ResourceDictionary`, `Style`, and `ControlTemplate` instances, registers portable window activation, and verifies a real `DrawingVisual.RenderOpen()` draw routes through `WpfRenderDataSinkProviderBridge` into a retained ProGPU owner branch.

`WpfManagedProjectGraphTests.RealXamlCompilerHarnessUsesWpfApplicationDefinitionAndPagePipeline` now locks down the first app-style XAML compiler smoke. `src/ProGPU.Wpf.RealXamlCompilerHarness` uses `InternalMarkupCompilation=true`, an `ApplicationDefinition` (`App.xaml`), and a `Page` (`MainWindow.xaml`) with code-behind partial classes. Building this project invokes the real `PresentationBuildTasks` `MarkupCompilePass1`/resource pipeline and emits `App.g.cs`, `MainWindow.g.cs`, `App.baml`, `MainWindow.baml`, and `ProGPU.Wpf.RealXamlCompilerHarness.g.resources` under `artifacts/obj/ProGPU.Wpf.RealXamlCompilerHarness/Debug/net11.0`.

`WpfManagedProjectGraphTests.RealXamlRuntimeHarnessLoadsCompiledBamlThroughRealPresentationFramework` now locks down the first runtime XAML/BAML smoke. `src/ProGPU.Wpf.RealXamlRuntimeHarness` builds the compiled XAML app as a build-only project reference, loads that app assembly with the real `PresentationFramework` and `PresentationCore` assemblies in an isolated load context, invokes the generated `App.InitializeComponent()` and `MainWindow.InitializeComponent()` paths, verifies real runtime resources, static-resource resolution, generated namescope connection, `TextBox`, `RichTextBox`, and `FlowDocument` content, and then attaches the XAML-created `Window` to the existing portable ProGPU activation path. This keeps the XAML runtime owned by WPF while ProGPU owns only the native window/rendering boundary.

The companion `ManagedWpfSubsystemProjectsDoNotReferenceProGpuBridge` test guards the reuse boundary by keeping `ProGPU.Wpf`, `ProGPU.Scene`, and direct `external/ProGPU` references out of `System.Xaml`, `PresentationBuildTasks`, `PresentationFramework`, `PresentationUI`, and Fluent theme projects. `PresentationUiUsesManagedPrintingReferenceForNonWindowsBringup` locks down the current non-Windows build edge: native `System.Printing.vcxproj` is Windows-only, and non-Windows managed bring-up references `System.Printing-ref`.

The new real-framework harness intentionally references `ProGPU.Wpf` and `ProGPU.Scene` only in the harness process, not in the managed WPF framework projects. It also references the real `PresentationCore` and `PresentationFramework` projects with `ReferenceOutputAssembly=false` and `PrivateAssets=all` so the harness builds the real assemblies but loads them explicitly. That keeps type identity honest while the bridge still adapts real WPF objects through reflection/object-sink registration.

## Portability Decisions Opened By The Harness

- Registry reads, ETW, Win32 system metrics/colors, keyboard-layout queries, cursors, font directories, DPI discovery, high-contrast/theme metadata, and hidden theme-notification HWNDs are now guarded with conservative non-Windows defaults only where the real framework smoke proved they were needed.
- The non-Windows dispatcher path no longer creates the Win32 message-only HWND; this is a bring-up gate, not the final loop. The final implementation should replace that with the existing Silk.NET/ProGPU native-loop wake and dispatcher drain services.
- `CompositionEngineLock` skips `wpfgfx_cor3.dll` on non-Windows so managed `MediaContext` startup can run disconnected while ProGPU owns render-data execution. Windows still uses the original MILCore lock and startup path.
- The default non-Windows DPI path is 96 DPI until a platform metrics service supplies real monitor/window DPI through the Silk.NET host.

## Current Verification

- Built `System.Xaml`, `PresentationBuildTasks` (`net11.0`), `PresentationCore`, and `PresentationFramework` directly.
- Built `PresentationFramework.Fluent` with internal markup compilation; this exercises the real theme XAML and `PresentationUI` graph.
- Built and ran `ProGPU.Wpf.RealPresentationCoreHarness`; the harness registered the real `PresentationCore` render-data provider successfully.
- Built and ran `ProGPU.Wpf.RealPresentationFrameworkHarness`; the harness constructed the real framework code-only app surface and verified retained ProGPU owner-branch `DrawingVisual.RenderOpen()` routing.
- Built `ProGPU.Wpf.RealXamlCompilerHarness`; the real markup compiler emitted generated partial classes, BAML, and `.g.resources` for `ApplicationDefinition` plus `Page` inputs.
- Built and ran `ProGPU.Wpf.RealXamlRuntimeHarness`; the harness loaded generated BAML through real `PresentationFramework`, verified runtime resources/namescopes/content, and attached the compiled XAML `Window` to portable ProGPU activation.
- Built `ProGPU.Wpf.Tests` and ran `WpfManagedProjectGraphTests`; 25 focused graph tests passed, covering the real `PresentationFramework` harness, the real XAML compiler harness, the real XAML runtime harness, and native-entrypoint guard coverage.
