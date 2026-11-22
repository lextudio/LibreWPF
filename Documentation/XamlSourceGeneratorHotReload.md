# Hot Reload for the XAML Source Generator path — research

Status: research, not implemented. Claims are tagged:
**[verified]** run or read in this tree, **[docs]** from published docs/specs, **[open]** unknown, needs an experiment.

## 1. What exists today

**OpenDevelop** [verified, `Main/Base/Project/HotReload`]
- `IApplicationHotReloadAdapter` / `IHotReloadSession` with capabilities and a state machine.
- `HotReloadChangeKind` already defines `CodeDelta`, but no adapter produces it.
- `HotReloadWorkflow.Classify` is deliberately crude (element count equal → `Property`, else `Subtree`).
- SharpDbg has no Edit-and-Continue / `ApplyUpdate` support (nothing in the tree references it).

**WpfHotReload.Runtime agent** (startup hook, named pipe) [verified, read `WpfHotReloadAgent.cs`]
- The IDE pushes XAML **text**. The agent escalates through levels: property patch (0), subtree/full
  re-parse and merge into the live tree (1/3), resource-dictionary reload (2), restart required (4).
- Level 1/3 parses with `XamlReader` on the pushed text, so it does **not** depend on how the app was
  built (BAML or XSG).
- Level 0 and resource reload depend on `VisualDiagnostics.GetXamlSourceInfo` /
  `ResourceDictionaryDiagnostics`. Those are populated only by the XAML/BAML loader
  (`WpfXamlLoader`, `FrameworkTemplate`) through an `internal` helper. **XSG output never populates
  them**, so Level 0 degrades to x:Name lookup.
- It already calls a convention hook `__WXSG_ApplyHotReload(string path, string xaml)` on the live
  root when present.

**LibreWPF runtime** [verified]: no `[MetadataUpdateHandler]` anywhere in the WPF assemblies.
`MetadataUpdater.IsSupported` is `False` on the net11 host by default and `True` with
`DOTNET_MODIFIABLE_ASSEMBLIES=debug`.

## 2. Prior art

**MAUI** [docs]: runtime inflation is what historically enabled XAML Hot Reload. XSG becomes the
default inflator in .NET 11 and is documented as supporting Hot Reload, delivered through C# metadata
updates. The framework-owned `MetadataUpdateHandler` that refreshes a page is still an open proposal
(dotnet/maui#35278): after a delta, the running page does not refresh without navigating away. Even
MAUI has not finished this.

**AXSG (wieslawsoltes/XamlToCSharpGenerator)** [docs, plan/12, plan/43, AGENTS.md §17] — the most
complete design found:
- Generated `__PopulateGeneratedObjectGraph` is **idempotent** (clear-before-add for collections and
  dictionaries, `-=` before `+=` for events).
- Instances register in a weak registry keyed by type; a `[MetadataUpdateHandler]` re-applies the graph
  to live instances of the updated types, on the UI thread.
- Replacement types are mapped back to originals via `MetadataUpdateOriginalTypeAttribute`; updates are
  serialized and queued.
- Phased handlers (`CaptureState`, `BeforeElementReload`, `AfterElementReload`, …) and per-registration
  state capture/restore.
- Resilience rules: keep last-known-good output on invalid XAML; **apply-then-revert must converge to
  the baseline state**; no silent fallback.

## 3. The structural constraint

There are two ways to deliver an edit, and they cover different edits:

| Edit | Route T: push text, re-parse (agent today) | Route D: regenerate + metadata delta + re-apply |
|---|---|---|
| Property value, add/remove child | yes | yes |
| Resource dictionary value | yes (see §4) | yes |
| `{cs: …}` expression | **no** — verified: `System.Xaml` rejects `cs:` ("Invalid XAML type name"), with or without a declared prefix | yes, it is compiled C# |
| Event handler change | no — agent strips event members | yes |
| New `x:Name` (field) | partial (no field exists) | yes — verified in E2 |
| Removed `x:Name` (field) | partial | **restart** — verified in E2 (rude edit) |
| `x:Class` / root type change | restart | restart |
| Needs a delta producer | no | **yes** — `dotnet watch` works (E1); OpenDevelop/SharpDbg have none |

Neither route alone is enough: T cannot express `{cs:}`, and D is unavailable in the IDE we ship.

## 4. Complex cases

| Case | Why it is hard | Direction |
|---|---|---|
| Resource edits | `StaticResource` resolves once; consumers keep the old value. LibreWPF has `ResourceDictionaryDiagnostics.StaticResourceResolved` and `GetFrameworkElementOwners` [verified], but the event is raised from `StaticResourceExtension.ProvideValue`, which generated code does not call | Generate resource lookups through the same extension (or raise the event), then track key→consumers and re-apply only those |
| DynamicResource | The generator currently emits `FindResource` for both Static and Dynamic [verified in generated code]; that is wrong for Dynamic and defeats the one mechanism that propagates edits for free | Emit `SetResourceReference` |
| Templates / Styles | Already-instantiated visuals do not change when a template factory changes | Re-apply `Template`/`Style` on registered owners; accept state loss inside the template |
| Partial failure | The agent mutates the live tree incrementally; a failure mid-apply leaves a half-updated UI | Build the new subtree first, swap atomically, roll back on error |
| Mid-edit invalid XAML | Constant while typing | Keep last good, apply nothing, resume automatically (AXSG rule) |
| State | Scroll, focus, selection, `TextBox` text, expander state | In-place re-apply of the same root (keeps `DataContext`); capture/restore hooks for the rest |
| Many live instances | A `UserControl` used N times, or inside templates and off-tree | Weak registry populated by the generated constructor, not found by tree walk |
| Re-entrancy | Overlapping saves | Serialize and queue |
| Correctness | "Looks fine" is not a test | Convergence invariant: random edit scripts, assert apply→revert equals baseline |

## 5. Recommendation: hybrid, classified per edit

1. **Fast path (Route T)** for anything the parser can express: no build, works unsaved. Keep it, harden
   it (atomic apply, last-known-good, serialized queue).
2. **Slow path (Route D)** only when the edit contains `{cs:}`, an event change, or an `x:Name` change.
   Classification moves from element counting to a real parse diff in the IDE.
3. Make the generator output **hot-reload-ready in Debug**: idempotent graph, instance registration,
   source-info registration, `SetResourceReference` for dynamic resources.
4. Add a LibreWPF `[MetadataUpdateHandler]` that re-applies registered instances (Route D delivery).
5. Until OpenDevelop can produce deltas itself, the practical Route D producer is `dotnet watch` (E1).
   Until then, report `{cs:}` edits as **BuildRequired** (the outcome already exists in
   `HotReloadOutcome`) instead of failing the parse.
6. Treat `x:Name` removal like an `x:Class` change: restart required (E2).

## 6. Experiments

Harness: a net11 console app, a minimal incremental generator that turns an `AdditionalFiles` `Page.xaml`
into a partial class (`Title`, one field per `Name=`, a lambda from an `Expr=` attribute, a `Build()`
method), and a `[MetadataUpdateHandler]` that calls `Build()` on the live instance. Run with
`dotnet watch run --non-interactive` from the LibreWPF-pinned SDK (11.0.100-rc.2). The generator is a
stand-in, not `LibreWPF.Xaml.SourceGenerator`: it isolates the delta mechanism from WPF, which cannot run
on this macOS host with the Microsoft pack.

**E1 — does an `AdditionalFiles` XAML edit produce and apply a delta? [verified: yes]**
`dotnet watch` watches the `.xaml` additional file, regenerates, and applies the result without
restarting the process (`C# and Razor changes applied in 30–477ms`). The handler received
`updatedTypes=View,<>c`.

**E2 — which generated-code shapes survive? [verified]**

| Edit | Result |
|---|---|
| Change a string in a method body (`Title`) | applied, handler fired |
| Change a lambda body (the `{cs: …}` case) | applied, handler fired |
| Add an `x:Name` (new instance field + new statement) | applied, new field live on the existing instance |
| **Remove** an `x:Name` (delete a field) | **"Restart is needed"** — the app restarted |
| Edit that makes the generated C# invalid | "Unable to apply changes due to compilation errors"; process keeps running on the last good state |
| Fix the invalid edit | applied automatically, no restart |

Consequences:
- `{cs:}` edits are deliverable through Route D, which closes the gap Route T cannot.
- Last-known-good and auto-resume come from the runtime for free; nothing to build for that case.
- Deleting an `x:Name` is a restart. So AXSG's apply→revert convergence invariant **cannot hold through
  deltas for removals**: adding and then removing a name ends in a restart, not in the baseline state.
  The IDE must surface "restart required" for this case, as it already does for `x:Class` changes.
- Compile errors are reported against the generated file (`View.g.cs(8,21)`), not the XAML line. The
  generator needs `#line` directives, or diagnostics located in the XAML, before this is usable.

**E3 — startup-hook coexistence [verified, with a caveat]**
A second `DOTNET_STARTUP_HOOKS` entry loads alongside the delta applier and deltas still apply. But the
variable is inherited, so the hook also initialized in 2 other processes (`dotnet watch` and its build
host), not only the app. Our agent's `Initialize` must detect it is in the target app (for example, that
a WPF `Application` type is loadable) and do nothing elsewhere.

**E4 — leak/idempotence soak [open]** Not meaningful until the generator is idempotent.

**Not yet tested:** a real LibreWPF app under `dotnet watch` (needs a cross-platform LibreWPF.Sdk app
with a live window); regeneration cost for large XAML sets; whether `dotnet watch` and OpenDevelop's
debugger can both own the process at once (SharpDbg has no EnC, so debugging and delta-apply are probably
mutually exclusive today).

## 7. Known generator gaps this surfaced

- Not idempotent: a Panel root does `this.Children.Add(...)` on every call.
- No event wiring is emitted at all.
- `x:Name` fields are typed `FrameworkElement`, not the element's type.
- DynamicResource emitted as `FindResource` (above).
- Fixed in this pass: a XAML file that failed XML parsing, or that relied on implicit namespaces, was
  silently skipped and the build still succeeded. It now injects the implicit namespaces and reports
  `LWXSG0002` on a parse failure.
