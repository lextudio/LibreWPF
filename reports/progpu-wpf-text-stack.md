# ProGPU WPF Text Stack Port

## Current Build State

After installing the repo-pinned SDK, restoring the managed projects, aligning `PresentationBuildTasks` with the bundled .NET target, fixing ApiCompat tool discovery, using system Perl on non-Windows, and skipping managed-graph `DirectWriteForwarder.vcxproj` references outside Windows, `PresentationCore` and `PresentationFramework` now compile on macOS. The `PresentationFramework` build reaches `ReachFramework`, so `ReachFramework.csproj` carries the same Windows-only DirectWriteForwarder guard as `PresentationCore` and `PresentationFramework`.

The non-Windows build uses a new managed `MS.Internal.Text.TextInterface.PortableTextInterface` boundary. It supplies the DirectWriteForwarder-managed type shape expected by WPF's existing font and text code, including font collections, font families, font faces, font metrics, glyph metrics, glyph offsets, typographic feature tags, `TextAnalyzer`, `ItemProps`, `IFontSource`, and factory types.

The first runtime text slice is now implemented for file-backed SFNT fonts. `PortableTextInterface` can enumerate WPF `IFontSourceCollection` entries and platform font folders, load `.ttf`, `.otf`, and `.ttc` files, parse TTC face offsets, read SFNT tables, expose localized `name` strings, read `head`/`hhea`/`maxp`/`hmtx`/`OS/2`/`post` metrics, map Unicode code points through cmap format 4 and 12, return glyph advances and basic bounds from `glyf`/`loca` when present, expose embedding rights, and hand GSUB/GPOS/GDEF table bytes to WPF's existing OpenType layout cache. The ProGPU submodule branch now strong-name signs its assemblies with public key token `c29c9752855ee183`, and `ProGPU.Text` exposes reusable `SfntFontFace` APIs for TTC face enumeration, localized name strings, table access, glyph count, cmap lookup, horizontal metrics, glyph bounds, embedding rights, and system font discovery through `FontApi`. The current portable parser remains self-contained for WPF-specific type shape and text fallback policy until the WPF build deliberately references the signed ProGPU text API and the remaining shaping/subsetting pieces are moved behind that boundary.

The WPF `PresentationCore` non-Windows build now source-includes ProGPU's `SfntFontFace.cs` from the submodule and uses it internally for SFNT face-offset enumeration, OpenType table-byte reads, glyph count, cmap lookup, glyph advances, glyph bearings, glyph bounds, symbol-cmap detection, and embedding-right reads. This avoids duplicating that slice while preserving WPF's public API surface. A direct `ProGPU.Text` project reference is still deferred because the WPF root build runs under its .NET 11 preview SDK while ProGPU currently targets net10.0 and pulls a larger backend/vector project graph.

The second runtime text slice adds a basic portable `TextAnalyzer` fallback. It maps one Unicode scalar to one glyph through the loaded portable font face, fills WPF cluster maps, handles surrogate pairs and formatting controls conservatively, and computes glyph advances from design metrics for `GetGlyphs`, `GetGlyphPlacements`, and `GetGlyphsAndTheirPlacements`. This is enough to move simple formatted-text paths past the DirectWrite call boundary, but it is not a full shaper: OpenType feature application, ligatures, mark positioning, contextual substitution, complex script shaping, bidi/script itemization beyond the current single-span fallback, exact TrueType subsetting, and LineServices replacement are still pending.

The portable `TrueTypeSubsetter.ComputeSubset` seam now has a conservative full-font-copy fallback. It avoids a non-Windows hard stop in `GlyphTypeface.ComputeSubset` and XPS font embedding paths, while deliberately preserving exact glyph subsetting as future work.

The old `MS.Internal.Span` issue is resolved by restoring the legacy non-generic span payload struct and updating mutable struct-list writes so modern C# compilers do not bind the formatter's internal spans to `System.Span<T>`.

## Decision

Do not restore the old DWrite wrapper as the final cross-platform implementation. The ProGPU port should introduce a managed text boundary that can compile WPF's existing font, glyph, and formatted-text code while routing platform-specific font discovery, shaping, and glyph metrics through ProGPU or a cross-platform shaping stack.

The transition layer should preserve WPF's public text API shape and already-shaped `GlyphRun` behavior first. Full `TextFormatter`, DirectWriteForwarder, TrueType subsetting, and LineServices replacement should come after this compile boundary is wired to real ProGPU or portable text services.

## Replacement Boundary

The cross-platform text layer should provide:

- a font collection abstraction for system fonts, folder fonts, and packaged fonts.
- a font face abstraction that exposes glyph advances, glyph bounds, baselines, style simulations, and localized informational strings needed by `GlyphTypeface`.
- a text analyzer abstraction for script itemization, bidi data, glyph shaping, glyph placement, OpenType feature application, and cluster maps.
- a feature/tag model compatible with WPF typography properties.
- metrics structs compatible with the existing WPF call sites.

The current implementation keeps WPF-specific font wrapper types, simple shaping fallback, and subsetting code inside `PresentationCore` for type-identity and incremental-port reasons. SFNT metadata, table, cmap, glyph metric, glyph bounds, and embedding-right discovery now come from the ProGPU-owned source-shared `SfntFontFace` helper. Future implementations should move shaping, fallback-chain policy, and exact subsetting as ProGPU exposes those APIs. More complete shaping should be backed by a portable shaping engine before formatted text fidelity is claimed.

## Build-System Decisions Already Made

- `PresentationBuildTasks` now uses `$(BundledNETCoreAppTargetFramework)` on Core MSBuild instead of a stale `net6.0` target.
- `DirectWriteForwarder.vcxproj` is only referenced on Windows in the managed `PresentationCore`, `ReachFramework`, and `PresentationFramework` build graphs.
- ProGPU assemblies are strong-name signed in the submodule branch with public key token `c29c9752855ee183`, and the branch carries its own `global.json` so nested WPF checkouts build ProGPU with SDK `10.0.201`.
- `PresentationCore` source-includes only `external/ProGPU/src/ProGPU.Text/SfntFontFace.cs` on non-Windows; the helper is public when built by ProGPU.Text and internal when linked into WPF so ApiCompat does not expose ProGPU types as WPF API.
- ApiCompat resolves the `tools/net9.0` layout used by the pinned `Microsoft.DotNet.ApiCompat` package.
- Perl-backed WPF source generation uses `/usr/bin/perl` outside Windows.

## Open Work

- Replace the remaining portable `MS.Internal.Text.TextInterface` simple glyph fallback with real signed ProGPU-backed or portable shaping logic. File-backed font discovery, glyph mapping, basic metrics, embedding rights, OpenType layout table access, and one-scalar-to-one-glyph advances now have managed implementations through the ProGPU-owned SFNT helper where the APIs are backend reusable.
- Replace the full-font-copy `TrueTypeSubsetter.ComputeSubset` fallback with a managed cross-platform subsetter or a ProGPU text implementation.
- Replace direct `DWriteLoader`/`dwrite.dll` assumptions behind a text factory abstraction on Windows and a portable implementation elsewhere. The module initializer now skips DPI/DirectWrite native loading on non-Windows.
- Decide whether LineServices is replaced directly, wrapped behind a compatibility layer, or bypassed for the first ProGPU formatted-text path.
- Add deeper runtime tests for `GlyphTypeface` construction against local `.ttf`/`.ttc` files once a test lane can reference the real built `PresentationCore` without colliding with the ProGPU shim identity.
