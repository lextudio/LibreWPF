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

## Required next work

Native GPU-memory accounting and the complete native Showcase report consumer
remain open; the performance gate is intentionally not qualified. Implement
accounting in ProGPU C++, where buffer/texture ownership actually lives, with
an additive typed batched diagnostic contract. Include persistent allocations,
atlas and image textures, nested layers/pictures, hit-query storage and retained
submission resources. Distinguish borrowed textures from owned allocations,
avoid double counting aliases, retain device/generation identity, and state
whether bytes mean logical requested storage or driver physical residency.
Managed process/heap observations cannot substitute for native ownership.

Then connect the report, retain its warmed memory-growth gate, and run matched
final Release Instruments/counter measurements and exact package/platform
qualification. Keep the current source/managed separation; do not fill unavailable
native fields with zero or infer raster counts from scene draw counts.

Separate merge blocker: ProGPU Build `34797950771`, Windows x64 package consumer
job `103841226346`, passes ordered queries but fails the default system-FXC
ellipse participation case (`flags=C0000001`): summary hit count is one while
the returned list count is zero. The adapter is Microsoft Basic Render Driver.
Do not waive the exact-result check or switch defaults merely to pass CI.
