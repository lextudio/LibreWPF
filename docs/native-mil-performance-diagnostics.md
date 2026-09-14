# Native MIL performance diagnostics

Acceptance application: **ProGPU.Wpf.ShowcaseApp**. Action: qualify its warmed
presentation loop after native glyph raster retention repaired the timeout.
The blocking source path was `ProGpuWpfDiagnostics`: native hosts reported the
unused managed compositor's zero timings, draw counts and GPU-memory counters.

## Implemented timing contract

`TryGetNativePerformanceSnapshot` publishes the actual native host's latest
successfully presented frame. The value contains its presentation count,
device-recovery count, native scene update and render metrics, whether source
serialization ran, and CPU wall-clock durations for:

- the complete native host frame;
- source update/serialization;
- native MIL scene compilation;
- external-image binding and scene installation;
- surface acquisition;
- the native render/submission call;
- surface presentation.

Submission includes native uploads and command encoding. These measurements
are not GPU execution durations and must not be relabeled as separate upload
and encoding timings. Total frame time also includes host work between stages;
the parts need not sum to the total. A skipped source update legitimately has
zero duration and `SourceUpdated=false`.

One bounded lock publishes and reads the value as a unit after the host records
successful presentation. Readers never combine new scene metrics with older
timings. Failed acquisition/render paths do not publish a successful frame.
Disposal/device-target replacement invalidates the value; the new frame carries
the actual recovery count. Timing uses monotonic timestamps, with O(1) work,
no per-frame diagnostic heap allocation and no added native crossings.
These sequential clock observations have no independent SIMD workload.

The existing managed performance and memory snapshot APIs now return false
for native mode. Managed mode retains its previous metrics and behavior.
Showcase diagnoses this distinction explicitly; it cannot print another false
native performance success using idle managed counters.

## Validation

The isolated source snapshot builds the WPF bridge with one existing warning
and the source-host harness with zero warnings/errors. The full test project
build has 116 existing warnings and no errors. All 214 focused window-host
tests pass, including native publication, renderer distinction and disposal.

The actual macOS source-host gate passes viewport/images, text/document/source
input, geometry and native device recovery, then verifies the new snapshot's
frame count, recovery count, metrics and positive measured stages. One recovered
frame reported 23 commands, 20 resources and five submitted draws, with CPU
frame 7.594 ms, source 0.168, compile 0.073, install 0.041, acquisition 0.205,
submission 6.993 and presentation 0.031 ms. This one-frame diagnostic result is
not a matched Release performance comparison or final package qualification.

An initial diagnostic mirror placed a guard in the wrong repeated source block;
the real host gate rejected it. The mirror was corrected to match the product
files before the successful run. The working checkout's unrelated older physical
dependency mix also fails compilation; it was not rewritten. Qualification
continues using the explicitly identified isolated source graph.

Showcase also compiles with zero warnings/errors against the rebuilt bridge in
an explicitly isolated diagnostic reference/assembly overlay. It completes the
live input actions and then rejects the incomplete performance gate as intended:
frame 65 reports genuine CPU time 22.773 ms, compilation 17.754 ms, native
submission 1.850 ms and 2,292 commands/118 draws. It no longer reports zero
native work as a successful performance result. This run fails the overall
qualification gate until the memory/report work below is complete; it is not a
package closure or performance pass.

## Native memory and report connection

ProGPU `7ffe85d2` supplies the original C++ inventory through its generated
`NativeGpuMemorySnapshot` contract. See
[native ownership and evidence](https://github.com/wieslawsoltes/ProGPU/blob/7ffe85d2d5deff299e4340960d3bbef7654f81ca/docs/native-gpu-memory-diagnostics.md).
The host's `EnableNativeMemoryDiagnostics` defaults to false. When enabled, one
inventory runs on the actual render thread after successful native presentation,
before publishing the paired performance snapshot. `GpuMemory` is nullable;
an uncaptured frame must not inherit old memory. Inventory CPU duration is
reported independently and included in total host time. Ordinary frames gain
no native inventory crossing or allocation. Snapshot reads remain O(1) and
cannot call WebGPU from a foreign thread. Existing disposal/recovery reset the
whole published value.

Showcase now chooses a separate native report after the same live input actions.
It restores the previous capture setting in finally, warms 16 frames and measures
120 presentations with the existing 300-by-2-ms polling bound. It checks actual
scene/generation, engine identity and recovery count, finite stage durations,
nonempty native work and the existing endpoint known-owned GPU growth limit of
1 MiB. Peak ownership is recorded separately, including pending batches. Opaque
texture storage or borrowed views reject memory qualification until their
respective accounting contracts exist. No physical-residency claim follows.

The native report emits p50/p95/p99 for source, compile, install, acquisition,
submission, presentation, inventory and complete host CPU time, plus process CPU,
heap/working set, managed allocation and native buffer/texture counts. Submission
is not split into fabricated upload/encoding timings. Native atlas-growth and
raster-submission counters are not inferred from the managed compositor; exact
atlas/raster retention remains covered by ProGPU's independent renderer gates.
Sample arrays are allocated before timing; diagnostic sorting is bounded to
eight sets of 120 dependent values, not a compute fallback or renderer algorithm.

The source bridge builds with one existing warning, the source harness and
Showcase with zero warnings/errors, and all 214 focused host tests pass. The real
source-host device-recovery gate also passes the new inventory/scene identity
checks. The diagnostic graph uses the earlier isolated source snapshot with
current product-file overlays and the ProGPU 7ffe85d2 managed/native artifacts;
it does not change the working checkout's mixed physical submodules or indexed
pins. One initial Showcase rebuild restored an older packaged PresentationFramework
and failed the scroll test; its hash differed from the current source assembly.
That attempt is not qualification evidence. A later rebuild likewise restored
old native dylibs and rejected a batch before diagnostics; restoring the exact
current pair removed that artifact mismatch.

The corrected diagnostic app completed live input and all 120 measured frames.
One run observed buffer bytes 90,259,480 at the start, 129,954,680 at peak,
and total owned bytes 95,339,616 at the end versus 98,669,552 initially.
Texture bytes remained 8,410,072 across six textures. Pending raster batches
grew from 27 to 63 at peak. Native `submit` retains these until periodic actual
completion observation; this is evidence of transient retained storage, not a
proven unbounded leak. The initial report candidate incorrectly applied the
existing endpoint limit to the transient peak. The final report preserves the
original endpoint comparison and reports the peak separately; it does not
increase the threshold, discard pending bytes or retire work during inspection.
The final diagnostic run exits zero after every live input action and 120 measured
frames. Native owned bytes are 93,281,184 → 93,290,800 (9,616 bytes growth), with
peak 138,364,752 and 63 pending batches. Six textures remain tracked. CPU host
p50/p95/p99 is 23.098/26.228/45.690 ms; compilation 17.821/18.181/18.563 ms;
native submission 2.079/2.593/24.733 ms; inventory 0.012/0.019/0.031 ms. It
reports 2,292 commands and 118 draws, 497,770,008 managed bytes allocated over
the measurement (4,148,083.4/frame), and working set 548,487,168 → 545,619,968.
These are diagnostic observations, not a final Release baseline or speed claim.
The allocation rate and retained-batch peak need matched profiling.

The sample was built with `ProGpuWpfRendererMode=NativeMilWgpu` and
`ProGpuWpfNativeMilHitTesting=true`, then run with
`PROGPU_HIT_TEST_EXECUTION=ordered-stages`,
`PROGPU_WPF_SHOWCASE_LIVE_VALIDATE=1` and
`PROGPU_WPF_SHOWCASE_PERFORMANCE_VALIDATE=1`. It used Debug Showcase with
Release source PresentationFramework/bridge and current ProGPU native backend
assemblies/dylibs restored after the build. Product/report source hashes match
their isolated copies. This explicit overlay does not qualify the unchanged
indexed dependency pins or SDK package graph.

## Required next work

Run matched final Release Instruments/counter measurements, investigate sustained
allocation/retention behavior, and finish exact package/platform qualification
and qualified dependency pins.
This is a diagnostic connection, not a measured performance improvement. The
managed report remains unchanged and native mode still rejects its legacy APIs.

Separate merge blocker: ProGPU Build `34797950771`, Windows x64 package consumer
job `103841226346`, passes ordered queries but fails the default system-FXC
ellipse participation case (`flags=C0000001`): summary hit count is one while
the returned list count is zero. The adapter is Microsoft Basic Render Driver.
Do not waive the exact-result check or switch defaults merely to pass CI.
