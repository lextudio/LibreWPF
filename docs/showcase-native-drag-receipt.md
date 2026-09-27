# Showcase native drag delivery receipt

The Linux XWayland-session smoke driver submits 36 native pointer moves and a
left-button release. Its `completed` file acknowledges submission by `xdotool`,
not delivery through GLFW and the WPF input manager. A WPF background dispatcher
checkpoint can run before that final release is polled.

The previous Showcase probe consequently opened the Aero menu while a native
mouse-up outside that menu was still pending. `MenuBase.OnClickThrough` correctly
closed the menu when the release arrived. This was a validation ordering race,
not evidence that Aero popup creation or MessageBox scrolling was broken.

## Delivery contract

Before publishing `ready`, the probe registers handled-events-too preview button
observers on its source Window. Only an observed left-button press followed by
release establishes source delivery. A release without a preceding press is not
accepted, and a subsequent press clears completion. The observer never handles
events, changes focus/capture, opens controls, or injects input.

Both the driver acknowledgment and this source receipt are required within the
existing 600-by-16-ms attempt budget. The existing dispatcher checkpoint follows
delivery, and both handlers are removed in `finally`. CI additionally requires
the source press/release marker before accepting the existing popup/theme results.
The 90-second live deadline, external 36-step gesture, popup assertions and
renderer selection are unchanged.

## Focused evidence

On Ubuntu ARM64 with .NET 10.0.11, Xvfb 1280x1024, simulated Wayland-session
environment and the ManagedPortable renderer:

- The original Showcase source reproduced the Aero failure using packages from
  successful complete [Build 36311226928](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36311226928),
  exact producer `c2e341b7a79a3690f2cb2ed6cd7b9e7a3bd936bb`.
- SDK/transport version was `0.1.0-preview.65`, ProGPU dependencies
  `0.1.0-source.08f4343e`. Output ProGPU.Wpf, PresentationFramework and
  PresentationCore assembly hashes matched the qualified package payloads.
- A temporary console trace observer captured the close stack through
  `GlfwMouse` mouse-up, `PortableWindowActivationService.ProcessMouseInput`,
  `MenuBase.OnClickThrough`, and `MenuItem.ClosePortableTemplatePopup`.
  It did not subscribe to Loaded/Unloaded or change source lifecycle state.
- With only the sample delivery-receipt fix and that stack observer removed,
  the same package baseline passed all seven themes, Menu/ComboBox/direct Popup
  native surfaces, and ownerless Popup open/close/reopen checks.
- The retained invalidation source-contract gate passed 651 cases with zero
  failures/skips, including ten linked receipt tests covering both acknowledgment
  orderings and incomplete/out-of-order gestures.

This standalone package diagnostic isolates sample input ordering. It does not
qualify a new whole-package build, real Wayland composition, NativeMilWgpu,
Windows/macOS popup behavior, or the remaining application/release gates.
