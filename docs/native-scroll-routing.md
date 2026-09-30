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

Deferred boundary overflow now continues through occurrences of the real
ScrollViewer default handler retained from the original bubble's `EventRoute`.
An internal typed route observer captures those occurrences in their source route
order, including defaults skipped because an earlier default claimed the packet.
It does not reconstruct visual ancestors, perform another hit test, retain a pooled
EventRoute, or raise the original event again. Existing branch-adjusted `Source`
and Popup route isolation remain authoritative; vector coordinates still belong
to the original presentation source, not the current branch's event `Source`.

Application handling remains separate from provisional default claims. The source
observes every `Handled` assignment, including assigning `true` when it was already
true. An application claim suppresses later default candidates; clearing it can
admit later candidates without rewriting earlier route decisions. Original handler
invocation rules are unchanged, and continuation never replays application handlers.
The exact original bubble and route nesting depth own capture: a nested raise of
the same argument object at a peer, including reentry during route construction,
cannot claim or append to that original continuation.

Reentrant layout may complete a child command while the bubble is still running.
Its residual is buffered until the original route seals successfully. A failed or
abandoned route cannot publish its pending continuation or execute its remaining
owned commands; already performed provider writes are not rolled back. This
ownership is distinct from dispatch revision: later ordinary input cannot cancel
accepted work. Source/provider identity, originating generation, gesture lifetime
and modal admission still apply at continuation time.

Only known residual motion is offered to later eligible captured candidates. It
joins each candidate's existing bounded queue tail, behind commands already
accepted there. There are no ancestor-slot reservations and no global order across
independent viewer queues. A full selected ancestor queue throws; it cannot be
silently skipped to redirect input or evict accepted work.

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
after reentrant hit tests, preview handlers, or application-owned scroll capability,
metric and transform reads. Recheck the dispatch after those reads, immediately
before queue or session-cache publication. This admission guard never retires
previously accepted commands on ordinary later input; their gesture leases remain
authoritative. A stale dispatch is host-handled without consuming its remainder,
so host fallback cannot replay it. Nested input from an independent source may
remain valid, but cannot replace a newer session installed in a shared routed
viewer; its accepted command retains its own source lease. A failing old callback cannot cancel
a newer nested dispatch. Modal admission resolves the actual source root/owning
window, not a child ScrollViewer as if it were a top-level owner. A full selected
queue throws rather than silently redirecting the overflow to another control.

Existing cross-source event routes now map point vectors through the originating
root, each source's explicit desktop transform, and the receiving root before
viewer-local consumption. Framebuffer DPI is not a desktop scale. Remainders
return to the original source frame, while line quantities remain unscaled.
The originating root-to-desktop matrix is captured before Preview; both initial
admission and deferred continuation use that same snapshot, even when the origin
and receiving source are the same object. Each receiving source/viewer mapping is
read at its actual admission. The admitted inverse and logical point scale are
frozen with that packet, so later geometry changes cannot reinterpret its overflow.
Singular/nonfinite mappings reject consumption without publishing a new remainder.
Affine vectors use matrix-vector multiplication, not subtraction of translated
positions; even large desktop origins cannot erase small accepted scroll motion.
Non-affine projections retain their actual endpoint mapping.
This does not add routes to unrelated windows or bypass Popup's existing event
isolation. Each phase lease weakly retains its originating source generation, so
closing/cancelling/replacing that source also retires commands already accepted by
another source, including after normal End. Receiving source/provider ownership
continues to be checked independently.

The original 41 source cases cover the consumer and routing, including
normal retargeting, momentum pinning/retirement, source and gesture cancellation,
unchanged state after invalid phase packets, preview handling/failure, nested
dispatch, owning modal roots, fractional handoff, native hit-provider ownership,
independent nested line axes, rotated point remainders, partial queue failure,
cross-source desktop/root mapping independent of raster DPI, unchanged line units,
origin retirement, singular frame rejection, undeclared custom line providers and
reentrant capability admission. Cross-source cases exercise an
explicit logical route between actual source hosts; native popup UI qualification
remains separate.
The consumer records execution-time point overflow and unissued boundary lines,
waits for final-line layout, and prevents fractional reverse-scroll debt. Ten new
provenance cases cover frozen frames, opposing fractions, rounding debt, unit
changes, cancellation, original owner order and bounded storage. Fourteen new
route cases cover handler ownership, same-args reentry, seal/abandon behavior,
accepted-command lifetime, ancestor queue order/capacity and cross-source frames.
Source counting gives 65 facts; the source gate requires all 65 with no skips and
the unchanged 60-second deadline. These new cases are authored, not local execution
evidence. Earlier consumer-only CI passed nine historical cases after its transform
fixture published layout before input; that is not qualification of this change.

Still required: actual popup-route qualification, legacy-only application handler
policy, complete source
registrar/factory integration, Forms source input and native UI/package validation
on all supported platforms. Unclaimed components remain visible to ancestors and
application handlers; deferred motion uses only the retained eligible source route.
No factory is enabled by this connection, no native scroll packet falls back to
legacy MouseWheel, and no complete application compatibility is claimed.
