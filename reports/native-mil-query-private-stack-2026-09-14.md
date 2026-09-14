# Native query traversal storage — 2026-09-14

Acceptance: **ProGPU.Wpf.ShowcaseApp**, pointer selection and geometry-region
queries. The immediate blocker is FXC X3511 during native query pipeline creation.

ProGPU's iterator had a 64-element stack in an inout aggregate. Exporting the
canonical shader through the existing pinned Naga dependency reproduces the
failure in the dynamic child-stack loop. Moving only that array to invocation-
private storage makes point, bounds and ellipse compile with the same FXC flags.
This is original ProGPU code, shared by managed/C++ providers. Traversal capacity,
order, clipping, geometry and counters are unchanged. No shared workgroup state,
CPU fallback, upstream patch, new dependency or policy/default change is added.

Evidence:

- Dense Metal: 120 complete result records/100 public ordered queries match the
  independent original reference. Sparse Dawn: 168 records/140 public queries
  match its independent original-shader reference, including counters/unused slots.
- Both providers compile with AppleClang and Windows ARM64 MSVC; 19 native CTests
  and 54 original source-diagnostic tests pass. Full documentation verification passes.
- The rebuilt native consumer passes on Metal single-pass and system ARM64 WARP
  DXC/ordered stages, preserving 38 resources/11 draws/174080 coverage and all
  owner/generation, repeated-wait, participation and region-first assertions.
- Stock FXC/single-pass completes its first point readback and 16 repeated waits,
  but crashes `0xC0000005` after the first bounds submission. Compiler repair is
  not full Windows runtime qualification. The FXC staged comparison remains separate.

Artifacts and diagnostic scripts: ProGPU `artifacts/query-hlsl`. The native DLL
overlays in these runs are explicit diagnostics, not a freshly qualified package.
The shader SHA-256 is
`1dbf5e9bab657323460d680e5417b6aa1f29b23656efca210520b1c4a23b3f4b`.
DXC successful stdout SHA-256 is
`3151874456b24e93b293c9b723887fafd21769d187315c471a731ee1f3d6f94f`.

Continue the concrete Windows bounds/runtime, final-head package/CI and actual
Showcase gates. No dependency pin, draft state or ordered merge admission changes.
ActivityMonitor and general Direct2D/COM/Win2D expansion remain out of this batch.
