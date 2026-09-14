# Native MIL exact-package validation — 2026-09-14

## Current-head Windows sample oracle follow-up

ProGPU `54adc6a005119d40fc25615b3823844c21453690` Build
`34819727963` published `progpu-directx-oracle-win-x64` (artifact
`10337999613`). It contains native Microsoft D3D12HelloTriangle and
D3D12HelloTexture captures from the pinned DirectX-Graphics-Samples commit
`213dd4fd4918ea009dd8f35adee1aff1f2ecaba4` (Agility `1.618.3`), together
with ProGPU's D3D12 frames. Running the repository's unchanged
`progpu-compare-directx-sample-oracle.py` on both pairs yields exact 1280×720
pixels: maximum, mean, every probe and channels/pixels over three all zero.
The outputs are retained under the external core-release diagnostic directory's
`progpu-directx-oracle-54adc6a0` folder. Metal/Vulkan differentials, full
package consumers and the overall current-head CI are still running; these
two Windows frames alone are not final cross-platform qualification.

## Source and package identity

ProGPU source: `0ac6a5ff79d79e7a6e5a2e5b0488955cfb2256d7`, incorporating
main `cde81083be9533761e2fe5573adf5d27015e5324`. Exact Build:
[34808350830](https://github.com/wieslawsoltes/ProGPU/actions/runs/34808350830),
package version `0.1.0-preview.3047.ci`, artifact `progpu-native-package`
(ID `10334860867`). Both inspected Backend and Native nuspec repository commits
match that full source SHA; their dependencies select the same package version.

The original consumer project is built from a clean detached checkout of that
commit, with `ProGpuNativeUseProjectReference=false`. Its assets file contains
zero project libraries: Backend, Native and Dawn all resolve as versioned NuGet
packages. No diagnostic assembly/native-library overlays are used. This is
stronger than the earlier mixed-source diagnostic runs, but remains ProGPU
package evidence, not a complete LibreWPF SDK application/package qualification.

Package SHA-256:

| Package | SHA-256 |
| --- | --- |
| ProGPU.Backend.Native | `1270a7e2003d9fe58ef7170da355e8e72d9c5bc486d234230223b4ae391b116c` |
| ProGPU.Backend | `1dcf99871b8738d3f3d5ccd8094b01f98752336f624e92999e7ac441db12080e` |

Clean source, the downloaded feed, isolated NuGet cache and macOS publication
are under `/Volumes/1TB-macOS/progpu-core-release.xtwndj/`. Windows publications,
the bounded guest runner and stdout/stderr logs are under
`artifacts/native-release-qualification.9mQdMk/`. The external SSD avoids filling
the internal volume; no existing artifacts or unrelated work were deleted.

## Actual full package consumers

All publications are self-contained Release builds of
`tests/ProGPU.Native.PackageConsumer/ProGPU.Native.PackageConsumer.csproj` using:

```text
-p:ProGpuNativeUseProjectReference=false
-p:ProGpuNativePackageVersion=0.1.0-preview.3047.ci
-p:ProGpuNativePackageSource=<downloaded exact CI feed>
```

All four runs below execute the **full consumer**, not `--mil-only` or the
owner-query-only probe. They pass ABI checks, both MIL exports, native document
rows/inline/positioned paragraph contracts, cubic control-hull rendering,
retained MIL rendering (38 resources, 11 draws, 174,080 coverage bytes), pixel
readback, native memory inventory, point/list/region owner queries, 16 repeated
waits, participation policy, first-query bounds/ellipse ordering and owner/
generation isolation. Each process exits zero and ends with the package smoke
result: ABI 4, Dawn ABI 1, one final draw and 16,384 pixels. This fixture does
not represent all WPF controls or the full Direct2D/Win2D API surface.

| Process/platform | Actual selected adapter | Result |
| --- | --- | --- |
| Windows ARM64 VM, `--software-adapter` | Microsoft Basic Render Driver / D3D12 | Pass |
| Windows emulated x64 VM, `--software-adapter` | Microsoft Basic Render Driver / D3D12 | Pass |
| Windows ARM64 VM, no adapter override | Parallels Display Adapter (WDDM) / D3D12 | Pass |
| macOS ARM64, no adapter override | Apple M3 Pro / Metal | Pass |

Query and compiler preferences are `auto` in all Windows runs. Diagnostics
confirm actual system FXC and **OrderedStages selected from Automatic**, not an
explicit query-stage override. The system WARP module is
`C:\WINDOWS\SYSTEM32\d3d10warp.dll`, version `10.0.26100.9278`; system
`d3dcompiler_47.dll` is `10.0.26100.9444`. The stage rejects an app-local WARP
DLL, and no external compiler directory is supplied. The default Parallels
adapter separately selects the existing ExplicitShader image path and RasterShader
glyph path under Automatic/Fastest; no CPU query fallback is introduced.
The Mac keeps automatic single-pass queries and its normal native compute path.

Parallels 27.0.1 (58670), Windows `10.0.26200.9445`, installed matching Tools,
four vCPUs and 6 GiB RAM were rechecked. Existing PowerShell 7 runs the scoped
script under the existing LocalMachine RemoteSigned policy. No policy, VM
configuration, compiler, adapter default or system file is changed. Each run
copies only the exact publication to a fresh named guest directory, keeps the
owned child handle, and retains the 300-second bound. No child timed out.

Windows payload SHA-256:

| Payload | SHA-256 |
| --- | --- |
| ARM64 progpu_native.dll | `f9a94eb629640be3e22f6f989a5cc5f1023834357c22ce33bbadee1167ab39e4` |
| x64 progpu_native.dll | `0fa90664f532df54cf4480fdf6e831a67ebc541dddaa3dcc414e963f19d8afd0` |
| ProGPU.Backend.dll (both) | `06548e75f5d4e3adf046d6fead5214a0de263b04894593307612e7fceed72651` |
| ProGPU.Backend.Native.dll (both) | `d2e35048eb7736baf3e11d047b7b12ff2a5475653e8bb846a441502c507d8958` |
| ARM64 stock wgpu_native.dll | `9f73e41536b3bd96a0a44692ea65888c9de004b19fbf5de90489768667fbbdbc` |
| x64 stock wgpu_native.dll | `4971fce5b4d93fc10b65d01cbcc57f9f35ad1bc479e654737974e4ad2e265be6` |

## Current-head cross-platform oracle evidence

The exact Build's published differential JSON was downloaded and inspected:

- Microsoft **D3D12HelloTriangle** and **D3D12HelloTexture** compare the captured
  native Windows sample with the equivalent ProGPU D3D12, Metal and Vulkan scenes.
  Both 1280×720 fixtures are byte-identical on all three candidates: maximum,
  mean and every probe difference are zero. These are the two pinned sample
  contracts, not a claim that arbitrary Microsoft DirectX samples run unchanged
  on non-Windows systems.
- The portable Win2D Canvas fixture passes its existing bounds: Metal changes
  one pixel, Vulkan 81 pixels, maximum channel difference one in both cases.
- The portable Direct2D COM fixture passes: Metal changes 304 pixels, Vulkan
  165, maximum channel difference one and zero pixels over one. These are
  cross-backend scene comparisons, not complete Windows COM ABI/behavior parity.

Artifacts: `progpu-directx-sample-differential` (`10334825891`),
`progpu-win2d-canvas-differential` (`10334442195`),
`progpu-direct2d-webgpu-differential` (`10334054690`). The image-brush WPF
differential artifact (`10334257767`) is also retained. No tolerance or fixture
was changed during this qualification. Captures are under the external staging
directory's `oracles/` subdirectories.

## Remaining merge gates at this checkpoint

Current-head native producer builds, both Windows explicit/automatic ordered-query
parity jobs, the DX12 NativeAOT package consumers and macOS/Linux package consumers
have passed. Both general Windows package-consumer jobs subsequently hit their
15-minute job limit, leaving Build 3047 terminal canceled. Their logs show
successful assertions until cancellation, not a completed qualification. The
exact-runtime staging helper correctly rejected this Build and produced no
qualified staging directory.

ProGPU `a8afeab6f53c0b8bdd470cf9d2d9283f6c1bdf8d` now splits each Windows RID
into core, drawing, visual and guideline jobs, retaining all nine independent
JIT and NativeAOT cases and the original deadlines. Runtime code and assertions
are unchanged. Selector coverage, Bash syntax, ShellCheck, Actionlint and release
documentation checks pass locally. The new exact
[Build 34811802371](https://github.com/wieslawsoltes/ProGPU/actions/runs/34811802371)
must pass; the older package results above are historical evidence, not proof
for this new commit. See the
[ProGPU scheduling contract](https://github.com/wieslawsoltes/ProGPU/blob/a8afeab6f53c0b8bdd470cf9d2d9283f6c1bdf8d/docs/native-package-consumer-groups.md).
Do not restart live jobs, advance dependency pins, or merge from partial results.

After exact ProGPU qualification, align LibreWinForms and LibreWPF to the same
qualified ProGPU commit, produce the exact WPF Windows payload/SDK packages,
run the native application gates and require downstream CI to pass. The earlier
Showcase checkpoint report is still an explicitly diagnostic WPF assembly graph.
Preserve the ordered ProGPU → LibreWinForms → LibreWPF merges and all final
Release/performance/platform gates. Broader API work is deferred from this core
delivery, not declared completed; ActivityMonitor remains outside scope.
