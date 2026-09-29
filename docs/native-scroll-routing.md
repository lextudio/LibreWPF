# Native scroll routing

Acceptance target: point/line scrolling inside ShowcaseApp dialogs and dropdowns.
The internal native source entrypoint now dispatches scroll packets through the
real source hit test and routed event tree to the existing ScrollViewer consumer.
It shares MouseDevice.LocalHitTest, including native owner promotion and the
authoritative GPU-cache-miss sentinel; a miss cannot trigger a CPU drawing walk.
The native registrar and owned Cocoa factory remain disabled pending the remaining
integration and qualification gates below.

`PortableScroll.PreviewScrollEvent` tunnels and `PortableScroll.ScrollEvent`
bubbles. Their `PortableScrollEventArgs` retains the immutable native packet,
original wrapping WPF timestamp and event-specific shortcut modifiers. Preview
handling suppresses default scrolling; handled-event observers can still inspect
the bubble. These are separate events, not integer MouseWheel deltas fabricated
from points or line counts. Legacy wheel dispatch remains unchanged.

The actual input provider declares the phase protocol. Untagged packets are only
admitted with both phase fields zero. Explicit AppKit input admits None, Began,
Stationary, Changed, Ended, Cancelled and normal MayBegin, using the exact SDK
values 0, 1, 2, 4, 8, 16 and 32. Momentum MayBegin, simultaneous normal/momentum
phases, unknown bits and combined phase values are rejected before route state or
queues change. Combined phases require a separate demonstrated contract; they are
not inferred from recognizing individual bits.

Normal input hit-tests the current point for every packet. Momentum pins a weak
reference to the source input element selected at momentum Began. Changed and
Ended never hit-test a replacement target. A missing, detached, disabled or
source-retired target consumes its stale momentum tail without moving another
control. New direct scrolling cancels pending momentum. This follows Apple's
[normal and momentum routing distinction](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/EventOverview/HandlingTouchEvents/HandlingTouchEvents.html).
The installed public AppKit NSEvent.h supplies the phase constants; no native
implementation was copied.

Each gesture has a shared cancellation lease across all its source targets.
Queued commands retain the lease separately from source/provider ownership.
Cancellation retires pending motion before callbacks. End allows already accepted
motion to finish. The normal-to-momentum handoff shares per-viewer fractional
state without sharing phase cancellation: interrupting momentum must not discard
accepted direct motion. Deferred queue execution, rather than a dispatch-time
fraction copy, owns that handoff.

Routing uses weak source/target ownership and viewer-owned session caches. Source
generations and dispatch revisions reject replacement roots and stale outer work
after reentrant hit tests or preview handlers. A failing old callback cannot cancel
a newer nested dispatch. Modal admission resolves the actual source root/owning
window, not a child ScrollViewer as if it were a top-level owner. A full selected
queue throws rather than silently redirecting the overflow to another control.

Eighteen authored source cases now cover the consumer and routing, including
normal retargeting, momentum pinning/retirement, source and gesture cancellation,
unchanged state after invalid phase packets, preview handling/failure, nested
dispatch, owning modal roots, fractional handoff and native hit-provider ownership.
Compilation is not execution evidence. Earlier consumer-only CI passed all nine cases after its
transform fixture published layout before input.

Still required: independent per-axis nested consumption, cross-presentation-source
coordinate transport through popup ancestors, default handling for undeclared
custom scroll providers and legacy-only application handlers, complete source
registrar/factory integration, Forms source input and native UI/package validation
on all supported platforms. Default source handling currently consumes a complete
vector only when one source-owned provider supports it; otherwise it leaves the
event unhandled. It never drops an unsupported component, interprets another
source's coordinates as local, or advertises complete application compatibility.
