# Native MIL exact-package validation — 2026-09-14

## Current-head package handoff

ProGPU Build [34819727963](https://github.com/wieslawsoltes/ProGPU/actions/runs/34819727963)
completed successfully with 54/54 checks, including all native package
consumer jobs. [ProGPU #139](https://github.com/wieslawsoltes/ProGPU/pull/139)
merged to `main` as `86f2f766d1f8e6b4041fa184de0fe9d03ae2840f`, whose tree
is identical to tested PR head `54adc6a005119d40fc25615b3823844c21453690`.
The Build published `progpu-native-package` artifact `10338682752`, version
`0.1.0-preview.3051.ci`. Both inspected Backend and Native nuspecs identify
repository commit `54adc6a005119d40fc25615b3823844c21453690`; Native depends on
Backend and Dawn at the same exact version. Downloaded package SHA-256 values:

| Package | SHA-256 |
| --- | --- |
| ProGPU.Backend | `c19347bd04a262e244e2fcebf9011cae34af62b595412721d8e0f4d15a135552` |
| ProGPU.Backend.Native | `f3320496ee5a54401707060bad76925594027951b77541c601e525ecbdf8bea5` |
| ProGPU.Backend.Dawn | `45ff4ead3f9be3dfd38d35086bd7f400930cd75ad617ad4555e42afcb52ab965` |
| ProGPU.Backend.Dx12 | `8287589556f0767a4968015d3b2723fedc66adb5eb5a06d4ae2b5792a406154c` |

The unchanged full `ProGPU.Native.PackageConsumer` was published from this
clean ProGPU source with project references disabled, this downloaded NuGet
feed, version `3051`, and an isolated restore cache. Its assets have zero
project libraries. The exact-package macOS ARM64/Metal process, Windows VM
emulated x64/system-WARP and default-Parallels-adapter processes, and native
Windows ARM64/system-WARP process exited zero with the final ABI 4, Dawn ABI 1,
one-draw/16,384-pixel smoke marker. Windows WARP used the system
`d3dcompiler_47.dll`, `D3D12Core.dll`, and `d3d10warp.dll`; no app-local WARP
or compiler override was supplied. The guest's `progpu_native.dll` SHA-256
`6449641b46d4568ffaf8c185e88366f30c2e52d97a683c44a23d6c893e818490`
matches the published package payload. The stock package `wgpu_native.dll`
hash is `4971fce5b4d93fc10b65d01cbcc57f9f35ad1bc479e654737974e4ad2e265be6`;
no source overlay was applied. Logs are under the external core-release staging
directory as `osx-arm64-package-3051-metal.log`,
`win-x64-package-3051-system-warp.log`,
`win-x64-package-3051-default-adapter.log`, and
`win-arm64-package-3051-system-warp.log`.

Separately, the current-source Windows x64 consumer passed both system WARP and
the default Parallels D3D12 adapter using the current CI runtime payloads. This
is useful integration evidence, not the NuGet package result above. Further
exact-package native ARM64/default-adapter and downstream SDK/application gates
remain open. The ProGPU producer is qualified and merged; do not infer that
LibreWinForms/LibreWPF package/application gates are complete.

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
`progpu-directx-oracle-54adc6a0` folder. The exact-head hosted differential
artifact `progpu-directx-sample-differential` (`10338942628`) also passed:
both pinned 1280×720 Microsoft frames compare byte-identically with ProGPU's
D3D12, Metal and Vulkan candidates. These two sample scenes are not full
DirectX API or final LibreWPF application qualification.

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
