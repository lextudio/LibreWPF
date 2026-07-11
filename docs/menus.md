# LibreWPF portable menus / popups — status and plan

This document tracks what's known about `Menu`/`MenuItem`/`Popup`/`ContextMenu` behavior on
the portable (non-Windows) backend, what's been fixed, and what's still broken. Written after
the first end-to-end pass at getting OpenDevelop's Help/File menus to actually respond to
clicks on macOS.

## Current status (verified working on macOS/OpenDevelop)

Menus are functional end-to-end: opening a top-level menu, hovering/clicking dropdown items,
leaf-item commands firing (File ▸ Exit), sibling top-level switching by **click**, and sibling
top-level switching by **hover** (open a menu, glide across the bar — the dropdown follows).
Cascading submenus (nested flyouts) stay open beside their parent; popups don't accumulate or
leak.

The fixes that got here, in the order the root causes were peeled back (details in the sections
below):

1. **Popup native windows never fed input into WPF** → wire `host.InputReceived` and route it
   against the popup's own `PresentationSource` (`WpfPortablePopupActivation` + `ProcessInputForSource`).
2. **Leaf-item clicks never executed** → the deferred `Render`-priority `Click`/command op wasn't
   pumped once the popup tore down; flush the dispatcher on popup `MouseUp` (`FlushSourceDispatcherOperations`).
3. **Re-entrancy abort + leaked "closed" popups** → `ProGpuWpfWindowHost.Dispose` must *defer*
   native window teardown while `_isRendering` (not just while `s_pumpDepth > 0`), and guard the
   popup input handler against disposal mid-dispatch.
4. **Cascading submenus evicted each other** → only top-level menu popups share the evicting
   portable window slot (`Role == TopLevelHeader`); nested ones get their own window.
5. **Hover-switch across top-level headers** → the real blocker was cross-window `Mouse.Capture`
   dying (which flipped `IsMenuMode` off and desynced logical menu state from the visible popup).
   Two parts: (a) `PortablePresentationSource.PortableMouseInputProvider.NotifyDeactivate` no longer
   drops mouse capture on a mere input-source switch (only real `Dispose` does), so a Menu's
   `SubTree` capture survives the cursor crossing into its popup window — the portable stand-in for
   Win32 cross-HWND `SetCapture`; and (b) `MenuItem` opens its submenu on hover whenever a sibling
   top-level menu is already open (`CurrentSibling.IsSubmenuOpen`), reusing WPF's own
   `OpenHierarchy`/`CurrentSelection` switch, driven by the main window's (correct) hover events.
6. **Modal `Window.ShowDialog` crashed on close, and a follow-up attempt at fixing it froze the
   entire app at startup** → see "Modal dialogs and the infinite-recursion trap" below. The fix
   ended up narrowly scoped to `Window`'s own dialog wait loop via a brand-new, dedicated
   `Dispatcher.PortableSynchronousPumpRequested` hook — deliberately kept separate from the
   existing, high-frequency `PortableProcessingRequested` hook used for routine dispatcher-queue
   wakeups, because conflating the two is exactly what caused the freeze.

**Key architectural lesson (validated the hard way):** the coordinate-redirect approach
(`TryRedirectToCaptureSource` — translate a popup-local point into the captured main window's
space) was the wrong model for this backend and has been **retired**. Both the main window *and*
each popup receive every mouse move on macOS, the main window's copies already carry correct
coordinates, and a popup can't hit-test menu content that lives in the main window's source anyway.
The working model instead: **let each window hit-test its own content, keep capture alive across
source switches, and drive menu behaviors off durable logical state** — which is much closer to
how Win32 WPF actually behaves. See "Fixed this session" → the hover-switch and capture sections.

## Diagnostics (kept, env-gated)

Set `LIBREWPF_MENU_INPUT_LOG=1` before launching to append menu/popup diagnostics to
`/tmp/librewpf-menu-input.log`. A lean, low-noise set is intentionally left in the code for future
troubleshooting (the noisy per-mouse-move and one-time stack-trace probes used during this
investigation were removed):

- `MENUCAPTURE` (`MenuBase`): `IsMenuMode` true/false transitions and `OnLostMouseCapture`
  decisions — the "why did the menu close / stay open" signal.
- `MENUCLICK` (`MenuItem`): `ClickItem` and `InvokeClickAfterRender` — the "did the click reach
  command execution" signal (if `ClickItem` fires but `InvokeClickAfterRender` never does, look at
  dispatcher pumping; if `ClickItem` never fires, look at hit-testing/capture).
- `POPUPLIFECYCLE` (`Popup`): `CreateWindow` / `HideWindow` / `DestroyWindowImpl`.
- `POPUPACTIVATION` (`WpfPortablePopupActivation`): shared-slot create/evict/dispose.
- `HOSTLIFECYCLE` (`ProGpuWpfWindowHost`): the native-window disposal defer/immediate decision
  (the `_isRendering`/`s_pumpDepth` path behind the leaked-popup bug).

## Modal dialogs and the infinite-recursion trap

Not a menu bug, but found and fixed in the same investigation, in the same portable-dispatcher
territory, and worth recording here for the next person who touches this code.

**Symptom #1:** `Window.ShowDialog()` (e.g. Tools ▸ Options) would let the dialog open, but
clicking Cancel/OK threw `InvalidOperationException: DialogResult can be set only after Window is
created and shown as dialog` — even though the dialog was still visibly open on screen.

**Root cause:** `Window.ShowHelper`'s modal wait — `Dispatcher.PushFrame(_dispatcherFrame)`, no
timeout, meant to block until the dialog closes — resolves, on the portable backend, to
`Dispatcher.PushManagedFrameImpl`'s `while (frame.Continue || HasPendingManagedOperation())` loop.
That loop **broke out the instant its own operation queue was momentarily empty**, regardless of
`frame.Continue` still being `true`. On real Windows this never happens: the equivalent loop calls
`GetMessage`, which *blocks* waiting for the OS's next message rather than giving up when idle.
The portable loop has no such OS queue to block on, so a dialog's modal wait returned almost
immediately after opening — clearing `_showingAsDialog` while the dialog was still visibly open,
so a later real click hit the `else` branch of `DialogResult`'s setter and threw.

**First fix attempt — wrong, and worse: froze the entire app at startup.** The fix seemed
straightforward: when `PushManagedFrameImpl`'s loop is idle but `frame.Continue` is still true,
pump one native event tick (via the *existing* `Dispatcher.PortableProcessingRequested` event /
`requestDispatcherProcessing` callback, upgraded from a fire-and-forget wake into a synchronous
`ProGpuWpfWindowHost.PumpOnce()`) and keep waiting. This is single-threaded, so nothing else would
ever pump native input for a blocked dialog otherwise — the reasoning was sound. **The
implementation was not**: `PortableProcessingRequested` isn't specific to modal dialogs — it fires
on *every* dispatcher operation posted, including the routine, high-frequency per-tick
`FlushDispatcherOperations` calls (`WpfPortableWindowActivation.OnHostUpdateTick`, an 8ms-timeout
background housekeeping flush that runs continuously, not just during dialogs). Making that
callback synchronously pump created unconditional infinite reentrant recursion:
`PumpAllActiveHosts()` → `OnUpdate`/`UpdateTick` → `OnHostUpdateTick` → `FlushDispatcherOperations`
→ (queue momentarily empty) → `PumpOnce()` → `PumpAllActiveHosts()` → ... forever, on the very
first tick after startup. The UI thread was pegged in this recursive loop before the workbench
ever finished laying itself out — which is exactly why the symptom looked like "OpenDevelop's
default layout doesn't fully render": the app wasn't broken mid-render, it never got past the
first render tick at all. Confirmed via `dotnet-stack report -p <pid>` (pipe through `tr '\r'
'\n'`, same gotcha as in the ProGPU sessions — the tool's live-updating output is otherwise
CR-mangled into a single unreadable line) showing the exact repeating four-frame cycle above.

**Real fix: two completely separate hooks, not one hook doing double duty.** The bounded,
timeout-guarded `FlushDispatcherOperations` calls were never actually broken by the *original*
"give up when idle" behavior — they have their own `DispatcherTimer` safety net that forces
`frame.Continue = false` after the timeout regardless, so returning early when idle was harmless
for them. Only the **unbounded** `Window.ShowDialog` wait (`ShowHelper`'s direct, no-timeout
`Dispatcher.PushFrame(_dispatcherFrame)` call) was actually broken by it. So:

- `Dispatcher.PushManagedFrameImpl` was **reverted to its original, unmodified form** — the
  generic pump loop used by every caller no longer pumps natively on idle at all.
- A **new, separate, narrowly-named** static event was added: `Dispatcher
  .PortableSynchronousPumpRequested` / `RequestPortableSynchronousPump()` — deliberately not the
  same event as `PortableProcessingRequested`, so routine dispatcher-operation posting can never
  reach it.
- `Window.ShowHelper`'s dialog branch now (portable only; real Windows keeps the single original
  `PushFrame` call) loops: `PushFrame` → if the frame didn't actually close (`_dispatcherFrame`
  still non-null and `.Continue`), call `Dispatcher.RequestPortableSynchronousPump()` → `PushFrame`
  again. Only this one call site ever triggers a synchronous pump.
- The bridge from this WindowsBase-side event to `ProGpuWpfWindowHost.PumpOnce()` needed a **new
  interop callback**, `PortableWindowActivationCallbacks.RequestSynchronousPump` (added as an
  optional, backward-compatible trailing constructor parameter in
  `external/ProGPU/src/ProGPU.Wpf.Interop`) — `ProGPU.Wpf` has no reference to the real
  `WindowsBase`/`Dispatcher` types at all (confirmed by a `CS0234` build error when first tried
  subscribing directly), so it can only ever be reached through the existing
  callback-bundle-over-the-interop-boundary pattern, the same shape `requestDispatcherProcessing`
  already uses. This is a case where extending the interop contract was the *correct* choice, not
  a shortcut — see `docs technotes` for the general repack-blast-radius note.

**Lesson, stated plainly for next time:** when a "pump native events while waiting" fix is needed
for exactly one unbounded wait, wire a *brand-new, single-purpose* hook for it. Do not extend an
existing high-frequency hook to sometimes behave differently depending on who's listening —
`Dispatcher` has no way to know it's being called from `Window.ShowDialog` specifically versus
routine per-tick housekeeping, and "make the shared callback do more" is how a targeted fix turns
into an unconditional infinite loop that hangs the entire app before it even finishes booting.

## Why none of these bugs exist on real Windows WPF

Every bug fixed or found this session has the same shape: "a behavior Win32 gives WPF for free
isn't implemented, or is implemented with the wrong scope, on the portable backend." Before
planning the cross-platform architecture, it's worth being precise about *which* OS-level
guarantees WPF is quietly standing on, because the fix isn't "port each behavior individually" —
it's "recognize these all come from ONE structural property of Win32 and build the portable
equivalent of that property once."

**1. One thread, one message queue, for every window the thread owns.** A Win32 UI thread calls
`GetMessage`/`DispatchMessage` in a loop. That single loop services **every HWND created by that
thread** — the main window, every open menu popup, every tooltip — not one loop per window.
`Dispatcher.Run()` *is* that loop; `Dispatcher.BeginInvoke` posts into the same queue a window
procedure reads from. This is why a menu item's deferred `Click` (queued at
`DispatcherPriority.Render` from code running inside the *popup* HWND's `WM_LBUTTONUP`) "just
works" on Windows: posting from the popup's window procedure and draining from the main window's
next paint cycle are **the same queue**, not two queues that need to be manually bridged.

**2. `SetCapture`/`ReleaseCapture` is a system-wide input redirect, not a per-window flag.** Once
some HWND calls `SetCapture`, the OS delivers *every* subsequent mouse message to that HWND —
including messages that occurred physically over a completely different HWND — with coordinates
pre-translated into the capturing HWND's client space. `MenuBase.IsMenuMode` relies on exactly
this: `Mouse.Capture(this, CaptureMode.SubTree)` is all it takes for hovering over a different
popup's screen region to still register on the menu tree. There's no per-call "which window did
this physically arrive on, and do I need to redirect it" logic anywhere in `MenuItem`/`MenuBase`
— Windows already redirected it before WPF ever saw the message.

**3. `ShowWindow`/`DestroyWindow`/`SetWindowPos` are just messages too — safe from anywhere.**
Calling `DestroyWindow(hwndA)` from inside `hwndB`'s window procedure is completely ordinary; it
posts `WM_DESTROY` into the same one queue above and returns immediately, no different from any
other cross-window call. There is no "reentrancy hazard to guard against" on Windows for this —
the whole architecture assumes any window can be told to do anything by any code running on that
thread, at any time, because it always funnels through the one queue.

**4. `WS_EX_NOACTIVATE` + `WS_EX_TOPMOST` + an explicit owner HWND give popup/owner z-order and
non-stealing-activation for free**, letting an arbitrary number of independent, simultaneously
visible popups (cascading submenus) exist without any single one of them needing to coordinate
with, evict, or share state with the others. Each cascading flyout is *just another HwndSource* —
WPF doesn't special-case "how many popups deep am I" at the windowing layer at all. The only
thing that closes a sibling top-level dropdown (File → Edit) is `MenuBase`'s own **logical** state
(`CurrentSelection.IsSubmenuOpen = false`), which drives an ordinary `Popup.IsOpen = false` on the
old header — the same code path used to close *any* popup for *any* reason. There is no
Windows-side notion of "shared popup window slot" anywhere in this story — **that concept exists
in this fork only**, invented to route around bugs 1–3 below, and should very likely be deleted
rather than refined further. See "Cross-platform architecture plan" below.

On the portable backend (macOS via Silk.NET/GLFW through ProGPU.Wpf), none of properties 1–4 are
free:

- Each `Popup` gets its own native `ProGpuWpfWindowHost` (`WpfPortablePopupActivation.TryCreate`,
  `src/ProGPU.Wpf/WpfPortablePopupActivation.cs`), mirroring the *shape* of HwndSource-per-Popup,
  but none of the plumbing that makes cross-window input/capture/lifetime work (properties 1–3
  above) comes with it — it has to be built by hand, one call site at a time, which is exactly how
  this fork ended up with three separate bugs (below) instead of one shared root cause.
- Every native window pumps its *own* GLFW/Silk event loop (property 1 does not hold). There is
  exactly one WPF `Dispatcher`/`InputManager` shared by the whole app, but multiple independent
  native loops feed it input, and nothing currently makes those loops behave like one queue.
- Cross-window capture (property 2): instead of redirecting coordinates (an early attempt,
  `TryRedirectToCaptureSource`, now retired — see "Current status"), the fix keeps the captured
  element's `Mouse.Capture` alive as the input source switches between the main window and popup
  windows (`PortableMouseInputProvider.NotifyDeactivate` no longer releases it), so WPF's own
  `SubTree` capture spans both sources via the logical tree, as on Win32.
- Cross-window native operations (Hide/Dispose, property 3) are called directly and
  synchronously from whatever callback happens to trigger them, which is exactly what caused the
  fix-#3 re-entrancy crash below — Silk/GLFW callbacks don't have Win32's "it's just a message,
  handle it whenever" safety net. Mitigated by deferring native window teardown while a pump/render
  is on the stack (`s_pendingNativeWindowDisposals`, gated on `s_pumpDepth`/`_isRendering`).
- There is no owner-chain/`NOACTIVATE`-equivalent z-order primitive in use yet, so property 4 is
  worked around with the shared-window eviction slot (`WpfPortablePopupActivation`'s
  `s_currentSharedMenuOccupant`), now scoped to top-level menu popups only so cascading submenus
  each get their own window.

## Cross-platform architecture plan

The four fixes below (sessions to date) were each landed as a *local* patch at the specific call
site a bug was observed: input forwarding added to one class, a dispatcher flush added to one
method, a disposal guard added to one handler, a sharing flag scoped by one enum check. That's
the wrong shape going forward — every one of properties 1–4 above is a general property of "how
native windows and the WPF dispatcher relate to each other," needed identically by `Menu`,
`ContextMenu`, `ComboBox`'s dropdown, and any future `Popup`-hosted control (a custom flyout, a
color picker, anything). Patching each control class individually every time a new one needs
popups is how this fork got three bugs where Windows has zero — the goal is **one shared
component that gives every `Popup`-derived native window properties 1–4, in one place**, so
control-level code (`MenuItem`, `ComboBox`, ...) never has to know or care whether it's running on
Windows or portable.

### Should the coordinator literally emulate Win32 messages? Yes, in shape — not in surface area

Properties 1–3 above ("one queue," "capture is a global redirect, not a per-call check,"
"lifecycle ops are safe from anywhere") are not incidental Win32 trivia — they are precisely what
a message-queue-plus-capture-manager primitive gives you, and this fork already conceptually
assumes that shape everywhere without formalizing it:
`PortableInputEventKind`/`WpfInputEventKind` are already de-facto `WM_*` enums; `Mouse.Captured`
already wants one global answer; `TryRedirectToCaptureSource` is already a hand-rolled, per-call-
site reimplementation of "deliver to whoever holds capture." **Making that shape literal — a real
`PortableMessageQueue` (`Post`/`Send`/`Dispatch`, FIFO, single consumer) and a real
`PortableCaptureManager` (`SetCapture(handle)`/`ReleaseCapture()`/`GetCapture()`, one source of
truth) — is less code than the four bespoke mechanisms built this session, because all four of
them collapse into "call the same two primitives Windows already validated for decades."**

What this is **not**: an attempt to run unmodified `HwndSource.cs`/`Popup.cs` Win32 code paths
against a fake `user32`. Those files carry substantial Windows-only surface beyond pure message
dispatch — DPI virtualization, `UpdateLayeredWindow`/layered-window transparency, IME window
association, UIA's `WM_GETOBJECT` hookup — none of which has a meaningful portable equivalent and
all of which would need stubbing regardless of how faithful the message queue is. The payoff is
in the **transport shape**, not in reusing those files' bodies. Concretely, this does *not*
change where the seam is (`PortableWindowActivationService`/`PortablePopupActivationService`
stay the boundary; `Popup.cs`'s `!OperatingSystem.IsWindows()` branches stay) — it changes what
those portable-side implementations are built *out of*.

Minimal message set worth emulating (a reshaping of what `PortableInputEventKind`/
`ProcessInput`'s switch already handles today, not new invention): mouse
move/down/up/wheel per button, a capture-changed notification, activate/deactivate, show/hide,
close/destroy, key down/up, char input. Naming them `WM_*`-alike (vs. plain descriptive C# names)
is a style choice, not a technical requirement — the value is the queue-of-messages shape and
global capture state, not Win32 nomenclature — but it does make "what does real WPF do for this
message" trivially greppable in upstream WPF source when porting more Windows-only files later,
which is worth weighing given how often that question has come up this session.

### Correction: most of properties 1–2 already exist — audit before building anything new

Before designing a new coordinator, it's worth being precise about what's *already there*, so
"开工" (start work) means extending real infrastructure, not duplicating it:

- **Property 1 (unified pump) is already substantially implemented.**
  `ProGpuWpfWindowHost.Run()` doesn't just pump its own native window — `PumpAllActiveHosts()`
  iterates a static `s_activeHosts` list (every live `ProGpuWpfWindowHost`, main window *and*
  every popup) and ticks each one's `DoEvents()` every loop iteration, tracked by a static
  `s_pumpDepth` counter. This is a real, working "one loop drains every window this process
  owns" primitive — it already exists, it's just not named/documented as the property-1 primitive
  it is.
- **Property 3 is partially implemented.** `ProGpuWpfWindowHost.Dispose()` already defers the
  *native* Silk `IWindow.Dispose()` call to a static `s_pendingNativeWindowDisposals` list when
  `s_pumpDepth > 0` (i.e., when disposal happens while some host's `DoEvents` is on the call
  stack), drained by `DrainPendingNativeWindowDisposals()` right after `PumpAllActiveHosts()`
  returns each loop iteration. What is *not* deferred: `_isDisposed = true` and
  `DisposeOwnedRenderScheduler()` both happen synchronously and immediately inside `Dispose()`.
  That turned out to be *correct*, not a gap — `OnPlatformInputReceived`/`OnHostInputReceived`
  both check `_isDisposed` at entry, so a *new* callback for an already-disposed host is already
  safely short-circuited. The fix-#3 crash was from a narrower case (a render call made from
  *inside* the very `InputReceived` invocation that triggered the disposal, i.e. mid-call rather
  than a subsequent callback) — already closed by the `MouseUp`-only flush scoping plus the
  targeted `_isDisposed` re-check/try-catch, not evidence that the general deferred-disposal
  primitive needs replacing.
- **Property 2 is already centralized, for both paths.** `ProcessMouseInput` — called by *both*
  the main window's `ProcessInput(Window, ...)` and popups' new `ProcessInputForSource
  (PresentationSource, ...)` — already calls `TryRedirectToCaptureSource` unconditionally. There
  is no duplicated per-path redirect logic to unify; it was unified from the start because both
  entry points share one function.

**What's actually missing is just property 4** (and, per the interim fix, only partially even
that). There is no evidence today that a from-scratch `PortableMessageQueue`/
`PortableCaptureManager`/`PortableNativeWindowCoordinator` needs to be built — the message-queue-
*shaped* design discussed above remains the right lens for evaluating *future* gaps as they're
found (and worth reaching for if `s_activeHosts`/`s_pumpDepth` ever prove insufficient for a
harder case), but building it speculatively now, on top of infrastructure that already covers
properties 1–3, would itself be the kind of duplicate code this plan is trying to avoid. Treat
`PumpAllActiveHosts`/`s_pumpDepth`/`s_pendingNativeWindowDisposals` in
`src/ProGPU.Wpf/ProGpuWpfWindowHost.cs` as the canonical property-1/3 primitive, and extend it in
place if a real gap surfaces, rather than introducing a parallel mechanism.

### Property 4: attempted deleting the shared-slot mechanism entirely — **reverted, negative result**

The theory was: with 1–3 confirmed solid, there's no remaining justification for *any* form of
popup-window sharing, since `MenuBase.IsMenuMode`/`CurrentSelection.IsSubmenuOpen` should already
drive an ordinary `Popup.IsOpen = false` → `HideWindow()` → `Host.Hide()` close for ever case,
cascading or sibling, the same way real Windows WPF needs zero window-sharing. **Tried this
directly: removed `UsesSharedPortablePopupWindow` from both `MenuItem.OnApplyTemplate` and
`ContextMenu.HookupParentPopup` entirely, so every popup always gets its own independent native
window with no eviction anywhere.**

**Result, confirmed by hands-on testing: wrong.** Clicking top-level menu headers in turn (File,
then Edit, then Help) left *every* previous dropdown's native window visibly open on screen —
they accumulated instead of the old one closing when the new one opened. This means
`Popup.IsOpen = false`'s native-hide path is **not** actually taking visible effect for top-level
siblings on this backend, contradicting the theory above. Whether the root cause is (a)
`IsSubmenuOpen = false` never actually getting set for the old top-level header when a *different*
header is clicked (as opposed to re-clicking the *same* open header, which is a different, already
-verified-working code path — see `MenuItem.ClickHeader`/`MenuBase`'s `CurrentSelection` setter
around line 340 for the mechanism that's *supposed* to handle this), or (b) `IsSubmenuOpen`
correctly flips false but `Host.Hide()`'s `_window.IsVisible = false` doesn't reach the actual
Silk/GLFW window (a genuine bug in the native layer, not the WPF layer) is **not yet
diagnosed** — it needs the `LIBREWPF_MENU_INPUT_LOG`/`MENUCLICK` tracing extended to
`MenuBase.CurrentSelection`'s setter and `Popup.HideWindow`/`ShowWindow`/`OnClosed` before
attempting this again, not another blind retry.

**Reverted to the `Role == MenuItemRole.TopLevelHeader` interim fix** (`MenuItem.cs`) and restored
`ContextMenu`'s unconditional `UsesSharedPortablePopupWindow = true` (a `ContextMenu`'s own root
popup behaves like a top-level header — one open at a time, old one should close when a new one
opens, and the pure-logical-close path is exactly what was just shown not to work for that case
here). This is confirmed working from earlier hands-on testing (nested flyouts stayed open beside
their still-open parent, no accumulation) and is the known-good state to build forward from.

**Takeaway for next time:** the architecture argument ("Windows doesn't need this, so we
shouldn't either") is a good hypothesis generator but is not itself evidence about *this specific
fork's* current bug inventory — property 3's "deferred disposal already covers this" claim earlier
in this doc was verified by reading the code path closely; property 4's "the logical close already
covers this" claim was *not* verified the same way before implementing, and turned out to be
false. Trace or read the actual call path before removing a workaround, even when the
architectural argument for removing it sounds solid.

### Root cause found and fixed: `_isRendering` silently dropped disposal entirely (real property-3 gap)

Reverting to the `Role`-based interim fix (previous section) didn't fully resolve the symptom
either — traced with new `HOSTLIFECYCLE`/`POPUPACTIVATION`/`POPUPLIFECYCLE` diagnostics
(`ProGpuWpfWindowHost.Dispose`/`DrainPendingNativeWindowDisposals`,
`WpfPortablePopupActivation.TryCreate`/`Dispose`, `Popup.CreateWindow`/`HideWindow`/
`DestroyWindowImpl`, all gated behind `LIBREWPF_MENU_INPUT_LOG=1`) and confirmed empirically: the
shared-slot eviction (`WpfPortablePopupActivation.TryCreate` → `previousOccupant.Dispose()`) ran
correctly on **every single** top-level transition with no exceptions — the WPF/activation layer
was never the problem. The actual bug was one level lower, in
`ProGpuWpfWindowHost.Dispose()` itself:

```csharp
// before
bool disposeNativeWindow = window != null && !_isInNativeWindowCloseCallback && !_isRendering;
bool deferNativeWindowDisposal = disposeNativeWindow && s_pumpDepth > 0;
```

When `_isRendering` was `true` at the moment `Dispose()` ran, `disposeNativeWindow` became
`false` — which skipped **both** branches below it (`deferNativeWindowDisposal`'s hide-and-queue
path, *and* the immediate `window.Dispose()` path). The native window was neither hidden nor
destroyed: a silent, permanent leak. Traced across ~20 top-level menu clicks in one session, this
happened for roughly two-thirds of them — matching the user-observed "some transitions work, most
don't" symptom exactly (previously misread as a shared-slot logic bug, when the shared-slot logic
was actually firing perfectly every time).

**Why `_isRendering` was true so often:** `ProGpuWpfWindowHost.OnRender()` sets `_isRendering =
true` for its own duration and, partway through, calls `ProcessDispatcherQueueCore()` — a generic
drain of the *one shared* WPF `Dispatcher` queue, not scoped to this host. If a queued WPF
operation drained at that moment happens to be the very thing that closes and disposes *this
host* (a top-level menu popup evicting itself as a side effect of opening a sibling, routed
through the one shared dispatcher), `Dispose()` runs **reentrantly on this host's own `OnRender`
call stack** — exactly the situation `s_pumpDepth > 0` already exists to detect and defer for, just
via a different flag.

**Fix:** fold `_isRendering` into the same defer condition as `s_pumpDepth`, instead of using it
to skip disposal outright:

```csharp
// after
bool disposeNativeWindow = window != null && !_isInNativeWindowCloseCallback;
bool deferNativeWindowDisposal = disposeNativeWindow && (s_pumpDepth > 0 || _isRendering);
```

Now every case that can't safely call native `window.Dispose()` synchronously — mid-pump *or*
mid-render — takes the same hide-immediately-then-queue-for-later-teardown path that already
existed and already worked correctly for the pump-depth case. This is a real, general fix to
`ProGpuWpfWindowHost`'s deferred-disposal mechanism (property 3), not something specific to
menus — any control whose popup gets evicted/closed while its own render pass is mid-flight would
have hit the identical leak.

**Lesson reinforced:** this is a second instance (after the property-4 revert above) of an
architecturally-plausible piece of infrastructure ("deferred disposal already covers reentrant
teardown") turning out to have a real, traceable gap once actually exercised — property 3 covered
*one* reentrancy source (`s_pumpDepth`, mid-pump) but not the other one that existed right next to
it in the same method (`_isRendering`, mid-render). Whenever a "should already be handled by X"
claim is made in this doc, the standard going forward is: grep every call site that sets/reads the
guarding flag, not just the one call site the current bug happens to route through.

### Fixed: hovering across sibling top-level headers didn't auto-switch menus

**Symptom (user-reported):** open File by clicking it, then move the mouse to Edit *without
clicking* — real Windows WPF closes File and opens Edit automatically (`MenuItem.MouseEnterHelper`
→ `OpenOnMouseEnter` → `MouseEnterInMenuMode`, unmodified WPF code, already present in this fork).
On the portable backend this simply didn't happen; only clicking switched menus.

**Investigated and rejected a workaround before finding the real fix.** The user's own suggested
approach — detect the cursor moving over a sibling header while a popup is open and synthesize a
click — was reasonable given the assumption that raw mouse-move events for the main window's menu
bar might not be reaching WPF at all while a popup is open. Traced it first rather than
implementing the workaround blind (per the lesson above): confirmed via `TracePortableMouseInput`
that once any popup opens, **every** subsequent `MouseMove` — even ones whose on-screen position is
over the main window's menu bar, not the popup — arrives tagged `source=PopupRoot`, and
`Mouse.Captured` correctly resolves to the `Menu` (`captured=System.Windows.Controls.Menu`) for
many of them. That is *exactly* the situation `PortableWindowActivationService
.TryRedirectToCaptureSource` already exists to handle: input physically arrived on window A, but
capture belongs to window B, so translate and redeliver to B. Checked whether it was actually
firing: **zero** `stage=captured` trace lines exist anywhere in the log (only `physical`/
`effective`) — the redirect was silently failing on every single call.

**Real root cause:** `TryRedirectToCaptureSource` needs the screen origin of *both* the physical
source and the captured source, resolved via `WpfPortableWindowActivation.TryGetScreenOrigin`,
backed by a `ConditionalWeakTable<PresentationSource, ProGpuWpfWindowHost>` populated **only** by
`WpfPortableWindowActivation`'s constructor — i.e., only for real WPF `Window`s. A popup's
`PresentationSource` (created and owned entirely by the separate `WpfPortablePopupActivation`
class) was never registered in that table, so `TryGetScreenOrigin` always returned `false` for a
popup source, and the redirect always bailed out before doing anything. This is a real,
previously-undiscovered gap distinct from every other fix above — a popup's own physically-arrived
input could never be redirected to *anywhere*, not just to the main window, since the lookup fails
before even checking which direction the redirect should go.

**Fix:** added `WpfPortableWindowActivation.RegisterPresentationSourceHost`/
`UnregisterPresentationSourceHost` (thin wrappers around the existing table, also used to de-
duplicate the constructor's own registration logic), and call them from
`WpfPortablePopupActivation.TryCreate`/`Dispose` so a popup's `PresentationSource` resolves through
the exact same lookup a real `Window`'s does. No new redirect logic, no menu-specific code — the
existing `TryRedirectToCaptureSource` and the existing `MenuItem.OpenOnMouseEnter` machinery should
both now work unmodified, which is the whole point: this was a registration gap, not a missing
feature.

**Hands-on retest after shipping the fix above: still broken, but with a different, more revealing
signature.** `captured=` in the trace read `null` for 100% of the session (previously it correctly
showed `System.Windows.Controls.Menu` at least some of the time), and `stage=captured` still never
appeared once. The screen-origin fix above was necessary but not sufficient — `Mouse.Captured`
itself was never actually held for the observed session, so `TryRedirectToCaptureSource`'s very
first check (`if (Mouse.Captured is not DependencyObject ...) return false;`) was failing before
screen origins even mattered.

### Root cause, take two: popup windows steal native OS focus, silently releasing `Mouse.Capture`

Traced `Mouse.Capture`'s actual success path (`MouseDevice.Capture` →
`PortablePresentationSource.PortableMouseInputProvider.CaptureMouse()`, which only fails if
`!_source.HasRootVisual` — not the issue here) and found the *release* path instead:
`PortableMouseInputProvider.NotifyDeactivate()` unconditionally releases capture, and it's wired
to `PortableWindowActivationService.SetActivationState(window, isActive: false)` →
`NotifyPortableInputProvidersDeactivated`, called whenever
`WpfPortableWindowActivation.OnHostWindowEventReceived` sees `WpfWindowEventKind.Deactivated` for
the main window — which is raised directly off Silk's own `window.FocusChanged(false)`
(`SilkNetWpfWindowEventService.cs`).

**This is the concrete, previously-only-theorized manifestation of the missing property-4 primitive**
("no owner-chain/`NOACTIVATE`-equivalent z-order primitive," flagged early in this doc): real
Windows WPF popups are `WS_EX_NOACTIVATE` + `WS_EX_TOPMOST`, so showing one **never** sends the
owner `WM_ACTIVATE(false)`/`WM_NCACTIVATE(false)` — the owner's `Mouse.Capture` (held by `Menu` via
`MenuBase.IsMenuMode`) survives for the popup's entire lifetime. On this backend, a popup's
separate native window steals real OS focus the instant it shows (Silk.NET 2.23's `WindowOptions`
has no `FocusOnShow`/equivalent knob to suppress this, confirmed by inspecting the assembly), which
fires a real `Deactivated` event on the main window, which faithfully (and, for this specific
case, incorrectly) propagates all the way down to releasing `Menu`'s capture — immediately, every
time, which is why `captured=` was `null` for the *entire* test session rather than intermittently.

This also explains why **click-based** switching (File → Edit → Help) kept working throughout every
other fix in this doc even while capture was being destroyed on every popup open: each click is a
fresh `ClickHeader`/`OpenMenu` cycle that re-establishes capture from scratch, so losing it a moment
later (once the new popup shows and steals focus) doesn't matter for that single click — it only
matters for anything that needs capture to *survive between* input events, like hover-tracking
across sibling headers.

**Fix:** can't suppress the focus-steal at the Silk/GLFW layer (no exposed knob), so suppress its
effect at the WPF-activation layer instead — added `WpfPortablePopupActivation.HasAnyOpenPopup`
(an `Interlocked`-guarded static counter, incremented in `TryCreate`, decremented in `Dispose`,
independent of the unrelated shared-slot bookkeeping) and check it in
`WpfPortableWindowActivation.OnHostWindowEventReceived`'s `Deactivated` case: skip propagating the
deactivation to WPF entirely while any of this app's own popups are open. This is a reasonable,
Windows-consistent approximation — real `WS_EX_NOACTIVATE` popups never generate the event in the
first place, so "ignore it while we know the only plausible cause is our own popup" gets the same
externally-visible result without needing a native-level fix. Known imprecision: if the user
switches to a genuinely different application while a menu happens to be open, this will also
suppress that (real Windows would actually deactivate in that case) — accepted as a much smaller
regression than the current 100%-broken hover state, revisit if it proves to matter in practice.
**Not yet re-verified hands-on.**

### Where this lives, so it doesn't turn into more duplicate code

- The coordinator belongs in `src/ProGPU.Wpf/` (it needs to own Silk.NET `IWindow`
  registration/pumping), exposed to the WPF side through the existing
  `external/ProGPU/src/ProGPU.Wpf.Interop` contracts (`IPortableWindowActivationServiceRegistrar`,
  `IPortablePopupActivationServiceRegistrar`) — no new WPF-side registration surface should be
  needed, since `WpfPortableWindowActivation`/`WpfPortablePopupActivation` already sit exactly at
  the boundary the coordinator needs to plug into. The change is *replacing* what those two
  classes currently do ad hoc (each owns/pumps its own host independently) with *registering* into
  one shared coordinator instance instead.
- **No behavior should move into `MenuItem`/`Menu`/`ContextMenu`/`ComboBox` at all.** Every fix so
  far correctly avoided this (the closest exception is the `Role`-based
  `UsesSharedPortablePopupWindow` scoping, which is exactly the kind of control-specific knowledge
  the plan above eliminates by deleting the concept it's scoping). Any future popup-hosted control
  should need **zero** portable-specific code — that's the test for whether the coordinator
  abstraction is right: adding a new `Popup`-based control should require touching `Popup.cs` at
  most, never `WpfPortablePopupActivation.cs`.
- Migration is incremental and low-risk: the coordinator can initially do nothing but (a) collect
  all live windows for one owner and (b) tick them in one loop instead of N — that alone
  establishes property 1 and lets `FlushSourceDispatcherOperations`'s explicit flush call be
  deleted. Properties 2–4 can follow once 1 is proven stable, each independently deletable-and-
  replaceable without touching control code.

### Status of the interim `Role`-based fix already shipped

The `MenuItem.OnApplyTemplate` change (`UsesSharedPortablePopupWindow = Role ==
MenuItemRole.TopLevelHeader`, see below) is a **stopgap**, not the target architecture. It reduces
the blast radius of the shared-slot bug without removing the underlying design flaw (properties
1–3 not being structurally guaranteed yet, which is *why* the shared-slot hack was ever needed in
the first place). Once the coordinator above exists, delete the shared-slot mechanism outright
rather than continuing to refine its scoping rules.

## Fixed this session

### 1. Popup native window never fed input into WPF at all

**Symptom:** opening a menu showed it, hovering did nothing (no highlight), clicking did
nothing.

**Root cause:** `WpfPortablePopupActivation.TryCreate` built a real `ProGpuWpfWindowHost` for the
popup but never subscribed to `host.InputReceived`, and never called
`WpfPortableWindowActivation.TryAttach` (which is what wires the *main* window's host input into
WPF). The popup's OS-level mouse events were being delivered and immediately dropped — nobody
was listening.

**Fix:**
- `WpfPortablePopupActivation.TryCreate` now subscribes `host.InputReceived +=
  activation.OnHostInputReceived`.
- `OnHostInputReceived` forwards each event to
  `IPortableWindowActivationServiceRegistrar.TryProcessInputEvent`, passing the popup's own
  `PresentationSource` (not a `Window` — popups have no owning `Window`) as the routing token.
- `PortableWindowActivationService.TryProcessInputEvent` (the WPF-side registrar) now accepts
  either a `Window` or a `PresentationSource` token and dispatches accordingly.
- New `PortableWindowActivationService.ProcessInputForSource(PresentationSource, input)` runs
  input against a bare source with no owning `Window` — `ProcessMouseInput`'s hit-testing already
  falls back to `source.RootVisual` (the popup's `PopupRoot`) when the window argument is null.

Files: `src/ProGPU.Wpf/WpfPortablePopupActivation.cs`,
`src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/PortableWindowActivationService.cs`.

### 2. Clicks were "received" but never executed a command

**Symptom:** after fix #1, hover/highlight worked, but clicking a leaf menu item (e.g. Help
About, File Exit) did nothing — no command fired.

**Root cause:** `MenuItem.OnClickImpl` doesn't raise `Click` (or invoke the bound command)
synchronously. It defers to `Dispatcher.BeginInvoke(DispatcherPriority.Render,
InvokeClickAfterRender)` — real WPF relies on its *own* render/message pump to eventually drain
that queued operation. On the portable backend, each popup pumps its *own* independent native
loop, and that loop is torn down (the menu closes) practically the instant the click is
processed. Nothing was ever pumping the *main* dispatcher's Render-priority queue in response to
input that physically arrived on a *different* (popup) native window, so the queued
`InvokeClickAfterRender` operation — and therefore the command — never ran.

**Fix:** `ProcessInputForSource` now flushes the dispatcher up to (and including)
`DispatcherPriority.Render` immediately after processing a `MouseUp` event on a popup source
(`FlushSourceDispatcherOperations`, same `DispatcherFrame`/`PushFrame` pattern already used by
`PortableWindowActivationService.FlushDispatcherOperations` for the main window). Gated to
`MouseUp` only (a click can only ever be deferred from `MenuItem`'s mouse-up handling) to avoid
paying for a nested pump on every mouse move.

Verified via a `MENUCLICK` trace point at `InvokeClickAfterRender`: it now fires for both Help ▸
About and File ▸ Exit.

### 3. Re-entrant crash from the new dispatcher flush

**Symptom:** after fix #2, a leaf-item click occasionally aborted the whole process with
`ObjectDisposedException` on `DispatcherWpfRenderScheduler.RequestRender()`, called from
`ProGpuWpfWindowHost.OnPlatformInputReceived`, called from a GLFW mouse callback.

**Root cause:** `FlushSourceDispatcherOperations`'s `Dispatcher.PushFrame` pumps a *nested*
message loop synchronously, inside the original popup mouse-up callback. A trailing mouse-move
event (still in GLFW's queue) got delivered and routed to
`WpfPortablePopupActivation.OnHostInputReceived` for a popup that the click itself had just
closed and disposed mid-flush.

**Fix:**
- `FlushSourceDispatcherOperations` is now only called for `MouseUp` (see fix #2) — this alone
  removes most of the re-entrancy window, since move events don't trigger a nested pump.
- `WpfPortablePopupActivation.OnHostInputReceived` re-checks `_isDisposed` after forwarding input
  (forwarding a click can synchronously dispose *this* popup), and wraps the post-forward
  render/wakeup calls in `try { } catch (ObjectDisposedException) { }` as a last-resort guard.

### 4. Cascading submenus evicted each other (interim fix shipped — see architecture plan above)

**Symptom (user-reported):** "the popup remains if I click a menu item that expands child items
— child items should be shown in a new popup (not shared)." Opening a submenu that itself has
children (a nested flyout, or any `MenuItem` with sub-items while its parent menu is already
open) left the UI in a stuck/wrong state instead of showing parent+child simultaneously like real
Windows WPF does.

**Root cause:** `MenuItem.OnApplyTemplate` unconditionally set
`_submenuPopup.UsesSharedPortablePopupWindow = true` for *every* `MenuItem`'s popup, regardless of
nesting depth (`ContextMenu.cs:485` did the same unconditionally for its own popup).
`UsesSharedPortablePopupWindow` routes popup creation through
`WpfPortablePopupActivation.TryCreate`'s single static slot (`s_currentSharedMenuOccupant`), which
**disposes** whatever previously occupied the slot before creating the new one — this exists so
that opening a *sibling* top-level menu (File → Edit) closes the previous top-level dropdown,
substituting for missing property 4 (no OS-level "owned, non-activating, independently-visible
popups" primitive — see architecture section above). That eviction logic is correct for **sibling
top-level menus** but actively wrong for **parent → child cascading submenus that must be visible
at the same time** (e.g. `File ▸ Recent Projects ▸ project1.sln`): opening the child's popup
silently disposed the parent's native window out from under it, while the parent's WPF
`Popup.IsOpen` still thought it was open.

**Interim fix shipped:** only share/evict the slot for **top-level** menu popups
(`MenuItemRole.TopLevelHeader`). Nested `SubmenuHeader` popups now get an independent,
non-evicting native window:

```csharp
// MenuItem.OnApplyTemplate
_submenuPopup.UsesSharedPortablePopupWindow = Role == MenuItemRole.TopLevelHeader;
```

**This is explicitly a stopgap, not the target design** — see "Status of the interim `Role`-based
fix already shipped" under the architecture plan above. The correct fix is deleting the
shared-slot mechanism entirely once the native window coordinator (property 1–3) exists, since
real WPF never needed *any* form of popup-window sharing to get correct sibling-closing behavior
— that came for free from `MenuBase.IsMenuMode`'s ordinary logical close, which this fork already
has and already uses correctly for cascading closes today.

**Not yet re-verified after this fix landed:** whether hovering from one nested flyout to a
sibling nested flyout under the same parent still closes the first sibling correctly now that
neither goes through the shared slot (should be handled by `MenuBase`'s own `IsSubmenuOpen`
cascading-close logic, same as on Windows, but this needs an empirical check, not just an
architectural argument).

## Things to watch when touching this area again

- **Popup destroy timing is priority-sensitive by design.** `Popup.HideWindow()`
  (`Controls/Primitives/Popup.cs`) destroys the native window via a `DispatcherTimer` at
  `DispatcherPriority.Input` specifically *below* `Render`, with an explicit comment: "Menus will
  allow all Render-priority queue items to be processed before firing the click event and we
  don't want to have disposed the window at the time that we route the event." Our new
  `FlushSourceDispatcherOperations` pump (fix #2) stops at `Render` priority, which is correct —
  it lets `InvokeClickAfterRender` drain (same priority, enqueued first, so FIFO puts it first)
  without dragging in the lower-priority `_asyncDestroy` timer tick. If this area regresses,
  re-check priority ordering first (WPF priority order, high→low, relevant subset: `Send` >
  `Render` > `Input` > `Background`).
- **`_secHelper.HideWindow()` calls `Host.Hide()` synchronously when not animating.** If a
  popup's `PopupAnimation` is non-`None`, the native hide is itself deferred — this hasn't been
  exercised yet on the portable backend and could hide another latent bug (native `IsVisible =
  false` called reentrantly from within that same window's own GLFW callback stack, mid-animation
  timer). Worth a dedicated test.
- **`Host.Hide()`/`Show()` mutate `IWindow.IsVisible` directly** (`ProGpuWpfWindowHost.cs`). No
  evidence yet that this is unsafe when called reentrantly from within the *same* window's own
  native callback (as opposed to a *different* window's callback, which is what fix #3 guards
  against) — but it hasn't been specifically ruled out either.
- **Diagnostics:** set `LIBREWPF_MENU_INPUT_LOG=1` before launching a portable-backend WPF app.
  Writes to `/tmp/librewpf-menu-input.log`:
  - `PortableWindowActivationService.TracePortableMouseInput` — one line per mouse
    move/down/up at each of three "stages" (`physical`, `captured`/`effective`), showing the
    resolved `PresentationSource`, `Mouse.Captured`, and hit-test result. Useful for diagnosing
    coordinate/capture-redirection bugs (`TryRedirectToCaptureSource`).
  - `MenuItem`'s `TraceMenuClick` — `MENUCLICK` lines at `OnMouseLeftButtonUp`, `HandleMouseUp`,
    `ClickItem`, and `InvokeClickAfterRender`. Useful for diagnosing "click doesn't do anything"
    bugs — if `ClickItem` fires but `InvokeClickAfterRender` never does, look at dispatcher
    flushing; if `ClickItem` never fires, look at hit-testing/coordinates/capture instead.
  - Both are compiled in but env-gated (no-op unless the env var is set) — safe to leave in place
    long-term rather than re-adding them each time this area needs debugging again. Consider
    promoting them from ad-hoc `File.AppendAllText` calls to something more structured if this
    keeps coming up.
- **Repack/relaunch workflow** for iterating on this code against OpenDevelop lives in
  `OpenDevelop/doc/technotes/librewpf.md` — packages are `LibreWPF.Transport` (this file lives
  there) and `LibreWPF.ProGPU` (the popup activation glue lives there). Don't forget the
  `~/.nuget/packages/librewpf.*` cache-clear step; the dev package version never changes so a
  stale restore is the most common reason a fix "doesn't seem to take."

## Open/unverified items

- **File ▸ Exit**: clicked and reached `InvokeClickAfterRender` in testing, but the process
  didn't visibly quit in the same test pass where the cascading-submenu bug was also present —
  worth re-verifying in isolation once that bug is fixed, since a stuck/evicted popup window
  could plausibly interfere with clean shutdown (pending native windows, dispatcher shutdown
  ordering).
- **Help ▸ About**: reaches command dispatch correctly (`InvokeClickAfterRender` fires,
  resolves to `ICSharpCode.SharpDevelop.Commands.AboutSharpDevelop`) but that command class isn't
  present in this OpenDevelop MVP build — an OpenDevelop-side porting gap, not a LibreWPF bug.
- **Keyboard menu navigation** (Alt-key access, arrow-key traversal) hasn't been tested at all on
  the portable backend yet — everything above was mouse-only. `MenuItem.OnAccessKeyPressed` and
  the keyboard-driven `OpenSubmenuWithKeyboard` path go through the same
  `UsesSharedPortablePopupWindow` machinery and should be re-tested once the cascading fix lands.
- **ToolTip popups** use the same native-popup-per-window mechanism (see
  `librewpf-popup-native-window` project memory from an earlier session) but weren't touched or
  re-verified this session.

## Auto-hide conditions: audited against real WPF before writing anything new

Before implementing "when should an open menu popup auto-close," audited the actual ported
source (`MenuBase.cs`, `Popup.cs`, `Menu.cs`, `ContextMenu.cs`) instead of guessing. Result: almost
every condition anyone would want is **already implemented**, driven entirely by
`Mouse.Captured`/hit-testing/keyboard routing — nothing new needed:

- Click outside the popup but inside the window → `Popup.OnPreviewMouseButton` (hit-tests
  `_popupRoot`, closes on a miss).
- Click on the menu bar itself while in menu mode → `Menu.HandleMouseButton`.
- Escape / Alt / F10 → `MenuBase.OnKeyDown`.
- `Apps` key → `ContextMenu.OnKeyUp`.
- Right-click outside → same `Popup.OnPreviewMouseButton` path (hooked for both left and right
  button).
- Click-outside through a chain of nested popups without double-dismissing → `EstablishPopupCapture`'s
  `isRestoringCapture` guard.
- Window deactivation from a click landing on a *different* real Win32 top-level window isn't
  separate logic either — Win32's `SetCapture` is system-wide, so that click *is* just another
  outside click to the first bullet. This only needs the capture-preservation-across-native-window-
  boundaries fixes already made earlier this session (see "Fixed this session" above) to keep working
  when the "other window" is one of ours.

**One condition WPF genuinely never has to handle, and we now do: main window drag.** On Win32,
popup HWNDs are owned/child windows, so the OS moves them in lockstep with the owner — WPF has no
`LocationChanged`-driven popup-close code anywhere because it's never needed. Portable popups are
fully independent native windows (`WpfPortableWindowActivation.TryCreate`), so nothing kept them
glued to the main window, and dragging the main window left an orphaned popup hanging in its old
screen position.

**Fix — emulate the missing message, then reuse everything downstream, per the project's driving
principle.** Rather than write bespoke portable dismiss logic, treated this as "one message Win32
would have sent but our backend can't produce" and synthesized it at the narrowest point, exactly
like the `Activated`/`Deactivated` (`WpfWindowEventKind`) and `HandleActivate` precedent already
established:

1. `Silk.NET` `IWindow.Move` → `SilkNetWpfWindowEventService` raises a new `WpfWindowEventKind.Moved`
   (`IWpfPlatformServices.cs`), carrying the window's `Host.Left`/`Host.Top`.
2. `WpfPortableWindowActivation.OnHostWindowEventReceived` forwards it through a new
   `IPortableWindowActivationServiceRegistrar.TryNotifyWindowMoved` (interop submodule,
   default-`false` so other implementers don't need to add it).
3. `PortableWindowActivationService.NotifyWindowMoved` → `Window.HandlePortableMove(left, top)` —
   a new method that mirrors `HandleActivate`'s shape exactly: it's the *portable substitute for the
   `WM_MOVE` case in `WindowFilterMessage`*, sourcing the new position from the native callback
   instead of `GetWindowRect(hwnd)`, then calling the same `WmMoveChangedHelper()` real WM_MOVE uses
   — so `Window.Left`/`Top`/`LocationChanged` all behave identically to Windows.
4. The actual "close the popup" step reuses existing WPF logic, not new logic: `HandlePortableMove`
   calls `Mouse.Capture(null)` when a menu/popup currently holds capture, which drives it through
   the *exact same* `MenuBase.OnLostMouseCapture`/`Popup.OnLostMouseCapture` path an outside click
   already takes. No bespoke "find and close all open popups for this window" code was written.

Files touched: `Window.cs` (`HandlePortableMove`), `PortableWindowActivationService.cs`
(`NotifyWindowMoved` + registrar impl), `WpfPortableWindowActivation.cs`
(`OnHostWindowEventReceived` Moved case), `SilkNetWpfWindowEventService.cs` (`IWindow.Move`
subscription), `IWpfPlatformServices.cs` (`WpfWindowEventKind.Moved`, `X`/`Y` on
`WpfWindowEventArgs`), `external/ProGPU/src/ProGPU.Wpf.Interop/PortableWpfServiceRegistry.cs`
(`TryNotifyWindowMoved` on the registrar interface).
