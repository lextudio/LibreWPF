# Showcase theme menu failure diagnostics

Build `36288813259` at `ba7dcd7cd` failed the Linux Wayland-session/XWayland
Showcase probe after native input and dispatcher readiness. The first Aero File
menu attempt exhausted its original popup readiness gate with zero open, visible,
native and presented popups. The log did not identify a source close, template
replacement, unloaded item, focus change or capture change. No product cause has
been established and this change does not reopen the menu or repair that failure.

Each existing live theme attempt now records a source-thread, in-memory journal:
64 entries of at most 768 characters, timestamped with `Stopwatch`. It observes
the original File `MenuItem`, at most four encountered template `Popup` objects,
Loaded/Unloaded, submenu and popup open/close, owner activation, routed keyboard
focus and mouse capture, plus read-only state around the existing theme actions.
Object hash labels distinguish diagnostic references only; they are not native
window identities. Overflow is explicit and stops further diagnostic reads.

Successful attempts emit nothing and detach in their existing close callback.
Failure reporting uses a source-dispatcher callback with only a native-loop wake,
then detaches and rethrows the original error; a diagnostic failure cannot replace
it. Owner closure also detaches. No observer sets input state, handles an event,
applies a template, repeats an open, or changes layout/render policy. The original
400 attempts, delays, popup assertions and enclosing process deadline remain.
The passive-idle fixture does not install this observer.

Seven source/journal controls are included in the existing strict source gate.
All seven passed locally with the actual checked-in journal and test source. An
isolated byte-identical full Showcase C# compile against retained canonical
assemblies and the unchanged, hash-matching generated XAML completed with zero
warnings/errors. This is a compile check, not a rebuilt package or native run.
The fresh worktree's docs verifier requires its absent ProGPU submodule; the
complete docs/package/build gates remain for CI. These checks are not a menu fix
or idle/rendering qualification. The failed
whole Build remains failed: x64 native idle passed, ARM64 exited `0xC0000409`
after `native-resize-request`, without an admitted WER dump. ARM64 diagnostics are
tracked separately; packages from this failed Build are not qualified.
