# ProGPU Branch Plan for WPF Port Acceleration

## Current Branch State

The WPF superproject tracks ProGPU submodule branch `fix/render-invalidation-and-leaks`. The branch has been fast-forwarded to the latest origin commits and now includes:

- vector rendering fidelity work for WPF-style pens, caps, joins, arcs, dashed geometry, gradient brush packing, brush transforms, bitmap sampling modes, aliased vector/text rendering flags, and related shader/storage-buffer changes.
- repo-level strong-name signing for ProGPU assemblies with public key token `c29c9752855ee183`.
- ProGPU-owned affine arc transformation in `ProGPU.Vector.ArcSegmentGeometry`, so WPF transformed path arcs can remain native ProGPU arc segments through translation, scale, rotation, shear, and orientation-reversing transforms.
- shader-native ProGPU arc stroke strips in `ProGPU.Scene.Compositor`/`ProGPU.Backend.Shaders`, using WPF arc center math once on the CPU and evaluating stroke vertices from transformed ellipse axes in WGSL instead of flattening arcs to line segments.
- exact ProGPU arc bounds for `PathAtlas` and path-op compilation, so WPF path fills and combined-geometry operations can keep native GPU arc segment records without atlas clipping from coarse sampled bounds.
- ProGPU-owned subarc construction for dashed WPF arc spans, allowing the WPF bridge to split visible dash intervals into native `ArcSegment` commands that still render through the ProGPU arc shader instead of cubic/polyline fallback geometry.
- reusable `ProGPU.Text.SfntFontFace` metadata/table/cmap/glyph-metric APIs plus `FontApi` updates for TTC face enumeration, localized name strings, and cross-platform system font directory discovery.
- reusable `ProGPU.Text.SfntSimpleGlyphShaper` APIs for the current one-scalar-to-one-glyph portable fallback, including surrogate cluster maps, soft-hyphen/control handling, and metric-derived advance calculation.
- reusable `ProGPU.Text.SfntFontSubsetter` APIs for glyph-ID-preserving TrueType subset writing, including composite glyph dependency closure, `glyf`/`loca`/`head` rewriting, checksum recalculation, and stale `DSIG` removal.
- reusable `ProGPU.Backend.PixelDataConverter` APIs for WPF/WIC-shaped bitmap row conversion to Pbgra32, so image upload normalization does not live only in the WPF bridge.
- native `ProGPU.Scene` WPF shader-effect execution primitives: `WpfShaderEffectParams`, `DrawWpfShaderEffect(...)`, and `WpfShaderEffectExtensionPipeline` for WGSL/WebGPU effects with WPF-style constant registers, a fixed 16-slot sampler-register bank, explicit compositor-compatible pipeline layouts, cached native bind groups/render pipelines, transparent fallback bindings for unbound samplers, and headless render-path coverage.
- ProGPU.Text source-sharing visibility so focused helpers remain public when built by `ProGPU.Text` but compile as internal when WPF links them under `PresentationCore`.
- a ProGPU-local `global.json` pinned to SDK `10.0.201` so the submodule builds consistently even when nested below WPF's .NET 11 preview SDK checkout.
- a signing regression test in `ProGPU.Tests`.

## Decisions

- Keep feature work that accelerates WPF rendering fidelity in the ProGPU branch when it improves the backend model directly, instead of compensating in the WPF bridge.
- Keep WPF arc semantics in ProGPU vector primitives. The WPF bridge may adapt WPF types, but affine arc math, sweep flipping, and shader-rasterized arc records belong in `ProGPU.Vector`/`ProGPU.Scene`.
- Keep WPF-specific type adaptation in `src/ProGPU.Wpf` until the real WPF `PresentationCore` and ProGPU shim type identities are unified.
- Prefer adding reusable ProGPU primitives for WPF concepts that also benefit other frontends: exact path stroking, gradient tables, texture sampling modes, text flags, mesh extensions, image effects, and retained resource lifetime helpers.
- Use signed ProGPU assemblies for direct references from strong-named WPF assemblies when the TFM/project graph is compatible. Until then, source-share small ProGPU-owned helpers only when they remain internal to WPF and avoid exposing ProGPU namespaces as WPF public API.

## ProGPU Features to Implement Next

- Text: build on the reusable SFNT, simple shaper, and subsetter helpers by adding richer OpenType layout table APIs, full glyph shaping hooks, fallback chains, compact glyph-remapping subset writing, CFF subsetting, and cmap/name pruning so WPF `PortableTextInterface` can delegate more of the remaining text stack instead of carrying compatibility code.
- Geometry: move remaining WPF fidelity gaps into ProGPU vector code, including exact boolean-result dashed outlines, non-axis-aligned guideline snapping policy, and robust cap clipping at complex joins.
- Brushes: keep expanding ProGPU brush ABI and shaders for WPF brush semantics, including more tile-brush transform cases, color-profile policy, and any remaining gradient interpolation/spread edge cases.
- Images: build on `PixelDataConverter` with ProGPU image codec and color-management seams so WPF bitmap sources can move away from reflection-based `CopyPixels` transition upload and toward backend-owned image resources.
- Effects: continue mapping WPF effect resources onto ProGPU-native shader/compute/render pipelines. Visual `BlurEffect` and `DropShadowEffect` descriptors can now flow through retained ProGPU host replay; next backend-facing work is WPF `ShaderEffect` lowering into the native shader-effect sampler bank, generated `PushEffect` resource mapping, and legacy Direct3D pixel-shader bytecode translation or explicit replacement registration. Avoid managed CPU shader emulation.
- Composition: add backend-level retained resource invalidation/versioning helpers so WPF `Freezable`/visual invalidation can map to ProGPU dirty tracking without reflection polling.
- Windowing: keep Silk.NET surface and input abstractions in ProGPU reusable enough for WPF's eventual `CompositionTarget` replacement, including resize, DPI, activation, file-drop, cursor, timer, and dispatcher wakeup hooks.
- WPF command conformance: add ProGPU-side fixtures for every generated WPF render-data instruction that reaches `IWpfCompositionCommandSink`, so unsupported command state is tracked in ProGPU tests before WPF bridge code grows new compatibility branches.

## WPF Integration Follow-Ups

- Replace the local `PortableTextInterface` text stack with signed `ProGPU.Text` services incrementally: face-offset/table/cmap/glyph-metric discovery, the simple one-scalar-to-one-glyph fallback, and glyph-ID-preserving TrueType subsetting now use ProGPU-owned source-shared helpers, while full shaping, fallback-chain policy, compact glyph remapping, and CFF subsetting should move as the ProGPU text APIs cover those surfaces.
- Use the portable WPF MCG path before editing generated render-data files by hand: `mcg.proj` can now run `Resources.rsp` and `Elements.rsp` through the Roslyn-backed `net10.0` CSP target on non-Windows, and the Elements XML serializer model is source-owned instead of regenerated by `xsd.exe`.
- Use the new object-sink render-data provider path as the immediate integration bridge for real WPF `PresentationCore`: `PushObjectSinkFactory(...)` can pass real WPF media objects to ProGPU.Wpf for reflection adaptation while the shim and real WPF type identities are still separate.
- Keep `src/ProGPU.Wpf.RealPresentationCoreHarness` green as the real-provider compatibility check for `WpfRenderDataSinkProviderBridge.TryRegisterRenderDataSinkProvider(Assembly, ...)` until type identity is unified.
- Make the ProGPU-backed WPF `DrawingContext` live in the real WPF type identity or a signed friend assembly later so `RenderDataDrawingContextSinkProvider.PushDrawingContextFactory(...)` can replace the object/reflection bridge.
- Add root WPF project references to signed ProGPU assemblies only when the referenced APIs are actually used by the non-Windows WPF implementation.
- Keep documenting each new backend requirement here before adding WPF-side compatibility code, so durable backend fixes are preferred over short-lived bridge workarounds.
