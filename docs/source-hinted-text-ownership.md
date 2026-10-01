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
Display and does not call the optional formatter. Continuation qualification, collapse,
intrinsic widths, tabs, objects, whole-word wrapping, empty-row carets and
transformed rendering require their remaining native/source contracts. Request
features must be scoped to individual explicit styles; unsupported inputs reject
instead of silently falling through to ordinary formatting.

The checked-in `external/ProGPU` pin is now
`60347a5f1026b8f95582535e6b7cfc8431a04499`, whose complete producer Build
`36915664259` passed all 59 jobs and whose full CI passed all 74 checks. It provides
the companion typed contracts and native overloads required below. The historical
source-only checks in this document used external dependency remapping before that
qualification. Package versions and WPF application/Display admission remain
separate; a qualified dependency is not whole WPF runtime/package qualification.

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
original font, advances and nominal source offsets as described below. The original
single-line frame now separates baseline from drawing translation. Display-specific
metrics/rounding, caret maps and continuations remain required for source selection.
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

The existing RTL source offset formula consumes nominal design advance
from `GlyphTypeface.AdvanceWidths`, whose dictionary explicitly uses Ideal metrics
divided by units-per-em. That value is neither hinted `HorizontalAdvance266` nor
positioned/GPOS `AdvanceX`. Explicit `FormatHintedWithNominalMetrics` now selects
the companion original hmtx preparation, which rejects unavailable metrics and
coordinate-bearing instances. The ordinary hinted preparation remains unchanged.
The separate source outline path's mode-dependent nominal query/rounding is not
qualified by the Ideal formula.
Display selection and hinted public caret/outline gates remain closed.

## Exact nominal offsets and retained line frames

The concrete owned hinted paragraph now implements the existing source paragraph
and inline paragraph contracts when its original resource has nominal metrics and
measured writer frames. Its source line top and ascent come from values captured
at the native writer's original publication, not a height prefix or subtraction
of baseline metadata. The source `PortableTextLine` consumes this same paragraph:
run acquisition retains original positioned indices, gets canonical offsets in
one native batch and publishes the existing typed owning GlyphRun binding.
Its ordinary Ideal path is unchanged; hinted runs never request a `TtfFont` alias
or use the source face's per-glyph advance dictionary for positioning.

Hit tests, caret distance and selections call the existing native interaction
operations on the retained boxes/carets. Logical movement uses only original
caret stops, without invented start/end stops. Selection Y conversion belongs to
the backend's original writer frame. Lines and continuation clones retain separate
uses of the same generation; disposing the original line or producer cannot end a
sibling's use. Failed retirement remains owned for retry. Ordinary paragraph
snapshots do not gain an extra reference/finalizer allocation.

This connects source consumers, not ordinary Display selection: source device
metrics and interpreter policy still require independent Windows comparisons.
Empty rows without native carets, hinted collapse, variable
instances and public hinted GlyphRun caret/outline operations remain unsupported.
Explicit preparation and source nominal-offset publication keep their existing
Ideal-only guard; there is no silent Display-to-Ideal conversion.

## Retained hinted width-changing continuations

`WpfHintedTextParagraph` implements `IPortableReflowTextParagraph`, connecting
the existing `PortableTextLine.CreateContinuation` path to the original native
resource's retained reflow. The call forwards the exact original input position
and requested width; the new paragraph keeps the complete original source text.
Native code validates a shaped cluster boundary, places the retained logical
suffix and provides new writer frames/interaction while preserving original
logical glyph, run and font identities. No source substring is shaped and no
source position, advance or line top is reconstructed in the adapter.

The continuation independently owns its native resource. The source gate rejects
recursive reflow and checks closure after pending retirement and native return.
Failed publication preserves its original exception and keeps an unsuccessfully
disposed resource for explicit retry before another reflow or during Dispose.
The cleanup slot is inline, so successful reflow adds no cleanup-object allocation.
Closing the source handle still attempts both original and failed-continuation
retirement without publishing a replacement generation.

The acceptance action is changed-width next-line formatting in ShowcaseApp,
through the original source `PortableTextLine` continuation path. The focused
checks compile the actual adapter against the companion backend and exercise
five retirement controls plus three source connection guards. They do not run
native reflow or the application, and do not qualify package integration, Display
selection, collapse, empty-row carets or variable font instances. The unchanged
submodule pin still awaits a successful complete producer Build.

The current bounded internal checks passed 63 actual-adapter/neutral tests and
40 companion backend/neutral tests, zero skipped, plus five source paragraph
reference lifetime tests. The adapter compile retains its existing shim/event
warnings. Native producer and transport controls are authored and strict C++20
syntax checked only; no native/font/GPU or application execution is claimed.
The actual source `PresentationCore.csproj` and reference assembly Release build
also passed with zero warnings/errors (29.05 seconds), compiling the real changed
`PortableTextLine`, `TextLineBreak` and paragraph-reference helper. Dependencies
were remapped from the unchanged old gitlink to the isolated companion contract
tree through `/private/tmp/hinted-lines-checks.uW7nmhiI/HintedSourceDependencies.targets`.
This is source compilation, not qualification of the pinned package or a full
WPF/native build. The focused harnesses are `Backend.Tests.csproj` and
`Adapter.Tests.csproj` beside that remap; both ran Release with `-m:1` and
`-p:BuildInParallel=false` from the internal scratch directory.

The source initializer now calls the complete font/advance/offset overload. It
copies IDs, advances and offsets, validates through the original native producer,
then publishes read-only source lists only after every check and ink read succeeds.
Null/empty source offsets retain their ordinary zero-offset meaning. Unsupported
providers reject through the additive default method rather than silently ignoring
the new requirements. Source baseline and em must be exactly representable floats;
the initializer explicitly rejects Display-mode injection while only the source's
existing Ideal nominal-offset convention is supported.

One producer call validates the original occurrence selection against its actual
retained line baseline and original nominal hmtx advances. Cross-line, vertical,
changed offsets and unrepresentable translations reject atomically. The original
paragraph glyph positions never change. The returned paragraph translation drives
both existing recorded and native MIL replay; a separately returned baseline-relative
translation drives source ink bounds. `SourceFrame` identifies the original line
and source baseline, which is no longer mistaken for a paragraph draw offset.
No source-local layout arithmetic, design-font alias, per-glyph crossing or reshaping
is added. Original source character/cluster/caret mapping, Display metric rounding,
full source frame coverage, continuation qualification, collapse and UI qualification remain open.

The actual changed source and reference PresentationCore compiled on 2026-10-01
with zero warnings/errors in 17.23 seconds. All 56 focused adapter/neutral controls
passed, zero skipped; the real adapter compiled with its existing shim/event
warnings. The companion managed resource/lease/frame suite passed all 26 cases,
zero skipped. Native producer/frame/alias controls are authored and strict-syntax
checked, not executed. The source compile remapped the unqualified old submodule
dependency only through the disposable external
`hinted-frame-checks.Ju7HFN5X/HintedSourceDependencies.targets`; no pin changed and
no successful complete WPF/package/native/UI qualification is claimed.

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

## Continuation formatting and device identity

The acceptance action is ShowcaseApp/AvalonDock text relayout after a formatting
mode or DPI change. `PortableTextLine.CreateContinuation` runs before the ordinary
formatter admission gate, so a retained Ideal break must not become an implicit
Display path or reuse device-owned glyphs at another DPI.

The continuation now checks requested mode, sideways state and exact original DPI
before width conversion, retained paragraph reads or native reflow. Formatter mode
is constructor-only; DPI is captured in a readonly generation field because both
`TextSource.PixelsPerDip` and the public `TextLine.PixelsPerDip` property are mutable.
Continuation and ordinary collapsed-view copies preserve that original field,
including symbol formatting and GlyphRun publication. No metrics are rescaled,
rounded or repaired. A mismatch rejects with an instruction to format a fresh
paragraph; fresh Display formatting still rejects at its existing policy gate.
Another Ideal formatter at the original DPI can consume a valid cloned break,
including width-only reflow, after the original line and registration are disposed.

The actual source `PresentationCore.csproj` Release build passed with zero warnings
or errors (8.62 seconds), using the existing external dependency-path remap,
`--no-restore`, and `BuildProjectReferences=false`. A small signed harness linked
the real `PortableTextLineTests.cs` against that newly built production assembly;
all five selected source tests passed, zero skipped (452 ms). The controls include
changed mode/DPI/sideways at both retained and changed widths, no provider reads or
reflow on rejection, public line-DPI mutation, valid cloned-break reuse, and ordinary
collapsed views retaining their original device identity. The harness is
`/private/tmp/librewpf-continuation-identity.k060SZ6C/Continuation.Tests.csproj`.
These are CPU/source checks, not native hinted reflow, GPU or application execution.
The ProGPU gitlink, package qualification, Display metrics/interpreter selection,
hinted collapse and public hinted GlyphRun caret/outline gates remain unchanged.

## Failed continuation producer retirement

A successful provider Reflow transfers an initial hinted producer reference to
the source caller before source validation or line construction can finish. The
line break now captures that exact producer before reading its metadata. If
validation, construction or publication fails, it keeps both the producer and any
constructed line whose independent cleanup failed. Another continuation or clone
drains those owners before paragraph reads, reflow or publication; a failed drain
does not create another generation. Original source exceptions remain primary.

Public break disposal closes admission before callbacks and retains the exact
continuation when retirement is incomplete. Reentrant disposal during reflow,
publication or clone acquisition is completed on the active operation's unwind;
no line or clone can be published after close. Failed explicit disposal can be
retried without ending a provider use twice, matching the existing
`WpfHintedTextLifetime.Lease` contract. The continuation's single-attempt finalizer
is suppressed for ordinary snapshots and armed only when an initial hinted
producer is captured. It provides the same bounded disposal fallback as the
existing hinted leases, not an unbounded finalizer retry or native completion
claim. Ordinary snapshot ownership and valid Ideal continuations stay unchanged.

The actual `PresentationCore.csproj` Release build passed with zero warnings and
errors in 10.10 seconds using the same external managed dependency remap. The
signed actual-source fixture harness above passed all 11 selected tests, zero
skipped (314 ms): six new fault/reentrancy cases plus the five preceding
continuation/device/collapse controls. The new cases cover exact validation and
constructor exception identity, failed same-width admission, repeated public
break disposal, deferred reentrant close, rejected clone publication, and both
post-construction owners failing cleanup independently. They use typed CPU fault
fixtures, not native font/rendering execution. Display policy, collapse/caret
qualification, the ProGPU gitlink and whole-package/application gates are unchanged.

## Source GlyphRun bidi direction

The original `TextShapeableCharacters.ComputeShapedGlyphRun` publishes
`rightToLeft ? 1 : 0`, although Line Services retains complete embedding levels.
`PortableTextLine` now makes that same projection only when constructing its public
`GlyphRun`. Adjacent native levels zero/two and one/three remain separate source
runs; grouping, source ranges, selection and paragraph snapshots retain their
original complete levels. `WpfHintedGlyphRunBinding.BidiLevel` also exposes zero or
one while retaining the original level in its owned state. Its selection validator
still rejects mixed complete levels even when parity agrees, and source publication
still requires exact equality with the projected binding. Caller-created public
runs at levels two/three are not silently accepted through a parity-only check.

The actual source `PresentationCore.csproj` Release rebuild passed with zero
warnings/errors (15.20 seconds), using the existing read-only external dependency
remap and `BuildProjectReferences=false`. The signed source harness passed both new
tests, zero skipped: actual formatter grouping/public direction and direct source
publication rejection, including all four caret methods and outline admission.
A separate managed harness linked the production hinted adapter sources against
the qualified `0.1.0-preview.3435.ci` feed from exact ProGPU `60347a5f`. All 42
binding, identity, snapshot and admission tests passed, zero skipped. The new
binding test used the existing qualified native runtime for CPU TrueType shaping
and scalar outline projection, checking original levels zero/two/one/three,
projected binding direction, retained raw selection indices and same-parity
mixed-level rejection. Its antialiased-vector coverage preserves the font's
original outline contract; no GPU or native build was run.

The source harness remains `/private/tmp/librewpf-continuation-identity.k060SZ6C/`;
the qualified binding harness and isolated package cache are in
`/private/tmp/librewpf-bidi-binding.U3poe9g4/`. These checks do not qualify Display
metrics, wrapping, hinted caret/outline behavior, WPF package integration or an
application. Those gates remain unchanged.

### Aligned source integration checkpoint

Build `36926579575` at WPF `88a1aa418a1e946cba1313a6b52e48260062750d`
failed before the affected source-contract tests ran: the new paragraph-lifetime
fixture had six internal-field naming violations and one redundant Xunit import.
The fixture now uses state properties and the existing global import; its five
original lifetime tests pass with code-style analyzers and the repository rules
enabled in `/private/tmp/librewpf-reference-analyzers.UkCL4OAJ/`. Neither assertions
nor production lifetime behavior changed.

The same Build's canonical Forms job correctly rejected a mismatched nested
ProGPU pin. WPF now pins exact LibreWinForms source
`6ea74029ba54b96c20422f2e64ae99d053a1f8b0`, whose ProGPU gitlink is the same
`60347a5f1026b8f95582535e6b7cfc8431a04499` retained by WPF. The canonical graph
guard is unchanged. This is a stacked source integration checkpoint: the Forms
source CI and the next whole WPF Build remain pending, not qualified by their
bounded source tests. Native runtime evidence remains limited to the existing
qualified ProGPU603 Build `36915664259`; no pending Forms artifact or new native
build is used by these tests.
