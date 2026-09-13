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

PRs 139 (ProGPU), 29 (LibreWinForms), and 115 (LibreWPF) remain unmerged. Required
order is upstream qualification/merge, exact downstream pins, downstream CI and
package/application qualification, then dependent merges. No full-goal completion
or one-hour merge guarantee follows from the staged passes.

Local logs are under `artifacts/native-core-validation.GwKsGq`, including
`native-raster-retention-windows.log`, `native-raster-retention-warp.log`,
`native-host-raster-retention-metal.log`, and the ARM64 host/trace logs. ProGPU
build, contract and consumer logs are in its worktree's `artifacts` directory.
VM lifecycle, configuration, installed runtimes and driver settings were unchanged.
