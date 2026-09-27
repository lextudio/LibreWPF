# Passive Showcase failure evidence

This is diagnostic localization, not a resize fix or native idle qualification.
The original native presentation assertions, four exact-zero intervals,
30-second post-Loaded first-frame bound and 120-second child deadline remain
unchanged. No debugger, renderer tracing switch, policy change or retry is added.

## Retained ARM64 failure and x64 control

At `ba7dcd7cd7288d9e0f2d2a3c07e44514489e50d2`,
[Build 36288813259](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36288813259)
retained an ARM64 child exit `3221226505` (`0xC0000409`), not a timeout. Its
journal reached `initial-observed`, `scrolled-observed`, then
`native-resize-request`; its final application receipt was empty. Before/after
payload hashes matched and no WER dump was produced. The original request
marker preceded dispatcher enqueue, so it did not establish that the resize
callback or native setter had executed. This status alone supplies neither a
faulting module nor a stack or fast-fail parameter; it is not proof of a
particular memory, rendering or lifetime defect.

The same-head x64 receipt succeeded: initial `2→2`, scrolled `3→3`, resized
`5→5`, restored `7→7`, with source restoration complete. This is an independent
x64 control, not ARM64 qualification. Both original artifacts/logs remain at
`/Volumes/1TB-macOS/librewpf-183-ci-triage.8wTXEi73/{arm64,x64}`.

## Additional checkpoints

The idle resize callback now records these transitions in the existing fresh,
flushed phase journal:

1. `native-resize-callback-entered`, immediately before the original setter.
2. `native-resize-setter-returned`, after `host.SetClientSize` returns.
3. `native-resize-wake-returned`, after the original explicit wake returns.
4. `native-resize-geometry-observed`, after the existing geometry wait completes.

The first three expand the existing two-action size helper without changing
their order or adding render requests. The fourth is before the next settling
and passive observation. None writes inside either endpoint-to-endpoint
measurement. A last marker localizes progress only; callback return is not GPU
completion or proof that deferred work succeeded. There remain at most 17
markers in the successful sequence, below the existing 32-record journal cap.

## Failure-only Windows Application Error records

Only the already opted-in CI Windows crash-capture child can trigger
`eng/showcase_idle_events.py`. The existing unique exact-byte apphost, per-image
WER ownership and dump collection stay unchanged. After a non-timeout child
failure, a separate read-only helper queries the local Application channel
using the documented [EvtQuery](https://learn.microsoft.com/en-us/windows/win32/api/winevt/nf-winevt-evtquery),
[EvtNext](https://learn.microsoft.com/en-us/windows/win32/api/winevt/nf-winevt-evtnext)
and [EvtRender](https://learn.microsoft.com/en-us/windows/win32/api/winevt/nf-winevt-evtrender)
APIs. No subscriptions, log clearing, remote sessions or registry writes occur.

Admission requires provider `Application Error`, event 1000, the exact unique
apphost name and full Windows image path, its **faulting EventData ProcessId**,
and a UTC event time between the runner's pre-launch timestamp and this one
post-failure query boundary. The event logger's System/Execution PID is never
treated as the crashed process. The unique image also prevents PID reuse from
matching a different invocation. UTC boundaries are retained; a wall-clock
regression is unavailable evidence, not an expanded search window.
Comparison preserves the event's seventh fractional digit (100ns); an event
just beyond either boundary is not admitted by microsecond truncation.

Only the correlated fault module/path, exception code, offset, process creation
time/report ID and event identity are retained. Unmatched raw XML, machine names
and other process records are never written. WER event 1001 is deliberately not
collected: filename-only fields without the owned faulting PID do not satisfy
this correlation contract.

The helper has a five-second hard process bound, no retries, a 100ms `EvtNext`
wait, at most 16 candidate records, 64KiB per XML render, 256KiB aggregate XML and
a 64KiB fresh JSON receipt. Reaching the record cap is explicit. All event/query
handles are closed on the same thread; timeout kills only this diagnostic
helper. Missing, inaccessible, late, malformed or unsupported event records
remain explicit unavailable diagnostics. The helper cannot change the original
child exit, timeout, cleanup status, or success judgment. This extra
post-failure budget never lengthens the application's 120-second bound.

## Local validation boundary

Offline checks: 16 event/XML/ABI/order controls, 20 launcher/child controls and
15 original crash-policy/minidump controls pass. The existing C# source guard is
extended without changing its case count; its full compilation/execution remains
the normal CI gate. Workflow actionlint and `git diff --check` pass. No Windows
event query, Showcase launch, GUI, VM, native render, debugger or full build was
executed for this change. The next actual failed run may still produce no event
or dump; these diagnostics do not resolve or reclassify the original crash.
Local logs are retained under `artifacts/idle-crash-evidence.GEXTqK/` in the
isolated `librewpf-idle-crash-evidence.9G281lnq` worktree.
