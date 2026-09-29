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

`RemainingScroll` exposes the unconsumed vector with the original native sign and
units. Each source-owned ScrollViewer queues its enabled axes and lets the other
components continue bubbling. Point remainders return through the inverse visual
mapping to the original source frame, including rotated/scaled viewers; line
counts remain unscaled. NativeInput is never rewritten. Routed Handled becomes
true only when all motion is claimed (or an application handles it). The host also
recognizes partial consumption so it cannot replay the original complete packet
through a separate fallback. Queue rejection never publishes a changed remainder.

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

Existing cross-source event routes now map point vectors through the originating
root, each source's explicit desktop transform, and the receiving root before
viewer-local consumption. Framebuffer DPI is not a desktop scale. Remainders
return to the original source frame, while line quantities remain unscaled.
Singular/nonfinite mappings reject consumption without publishing a new remainder.
This does not add routes to unrelated windows or bypass Popup's existing event
isolation. Each phase lease weakly retains its originating source generation, so
closing/cancelling/replacing that source also retires commands already accepted by
another source, including after normal End. Receiving source/provider ownership
continues to be checked independently.

Twenty-six authored source cases now cover the consumer and routing, including
normal retargeting, momentum pinning/retirement, source and gesture cancellation,
unchanged state after invalid phase packets, preview handling/failure, nested
dispatch, owning modal roots, fractional handoff, native hit-provider ownership,
independent nested line axes, rotated point remainders, partial queue failure,
cross-source desktop/root mapping independent of raster DPI, unchanged line units,
origin retirement and singular frame rejection. Cross-source cases exercise an
explicit logical route between actual source hosts; native popup UI qualification
remains separate.
Compilation is not execution evidence. Earlier consumer-only CI passed all nine cases after its
transform fixture published layout before input.

Still required: deferred boundary/overscroll chaining, actual popup-route
qualification, default handling for undeclared
custom scroll providers and legacy-only application handlers, complete source
registrar/factory integration, Forms source input and native UI/package validation
on all supported platforms. Unclaimed components remain visible to ancestors and
application handlers; source providers retain their existing edge clamping rather
than forwarding deferred boundary overflow. No complete application compatibility
is claimed.
