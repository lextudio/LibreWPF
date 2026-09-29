# Native pointer source reports

The internal source path consumes native move, drag, enter and five-button
down/up packets without replacing native coordinates, event time or click counts
with dispatch-time state. Its registrar does not advertise native-pointer
capability yet: scroll units/phases and source hover/leave retirement must connect
before an application can select the owned native factory.

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

The source gate exercises five-button preview/bubble delivery, fractional captured
coordinates, native timestamps and wrapping, nested click delivery, unsupported
packet rejection, callback disposal/root replacement, original/routed generation
retention, modal cancellation, throwing/filtered callbacks, reentrant recapture,
and legacy raw/event behavior. Cancellation regressions are authored for the
existing source CI gate; compilation alone is not their execution evidence.
This path does not use real AppKit windows, qualify native capture, retire hover
on source leave, or admit precision scrolling. Unadmitted events are rejected before any
legacy wheel conversion or button mutation.
