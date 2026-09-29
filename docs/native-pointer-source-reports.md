# Native pointer source reports

The internal source path consumes native move, drag, enter and five-button
down/up packets without replacing native coordinates, event time or click counts
with dispatch-time state. Its registrar does not advertise native-pointer
capability yet: the remaining scroll and source integration gates must finish
before an application can select the owned native factory. The internal
[native scroll route](native-scroll-routing.md) now connects explicit units/phases
to source hit testing and routed events independently of legacy wheel dispatch.

The original raw report keeps its existing scalar storage. A portable derived
report retains the immutable native packet and separately mapped double client
point. All activation/movement splits preserve those values. Native activation
uses its following exact move for initial hit testing, not an integer-rounded
synthetic synchronization. Up also updates its actual position before dispatch.
Legacy reports retain their original integer coordinates and synchronization.
Portable capture/re-hit-test synchronization also retains the double client frame,
but has no physical native packet and does not fabricate another native event.

Portable move/button event subclasses carry the same report through preview and
bubble delivery. Original double native time remains available internally;
ordinary WPF timestamps use its wrapping 32-bit millisecond representation.
Down and up retain actual click counts for all five mapped buttons. Native
modifier flags remain separate from the normalized source shortcut snapshot.
Capture routing transforms only the target client frame, not the original packet.
Legacy constructors, event types and click-count calculation remain unchanged.
`PortableInputEventArgs` retains its original ten-argument constructor identity;
native metadata uses a separate overload. An optional extra argument is not
reflection/binary compatibility, as the SDK Application.Run harness exposed.

Each native report captures both its original and capture-routed presentation
source's input generation. Root replacement, cancellation or disposal invalidates
pending reports before callbacks. Split
reports and promoted events retain the old generation rather than recapturing it.
Every raw and routed mouse processing stage rejects retired generations, so a
move callback cannot deliver its pending old down to a newly installed root.
Retained event arguments still expose their original immutable metadata.

Native cancellation retires the original source generation and still-owned
presses before callbacks, without a synthetic up/click/move. It releases capture
only from that source's exact provider, independently of the active source or
the provider's stale capture flag. It is lifecycle cleanup, not suppressible
ordinary input: hidden, rootless and modal-blocked sources can still release
their ownership. The resulting LostMouseCapture retains native time, packet and
the normalized modifier scope. Callback failures propagate after capture and
press state retirement; modifier scope unwinding does not rewrite physical keys.

Portable capture transitions track their own generation. A callback that takes
new capture owns the resulting GotMouseCapture and synchronization; the retired
transition does not repeat those operations or clear a new same-provider press.
An original popup's cancellation invalidates its pending capture-routed down
without cancelling the other provider's capture. Ordinary native Windows capture
and its synchronization path remain separate.

Portable capture also completes property publication when an application callback
throws before LostMouseCapture. Reverse-inherited capture-within values continue
through the actual ancestor paths using the existing changed flags; bindings do
not retain old dependency-property values while the fast cache already says false.
Direct captured flags and capture loss/gain notifications complete against live
ownership, then the original failure escapes. Multiple failures are retained in
an AggregateException. A callback's newer capture owns its own gain and hover
synchronization, never the retired transition's tail. The opt-in completion mode
does not change native Windows capture or other reverse-inherited properties.

Five additional source cases cover throwing capture-within/direct callbacks,
ancestor dependency-property publication, same/different-element recapture,
multiple simultaneous failures and acquisition failures. These are authored for
the automatic source CI gate; compilation alone does not establish their result.

Native hover notifications retain the source packet, original timestamp and
event modifier scope through the existing reverse-inherited MouseEnter/MouseLeave
properties. Each notification scope is restored after nested delivery or failure.
Pointer/hover revisions reject an older hit-test result or notification after a
callback changes the current pointer state; direct-hover flags describe the live
element, not the old callback's target.

Leave matches the original physical source, independently of a capture-routed
destination. A late leave from a previous source cannot clear the current hover.
Its exact last point is mapped into the active client frame, and the source is
marked outside before notifications. Layout/capture synchronization cannot reuse
that outside point as physical re-entry; a real position report resumes hit testing.
WPF logical hover stays on the captured element while physically outside its
source, including owner capture across a separately surfaced popup. Leave neither
releases presses/capture nor synthesizes a MouseMove. Programmatic capture changes
outside update logical hover without fabricating native pointer metadata.

Cancellation also retires still-owned hover after capture loss, even when a loss
callback throws. A new capture or pointer revision from that callback is retained.
If both loss and hover callbacks fail, both exceptions propagate. Physical origin
tracking uses one reusable weak reference, not an additional closed-window lease.

The source gate exercises five-button preview/bubble delivery, fractional captured
coordinates, native timestamps and wrapping, nested click delivery, unsupported
packet rejection, callback disposal/root replacement, original/routed generation
retention, modal cancellation, throwing/filtered callbacks, reentrant recapture,
native enter/leave metadata, outside synchronization, late leaves, nested hover,
throwing hover and capture callbacks, and legacy raw/event behavior. Regressions are authored for the
existing source CI gate; compilation alone is not their execution evidence.
This path does not use real AppKit windows, qualify native capture, or admit
full precision-scroll application compatibility. Its
[scroll consumer](native-scroll-source-consumer.md) queues actual point/line
commands through the routed path; the remaining admission boundaries are documented
there. Unadmitted events are rejected before any
legacy wheel conversion or button mutation.
