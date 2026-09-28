# Portable visual-only HwndHost

AvalonDock's portable auto-hide host already owns an ordinary WPF visual tree.
It must not manufacture a native child HWND or return its parent's handle to
make that tree display. The latter aliases the containing presentation source
and can detach its root before attempting to create a visual-tree cycle.

LibreWPF adds an explicit opt-in for that existing visual-only implementation:

```csharp
protected override bool UsesPortableVisualHosting => true;
```

The default is false. The opt-in applies only when the host is attached to a
`PortablePresentationSource`, including portable rendering on Windows. It is
not selected by operating-system name, control type name or a zero return value.
A native Windows HWND source continues to use the original child-window path.

In visual-only mode:

- The derived class owns its visual/logical children, measures and arranges them,
  and implements its normal input and disposal behavior.
- `BuildWindowCore` and `DestroyWindowCore` are not invoked. Initialization needed
  by the portable visuals must not live solely in those native-window callbacks.
- `Handle` remains zero. No parent HWND or synthetic child handle is substituted.
- Attaching, loading again, detaching, moving to another portable source and
  disposing the host do not reparent or dispose the derived class's children.

This override belongs in a LibreWPF build of the consumer; stock Microsoft WPF
does not expose the property. Existing native callback implementations can stay
in the same derived class for its Windows HWND build. `WindowsFormsHost` does
not opt in and retains its separate portable compatibility path.

The existing default portable child-source path still requires a real,
source-owned `HwndSource`. Zero handles remain invalid. Returning the containing
source's handle is rejected before changing its root visual.

## Validation

`PortableVisualHwndHostTests` exercises real source presentation trees, visual
and logical ownership, layout, repeated loading, detach/reattach, reparenting and
disposal. Separate controls retain zero/parent-handle rejection and the ordinary
portable child-source build/destroy contract. The source gate selects portable
media explicitly and rejects skipped or missing cases.

These source contracts do not qualify the reporter's AvalonDock application,
native popup/input behavior or a release. The consumer must opt in and its
actual auto-hide, docking, resize and close/reopen paths still require execution.
