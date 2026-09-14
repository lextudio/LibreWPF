# Windows compiler runtime package — 2026-09-14

Acceptance: **ProGPU.Wpf.ShowcaseApp**, startup and ordinary native pointer/region
queries. ProGPU `dec74b5b` adds `ProGPU.Backend.Dx12`, the optional pinned Windows
WebGPU/DXC runtime package shared by managed and C++ renderers. ActivityMonitor
and broader Direct2D/COM/Win2D expansion remain outside this finish batch.

The package contains x64/ARM64 feature-enabled WebGPU libraries, signed/hash-pinned
compiler payloads, receipts and original notices. It selects only the known Silk
Windows native asset during build/publish, never caller-owned assets, NuGet-cache
files or system DLLs. Explicit RID admission, pre-pack completeness and WARP
exclusion guards are implemented. Compiler/query defaults remain unchanged.

## Evidence

- The actual DX12 NuGet restores and publishes the expected ARM64 library hashes.
  Its full native consumer passes on system WARP without an external compiler
  directory: retained MIL pixels, owner/generation isolation, participation and
  region-first queries. All four native/compiler/system module paths are checked.
  The renderer itself is still project-reference C#/C++ in this local run, not
  the complete CI NuGet graph.
- Successful stdout SHA-256:
  `094e4fc40c85637cfe761a6f321574152c20ab42a93ce04035d2589e71f28f75`.
  Artifacts: `artifacts/dx12-package.toCGR2`.
- x64 dependency cross-build takes 2m14s and passes exact ABI/PE checks. Its DLL
  SHA-256 is `5777347a165be3d640424b48f2eab8e11523d678c381e9ca1373fdd7ff9101c3`.
  Cross-compilation is not x64 runtime qualification.
- Nine runtime-input and three MSBuild selection tests pass. Missing executable
  RID and missing package RID reject; package completeness now rejects before
  NuGet creation. Both-RID package production has zero warnings. Workflow lint
  and all four new/changed production/test PowerShell parse checks pass.

## Remaining finish gates

Build CI now produces both compiler-runtime payloads and the optional NuGet, then
runs added full Windows x64/ARM64 JIT and NativeAOT package consumers. Existing
default gates remain intact. Final-head CI must complete; the prior `089e9120`
had no failed jobs but native lanes were still running at this checkpoint.

Complete the exact CI-built package graph, x64/NativeAOT/hardware qualification,
release/default integration and Showcase source/application input, popup, DPI
and lifetime checks. The independently reproduced stock-FXC X3511 remains open.
No dependency pins advance and no PR merges occur until the required gates pass.
Merge order remains ProGPU 139 → LibreWinForms 29 → LibreWPF 115.
