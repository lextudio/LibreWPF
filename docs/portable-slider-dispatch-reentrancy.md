# Slider input during a source dispatcher flush

The original Gallery Slider action in issue #52 is dragging a real `Thumb`.
Ordinary native batches already flush layout between pressed moves. A separate
source-level gap remained: input reentering an active activation flush ran
synchronously, but the nested Render flush returned immediately. `Thumb` then
measured two moves against its old position and `Slider` accumulated both deltas.
This establishes a reachable ordering defect, not the cause of the reopened
Gallery report; the original application still requires desktop qualification.

The same activation now owns a lazy, lock-protected FIFO during its flush or
deferred replay. Each packet retains its native metadata and immutable input
fields; a separate `Handled` result claims native ingress without publishing
the later source result back into an already returned callback. No lock covers
application callbacks. Normal nested input outside a flush is unchanged.

After the outer flush unwinds, each packet follows original input dispatch,
press-generation cleanup and the pressed-event Render boundary. Reentry during
replay appends behind existing packets. Cancellation immediately retires older
pending packets and retains the original source capture-cancel delivery; new
presses from cancellation callbacks belong to a new generation. Hide,
deactivation and disposal also retire pending work. Replaced roots/bridges and
current modal rejection cannot replay against a new owner.

A failed flush or source callback propagates its original exception without
running more source callbacks in `finally`. The attempted packet is not retried;
unattempted packets retain FIFO order ahead of later input until the next normal
host input/update turn, or retirement. This is not a global dispatcher drain,
sleep, coordinate correction, changed Slider algorithm or renderer fallback.

The original ten real Slider/Track/Thumb source cases and their 30-second process
deadline remain. Two additional cases (1× and 2× source DPI) deliver two moves
from an actual Render-priority callback and require values 25 then 30, arranged
Track state and real capture release. Eleven bridge ownership cases cover FIFO,
replay reentry, cancellation, retirement, exceptions and off-thread enqueueing.
These are authored regression controls, not executed evidence or native pixels.
This implementation phase permits compilation only; no application, tests,
clipboard, image capture or VM execution qualifies this change.

One isolated compile-only attempt used SDK `11.0.100-preview.5.26302.115`
through `/usr/local/share/dotnet/dotnet`, current bridge source and read-only
warm shim/backend references. Product signing and WindowsBase facade removal
were retained. It stopped with 15 analyzer errors in unchanged source
(`CA1859`, `CA2249`, `CA1513`, `CA1725`), with no C# errors reported; the test
and Slider projects restored but did not compile because that dependency failed.
No analyzer suppression or retry was used. This diagnostic composition is not
an exact-current package build; ordinary PR CI must compile and run the gates.
The retained log is
`/Volumes/1TB-macOS/librewpf-slider-compile.M0mPBFyz/compilation.log`, SHA-256
`e3103878559c9c9ca5a4e6d1b0e02cea65330fdb9b165975e933df8062b57748`.
