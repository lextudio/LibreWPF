# Native pointer host transport

The Silk input service selects `NativeWindowInput` by actual provider. When the
context supplies native pointer events it subscribes to that stream alone, not
the equivalent Silk mouse callbacks. Keyboard delivery remains separate. Native
double coordinates, timestamp, button/click identity, modifier snapshot, scroll
units and phases cross the host as immutable `PortablePointerInput` data.
The actual provider's scroll-protocol tag travels with them, including through
coordinate copies. Neither the host OS nor recognizable raw phase bits select
AppKit semantics for an untagged provider.
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

The earlier focused host run passed 401 cases: 249 window-host cases, 44 Silk input cases,
15 native transport cases and 93 activation cases. It includes complete popup
routes with typed recording source/native hosts, not real AppKit windows. The
existing fast CI gate now includes these classes and per-class minimum counts.
Activation and popup fixtures share the same test collection because both replace
the process-wide source registry; this does not serialize unrelated test classes.

The canonical LibreWinForms dependency pins the same ProGPU commit as this host.
The existing source-integration identity check remains unchanged; upgrading only
the outer ProGPU pin fails that check. Package staging still requires the entire
producer Build to succeed on the exact shared commit.

That shared revision also repairs System.Drawing's captured clip mapping when a
float determinant or inverse overflows but the relative mapping is representable.
The ordinary float path and rejection of genuinely unrepresentable mappings stay
unchanged. This is a drawing dependency repair, not a change to native pointer or
scroll coordinate policy, and it does not opt the source registrar into native input.

The canonical Forms dependency also guards source pointer continuations across
public hover/focus callbacks. Nested input owns its new hover and capture; a
disposed, hidden, disabled, reparented or recreated recipient cannot receive the
old event. Reopening the same Forms popup object starts a fresh handle-bound hover
lifetime. The fix and its source regressions belong to LibreWinForms, not a WPF
host copy, and do not enable either framework's owned native input factory.

The same canonical dependency transfers physical hover across Forms top-level
windows independently of keyboard focus. Moving between an owner, dropdown and
submenu retires the previous hover before callbacks, reusing canonical item-leave
and timer cancellation. Twelve additional source regressions cover that boundary;
they do not qualify native OS leave, cross-window capture or WPF pointer routing.

Canonical Forms also handles its backend's input-loss notification independently
of whether a nonactivating popup ever held keyboard focus. Capture, pressed state
and hover retire before public callbacks; per-button window ownership protects
another window's input. New pointer/focus generations survive old cleanup, while
throwing callbacks cannot roll back a still-current focus loss. This remains the
existing Forms input contract, with fifteen authored source regression cases;
it does not opt either source registrar into the native pointer provider.

The aligned dependencies also carry Forms' explicit pointer Leave/Cancel
boundaries. Leave retires exact-window hover while preserving capture and held
buttons; Cancel retires only that window's capture, presses and hover without
keyboard focus loss or synthetic up/click delivery. Nested input and another
window's held-button ownership remain authoritative.

ProGPU's native popup owner can now be assigned after hidden panel creation,
without replacing the rendering view. Its provider-aware retirement API reports
pending callback/view ownership. Forms' actual dispatcher retains the native
window and failed renderer-cleanup owner until both retire, hides separately,
and never destroys the native surface after renderer cleanup fails. Retries run
after existing polling/callbacks; dispatcher shutdown cannot abandon a pending
owner. Both superproject pins select the same immutable ProGPU revision. These
dependency changes do not select either framework's unfinished native factory,
change scroll compatibility or establish native modal/UI qualification.

## Remaining source and application work

The actual source registrar does not advertise native-pointer capability yet.
The host now has an owned Cocoa factory connection gated by a bound portable
source, that explicit registrar capability, and the shared owner's actual Cocoa
identity. Ordinary windows and non-Cocoa owners retain their existing factory.
Once the owned path is admitted, a missing owner or creation failure propagates;
it cannot silently create an ordinary NSWindow. The same shared-device and
surface-before-window teardown paths remain in use. Seven authored factory cases
raise the host-class CI minimum to 256; no native panel or UI qualification is
claimed. Because the real registrar has not opted in, this connection does not
yet select the owned factory in applications.
The internal [source report path](native-pointer-source-reports.md) now retains
native positions, time, click identity and source generations for movement and
five-button input. Source hide/modal cancellation now retires owned presses and
exact-provider capture before callbacks. Source hover/leave retirement retains
native metadata and original physical-source ownership without cancelling capture.
The [source scroll consumer](native-scroll-source-consumer.md) preserves point/line
units through real source metrics and the command queue. Its internal
[routed path](native-scroll-routing.md) now validates declared AppKit phases and
retains momentum targets, generations and fractional state. Independent nested
axes now route through source-frame remainders, and existing cross-source routes
use explicit desktop/root transforms. Custom providers can declare their point
units publicly; ordinary IScrollInfo still supports native line commands. Deferred
boundary chaining, legacy-only handlers, actual popup-route qualification and source
admission remain unfinished.
The owned Cocoa factory is therefore still not selected. Complete
source consumption, callback/queued-dispatch lifetime, Forms integration and real
native popup interaction/visual tests remain required. No automatic modality,
package/UI parity or native capture qualification is claimed by this host bridge.
