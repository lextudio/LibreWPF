# ProGPU Branch Plan for WPF Port Acceleration

## Current Branch State

The WPF superproject tracks ProGPU submodule branch `fix/render-invalidation-and-leaks`. The branch has been fast-forwarded to the latest origin commits and now includes:

- vector rendering fidelity work for WPF-style pens, caps, joins, arcs, dashed geometry, gradient brush packing, brush transforms, bitmap sampling modes, aliased vector/text rendering flags, and related shader/storage-buffer changes.
- repo-level strong-name signing for ProGPU assemblies with public key token `c29c9752855ee183`.
- a reusable `ProGPU.Text.SfntFontFace` metadata/table reader plus `FontApi` updates for TTC face enumeration, localized name strings, and cross-platform system font directory discovery.
- `SfntFontFace` source-sharing visibility so the helper remains public in `ProGPU.Text` but compiles as internal when WPF links that single source file under `PresentationCore`.
- a ProGPU-local `global.json` pinned to SDK `10.0.201` so the submodule builds consistently even when nested below WPF's .NET 11 preview SDK checkout.
- a signing regression test in `ProGPU.Tests`.

## Decisions

- Keep feature work that accelerates WPF rendering fidelity in the ProGPU branch when it improves the backend model directly, instead of compensating in the WPF bridge.
- Keep WPF-specific type adaptation in `src/ProGPU.Wpf` until the real WPF `PresentationCore` and ProGPU shim type identities are unified.
- Prefer adding reusable ProGPU primitives for WPF concepts that also benefit other frontends: exact path stroking, gradient tables, texture sampling modes, text flags, mesh extensions, image effects, and retained resource lifetime helpers.
- Use signed ProGPU assemblies for direct references from strong-named WPF assemblies when the TFM/project graph is compatible. Until then, source-share small ProGPU-owned helpers only when they remain internal to WPF and avoid exposing ProGPU namespaces as WPF public API.

## ProGPU Features to Implement Next

- Text: build on the new reusable SFNT metadata/table reader by adding cmap/metrics APIs, OpenType layout table access, glyph shaping hooks, glyph placement, fallback chains, and exact subset writing so WPF `PortableTextInterface` can delegate instead of carrying a duplicate parser.
- Geometry: move remaining WPF fidelity gaps into ProGPU vector code, including exact boolean-result dashed outlines, non-axis-aligned guideline snapping policy, exact arc stroking beyond bounded polyline approximation, and robust cap clipping at complex joins.
- Brushes: keep expanding ProGPU brush ABI and shaders for WPF brush semantics, including more tile-brush transform cases, color-profile policy, and any remaining gradient interpolation/spread edge cases.
- Images: add ProGPU image codec and color-management seams so WPF bitmap sources can move away from reflection-based `CopyPixels` transition upload and toward backend-owned image resources.
- Composition: add backend-level retained resource invalidation/versioning helpers so WPF `Freezable`/visual invalidation can map to ProGPU dirty tracking without reflection polling.
- Windowing: keep Silk.NET surface and input abstractions in ProGPU reusable enough for WPF's eventual `CompositionTarget` replacement, including resize, DPI, activation, file-drop, cursor, timer, and dispatcher wakeup hooks.

## WPF Integration Follow-Ups

- Replace the local `PortableTextInterface` SFNT reader with signed `ProGPU.Text` services incrementally: face-offset/table discovery now uses the ProGPU-owned helper, while glyph metrics, shaping, and subsetting should move as the ProGPU text APIs cover those surfaces.
- Make the ProGPU-backed WPF `DrawingContext` live in the real WPF type identity or a signed friend assembly so `RenderDataDrawingContextSinkProvider.PushDrawingContextFactory(...)` can be activated without reflection/type mismatch.
- Add root WPF project references to signed ProGPU assemblies only when the referenced APIs are actually used by the non-Windows WPF implementation.
- Keep documenting each new backend requirement here before adding WPF-side compatibility code, so durable backend fixes are preferred over short-lived bridge workarounds.
