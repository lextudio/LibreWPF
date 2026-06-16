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

The companion `ManagedWpfSubsystemProjectsDoNotReferenceProGpuBridge` test guards the reuse boundary by keeping `ProGPU.Wpf`, `ProGPU.Scene`, and direct `external/ProGPU` references out of `System.Xaml`, `PresentationBuildTasks`, `PresentationFramework`, `PresentationUI`, and Fluent theme projects. `PresentationUiUsesManagedPrintingReferenceForNonWindowsBringup` locks down the current non-Windows build edge: native `System.Printing.vcxproj` is Windows-only, and non-Windows managed bring-up references `System.Printing-ref`.

## Current Verification

- Built `System.Xaml`, `PresentationBuildTasks` (`net11.0`), `PresentationCore`, and `PresentationFramework` directly.
- Built `PresentationFramework.Fluent` with internal markup compilation; this exercises the real theme XAML and `PresentationUI` graph.
- Built and ran `ProGPU.Wpf.RealPresentationCoreHarness`; the harness registered the real `PresentationCore` render-data provider successfully.
- Built `ProGPU.Wpf.Tests` and ran `WpfManagedProjectGraphTests`; 21 focused tests passed.
