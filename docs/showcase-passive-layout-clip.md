# Passive Showcase layout-clip gate

## Bounded crash evidence

The Windows CI jobs opt into `-CaptureIdleCrashDump` only for the existing
120-second passive child. The runner copies the already checked apphost to a
fresh UUID basename beside its unchanged managed entrypoint, verifies all bytes
and the embedded DLL binding, and exclusively creates that image's
[WER LocalDumps key](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps).
An existing key is never changed. `finally` removes only the newly owned key and
copied image; configuration, collection and cleanup errors preserve a nonzero
child result and fail an otherwise successful run. Original package, PE,
displayed-window, mode and payload-hash checks remain mandatory.

WER is configured for one mini dump, never a full-memory dump. Raw output is
outside the upload tree. Only a regular, exact-image/exact-PID dump at most 32 MiB
with a normal-minidump header, bounded stream directory and one exception stream
may enter the existing retained artifact. Other dump flags/full-memory streams
fail closed. A mini dump contains thread stacks and module metadata; it is not
safe evidence for arbitrary applications containing private data. This capture
is restricted to the ephemeral CI Showcase fixture and records no environment
values. No dump or a rejected dump leaves the fault stack explicitly unavailable.
No local registry operation or native crash was executed to validate this code.

The child also writes an exclusive, at-most-32-record phase journal containing
only phase, PID and monotonic timestamp. Writes occur before admission or after
an observed interval, never between its endpoints. These diagnostics do not
change settling, observation or process deadlines, and do not attribute the
retained ARM64 `0xC0000005` failure. The headless minimum is now 649, preserving
all prior 630 cases and adding seven actual-host and twelve boundary/journal/source
cases. Offline synthetic dump/registry controls are not Windows crash qualification.

At source `26e79f35f`, the actual canonical `ProGPU.Wpf.Tests` Release project
built with its exact `08f4343` source dependency: zero errors, 117 warnings.
VSTest 18.9 on .NET 10.0.5 ARM64 passed all 32 selected cases, zero skips:
seven actual host cases, eleven boundary/journal cases, eight original interval
cases, four source contracts and two workflow guards. The unchanged full suite
was compiled, not executed. Evidence is `artifacts/passive-canonical/`, with TRX
SHA-256 `85a1be9ada25310b779e849abedad03c7326ac818c088fa816d269d86b28b763`.
The runner's nineteen Python controls, ten crash-policy controls and six
PowerShell launcher controls also pass without native/registry execution.
Both actual Windows native idle results and a useful ARM64 fault stack remain
outstanding; the earlier failures are retained without reinterpretation.

This is a separate opt-in native application gate for the retained layout-clip
change in #179. Implementation and source/metric controls are not evidence that
the application has passed idle qualification. No native run is claimed here.

Use a freshly built, loose-file **ShowcaseApp** package consumer with
`ProGpuWpfRendererMode=NativeMilWgpu`. The default managed renderer, a detached
source window, pre-display self-tests, and `LIVE_VALIDATE=0` Windows package
self-tests cannot satisfy this gate. No renderer fallback is allowed. Retain
the producer/package identity separately; the receipt records actual loaded
managed assembly MVIDs and SHA-256 hashes, not an inferred source commit.

Run the bounded launcher against that existing executable (or its DLL with an
explicit `--dotnet` host), in a real supported graphical session:

```sh
python3 eng/progpu-wpf-showcase-idle.py \
  --app /absolute/package-output/ProGPU.Wpf.ShowcaseApp \
  --evidence-parent /absolute/existing-evidence-directory
```

The launcher owns one child, a fresh evidence directory, a 120-second deadline,
the complete child output, and a mandatory JSON receipt. It does not build,
install packages, select a software adapter, start a VM, disable application
timers/carets/input, or reuse an old receipt. Do not interact with the application
during the observation. Other activity causes a failure, not a tolerance.

The constructor validates the idle configuration but does not start its task.
The existing actual `Loaded` callback admits it once, after synchronous native
window/composition initialization. `Loaded` is not a presentation acknowledgement:
the separate, unchanged 30-second check still requires the actual host's
`HasPresentedFrame`. The outer 120-second deadline includes cold startup. Ordinary
live-input validation keeps its existing constructor startup behavior.

The application uses its existing Selectors tab, Expander, ScrollViewer and text.
It expands normal text to produce pixel scrolling and temporarily attaches an
ordinary zero-height `Border` with `ClipToBounds=true`. Typed source layout
state must describe both the real viewport clip and the non-Empty 80×0 clip.
The root remains the actual `Window`, owned by its frozen portable presentation
source and genuine native MIL host. Every phase checks native window/source
identity, positive native command/draw/submission counts and unchanged device
recovery. Setup uses ordinary source changes and actual native client resizing.

Four phases are required, in order:

1. Initial overflowing, clipped content at scroll offset zero.
2. Actually scrolled source content, displaced by its real pixel offset.
3. Real native resize with larger source content and surface dimensions.
4. Restored native dimensions and scroll offset zero.

Each phase has one fixed one-second settling delay and one two-second passive
interval. The observer reads the atomic presented-frame count, wall time,
process CPU, total managed allocated bytes and GC collection totals only at the
two endpoints. It performs no dispatcher calls, layout, hit queries, render
requests, native memory polling, status writes or logging between them. A
continuously redrawing baseline still yields bounded metrics and fails; there
is no quiet-until-success loop. All four intervals require **exactly zero new
presentations**. CPU, allocation and GC deltas are reported without invented
performance thresholds; they include process-wide activity and observer cost.

## Windows package CI connection

The existing Windows x64 and ARM64 native MIL package Showcase jobs opt in with
`eng/progpu-wpf-windows-native-mil-showcase.ps1 -ValidatePassiveIdle` (plus
`-TargetArchitecture arm64` on ARM64). This uses the same apphost whose PE
architecture, requested native mode and exact package assets have already been
checked. The original pre-display, displayed and same-source text-layout matrix
remain mandatory with their original deadlines; each job still has 40 minutes.

Only around this additional child, the PowerShell launcher saves and clears the
four conflicting pre-display, displayed, live and forced-performance modes,
then restores the caller's exact settings in `finally`, including launch or
receipt failures. It does not change renderer selection, adapter choice or the
Python runner's 120-second deadline. Standalone Python invocation still rejects
conflicting modes. Offline PowerShell controls verify this boundary without
launching Showcase; they are not application qualification.

Each attempt retains a fresh launcher transcript plus the existing runner's
payload hashes, child output and mandatory receipts under
`artifacts/showcase-native-idle/win-{x64,arm64}/`, outside the private temporary
build/cache directory. Both CI jobs always upload that RID-specific directory,
including failed attempts. Failure before a launch can leave no application
receipt and is never an idle pass. Enabling these jobs is not itself a native
idle result; qualification requires their actual four-phase successful receipts.

Source/native checks happen outside the intervals. Failure receipts retain
observed metrics. The original text, scroll request, selected tab, Expander,
native size and temporary child are restored in `finally`; an unresponsive child
still fails at the launcher's deadline. Existing live input, clipping/picking,
forced-frame performance, source tests and package gates remain independent and
unchanged. Passing this idle gate would not establish GPU residency, pixel
fidelity, all application interactions or another platform's qualification.

## Validation state

The first Windows wiring Build at `a00b37a2a` stopped in the SDK source guard
([job 108479995458](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36268541312/job/108479995458)):
the two new exact-head evidence names increased the workflow reference count
from 22 to 24. Neither native Windows job ran. The corrected guard retains all
ten exact-head checkouts and separately requires Python 3.12, launcher controls,
the idle opt-in and retained evidence inside each architecture's job. The
focused checked-in C# guard passed in a source-only harness; that is not a full
SDK or native application result. The failed Build remains historical evidence,
and the replacement exact head still requires the complete CI matrix.

The subsequent `2f8eb19b3` Build
([36271307857](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36271307857))
failed the shared Showcase Thumb input acceptance before either Windows idle
job ran. That failure remains evidence, not a native idle result. This branch
now integrates qualified main `455d962b758788bb8d0a083edb299eeb39c95ec0`, including
the retained dirty-source/topology fix from PR #174. Its original strict input
gate and failure archives remain unchanged. The combined workflow has ten
exact-head checkout references and sixteen artifact names, checked by both
complete source guards. Full CI on this combined head, including actual x64
and ARM64 four-phase native idle receipts, is still required; the earlier
failed Build is not reclassified as passing.

The combined `0889ffdbaf` Build
([36280045293](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36280045293))
reached both Windows jobs, but each stopped at **Validate native idle launcher
isolation** before launching Showcase. All five offline controls passed; the
invalid-receipt stub left a synthetic global `LASTEXITCODE=1`, which the GitHub
PowerShell wrapper propagated after the final launch-exception control. The
fixture now saves and restores the caller's exit-status presence and value
around each injected invocation in `finally`, and asserts that restoration.
The production launcher, its original failure assertions and all native gates
are unchanged. Local PowerShell 7.5.2 reproduced the original wrapper exit 1;
the corrected fixture passes all five controls with absent/zero caller status,
preserves a prior nonzero status, and still fails an independently injected
case-count assertion. These are offline launcher checks, not native idle
receipts; both actual Windows four-phase runs remain required.

At `f8f419321`, [Build 36282082103](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36282082103)
passed the offline controls, four clipboard contracts, pre-display Showcase and
displayed `Application.Run` self-tests, then failed both actual idle children:
[x64 job 108519668366](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36282082103/job/108519668366)
and [ARM64 job 108519668286](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36282082103/job/108519668286).
Both application receipts have no phases, `success=false`, `uiRestored=false`,
and `No actual Showcase native presentation within 30 seconds.` Both runner
receipts preserve child exit 1, `timedOut=false`, no cleanup errors and identical
before/after managed payload hashes. They are failures, not idle qualification.

Source inspection found that the constructor started the first-frame timer
before `Window.Show` and synchronous native initialization; the later `Loaded`
callback was already suppressed by the started flag. The corrected admission
waits for `IsLoaded` and retains the original presentation assertion and both
deadlines. Focused controls exercise pre-Loaded rejection, first Loaded admission
and repeated/unload-reload rejection, while source guards connect that helper to
the real constructor/Loaded paths. The new source guard fails on the former
ordering; all 11 focused endpoint/startup/source cases pass after the change in
a small .NET 10.0.5 harness compiling the exact checked-in helper/test files.
This does not execute WPF's Loaded lifecycle or prove that initialization timing
was the sole cause of either Windows failure. Fresh complete CI and both actual
four-phase receipts remain required.

Both complete failed job logs and their original artifacts are retained in
`/Volumes/1TB-macOS/librewpf-idle-native-ci.Dcy2Vcmk`. Archive SHA-256 values match
GitHub's artifact digests: x64 artifact `10920185668` is
`f67ce8b661c16c99f099de810b6a5887e59d71ac6c955708ee3a95c36d09b290`;
ARM64 artifact `10919513268` is
`0f98655c84a8c7b3796c0bd0fb541032c20cd7be609ff0f23158c021d929301f`.
The focused harness preserves its baseline failure and corrected result under
`artifacts/windows-idle-startup.30t9Vp/`. No native application, VM or full source
build was run for these local checks.

The endpoint helper has strict zero/nonzero/regressed-frame controls and a
two-endpoint execution contract. Source guards preserve the side-effect-free
interval and real Showcase ownership seam. The launcher has offline negative
receipt/child controls. CI adds these controls to the existing retained lane:
all original 619 cases remain mandatory, plus 11 endpoint/startup/source cases (minimum
630), followed by the offline launcher suite. No live application is launched
by that headless lane. These are implementation regressions, not native idle
receipts. Compilation, complete CI and subsequent actual graphical execution
remain required before claiming the #179 application acceptance criterion.
