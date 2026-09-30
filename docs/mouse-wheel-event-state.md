# Mouse wheel event state

Each `MouseWheelEventArgs` owns its immutable wheel delta. The inherited static
field let constructing any later wheel event overwrite all previously created
events, including an outer preview event while a nested input callback ran.
That could reverse the outer event's direction and change its amount before
preview-to-bubble promotion. Events on different dispatchers shared the same
incorrect storage.

The delta is now a readonly instance field. Constructors, public property shape,
timestamps, device identity, preview/bubble routing, `Handled` and exceptions
remain unchanged. The shared source implementation applies to both portable
and native Windows input; there is no platform-specific substitute or replay.

Five actual source regressions reproduce the old bug: retained instances with
zero, positive, negative and extreme deltas; separate dispatcher instances; and
nested source preview delivery with normal, handled and throwing inner events.
The routed tests verify the outer delta after the nested callback and the exact
bubble sequence. The existing source CI job runs all five using its already-built
assemblies, with explicit minimum counts, no skips and unchanged job deadlines.

All five fail against the original static field and pass with instance storage.
The eight existing source pointer-ownership tests also pass unchanged. These are
headless source checks, not native desktop interaction or platform UI validation.

This fixes event ownership, not precision scrolling. Native Cocoa point/line
conversion, phase ownership, original timestamps/click counts and explicit
source cancellation remain separate required work before enabling its factory.
