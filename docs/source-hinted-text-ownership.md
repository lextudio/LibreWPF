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
Display, and neither source `GlyphRun` publication nor either renderer selects
the new resource. Continuations, collapse, intrinsic widths, tabs, objects,
whole-word wrapping, empty-row carets, authoritative hinted ink bounds and
transformed rendering require their remaining native/source contracts. Request
features must be scoped to individual explicit styles; unsupported inputs reject
instead of silently falling through to ordinary formatting.

This draft depends on the companion ProGPU typed-contract/native-overload change.
The checked-in `external/ProGPU` pin and package versions remain unchanged and do
not yet provide those APIs. Do not claim this draft builds against the old pin.
Source-only compile review can link these adapter sources into an isolated test
project referencing the companion ProGPU source projects, or place that exact
companion checkout at `external/ProGPU` in a disposable build checkout without
committing its gitlink. Neither approach is runtime/package qualification. Only
the complete successful producer Build for the eventual exact integrated commit
can qualify staging and a subsequent submodule/package update.

The companion ProGPU `docs/source-hinted-text-ownership.md` records architecture,
primary-source design references, complexity and remaining qualification. Current
tests cover the optional contract, pre-native request rejection and independent
ownership/fault/retry semantics; authored controls are not executed native or UI
evidence. Showcase Display text remains the acceptance application/action blocked
by the original source formatter and glyph replay integration.

Source-only validation on 2026-10-01 compiled the actual ordinary/hinted adapter
files, ownership types, companion ProGPU projects, all 29 new contract/admission/
lifetime cases and the native package paragraph validation source through one
isolated C# project. The final Release build reported zero warnings and errors.
No test, native/font call, GPU work or source application was executed. This is
not a whole WPF build against its still-unchanged submodule pin.
