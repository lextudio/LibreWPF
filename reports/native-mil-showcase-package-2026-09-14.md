# Native Showcase package qualification — 2026-09-14

The isolated `artifacts/native-source-qualification.PmByoe/wpf` snapshot contains
WPF `1ee08c876`, ProGPU `960dfbfb` and LibreWinForms `f268f73c` source. Physical
dirty submodules and indexed dependency pins were not modified.

## Completed evidence

The fresh real source native host passed retained viewport/image updates,
native text/inline controls, document editing/undo/layout, geometry selection and
device recovery. It presented a native frame with 23 commands, 20 resources and
five submitted draws. This is macOS source-host evidence, not Windows/package
application admission.

WPF Build `34795305358` produced all three Windows managed payloads and passed
canonical WinForms source integration at `1ee08c876`. Exact artifacts:
`10329208818` (Windows runtime) and `10329698235` (canonical package closure).
Canonical packages identify `f268f73c`/`1ee08c87`, with their separately pinned
ProGPU `e56d45c5` graph; they are not the newer renderer package graph.

ProGPU Build `34793857889` at `4b6d9cbe` completed both full DX12 JIT/NativeAOT
consumer jobs, x64 and ARM64. Native package artifact `10328964352` and portable
package artifact `10329508340` carry `0.1.0-preview.3036.ci`. These precede the
private query-stack change, so final-head qualification is still required.

Using that exact portable package feed and the current WPF Windows payload,
`progpu-wpf-sdk-ci.sh --build-packages-only` completed all three WPF packages.
The native-mode Showcase package consumer built with zero warnings/errors.

## Actual application blockers

The package-only Showcase live probe reached a presented frame, geometry,
windowing and resize checks, then failed during TextBox interaction while
serializing `CaretElement` guidelines. Source Y `[0, double.MaxValue / 2]`
overflows the protocol's float coordinate. ProGPU's static-guideline fix retains
infinite anchors, zero remote-anchor displacement and NaN rejection, without
changing WPF adorner layout, source input or filtering visual types.

Both native providers and all 19 native tests pass; all 124 linked native
interop tests and generated contracts pass. A diagnostic rebuilt-library overlay
gets past that failure and reaches a separately surfaced popup target, where
native scene compilation returns `UnsupportedCommand`. The temporary diagnostic
bridge exception instrumentation was removed and its original package assembly
restored. Rebuilt ProGPU overlays remain identified as diagnostic artifacts,
not qualifying package output.

Next: isolate that popup operation, rebuild exact packages, finish actual
Showcase/Toolkit/SciChart and platform gates, then merge dependencies in order.
No PR draft state, pins or merge admission changed. ActivityMonitor is excluded;
general Direct2D/COM/Win2D expansion remains deferred.

### Popup input follow-up

ProGPU `27d13562` fixes the actual popup vector-rectangle/client-rectangle
intersection and preserves declared rectangular geometry masks on source
blur/shadow layers. No shadow allocation bounds, alpha-mask pixels or managed
input fallback are used. Both native providers compile; all 19 native CTest
suites pass, including 24 state/effect clip variants and canonical MIL effect
fixtures. Generated contracts and documentation checks pass.

The rebuilt diagnostic-overlay Showcase advances through text selection,
control mouse input, mouse bindings, discrete controls, toolbar, framework
themes, popup surfaces and keyboard navigation. Stage logs distinguish completed
predecessors from the current wheel/capture stage. The overall 180-second gate
still times out; a native process sample shows a pending GPU hit-query map wait.
This is not successful final-package or full application qualification.

At WPF `37042271d`, canonical WinForms and Windows managed production pass, but
SDK smoke fails while staging native dependencies: indexed ProGPU `e56d45c5`
requires Build `34763887821`, which failed. Keep this exact-commit check; after
final ProGPU qualification, update dependent pins and rerun the whole SDK lane.
Do not substitute unrelated artifacts or waive the failed dependency run.
LibreWinForms PR #29 remains green; ProGPU `27d13562` CI is queued/running.

## Separate Windows diagnostic

Moving scalar query traversal state to invocation-private storage in addition
to the private array did not repair stock single-pass bounds execution:
the isolated VM process still exited `0xC0000005`. It was not applied to product
source. The baseline shader was restored and forcibly re-embedded in both
providers (restored source timestamp alone initially missed that rebuild).
Source SHA-256 remains
`1dbf5e9bab657323460d680e5417b6aa1f29b23656efca210520b1c4a23b3f4b`.
The superseded pre-fix Build `34794383097` was cancelled to release runners;
current-head and required package-producing runs were retained.
