# Native raster-resource lifetime and merge qualification

## Acceptance path and diagnosed boundary

Application: `ProGPU.Wpf.ShowcaseApp`. Action: present its first native MIL frame.
Blocking shared path: C++ path rasterization, coverage upload and vector draw.
Broader Direct2D/COM/Win2D expansion remains deferred under the core delivery plan.

The exact package 3005 Windows comparisons isolate early raster-resource release:

| Independent comparison | Windows x64 | Windows ARM64 |
| --- | --- | --- |
| Keep references through exact submission completion | Pass | Pass |
| Release only command-buffer reference before completion | Pass | Pass |
| Release five raster buffers and their bind group before completion | Black frame | Access violation |

Evidence: [ProGPU run 34775504926](https://github.com/wieslawsoltes/ProGPU/actions/runs/34775504926).
The original native cubic fixture remains black on both architectures in that run.
This isolates the lifetime-sensitive boundary outside the C++ renderer; it does
not identify an individual buffer or establish a driver-internal cause.

## Implementation and local qualification

ProGPU `f5dcfb15` introduces submission-bound leases for uncached native path,
clip and glyph staging. Active recording survives split submissions; borrowed
semantic encoders retain resources through their future submission. Existing
periodic polling and bounded draining own retirement. Latest-token explicit waits
and engine disposal release completed resources. No per-draw wait, frame retry,
extra submission, altered shader, CPU fallback or relaxed assertion was added.
`7e6b749c` avoids scanning the retirement list on ordinary lease publication before
completion, retaining amortized constant-time publication.

Both desktop C++ providers use the policy. Managed buffers already use deferred
disposal. Browser WebGPU retains its existing encoded-reference ownership without
creating an undrainable native synchronous-poll queue. Cached path/glyph replay
does not allocate a new staging lease.

- Both C++ providers compile on Metal and Windows ARM64/MSVC.
- All 20 native CTest cases pass, including recording/completion/cancellation
  lifetime regressions; native generated-contract verification passes.
- The full Metal native package consumer passes original drawing and native
  owner/generation/participation/region checks, including after the publication
  optimization.
- Actual source-host/device recovery passes on Metal with the retained backend.
- Original rectangle and cubic pixel fixtures pass on the Parallels Display
  Adapter and Microsoft Basic Render Driver with the rebuilt native backend.

The Windows comparison uses the existing package-3000 managed/runtime assembly
closure with the rebuilt C++ library. The software-only diagnostic selects WARP;
that Backend assembly is not shipped. These are staged comparisons, not exact
final-head package qualification. Native DLL SHA256:
`079FB052B7CA73F95AAB2BFF1F1F27D9EFA4A464599F512B5C1ACA315059CF1E`.

## Remaining merge gates

Final-head [ProGPU Build 34775916222](https://github.com/wieslawsoltes/ProGPU/actions/runs/34775916222)
is running for `7e6b749c`. Superseded builds were cancelled to free runners; none
of those cancellations count as passing qualification. Dependency pins are not
advanced before the required upstream checks pass.

The staged Windows ARM64 source host passes source text, document and geometry
checks, then fails with a native-loop cleanup exception. Its existing native-loop
trace is being used to recover the primary failure. The original 15-second
recovery deadline remains unchanged. Earlier x64 recovery timeout is not closed
by the independent pixel probes.

The trace run subsequently reaches a real native presentation, drains the close
request and exits with the original `NativeMilHostDeviceRecoverySmoke.RunAsync`
line-29 timeout while waiting for dispatch of the injection callback. The earlier
cleanup exception is not evidence of a fixed recovery path.

The full staged WARP consumer subsequently passes the original cubic and retained
MIL scene (38 resources, 11 draws, 174,080 coverage bytes), then exits with access
violation `-1073741819` after native owner-query submission (21,507 ms), at 1m38s
total. This is a separate open query blocker. Query buffers and bindings are
engine-owned through the pending request; inspection does not find the same
temporary-raster release pattern there. Do not claim the raster repair fixes
software-adapter query execution or replace it with an unqualified fallback.

PRs 139 (ProGPU), 29 (LibreWinForms), and 115 (LibreWPF) remain unmerged. Required
order is upstream qualification/merge, exact downstream pins, downstream CI and
package/application qualification, then dependent merges. No full-goal completion
or one-hour merge guarantee follows from the staged passes.

Local logs are under `artifacts/native-core-validation.GwKsGq`, including
`native-raster-retention-windows.log`, `native-raster-retention-warp.log`,
`native-host-raster-retention-metal.log`, and the ARM64 host/trace logs. ProGPU
build, contract and consumer logs are in its worktree's `artifacts` directory.
VM lifecycle, configuration, installed runtimes and driver settings were unchanged.

## Query execution and source-startup follow-up

The isolated zero-segment canonical-shader diagnostic passes a point query on
Microsoft Basic Render Driver but crashes before rectangle completion. Compiling
the rectangle pipeline alone succeeds in 22.802 seconds without creating query
buffers or submitting commands. A separate cold rectangle probe retains every
buffer, binding, pipeline, encoder and command through readback: it creates the
pipeline in 23.722 seconds, submits at 23.744 seconds, then exits with access
violation before readback completes. The matching Metal probe returns all three
expected owners. Thus this query failure does not reproduce the temporary raster
reference-release boundary fixed above.

A diagnostic replaces only the repeated rectangle edge/quad corner call sites
with bounded loops over the same predicates, vertices and short-circuit order.
The zero-segment WARP fixture then returns all three owners in 31.972 seconds.
The full shader and remaining families still require validation; neither this
diagnostic rewrite nor zero-segment specialization is enabled in production.

LibreWPF now records a missed initial 15-second presentation prerequisite before
attempting device recovery. Once a prerequisite fails, the harness closes and
reports that failure instead of launching subsequent native queries which could
hide it behind another crash. Successful runs retain every existing input and
recovery assertion. The explicitly enabled native-loop trace records invariant,
monotonic elapsed milliseconds and includes native window initialization.
Two focused source-graph tests, source-host compilation and Metal host/recovery
pass; these are not Windows qualification.

The first timed Windows run presents after 7.354 seconds measured from input
attachment, then crashes in `BeginHitTest` from the harness rectangle owner query.
A live stack confirms that call path. That timestamp excludes earlier window/
composition initialization and cannot establish the original first-frame deadline
was met. The trace now starts before native initialization to measure it directly.

Code inspection also found eager managed glyph/path pipeline compilation during
native-host composition-target construction. ProGPU now defers those pipelines
until actual atlas rasterization, retaining shader algorithms, captured execution
policy and cache/disposal contracts. Its native providers already create the
corresponding resources on demand. See ProGPU's
`docs/native-atlas-pipeline-initialization.md`; Windows timing is still required
before claiming a startup fix.

During this follow-up the Windows VM became suspended before a probe started.
It was resumed without reset or configuration changes, and guest command access
was verified. The failed transport attempt is not test evidence. Logs include
`query-zero-segments-compile-warp.log`, `query-retained-bounds-warp-resumed.log`,
`query-quad-loop-warp-dispatch.log`, `source-first-frame-timing-windows.log`, and
`source-first-frame-timing-stack.log` in the existing validation artifact directory.

### Measured atlas startup comparison

ProGPU `f4002192` passes 78 focused Release atlas/resource tests. In the matched
Windows ARM64 staged source host, native window initialization drops from
34.526 seconds (baseline) to 0.635 seconds (lazy atlases). The baseline explicitly
misses the original 15-second initial-frame prerequisite and presents only at
40.251 seconds. With the new Text/Vector assemblies, the first native presentation
occurs at 9.147 seconds; injected device loss is recovered and the replacement
device presents at 15.123 seconds, about six seconds after the first frame. The
existing recovery assertions report success. No deadline, shader or renderer
selection changes were used. The paired Metal source-host/recovery run also passes.

These are staged runtime measurements, not exact final-package or full application
qualification. The Windows run continues into native owner-query validation, which
remains open. The same native DLL and host setup are retained; the logged SHA256
values identify the changed managed assemblies. Logs:
`source-initialization-baseline-windows.log`, `source-lazy-atlas-timing-windows.log`,
and `source-lazy-atlas-timing-metal.log`.

The baseline also exposes a fixture-owned dispatcher leak: creating the paginator
for deliberate synchronous block/inline/table rejection enables background
pagination by default. Its later queued unsupported-layout exception can interrupt
the unrelated native host and produce a secondary Silk cleanup failure. The
rejection fixture now disables its own background pagination before the same three
`GetPage` assertions. Product pagination defaults and unsupported-layout rejection
are unchanged; no queued exception is globally swallowed.

The full canonical shader with the diagnostic rectangle loops still fails: its
pipeline takes 125.247 seconds to create and readback reaches the unchanged
30-second timeout. The process subsequently exits, confirmed by guest process
inspection. The loop rewrite and zero-segment specialization remain diagnostic-only.
