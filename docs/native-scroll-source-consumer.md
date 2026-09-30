# Native scroll source consumer

Acceptance target: scrolling content in ShowcaseApp dialogs and dropdowns.
The internal `PortableScrollSession` connects native point/line units to the real
`ScrollViewer` command queue and `IScrollInfo` provider. The internal
[routed source path](native-scroll-routing.md) now connects this consumer; the
registrar does not yet advertise native input or select the Cocoa factory.

`IScrollInfo` does not specify offset units. Source providers explicitly declare
physical or logical units per axis through the public optional `IPortableScrollInfo`
interface, which extends `IScrollInfo`; the consumer
does not infer them from `CanContentScroll`. ScrollContentPresenter, TextBoxView,
FlowDocumentView and DocumentGrid use physical offsets. StackPanel declares its
stacking axis logical. VirtualizingStackPanel uses its actual pixel/item mode.
Applications may implement the same interface. `Pixels` means source DIPs, not
framebuffer pixels; HorizontalItems/VerticalItems declare the actual offset,
extent and viewport units independently. Unknown flags are rejected atomically.
Providers implementing only `IScrollInfo` may receive native line commands, whose
meaning is already defined by their LineLeft/Right/Up/Down methods. Native points
remain rejected for those providers; CanContentScroll does not prove their units.
Capability getters are application code, so admission rechecks source/provider
identity after reading them. The public reference surface includes this contract
and the lossless routed scroll events.

Point vectors use the actual source-to-viewer visual transform, including scale
and mirroring, without applying a desktop translation to the vector. Physical
offsets retain fractions. Logical offsets share WPF's existing measured panning
ratio and rounding, including the partially visible last item; they do not use
a guessed line height. Each session carries fractional residuals with their
original owners. Both axes' current
metrics are checked before queueing and before any provider offset write.

Line deltas remain unscaled, retain fractional remainders and invoke the
provider's LineLeft/Right/Up/Down commands. They are not converted into wheel
notches or fixed point deltas. One line advances per existing layout/command
pass, so a provider can publish its new offsets during layout before the next
line. Remaining lines precede later commands. Reentrant layout from a provider
callback cannot execute the active command again or drain later commands.

The queue now separates unused boundary input from its retained fractional state.
Point commands expose the clamped request overflow in the admitted provider's
offset units, including carried logical fractions; correction of an offset outside
a shrinking extent never creates extra input. Line commands check both axes'
published metrics between layout passes and after the final issued line. At an
edge they retire only the unissued lines and their fractional remainder. A line
which moved partly to the edge is still one provider command: no pixels-per-line
ratio is inferred. Fractional outward input cannot become reverse-scroll debt.
This also applies when an ordinary queued scroll or a layout change reaches the
edge between native packets: retire the old outward fraction before combining
the new motion, for both logical points and lines.
Completed commands cannot execute twice, and cancellation suppresses their unused
result. Invalid line metrics fail before changing existing fractional state.

The direct consumer's `Unconsumed` result retains its existing provider-unit
contract. Routed commands additionally carry an immutable admission frame and a
typed continuation into the original bubble route. Point overflow uses that
packet's frozen inverse visual mapping, inverse source-frame matrix and logical
point scale to return to original-source coordinates and sign. The route captures
its originating root-to-desktop frame before Preview, so later source geometry
cannot replace it during initial or deferred admission. Lines remain native line
quantities, without visual or logical-point scaling.

Fractional storage retains ordered provenance segments per axis rather than one
latest conversion. Opposing movement cancels old fractions in order; item-rounding
debt keeps the packet that caused it. At a boundary, older outward fractions are
returned under their own frames before new inward motion is applied. Unit changes
forward owned fractions through their original point/line continuations, never
relabel them as the new unit. Cancelled or abandoned owners cannot contribute
fractions to newer motion. Overflow coalesces both axes per original packet and
delivers in acceptance order, preserving the first continuation exception and
preventing replay after completion.

Continuation is not a new routed event: the source retains actual default-handler
occurrences and application handling decisions, buffers until successful original
route sealing, and never replays handlers or walks a replacement ancestor tree.
Same-args nested raises cannot own that capture. Each eligible ancestor tail-admits
only residual motion known at that time. Earlier accepted ancestor commands remain
ahead; there is no global ordering or advance reservation across viewer queues.
Full admission fails explicitly rather than skipping that ancestor. Legacy wheel
handling and original routed-handler invocation rules remain unchanged.

The existing bounded queue retains 31 pending commands, plus at most one active
native command. Full native admission fails before changing fractional state.
An ordinary command cannot silently evict an accepted native packet; a required
eviction throws instead. Existing safe ordinary-command coalescence is retained.
Each line packet is bounded to 4096 lines per axis. The direct consumer retains
all-or-nothing axis admission. Routed consumption queues enabled axes and returns
the others in the original source frame, publishing the new remainder only after
queue acceptance. Rotated point remainders use the actual inverse visual mapping;
line counts are not visually scaled.

Each session separately bounds provenance to 64 live packet reservations. A
reservation covers its queued command, retained fractional pieces and pending
overflow until all owners release it; it is not an ancestor queue slot. Capacity
is checked before accepting another owned packet, and cancelled fractional owners
can release it. This bounds retained tiny fractions without silently dropping or
combining their frames. The independent 31-pending/one-active command limit and
4096-lines-per-axis limit remain unchanged.

Sessions retain the exact source generation, viewer and provider. Source/root
retirement, provider replacement, unit/axis enablement changes, modal blocking and explicit
cancellation prevent queued writes. The second axis is rechecked after a first
axis callback so cancellation cannot continue writing through a retired session.
Queue fetch releases the stored session reference; later sessions do not inherit
fractional state.

The original nine source regression cases cover:
fractional transformed points, measured logical points, line/ordinary-command
ordering, source retirement, queue exhaustion, invalid units/metrics/axes, source
provider traits, callback cancellation/atomic validation, and deferred offset
publication with layout reentry. Compilation is not test execution evidence.
Eight additional cases cover deferred physical/logical overflow, final-line layout,
following command order, fractional reversal, changed extents, provider-owned
partial line movement, invalid-metric atomicity, cancellation and completed-command
idempotence. The original suite contains 41 facts. Ten new provenance cases and
14 original-route continuation cases bring the source-counted total to 65; the
hosted gate requires all 65 with the original no-skips policy and 60-second deadline.
The new cases have not been executed locally; authoring and compilation are not
native application or package qualification.

The routed path adds explicit AppKit phase validation, normal/momentum ownership
and lossless routed events with independent nested axes. Still required before
factory admission: native popup cross-source qualification,
legacy-only application handler compatibility, source
registrar/factory integration, Forms integration
and actual native popup/application qualification on every supported platform.
The legacy Windows and portable wheel paths are unchanged; there is no conversion
fallback when the native path is unavailable.
