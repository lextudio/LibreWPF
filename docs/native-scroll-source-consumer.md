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
a guessed line height. Each session carries fractional residuals, resets them
when units change, and discards boundary overscroll debt. Both axes' current
metrics are checked before queueing and before any provider offset write.

Line deltas remain unscaled, retain fractional remainders and invoke the
provider's LineLeft/Right/Up/Down commands. They are not converted into wheel
notches or fixed point deltas. One line advances per existing layout/command
pass, so a provider can publish its new offsets during layout before the next
line. Remaining lines precede later commands. Reentrant layout from a provider
callback cannot execute the active command again or drain later commands.

The existing bounded queue retains 31 pending commands, plus at most one active
native command. Full native admission fails before changing fractional state.
An ordinary command cannot silently evict an accepted native packet; a required
eviction throws instead. Existing safe ordinary-command coalescence is retained.
Each line packet is bounded to 4096 lines per axis. The direct consumer retains
all-or-nothing axis admission. Routed consumption queues enabled axes and returns
the others in the original source frame, publishing the new remainder only after
queue acceptance. Rotated point remainders use the actual inverse visual mapping;
line counts are not visually scaled.

Sessions retain the exact source generation, viewer and provider. Source/root
retirement, provider replacement, unit/axis enablement changes, modal blocking and explicit
cancellation prevent queued writes. The second axis is rechecked after a first
axis callback so cancellation cannot continue writing through a retired session.
Queue fetch releases the stored session reference; later sessions do not inherit
fractional state.

Nine source regression cases are authored for the existing automatic CI gate:
fractional transformed points, measured logical points, line/ordinary-command
ordering, source retirement, queue exhaustion, invalid units/metrics/axes, source
provider traits, callback cancellation/atomic validation, and deferred offset
publication with layout reentry. Compilation is not test execution evidence.

The routed path adds explicit AppKit phase validation, normal/momentum ownership
and lossless routed events with independent nested axes. Still required before
factory admission: deferred boundary chaining, native popup cross-source qualification,
legacy-only application handler compatibility, source
registrar/factory integration, Forms integration
and actual native popup/application qualification on every supported platform.
The legacy Windows and portable wheel paths are unchanged; there is no conversion
fallback when the native path is unavailable.
