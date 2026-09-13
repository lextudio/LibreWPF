# Clean native MIL source-host startup

## Acceptance path and provenance

The source-built `ProGPU.Wpf.RealPresentationFrameworkHarness` is an existing
prerequisite for `ProGPU.Wpf.ShowcaseApp` qualification. The action is a clean
source build followed by native text/input, retained rendering and device
recovery, not a substitute application or final package-mode qualification.

The isolated checkout starts at LibreWPF `2e2047a46` with ProGPU `5a3b6bfd`.
The native libraries are the rebuilt `5e3bbc27` production sources (the later
ProGPU commit changes CI only). Existing dirty checkouts were preserved. An
initial attempt to share dependency outputs through a symlink produced missing
reference assemblies; it was replaced by a real clean ProGPU worktree before
the failures below were diagnosed.

## Reproduced prerequisite failures

1. The harness's source build dynamically invokes `PresentationBuildTasks`, but
   its normal restore graph does not include that project. A clean checkout
   fails with `NETSDK1004` for its missing `project.assets.json`. The host script
   now restores that prerequisite explicitly, matching the existing Linux
   windowing smoke's setup. Explicit skip-build mode remains unchanged.
2. After restoring the task, execution reached rich-text checks and reported
   that an explicit line break lost its outer decoration scope. A diagnostic
   run traced the original text runs: no decoration scopes existed even before
   the line break. The default portable Aero2 theme was absent. Source
   `Underline` obtains its decoration from that actual theme style, not from
   a hard-coded formatter policy or its constructor.
3. Building the existing source Aero2 project restored the paired nested
   modifiers and the unchanged rich-text checks passed. The harness now declares
   that theme as a build-only project reference for its isolated source-WPF load
   context. No local decoration property, fake style or relaxed assertion was
   added. The temporary run tracing was removed.

## Validation

- The complete native-host script passes with both prerequisite changes, exit 0.
- Existing text collapse/justification, inline ownership, rich-document source
  editing/undo/table navigation, retained MIL, geometry selection, native input
  index and device-recovery assertions are retained.
- The host reports 23 commands, 20 resources and nine compiled draws, with five
  submitted draw calls in its final frame and recovery in the existing window.
- The focused project-graph regression passes (one test); shell syntax and diff
  whitespace checks pass. This is not a claim of a full bridge test-suite run.
- ProGPU's current-head hosted browser and GCC/MSVC compatibility checks pass.
  Full Windows native/package and downstream application gates are still pending.

Logs are under `artifacts/native-core-validation.GwKsGq/`: the initial failures
are `native-host-clean-dependencies.log` and
`native-host-prerequisite-restore.log`; the diagnostic is
`native-host-rich-scope-trace.log`; the final unchanged-assertion run is
`native-host-final.log`, and the focused regression is
`native-host-bootstrap-test.log`.

This closes clean source-host prerequisites, not SDK startup, complete theme
coverage, full cross-platform application qualification or the broader DirectX
goal. Dependency pins and merge admission remain unchanged until exact-head
required gates pass.

## Linux ARM64 follow-up

The six focused host/SDK-boundary graph checks pass, including the real framework
harness contract, source-path exclusion from package qualification, package-only
production boundary, early native service registration and full application
lifetime gate. Log: `native-host-sdk-boundary-tests.log` in the directory above.

The same rebuilt, architecture-neutral managed source-host output was staged in
Ubuntu ARM64 with the current Build's `progpu-native-runtime-linux-arm64` artifact
(`10321354138`, run `34770390199`, ProGPU `5a3b6bfd`). This is a runtime portability
check, not a Linux source-build or exact package-consumer claim. The guest uses
.NET 10.0.11 and Xvfb/X11; Wayland, interactive desktop/mixed-DPI behavior and the
package-mode Showcase remain separate requirements.

Both retention and the unchanged native-host/device-recovery runs pass, exit 0.
The run includes the original rich-text decoration assertion without diagnostic
tracing or assigned replacement styles. Native input, source document/editing
checks and recovery complete, with the same final command/resource/draw counts
as the macOS host. Log: `native-host-linux-arm64.log` in the directory above.

Runtime hashes:

- `libprogpu_native.so`: `ba34f437bfd6b399b05d0a994ef810786f336518c7fd8bd07e9c8aaea1ccd099`.
- Packaged `libwgpu_native.so`: `e1f5bbef1264c9c4490c88a967ccb0ed86166a241f9e3ffb6e5736641bd7d084`.

The Parallels CLI skill guided VM discovery, readiness checks, scoped guest
execution and lifecycle verification. The existing suspended Ubuntu VM was
started for this check and returned to suspended state afterward. No VM setting,
driver, package installation or user data was changed. The first guest-command
attempt occurred before Tools was ready; retrying after readiness succeeded
without restarting the VM. Staged test files remain in the owned host directory
`/Volumes/1TB-macOS/progpu-native-host.c6dUuh` and guest directory
`/tmp/progpu-native-host.scd7mr`.

Hosted Windows x64 native-renderer CI also passes on `5a3b6bfd`. Windows ARM64
native and Windows managed tests are still running; package consumers have not
yet qualified this head. No merge or dependency update follows from these
partial results alone.
