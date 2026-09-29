# Native pointer host transport

The Silk input service selects `NativeWindowInput` by actual provider. When the
context supplies native pointer events it subscribes to that stream alone, not
the equivalent Silk mouse callbacks. Keyboard delivery remains separate. Native
double coordinates, timestamp, button/click identity, modifier snapshot, scroll
units and phases cross the host as immutable `PortablePointerInput` data.
Command-to-Control shortcut normalization is separate from the original flags;
pointer delivery never polls a later keyboard state to replace its snapshot.

Coordinate conversion preserves the complete packet. Precise scroll vectors use
the same client-frame scale as pointer positions, without desktop-origin offsets;
wheel-line deltas are never scaled or multiplied by 120. Owned view-local input
does not use the old Cocoa GLFW owner-coordinate heuristic. Native popup routing
does not discard captured drags/up or leave events outside the popup rectangle.
Hide cancellation still reaches the popup's source after visibility is cleared.
Owner-window cancellation is not redirected into an arbitrary overlay popup.

Subscription disposal keeps native cancellation subscribed while the provider
retires. Reentrant disposal is guarded. A throwing source handler cannot prevent
remaining unsubscribe/context-map cleanup; failures still propagate. Actual
source teardown retains the separate source-owned button/capture rules.

The source registrar must explicitly implement `IPortableNativePointerInputService`.
Absent or rejecting capabilities fail; native fields are never discarded into a
legacy wheel event. Legacy constructors, input services and source paths remain
available unchanged. Cancellation/leave without their native packet also fails
instead of reaching a provider that cannot interpret those new event kinds.

The focused host run passes 401 cases: 249 window-host cases, 44 Silk input cases,
15 native transport cases and 93 activation cases. It includes complete popup
routes with typed recording source/native hosts, not real AppKit windows. The
existing fast CI gate now includes these classes and per-class minimum counts.
Activation and popup fixtures share the same test collection because both replace
the process-wide source registry; this does not serialize unrelated test classes.

The canonical LibreWinForms dependency pins the same ProGPU commit as this host.
The existing source-integration identity check remains unchanged; upgrading only
the outer ProGPU pin fails that check. Package staging still requires the entire
producer Build to succeed on the exact shared commit.

## Remaining source and application work

The actual source registrar does not advertise native-pointer capability yet.
The internal [source report path](native-pointer-source-reports.md) now retains
native positions, time, click identity and source generations for movement and
five-button input. Source hide/modal cancellation now retires owned presses and
exact-provider capture before callbacks. Source hover/leave retirement retains
native metadata and original physical-source ownership without cancelling capture.
Precision scrolling with real source scroll metrics and phase ownership remains unfinished.
The owned Cocoa factory is therefore still not selected. Complete
source consumption, callback/queued-dispatch lifetime, Forms integration and real
native popup interaction/visual tests remain required. No automatic modality,
package/UI parity or native capture qualification is claimed by this host bridge.
