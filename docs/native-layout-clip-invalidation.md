# Retained layout clip invalidation

Acceptance targets are idle ShowcaseApp and Toolkit/AvalonDock windows after
layout or scrolling has settled. The blocking source path is
`FrameworkElement.GetLayoutClipInternal` → the typed portable layout descriptor
→ `WpfVisualInvalidationTracker`. A layout getter may legitimately return a new
geometry instance on every read, including a zero-width or zero-height clip.
Comparing that temporary object by reference continuously requests another
dirty pass. [Issue #179](https://github.com/wieslawsoltes/LibreWPF/issues/179)
reports this behavior in an AvalonDock application.

The bounded change is in the shared invalidation tracker, used by both renderer
modes. It does not change source layout, clip geometry, native input, rendering
admission, or window scheduling. No source getter result is cached blindly.

## Value capture and ownership

`WpfLayoutClipKey` reads `IPortablePrimitiveGeometrySource` first. Its inline
key preserves kind, points, rectangle (including `IsEmpty`), radii and all six
double-precision affine components. Supported primitives do not request a path
or allocate snapshot arrays. This describes the key capture, not allocations
inside the source getter itself.

Other supported clips use `IPortableGeometryPathSource`. The key owns a private
deep copy of the path tree, figures and segment arrays. All path metadata,
ordered figures and segments, fill/closure/stroke flags, arc parameters,
transforms, bounds, combined operation and ordered operands participate in
equality. Caller-owned path DTOs remain mutable; neither a shared object nor an
unchanged array reference proves unchanged content.

Polling compares actual current values against the retained copy and reuses
that copy when equal. New durable storage is needed only for initial capture
or a changed path. Equality never relies on a hash alone. Hashes use the same
fields and `double` equality semantics, including signed zero and NaN.
Snapshotting malformed numeric data does not make it admissible for drawing.

Unavailable or unrecognized descriptors retain the original reference identity
policy; their virtual `Equals` is not consulted. Recursive copies are bounded
to 64 levels and 65,536 total node, figure and segment occurrences, counting
shared subtrees each time. Cycles, missing children/arrays, unknown enum values
and oversized inputs retain identity comparison rather than becoming empty.
These are optimization limits, not new renderer limits. An absent layout-clip
property, null clip, empty clip and finite zero-area rectangle remain distinct.
Transitions between an available value descriptor and rejected metadata on the
same provider remain observable. Existing typed-export exceptions propagate.

The existing concrete dictionaries, single visual/dependency traversal, dirty
batch publication, subscription coalescing and source-owner identity are
unchanged. Ordinary visual `Clip` and `Transform` policies are not generalized
by this layout-clip repair.

## Qualification

The public-tracker regressions cover repeatedly allocated equal clips,
primitive fields and transforms, in-place path/array mutation, combined
operands, missing/unknown descriptors, cycle and size bounds, and coalesced
invalidation. They assert dirty-pass admission and events, not a fabricated
rendered-frame count. Separate key tests verify immutable ownership and equal
hashes. On macOS ARM64 with the repository SDK and .NET 10.0.5, the actual
`eng/progpu-wpf-layout-clip.sh` gate passed all 619 tracker, renderer and
window-host cases with zero skips. The unchanged parent, with the same 97
public regression cases added, failed 86 of those cases and passed the other
11 controls plus all 516 existing focused cases. No retry or test/GC policy
change was used to produce these results.

Sixteen separate `PresentationFramework.Tests` cases exercise actual
`FrameworkElement` and `Border` layout getters after arrangement: positive
and zero extents, constrained margins, right/bottom alignment, RTL transforms,
resize/margin/direction transitions and `ClipToBounds` toggles. They passed
with the Portable backend, zero skips, a minimum of 16 tests and a 60-second
deadline. The typed primitive and path exports are compared against literal
source values across fresh geometry objects. These source tests do not import
the bridge's compatibility shim or replace `GetLayoutClip` with a test getter.

CI adds a fast independent Ubuntu retained-invalidation job and runs the
actual-source producer gate after the existing source MessageBox gate, reusing
its compiled test assembly. All existing package, native and application jobs
and deadlines remain required. Missing tests, failures and skips fail the new
gates. Exact-head full CI remains a separate merge requirement.

The initial Ubuntu job at `b4c1a80cd537ae3d1632f74f068b1d62ca9749e3`
[built successfully and executed all 619 cases](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36258688561/job/108450226844):
614 passed, five failed, zero skipped. All 103 new clip contracts passed. The five
existing window-host cases failed while `CreateHeadless` initialized WebGPU,
reporting that no suitable adapter was available; they did not fail a clip-value
assertion. The new job had omitted the headless Linux prerequisite already used
by ProGPU's CI. It now installs `libvulkan1` and `mesa-vulkan-drivers` through the
pinned ProGPU installer before running the unchanged 619-case gate. No adapter
override, renderer fallback, test exclusion, timeout, GC, or parallelism change is
introduced. The job retains its original 20-minute deadline; corrected-head Linux
execution and the whole CI result remain required.

The full unchanged-parent bridge run initially had 18 missing-native-library
failures and two stale source-string guards in addition to the 86 regressions.
Staging both native providers from the exact successful ProGPU
`b622c4b0f106d003c9259a0b24abeba8c54773db` Build
[35921346610](https://github.com/wieslawsoltes/ProGPU/actions/runs/35921346610)
removed all 18 provisioning failures: 1,860 passed, 88 failed, zero skipped.
The two stale guards describe contracts changed by the already merged
hyperlink-owner and window-chrome changes; they require current typed signatures,
not removal of the guarded behavior.

The reported application CPU/allocation rates are not benchmark results for
this change. Actual settled-window frame counts and native application/package
qualification remain required; source contract success alone does not close
the application report. DPI lookup cost and transparent-control input policy
are separate issues, not implicitly fixed here.
# Passive native boundary follow-up

The b0a78 Windows x64 receipt retained initial and scrolled exact-zero intervals,
then native-resized presented frame 5 → 6 despite equal source/frame-state fields.
ARM64 exited with `0xC0000005` after creating an empty application receipt; it
provided no fault stack. Neither failure is waived or attributed to layout clips.

Source inspection identified an independent observer ordering defect: resize
geometry polls requested unconditional frames, and a dispatcher read could finish
inside `OnRender` before its presentation. Passive-only resize polls now use the
native-loop wake. Each endpoint observes once from the actual captured Silk window
Update callback, revalidates source/native identity and reads host activity before
and after source state. Active rendering (including an already-consumed request),
current explicit presentation requests, recovery and DPI work reject admission;
no pending work is canceled, consumed or retried until quiet. Delayed scheduler
callbacks are not claimed absent and still count in the observation.

The 1-second fixed settling, 2-second interval, 30-second initial-frame limit,
600 × 16 ms existing native observation budget, 120-second child deadline and all
four exact-zero phase assertions remain. The focused retained gate adds 17 cases
(minimum 647). A small linked-source executable passed all 21 BCL/predicate/source
controls; seven added actual-host cases are authored, pending the full build.
This is not a native application pass or an ARM64 crash fix.

## ARM64 native resize failure location

Exact head `40fe7f9fdac9382ef86708130bb0f420a7327dad`, Build
[36291351738](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36291351738),
passed nine of ten jobs. Windows x64 completed all four unchanged idle phases
with zero additional presentations. ARM64 exited with `0xC0000409` after
`native-resize-callback-entered` and before `native-resize-setter-returned`.
The child did not time out; no dump or exact-correlated Application Error record
was available to the collector. The exit code is not a proven crash cause.

The next diagnostic connection surrounds only this synchronous resize action.
One bounded, per-host observer records native size resolution/assignment,
framebuffer and size callbacks, source client-layout publication, and swapchain
configuration into the existing phase journal. Records retain cached dimensions,
managed thread/registration identity, existing render activity and whether the
window, target and source still match the registered objects. No extra native
geometry query, dispatcher pump, resource inspection or render request is made.
`IsRendering` does not establish that a surface has already been acquired.

The observer is removed before the fixed settling/observation intervals. Its
64 records have a separate 2048-byte per-record budget; the original phase budget
is unchanged. Observer failures are deferred until the setter returns, and scope
disposal cannot replace the original resize exception. Rejected source updates
and unready framebuffer sources have distinct checkpoints. All original native
assertions, deadlines, jobs and renderer selection remain unchanged.

Seven actual new test bodies passed through a linked-source executable on macOS
ARM64 with SDK 10.0.201: observer failure/retirement, capacity, original exception
preservation, typed state, journal bounds, failed writes and source ordering.
The first compile exposed an ambiguous throw-only xUnit delegate; an explicit
synchronous `Action` fixed that test without changing its assertion. Four existing
async-test analyzer warnings remain in this isolated harness. The ordinary full
source/package CI is still required; this does not fix or qualify the ARM64 crash.
