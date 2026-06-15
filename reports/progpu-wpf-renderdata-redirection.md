# ProGPU WPF RenderData Redirection Plan

## Purpose

WPF managed drawing currently enters `RenderDataDrawingContext`, then generated methods serialize each draw call through `RenderData.WriteDataRecord(MILCMD, ...)`. That is the last fully managed point before DUCE/MIL byte records and Windows-only native composition.

The ProGPU port should redirect that managed draw-call surface into `IWpfCompositionCommandSink` before MIL serialization. `WpfMilRenderDataDecoder` remains a transition bridge for existing `RenderData` buffers, but it should not be the final architecture.

## Current Source Evidence

- `src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/RenderDataDrawingContext.cs` owns lazy `RenderData` creation, close/dispose behavior, and push/pop balancing.
- `src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Generated/RenderDataDrawingContext.cs` contains the generated draw and push methods that call `_renderData.WriteDataRecord(MILCMD.Mil..., ...)`.
- `src/WpfGfx/codegen/mcg/generators/renderdata.cs` generates those methods, so the durable port needs a generator change rather than manual edits to generated files.

## ProGPU Transition Artifact

`src/ProGPU.Wpf/Composition/WpfCompositionDrawingContext.cs` is the first source-level command sink adapter. It exposes the constant drawing/push API shape used by managed WPF drawing code and forwards directly to `IWpfCompositionCommandSink`:

- line, rectangle, rounded rectangle, ellipse, geometry, image, text, and glyph run draw calls.
- generated no-op draw guards for null pens, null image/glyph resources, null geometry, and brush-plus-pen-null shape draws, matching generated `RenderDataDrawingContext` before any sink command or animation state is recorded.
- drawing-resource replay for WPF-shaped `GeometryDrawing`, `DrawingGroup`, `ImageDrawing`, and `GlyphRunDrawing` through the same transition replay helper used by the MIL decoder.
- generated animated overloads for line, rectangle, rounded rectangle, ellipse, image, video, and opacity, currently forwarding base values and counting non-null animation clocks as unsupported animation state.
- clip, opacity, opacity-mask, transform, and guideline push scopes.
- WPF-like stack-depth tracking, too-many-pop validation, close-time auto-balancing, and no-op scopes for null generated resources that still need matching pops.
- generated guideline fast paths for dynamic frozen guideline sets with one or two Y guidelines, preserving WPF's `PushGuidelineY1`/`PushGuidelineY2` behavior and feeding the ProGPU sink's basic Y-guideline snapping path; other dynamic frozen guideline sets are passed to sinks through `PushGuidelineSet(object?)` so ProGPU can snap supported X/Y primitive coordinates.
- source-level operation counters for applied and unsupported work, with drawing-resource replay returning `Applied`, `PartiallyApplied`, `Skipped`, or `Unsupported` status.
- explicit unsupported hooks for video draws and effect push scopes.

This bypasses MIL byte generation entirely for callers that can target the adapter. Tests prove direct forwarding and nesting behavior without requiring WebGPU.

Unsupported effect pushes are still treated as scopes, so a generated `Pop` or close-time auto-balance does not corrupt nesting. This mirrors the byte decoder's rule that unsupported push records must preserve stack balance even when their rendering behavior is unavailable.

The MIL byte decoder follows the same no-op scope rule for generated null resources: token `0` clip, opacity-mask, and transform pushes become applied no-op scopes, while nonzero unresolved resources remain skipped. `PushGuidelineSet` can use the optional `IWpfGuidelineSetResourceResolver` interface to pass raw guideline-set resources to capable sinks, falling back to the no-arg balance-only scope when no resolver is available.

The decoder also follows the source-level adapter's animation policy: base values are replayed for supported animated draw and opacity records, and every nonzero animation handle is counted as unsupported state until a ProGPU/WPF animation-resource bridge exists.

`ProGpuWpfCompositionTarget.OpenCompositionDrawingContext` creates a source-level context over the target's retained ProGPU root visual. `ProGpuWpfWindowHost.WpfDraw` exposes the same path during normal Silk.NET frame rendering and stores the callback's `WpfCompositionDrawingContextResult` in `LastSourceDrawingResult`.

`ProGpuWpfDrawingFrame` now owns the per-frame ProGPU command-buffer setup for that path. It clears and sizes the retained root once, then exposes drawing-context factories that can create multiple WPF-shaped `DrawingContext` wrappers over the same frame command buffer. This matches the provider shape needed by WPF's `VisualDrawingContext.Create`: multiple `RenderOpen()` calls in one frame must append into the active ProGPU frame instead of clearing prior visual output. The frame tracks drawing-context creation counts and the last owner visual so provider-created `RenderOpen()` activity is observable during bring-up.

`WpfRenderDataSinkProviderBridge` is the transition registration adapter for that provider shape. It probes the loaded `PresentationCore` identity for `System.Windows.Media.RenderDataDrawingContextSinkProvider.PushDrawingContextFactory(...)`, adapts a frame factory into the provider's exact delegate type, and returns the provider's scoped registration. In the current shim-only lane the provider type is absent, so the bridge returns `false`; once the real WPF provider and ProGPU drawing context share the same `PresentationCore` identity, this is the activation path for generated render-data redirection.

`ProGpuWpfWindowHost` now attempts that registration for each active render frame. The scoped registration wraps visual replay, source-level WPF drawing callbacks, raw drawing callbacks, and frame render events, then is disposed before presentation continues. This makes provider-backed `RenderOpen()` calls frame-local and prevents a ProGPU frame factory from leaking into later MIL/default rendering. `ProGpuWpfFrameEventArgs.DrawingFrame` exposes the active frame to callbacks that need frame-local diagnostics or additional provider-shaped drawing contexts.

Direct target replay uses the same frame-scoped activation path. `ProGpuWpfCompositionTarget.ReplayVisualSubtree(rootVisual, pixelWidth, pixelHeight, ...)` begins a `ProGpuWpfDrawingFrame`, attempts `TryRegisterRenderDataSinkProvider(...)`, and opens drawing contexts from that frame before replaying the WPF-shaped subtree. This keeps composition-target-only callers aligned with the Silk.NET host path.

`src/ProGPU.Wpf/Composition/IWpfGeneratedRenderDataDrawingContext.cs` is the current compile-time redirection contract for the generated draw/push/pop method surface. `WpfCompositionDrawingContext` implements it, including an explicit void `DrawDrawing` contract method over the status-returning replay API. This keeps the ProGPU source-level adapter shaped like WPF's generated `RenderDataDrawingContext` methods while the real generator still writes MIL records.

`src/ProGPU.Wpf/Composition/WpfRenderDataInstructionRedirectionCatalog.cs` records the WPF render-data instruction inventory, advanced-overload presence, generated no-op checks, generated internal methods, scope operations, and ProGPU-specific null-resource scope preservation decisions. `PushClip`, `PushOpacityMask`, and `PushTransform` deliberately preserve null resource scopes in the ProGPU bridge even though WPF's public source generator does not declare those as no-op groups; the transition MIL decoder can observe token `0` resources and still must keep later `Pop` calls balanced.

The real WPF source tree now has the first in-assembly redirection hook:

- `src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/IRenderDataDrawingContextSink.cs` defines the internal generated-method sink surface using real WPF media types.
- `RenderDataDrawingContext` has an internal sink constructor, closes the sink on dispose, and auto-pops pending sink scopes even when no `RenderData` buffer was allocated.
- `DrawingContextRenderDataSink` adapts that generated-method sink surface back to WPF's managed `DrawingContext` abstraction. This lets the redirected generated calls target an ordinary drawing-context implementation, including a future ProGPU-backed `DrawingContext`, without every backend reimplementing the sink interface directly.
- `src/Microsoft.DotNet.Wpf/src/WpfGfx/codegen/mcg/generators/renderdata.cs` emits `_renderDataSink` branches before `_renderData.WriteDataRecord(...)` in constant and animated generated methods. The generated no-op checks still run before redirection, and generated push/pop stack accounting remains owned by `RenderDataDrawingContext`.
- `src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Generated/RenderDataDrawingContext.cs` has been updated to match that generator shape: every generated draw/push/pop method now checks `_renderDataSink` before `EnsureRenderData()`, so sink-backed contexts do not allocate `RenderData` or write MIL records.
- `RenderDataDrawingContextSinkProvider` exposes the internal visual-owner-to-sink factory delegate through scoped `PushSinkFactory(...)` registration and a `PushDrawingContextFactory(...)` convenience path that wraps drawing contexts in `DrawingContextRenderDataSink`. Provider scopes are tracked as a nested registration stack so out-of-order disposal marks a scope inactive without replacing the current active frame, and top-scope disposal restores the nearest still-active previous registration. `VisualDrawingContext.Create` selects either a sink-backed or default MIL-backed visual drawing context.
- `DrawingVisual.RenderOpen()` and `UIElement.RenderOpen()` now call `VisualDrawingContext.Create(this)`, making the source-level WPF visual rendering entry points eligible for ProGPU sink routing while preserving the default MIL path when no factory is installed.
- `PresentationCore.csproj` includes the new internal sink contract.

The provider registration is deliberately owned by WPF `PresentationCore` for this step. The real WPF assembly uses strong-named friend access. The ProGPU submodule assemblies are now strong-name signed, but the current `ProGPU.Wpf` bridge still references the ProGPU shim `PresentationCore`; keeping the hook internal avoids exposing real WPF media types through a public cross-assembly API before the type-identity plan is settled.

The checked-in generated file was updated directly before the local workspace had the root WPF SDK pinned by `global.json`. The SDK is now installed under the ignored `.dotnet/` directory, `PresentationBuildTasks` was moved to the repo's bundled .NET target for Core MSBuild, and the non-Windows `PresentationCore` compile is unblocked. Rerun the WPF codegen pipeline next to verify the generated file is identical to `renderdata.cs` output.

## Generator Migration

The long-term change should complete the ProGPU generated path:

1. Rerun the WPF codegen pipeline with the required SDK and verify `System/Windows/Media/Generated/RenderDataDrawingContext.cs` remains in sync with the updated `renderdata.cs` generator.
2. Resolve WPF `PresentationCore` type identity and friend-assembly shape so `WpfRenderDataSinkProviderBridge` can successfully install the active `ProGpuWpfDrawingFrame` drawing-context factory into the real `RenderDataDrawingContextSinkProvider.PushDrawingContextFactory(...)` during ProGPU render frames. Strong-name signing for ProGPU assemblies is now in place.
3. Bridge the ProGPU command sink behind a WPF `DrawingContext` implementation so generated render-data methods can reuse the managed WPF drawing abstraction without reintroducing cross-assembly media type identity problems.
4. Preserve current behavior for animated parameters by forwarding base values first, count animation clocks as unsupported state while animation resources are unavailable, and add animation-aware sink methods only when ProGPU animation resource support exists.
5. Preserve `EnsureCorrectNesting` semantics by auto-popping the sink scopes on close, matching existing `RenderDataDrawingContext` behavior.
6. Keep unsupported generated operations such as video/effects explicit, counted, and testable rather than silently dropping them. The prototype path exposes `DrawVideo` and `PushEffect` hooks for this purpose.

## Open Decisions

- Where the final sink contract lives once real WPF `PresentationCore` is cross-platform. Keeping it in `PresentationCore` avoids type-identity problems with WPF `Brush`, `Pen`, `Geometry`, and `ImageSource`.
- Whether animation-aware generated overloads should become sink methods once animation clocks can be evaluated against the ProGPU timeline, or whether generated code should continue evaluating base/current values before recording ProGPU commands.
- How opacity-mask bounds are supplied for the source-level path. MIL records carry bounds; public `DrawingContext.PushOpacityMask(Brush)` does not.
- Whether source-level operation counters should be surfaced directly from the final WPF `CompositionTarget` or folded into the same replay-result model used by transition visual replay.

## Verification

`src/ProGPU.Wpf.Tests/Composition/WpfRenderDataGeneratorRedirectionTests.cs` parses `src/Microsoft.DotNet.Wpf/src/WpfGfx/codegen/mcg/xml/Resource.xml` and verifies the ProGPU redirection catalog covers every WPF `RenderDataInstruction`, preserves advanced-overload/no-op/internal/scope metadata, and that `WpfCompositionDrawingContext` implements the generated-method contract.

The same test file now verifies WPF's internal `IRenderDataDrawingContextSink` method surface, `PresentationCore.csproj` inclusion, sink lifetime/autopop changes in `RenderDataDrawingContext`, nested scoped sink-provider registration, the `DrawingContextRenderDataSink` forwarding surface, the generator branch that redirects to `_renderDataSink` before MIL serialization, the checked-in generated file's sink branches and stack accounting, and the visual `RenderOpen()` factory seam.

`src/ProGPU.Wpf.Tests/Composition/WpfManagedProjectGraphTests.cs` verifies the managed WPF `PresentationCore`, `PresentationFramework`, and `ReachFramework` project files keep `DirectWriteForwarder.vcxproj` behind `Condition="'$(OS)' == 'Windows_NT'"`, preventing the non-Windows render-data bring-up graph from importing Visual C++ targets.

`src/ProGPU.Wpf.Tests/Composition/WpfCompositionDrawingContextTests.cs` verifies direct draw forwarding, generated no-op draw guards, source-level drawing-resource replay, partial drawing-group replay accounting, animated overload degradation, null-resource no-op scopes, dynamic guideline fast paths, dynamic guideline-set pass-through, stack tracking, close-time auto-pop, too-many-pop errors, closed-context safety, unsupported video/effect accounting, and unsupported push-scope balance. `src/ProGPU.Wpf.Tests/Composition/Mil/WpfReplayToProGpuCommandTests.cs` verifies decoded MIL guideline scopes preserve pop balance, snap supported dynamic `GuidelineSet` X/Y primitive coordinates, snap supported Y-guideline primitive coordinates, preserve `PushGuidelineY2` driven-edge offsets, leave rotated transforms unsnapped, replay positive or dot-style dash-array line, rectangle, rounded-rectangle, ellipse, and path-compatible geometry outlines as native ProGPU line segments with transition square/round/triangle line-cap handling, replay filled dashed combined geometry as native combined fill plus operand-boundary dash segments with unsupported-fidelity accounting, preserve reflected WPF line-join/miter/cap metadata on native ProGPU command pens, and preserve WPF path-segment smooth-join metadata on native ProGPU path commands. `src/ProGPU.Wpf.Tests/Composition/Mil/StrokeJoinGeometryTests.cs` verifies native straight line-line bevel, miter, round, miter-limit fallback, smooth-join suppression geometry, and tangent-driven curve join geometry used by ProGPU path stroking. `src/ProGPU.Wpf.Tests/Composition/Mil/StrokeCapGeometryTests.cs` verifies native straight line square, round, triangle, flat, and tangent-driven curve endpoint cap geometry used by ProGPU path stroking. `src/ProGPU.Wpf.Tests/Composition/Mil/ArcSegmentGeometryTests.cs` verifies native arc center math, bounded flattening, sweep direction, zero-radius line fallback used by arc stroke emission, and static path-compiler arc record/fallback behavior. `src/ProGPU.Wpf.Tests/ProGpuWpfDrawingFrameTests.cs` verifies frame-scoped root clearing, pixel-size clamping, owner tracking, creation counters, and multi-wrapper drawing-context factories over a single ProGPU command buffer. `src/ProGPU.Wpf.Tests/WpfRenderDataSinkProviderBridgeTests.cs` verifies reflection registration against the provider delegate shape, frame-factory registration, direct composition-target replay source wiring, missing provider behavior, and incompatible delegate rejection. `src/ProGPU.Wpf.Tests/ProGpuWpfWindowHostTests.cs` verifies host callback invocation, frame-event active-frame exposure, result capture, default missing-provider behavior, and injected provider-registration scope disposal without requiring a live Silk.NET window.

`PresentationCore.csproj` and `PresentationFramework.csproj` now build on macOS with the repo-pinned SDK: `dotnet build src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj --no-restore --verbosity minimal` and `dotnet build src/Microsoft.DotNet.Wpf/src/PresentationFramework/PresentationFramework.csproj --no-restore --verbosity minimal` both succeed. The ProGPU lane test project is pinned to SDK `10.0.201`; running `dotnet test ../ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj --no-restore --verbosity minimal` from `src/ProGPU.Wpf` passes 341 tests.
