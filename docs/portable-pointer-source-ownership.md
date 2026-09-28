# Portable pointer press ownership

A portable mouse previously retained only a global pressed/released value for
each button. If a source was disposed without another mouse-up, its pressed value
survived indefinitely. Capture cancellation alone did not change this cache.

Each retained press now records both the source that delivered the report and
the source selected by element-capture routing. Detaching/replacing a source root
or disposing either source releases only the presses still owned by that source.
Replacing a press with a later one from another source transfers ownership; late
teardown of the former source cannot clear the new press. Source identities are
the actual presentation objects, not portable handles or native window pointers.

This cleanup publishes no mouse-up, click or movement. The existing exact-provider
capture-cancellation path remains independent: disposing a provider releases its
own capture, never another provider's capture. A source change during ordinary
pointer movement still retains presses and capture. Assigning the same root is
also a no-op. A real mouse-up remains authoritative across source boundaries,
including an up with no locally observed down.

The device rejects invalid buttons/states, cross-thread use and disposed sources
before replacing live press ownership. Cleanup is source-thread-bound and uses a
bounded scan of the five supported mouse buttons with no scratch allocations.
It runs before source detachment can invoke callbacks, including when a mouse-down
handler itself disposes its source. Native Windows mouse-state queries are unchanged.

`eng/progpu-wpf-pointer-ownership-source.sh` exercises five actual routed/source
cases and three device cases. It reuses the original source assemblies already
built by the existing CI job, requires all eight tests and rejects skips.

This closes teardown ownership, not the remaining native input transport. Cocoa
hide/modal-policy cancellation must still reach the live source explicitly;
native leave events, precision scrolling, timestamps and popup factory admission
remain separate work. No native OS capture or application rendering is qualified
by these source-only tests.
