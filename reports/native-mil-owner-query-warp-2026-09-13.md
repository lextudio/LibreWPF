# Native owner-query Windows runtime differential

Acceptance application: `ProGPU.Wpf.ShowcaseApp`; action: native source point
and region selection. This batch isolates first-query execution independently
of preceding rendering and repeated-query reuse. It does not change source
input admission, shader geometry, query deadlines or dependency pins.

ProGPU's package consumer adds `--native-owner-query-probe`, invoking the full
existing owner-snapshot fixture in a fresh context. Release compilation has
zero warnings/errors and the complete fixture passes on Metal. Windows staging
retains the earlier WARP-selecting managed diagnostic and repaired native DLL
SHA256 `902431121dda4d2b189efbf2300950b05486f1751a8eff8bfec42a11d22c6248`;
it is not an exact final-package qualification.

## First-chance failure

System WARP version `10.0.26100.9278` fails after 19,879.905 ms submission,
before first readback completion. A matched apphost control also confirms
the system WARP is loaded and exits `-1073741819` before first readback, after
28,402.209 ms submission. Thus the successful development-runtime point query
is not explained by apphost versus shared-dotnet launch alone. Local artifact
`artifacts/native-core-validation.GwKsGq/dotnet.exe_260913_224509.dmp`
records an invalid generated-code store to `0x2144`; the return address maps
inside the system `d3d10warp.dll`. Nearby SP-relative stores suggest erroneous
stack-spill addressing. This is stronger evidence than the earlier terminal
unknown-module event and does not support calling it a null managed callback.

## Isolated development comparison

Microsoft [WARP 1.0.20](https://www.nuget.org/packages/Microsoft.Direct3D.WARP)
lists ARM64 code-generation fixes. A valid Microsoft-signed ARM64 DLL is staged
only beside a copied consumer apphost in `C:\ProGPU.OwnerQuery-Warp-1.0.20`.
The actual child module path is verified; Windows system DLLs, VM configuration
and shared dotnet installation are unchanged. No WARP package/binary is committed
or redistributed. Its testing-only license does not provide a product fix.

Using that runtime, the same point query submits in 22,532.337 ms, completes
readback in 155.797 ms, and passes all 16 repeated waits. The first bounds-region
query submits in 341,098.475 ms and passes its assertions; ellipse compilation
is now live. This cold latency is not usable application behavior. Keep process
4640 / execution session 71699 as the current observation
handle until authoritative terminal state; slow observation is not a failed or
completed process. Full region/input compatibility and cold latency remain open.

The ProGPU worktree's `docs/native-owner-query-warp-diagnostics.md` contains the
fixture contract, exact development package/DLL hashes and reproduction steps.
The local scripts `capture-fence-owner-query.ps1` and
`test-owner-query-development-warp.ps1` retain the exact Windows staging and
capture commands in the same validation directory. Dumps remain local.

## Remaining gates

Do not replace source-owned native queries with managed/CPU geometry or pass the
system-runtime gate using a substituted development runtime. Resolve supported
Windows query execution and latency, finish current-head CI/package validation,
then complete source-host/application platform checks and ordered dependency
integration. Broader DirectX/Direct2D/Win2D work remains deferred, not complete.
No PR is merged or dependency pin advanced by this diagnostic result.
