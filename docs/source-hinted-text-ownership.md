# Optional hinted source generation

`WpfPortableTextFormatting` now exposes `IPortableHintedTextFormatting` separately
from ordinary formatting. It formats complete original UTF-16 using ProGPU's shared
native mapper/producer, explicit source metrics and physical style settings, then
owns the target-independent hinted resource. `WpfHintedTextParagraph` copies actual
baseline/height, horizontal pen origins and native interaction. It never computes
replacement line tops, source positions or caret geometry.

Independent paragraph and glyph-run references share the live original resource.
Glyph-run references own exact original positioned indices in the supplied order,
including repeated and non-ink occurrences. Closing one handle cannot retire a
sibling; the last handle retains failed teardown for retry, including reentrancy.
No `TtfFont` or generic `NativeFont` object represents the hinted generation.

This is an explicit adapter capability only. `PortableTextLine` still rejects
Display and does not call the optional formatter. Continuations, collapse,
intrinsic widths, tabs, objects, whole-word wrapping, empty-row carets and
transformed rendering require their remaining native/source contracts. Request
features must be scoped to individual explicit styles; unsupported inputs reject
instead of silently falling through to ordinary formatting.

This draft depends on the companion ProGPU typed-contract/native-overload change.
The checked-in `external/ProGPU` pin and package versions remain unchanged and do
not yet provide those APIs. Do not claim this draft builds against the old pin.
Source-only compile review can link these adapter sources into an isolated test
project referencing the companion ProGPU source projects. Actual source-core
compilation can temporarily remap the existing `external/ProGPU` project references
and linked compile items to that companion checkout using an external MSBuild
`CustomAfterMicrosoftCommonTargets` import, without editing the gitlink or source
project. Neither approach is runtime/package qualification. Only
the complete successful producer Build for the eventual exact integrated commit
can qualify staging and a subsequent submodule/package update.

The companion ProGPU `docs/source-hinted-text-ownership.md` records architecture,
primary-source design references, complexity and remaining qualification. Current
tests cover the optional contract, pre-native request rejection and independent
ownership/fault/retry semantics; authored controls are not executed native or UI
evidence. Showcase Display text remains the acceptance application/action blocked
by source policy/interaction integration and complete runtime qualification.

Source-only validation on 2026-10-01 compiled the actual ordinary/hinted adapter
files, ownership types, companion ProGPU projects, all 29 new contract/admission/
lifetime cases and the native package paragraph validation source through one
isolated C# project. The final Release build reported zero warnings and errors.
The root agent subsequently executed all 29 device-free contract/admission/lifetime
tests successfully (zero skipped); no native/font call, GPU work or source
application was executed. This is not a whole WPF build against its still-unchanged
submodule pin.

## Explicit source GlyphRun transport and replay

An owned run may explicitly publish through `IPortableHintedGlyphRunBindingFactory`.
`WpfHintedGlyphRunBinding` checks one original physical font, em size and bidi level,
retains the exact occurrence indices, and copies original glyph IDs/positions without
reshaping, advance reconstruction or source-local positioning. It selects a view
of one cached paragraph geometry: physical outlines/segments are shared, while
original occurrence order, repeats and non-ink slots stay distinct. Ink bounds use
ProGPU's shared original-geometry projection, including its canonical DPI arithmetic.

Source `GlyphRun.InitializePortableHintedGlyphRun` is explicit, one-shot, and must
precede observed ink or any ordinary export. It verifies source IDs, bidi, exact
em/origin/DPI and rejects sideways/simulated/device-font runs. The typed owned export bypasses
design-font adaptation; `ComputeInkBoundingBox` reads original hinted ink, while
the unconnected public outline-building path rejects rather than rebuilding design
outlines. Its four public caret methods explicitly reject hinted bindings until
original paragraph source-map interaction connects. The initializer now validates
original font and advance identity as described below. Source offsets and the
original source frame remain required before source formatter selection, alongside
carets/continuations.
This does not make `PortableTextLine` create hinted GlyphRuns automatically.

Actual render-data/object/drawing ingress passes the typed owner to recorded replay.
`ProGpuCompositionCommandSink` uses lease-aware `DrawHintedGlyphs`, preserving the
source hit owner, brush domain, original origin and explicit transform. The picture
owns the selected view after source bindings end; managed and native recorded-scene
consumers use the existing hinted command. Additional guidelines, nonidentity bases,
unsupported rendering modes/DPI and hinted BitmapCacheBrush coverage remain rejected.

Native MIL serializes original IDs/positions with zero canonical advances (positions
are already paragraph-local), then atomically imports the exact original generation
and occurrence indices. It does not attach a design-font alias or call the ordinary
SFNT sideband for hinted runs. Batch copies acquire independent owners; compilation
sessions retain their own batch, import unchanged and changed occurrence selections
in the same transaction as canonical deltas, and rebuild when producer identity
changes. Actual native transaction metrics are returned through the companion API.
Retired source batches remain queued after failed disposal for creating-session
retry; imported channels/compiled scenes retain their independent copied generation.

The paragraph geometry copy costs O(outlines + segments + occurrences) once;
publication costs O(selected occurrences) without recopying physical geometry.
Each MIL batch flattens exact indices once and imports each original resource once,
not once per glyph. No source font is decoded or executed during publication/replay.

The companion geometry change passed 35 focused device-free selection/recording
tests. A bounded isolated Release compile of all actual `ProGPU.Wpf` adapter sources
against companion managed shim/source projects passed with zero errors (two existing
shim/event warnings); it caught and corrected a bounds type and local-name collision.
This check matches the adapter's existing analyzer settings and is not source-core
`GlyphRun` compilation, native execution, package staging or a complete WPF build.
New device-free admission controls cover unknown/malformed hinted providers, cleanup
fault preservation, no design-font fallback and no command publication on rejection.
The root agent ran 36 focused adapter/contract/lifetime tests successfully before
the subsequent batch-copy and source-method-placement guards were added. Source
review caught a misplaced ink return outside the actual source method; it was moved
to `ComputeInkBoundingBox`, and ink is now read before final owner publication.
Method-placement guards are not a substitute for compiling source PresentationCore.

## Exact source font and advance admission

The additive binding-factory overload accepts the actual source `PortableTextFont`
and source advances. Its default implementation rejects, preserving older factories
without silently skipping identity checks. The concrete provider compares the
selected original resource's exact font bytes, face index and units-per-em before
allocating/publishing a binding. Every selected occurrence must use a style without
design variation coordinates, and the original shaping context must have no
normalized coordinates: source `GlyphTypeface` cannot express these instances,
so even explicitly default coordinates reject.
Bytes/face/UPM are not advertised as complete variable-instance identity.
Inputs are borrowed synchronously: the provider
does not retain caller font memory or advance spans. `GlyphTypeface` owns a copied
font-stream snapshot shared with ordinary `PortableTextLine`; moving that existing
cache does not change Ideal formatting or font selection.

Every source advance must be the exact double promotion of that selected original
positioned occurrence's horizontal advance, including repeated and non-ink slots.
Validation rejects vertical or nonfinite advances, narrowing differences and
changed zero signs; it never reconstructs advances from positions. Source `GlyphRun`
copies glyph IDs and advances before validation, then publishes read-only owned
lists only after all binding checks and ink reads succeed. Later caller-list edits
cannot change those validated source identities. Comparing font bytes is O(font
bytes); selected-advance validation and the owned source snapshots are O(occurrences).

This does not validate source offsets, characters, cluster/caret maps or baseline
frames. The existing RTL source offset formula consumes nominal design advance
from `GlyphTypeface.AdvanceWidths`, whose dictionary explicitly uses Ideal metrics
divided by units-per-em. That value is neither hinted `HorizontalAdvance266` nor
positioned/GPOS `AdvanceX`; the retained flat native resource does not expose that
original nominal design metric. A future producer contract must retain exact
nominal metrics and their original face/instance identity, with unavailable metrics
rejected, and connect the original line/source frame. The separate source outline
path's mode-dependent nominal query/rounding is not qualified by the Ideal formula.
Display selection and hinted public caret/outline gates remain closed.

On 2026-10-01, the bounded actual source `PresentationCore.csproj` Release build
and its reference assembly passed with zero warnings/errors (24.15 seconds), using
the external dependency-path remap above. This compiled the real changed `GlyphRun`,
`GlyphTypeface` and `PortableTextLine`, not substitutes or source-text guards. The
source build's existing managed project/reference and PresentationBuildTasks
dependencies were restored/built; installed SDK 10.0.201 was selected from the
external harness directory. The adapter compiled separately against companion
managed sources with zero errors and its existing shim/event warnings. All 54
focused device-free adapter/contract/admission/lifetime tests passed, zero skipped,
including 14 new identity cases and the new older-factory rejection control. No
font parser/native call, GPU, application, whole WPF matrix or old-pin build was
executed or qualified by these checks.
