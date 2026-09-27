# SDK runtime copy in multi-target builds

LibreWPF #185 reported `MSB4057` from a WinForms-enabled application using
`TargetFrameworks`, even with only one framework. The SDK's final runtime-copy
target ran after the outer `Build`, where `ResolveLockFileCopyLocalFiles` does
not exist. Inner builds had already copied their payloads successfully.

`_ProGpuWpfSdkCopyPortableWinFormsCompatRuntimeAssets` now requires a nonempty
`TargetFramework` and excludes `IsCrossTargetingBuild=true` on the target itself,
before its dependency list is evaluated. All inner runtime resolution, canonical
closure checks, copy policy and explicit opt-out remain unchanged. No global
build hooks, package defaults, desktop intent or framework validation are disabled.

Regression evidence:

- The actual SDK import test reproduces the exact missing-target error with both
  one and two plural frameworks against the old targets. The complete six-method
  desktop-property suite, including its existing subcases, passes with the fix.
- `eng/sdk-runtime-copy/Run.proj` executes the actual shipped copy target through
  eight isolated MSBuild target graphs. Twelve inner evaluations verify exact
  framework-specific payload text or explicit no-copy behavior, covering singular,
  plural, multiple, compatibility and canonical cases. Per-framework dependencies
  are deliberately absent from outer evaluations, not replaced with no-op stubs.
  Marker files test copying, not assembly/runtime behavior.
- The normal SDK CI lane runs that matrix. Its real canonical package consumer
  now uses plural `TargetFrameworks`; the Forms-only consumer remains singular.
  Both keep central/noncentral restore, exact staged-package and runtime checks.
  The run command explicitly selects its framework; restore/build still exercise
  the outer build. Build-packages-only mode still skips qualification.

Local logs are under `artifacts/sdk-cross-targeting/`. The matrix and SDK import
checks pass on macOS ARM64 with SDK 11.0.100-preview.5.26302.115. Full hosted
package/application checks remain required before merge or release.
