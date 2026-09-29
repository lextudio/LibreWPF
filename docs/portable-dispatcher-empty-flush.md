# Empty portable dispatcher flushes

The native host updates even when frame coalescing suppresses presentation.
`WpfPortableWindowActivation.OnHostUpdateTick` promotes source timers and then
flushes the source dispatcher through Background priority with its existing
8 ms bound. Previously every empty update still allocated a marker operation,
captured callbacks, dispatcher frame, timeout timer and nested synchronization
context. This is a concrete continuously executed source-allocation path, not
an attribution of all process CPU or allocations to the UI thread.

The source dispatcher now admits an empty-flush shortcut only for its managed
message pump, on its owner thread, at an active valid priority. Under the existing
queue lock it requires no work at or above the marker priority, no due timer,
no disabled processing, no shutdown and no requested frame exit. Lower-priority
operations and future timers remain queued. Posts racing the snapshot remain
owned by the unchanged next host update and native wake mechanisms.

Due timers use the original frame, including its timer promotion, synchronization
context and reentrant hooks. The predicate does not promote or execute anything.
The host's earlier timer promotion may run hooks; its resulting state is read
fresh at admission. Invalid priorities/timeouts and immediate timeouts use the
original exception/timeout path. The captured marker implementation is a separate
method so rejected allocations are not created before an early return.

Windows always keeps the original frame because an empty managed queue does not
prove the Win32 native message queue is empty. Native event pumping, the 16 ms
timer wake, 1/16 ms owner-loop delays, completion polling, input, caret/animation
timers and presentation admission are unchanged. This change does not establish
a Windows CPU improvement or qualify native idle/application performance.

## Coverage and qualification

Authored actual-source controls (not executed in the compilation-only phase):

- `WindowsBase.Tests`: `System.Windows.Threading.Tests.PortableDispatcherFlushTests`,
  18 cases covering admission, priority retention, owner thread, disabled/shutdown/
  frame-exit state, real due/future timers and earlier reentrant promotion hooks.
- `PresentationFramework.Tests`: `System.Windows.PortableDispatcherFlushTests`,
  11 cases covering the consumer's empty path, original FIFO/priority work, lower
  priorities, invalid/immediate timeouts, disabled/shutdown behavior and actual
  due-timer execution in the original dispatcher synchronization context.

Run each class through the original source test executable with its exact class
filter, the above minimum, `--fail-skips on --timeout 60s --no-progress`. Preserve
all existing source and native gates. No local runtime, GUI, VM or profile was
executed while authoring this change; source compilation and execution evidence
must be reported separately.

## Remaining CPU attribution

The original qualified PR217 native idle artifacts show zero extra presentations
in all four phases on both Windows architectures, but 1.75–7.84375 process CPU
seconds and 143,552–216,312 managed allocated bytes over approximately two-second
intervals. Those whole-process values do not identify the UI thread or native
workers. Their provenance is Build `36604077464`, source tree
`4d716375972e68c3dc2863724e37810db857a64a`, ProGPU
`e17ba9bda99971f65cf4cd516fb194c6076336bf`; the x64 and ARM64 idle artifact IDs
are `11052892499` and `11053485938` respectively.

The next Windows attribution requires one bounded, exact-PID trace of the
unchanged four-phase application: sampled CPU with native and managed stacks,
thread/context-switch attribution and CLR allocation ticks, correlated with the
existing phase journal and endpoint counters. Keep the original 120-second child
deadline and idle assertions. Native wgpu/driver workers must remain distinguishable
from UI dispatcher work; managed-only samples or stable frame counters cannot
establish that cause. Sampling is not an exact allocation count. The original
macOS AvalonDock report remains a separate application/profile qualification.
