# Owned Cocoa popup source input gate

The core application path is Showcase About / `ShowDialog` with a separately
surfaced source popup. The source already enters `PortableModalInputScope`, but
the host registered native input gates only on Windows. Its new owned Cocoa
popup therefore did not receive the source modal policy, including when created
under an already active dialog. This is a source-backed missing connection, not
a reproduced AppKit application result.

`EnsureWindow` now binds only the exact successful `CreateOwnedCocoaWindow`
factory result to its own `SilkWindowController.SetInputAllowed`. That controller
already admits `CocoaPopupNativeWindowPlatform` and rejects ordinary Cocoa
windows. Neither an opaque handle, a platform kind nor the Popup option is
sufficient for this source binding. The existing typed source/provider factory
admission remains required. No ProGPU API or dependency pin changes.

The source registration uses the real owning Window identity and immediately
receives the current scope state. Pre-initialization state is cached, then applied
on Load and before each checked owner-show boundary. The provider combines that
independent gate with current enabled and input-transparency intent. Its existing
typed cancellation invalidates queued tails and releases held state without
synthetic up/clicks; the source adds no keyboard, wheel conversion or event poll.
The popup still drains its own queue under the owner's event pump.

Registration and each native callback retain the same `IWindow` and controller
operation. Changed identity, rejected native input, close/dispose, or Hide during
show admission cannot publish visibility. Initial cancellation cannot leave a
registration published after teardown. Unregistering a disposed/closing host
does not re-enable its view. The binding retains that exact provider until the
existing native retirement coordinator reports completion, including a failed
disposal followed by a creating-thread retry. Rendering/view leases and provider
cancellation retain their existing owners.

## Limits and evidence

This gates only the owned non-key popup's native pointer surface. It does **not**
block its ordinary GLFW Cocoa owner natively, enable `NativeWindowModalSession`
automatically, establish application-wide modality, or admit legacy-only source
input. Ordinary Cocoa/Linux native-gate restrictions, both source-host input and
presentation requirements, focus restoration and complete desktop qualification
remain. In particular, this is not completion of Showcase About modality.

Seventeen new actual `ProGpuWpfWindowHost` source controls are authored with
recorded `IWindow` operations: late/early source binding, immediate/nested policy,
hidden precreation, native rejection/original exception, hide/disposal during
admission, registration reentry, fixed owner/provider identity, getter reentry,
pending native retirement, wrong thread and ordinary/foreign-provider rejection.
They do not create an NSPanel or device. The existing hosted source lifecycle
selector already includes this partial test class; its minimum increases from
304 to 321 without changing the selector, deadlines or failure policy.

Execution and compilation of those tests are pending the actual hosted source
graph. No current exact-head local host/test cache was available, so no stale
assembly graph, native runtime download, native/GPU/VM build or execution was used.

Postcommit offline checks at implementation `ade30aa8b970fe40331ee10dbed74acb536dc6ce`
passed: `git diff HEAD^ --check`, `bash -n eng/progpu-wpf-layout-clip.sh`, Python
syntax of its embedded TRX verifier, and source inventory/wiring guards. The
inventory is seven Facts plus ten InlineData rows, all selected by the existing
host-class filter with minimum 321. These checks do not compile or execute the
17 cases and do not qualify native input or application behavior.

The original #230 collapse defect is separate: typed local visibility already
excludes Hidden/Collapsed before mask input admission in the current dependency.
Visible-mask input remains explicitly unsupported by source hit-only traversal;
this change does not alter that gate.
