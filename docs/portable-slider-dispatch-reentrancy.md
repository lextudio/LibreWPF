# Slider input during a source dispatcher flush

The original Gallery Slider action in issue #52 is dragging a real `Thumb`.
Ordinary native batches already flush layout between pressed moves. A separate
source-level gap remained: input reentering an active activation flush ran
synchronously, but the nested Render flush returned immediately. `Thumb` then
measured two moves against its old position and `Slider` accumulated both deltas.
This establishes a reachable ordering defect, not the cause of the reopened
Gallery report; the original application still requires desktop qualification.

The same activation now owns a lazy, lock-protected FIFO for pointer input during
its flush or deferred replay. Keyboard down/text/up retain their original
synchronous owner-thread dispatch during a flush when no pending or active
deferred work owns delivery. Otherwise they append behind that older work.
Each queued packet retains its native metadata and immutable input
fields; a separate `Handled` result claims native ingress without publishing
the later source result back into an already returned callback. No lock covers
application callbacks. Direct nested input outside both a flush and deferred
processing is unchanged.

Each packet runs in its own real source Dispatcher Input operation through the
optional typed `IPortableWindowInputDispatcher` capability. Source Render/layout
work precedes the next Input packet, and accepted packets precede Background
barriers even inside an ApplicationIdle frame. There is no synchronous
post-flush replay or fallback when the capability is absent/rejects admission.
An operation identity prevents a retired scheduled callback from consuming a
replacement queue. Original input dispatch, press-generation cleanup and the
pressed-event Render boundary remain. Reentry appends behind existing packets.
Cancellation immediately retires older
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
Track state and real capture release. A thirteenth case injects Move/Down/Up
from Send inside ApplicationIdle and requires the original down and release to
precede a Background/Send assertion. Fifteen bridge ownership cases cover FIFO,
replay reentry, cancellation, retirement, exceptions, off-thread enqueueing and
rejected asynchronous admission, plus synchronous keyboard delivery and intact
keyboard payload/order behind either a queued or currently executing pointer.
These are authored regression controls, not executed evidence or native pixels.
This implementation phase permits compilation only; no application, tests,
clipboard, image capture or VM execution qualifies this change.

The first queue implementation (`91bef835a`) failed the existing macOS external
SDK live mouse probe in Build `36635795621`: the unchanged down-count assertion
observed zero. Its private queue drained only after the entire source flush, so
the probe's Background barrier and subsequent Send assertion could run inside
that still-active ApplicationIdle frame before accepted Down delivery. Posting
each packet at actual Input priority repairs that supported ordering defect;
the external probe's actions, waits and assertions are unchanged. This is not
evidence that an OS click was lost, or that the original Gallery issue is closed.
The dependency order is ProGPU PR229 (typed optional capability), Forms PR134
(aligned engine pin), then WPF PR219 (source implementation and consumer).

Build `36642624705` at `00abfaddb` passed the external SDK smoke gate, which
requires all 13 Slider child success markers and the unchanged external live
mouse probe. The whole Build still failed: Toolkit WindowControl expected
`Pane` but observed empty text. Its Send callback injects keyboard packets and
then uses another Send callback to assert their result. Deferring keyboard input
solely because a flush was active let that second Send overtake delivery, even
though no older deferred pointer work remained. The keyboard exception above
restores the existing delivery semantics without changing Toolkit actions,
assertions, barriers, deadlines or dispatcher priorities. That failed Build is
not a qualified package producer; final native/application gates remain open.

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

One follow-up compile-only attempt imported the original checked-in bridge and
test `Directory.Build.props` (including their existing analyzer policy) and used
the actual compiled `bb66c0f83622a68fd41a8f81f2a78134139a4f59` Interop DLL.
The bridge and actual 13-case Slider program compiled; the bridge reported one
existing unused-event `CS0067` warning. The isolated test project stopped on
seven missing-type errors because its narrow source include omitted existing
`WpfPortableWindowActivationServiceTestShim.cs` and the collection declaration
in `WpfRenderDataSinkProviderBridgeTests.cs`. No retry or execution followed.
The source registrar's full PresentationFramework compilation remains a CI gate,
and these mixed warm-reference outputs are not package qualification. Log:
`/Volumes/1TB-macOS/librewpf-slider-compile.M0mPBFyz/input-dispatch-compilation.log`,
SHA-256 `b6bce9ba6f718e776109a8c392ce268be736568335ab116c3701e5da9b2ec082`.

The keyboard follow-up used one warm compile-only invocation with those existing
test-support source files included. Bridge, activation-test sources (including
the three new rows) and unchanged Slider program compiled successfully, with
zero errors and the same existing `CS0067` warning. No tests or applications ran;
this remains source-composition evidence, not full-graph or package qualification.
Log: `/Volumes/1TB-macOS/librewpf-slider-compile.M0mPBFyz/keyboard-dispatch-compilation.log`,
SHA-256 `31233757d43858edc05914b42b194ed8c2f03a44b0a3bcd7bb52d8cff73315a1`.
