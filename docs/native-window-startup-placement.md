# Native window startup placement

The source-built `Window` exports `WindowStartupLocation` through the typed
`PortableWindowState` snapshot. `WpfPortableWindowActivation` applies it after
the portable host's initial client size and owner have been synchronized, before
the first native show. Later `Hide`/`Show` cycles preserve the user's moved
position. `Manual` remains controlled by source `Left`/`Top`; maximized and
minimized startup states take precedence over centering.

The shared ProGPU `PortableWindowStartupPlacement` contract performs the
work-area center and owner-center clamp in desktop coordinates. The WPF host
selects the owner's monitor using ProGPU's existing nearest/overlap monitor
selection, or the primary monitor for an unowned window. The source and native
host use the same unscaled desktop origins: framebuffer DPI is not multiplied
into these positions. An unavailable monitor inventory or unresolved owner does
not fabricate a placement.

This implements the primary-monitor route for the top-left startup behavior
seen in `ProGPU.Wpf.TextLayoutParityApp`, and routes
`ProGPU.Wpf.ShowcaseApp`'s owned About window through `CenterOwner`.
Neither application path is qualified by compilation alone.
It is not yet exact native-WPF placement parity:

- Native WPF selects the mouse monitor for unowned `CenterScreen`; the current
  portable monitor interface has no global pointer-screen query. It uses the
  primary monitor until a typed cross-platform capability supplies that point.
- Host width/height currently describe the client rather than the complete
  decorated top-level frame. Native frame extents and size-to-content changes
  need a post-layout/pre-show placement contract before exact pixel comparison.
- Wayland may reject global desktop positioning. Such a rejection must remain
  visible in platform qualification, not be counted as a centered window.

Compilation gate: build `external/ProGPU/src/ProGPU.Wpf.Interop` and
`src/ProGPU.Wpf`. Focused math and monitor-selection regression cases are
authored in the ProGPU and LibreWPF test projects. Run those, then compare the
Showcase and text-layout windows against Windows WPF in the final integrated
qualification phase defined by `native-mil-core-delivery.md`.
