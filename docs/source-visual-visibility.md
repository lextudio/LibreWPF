# Collapsed source ancestors and retained masked children

This addresses the source-state loss behind ProGPU
[#230](https://github.com/wieslawsoltes/ProGPU/issues/230). Canonical WPF stores
Hidden/Collapsed visual opacity as zero. The former typed bridge carried opacity
but no local visibility, leaving a collapsed retained owner marked visible in
ProGPU. Transparent-source hit capture then descended into its retained masked
child and correctly rejected unsupported visible-mask input.

The paired engine dependency is
`0d33ef68aaf9c9c58685f449e6ab386f5156fce5` (local visibility DTO plus native MIL
sideband). Its complete [Build 36697924609](https://github.com/wieslawsoltes/ProGPU/actions/runs/36697924609)
passed all 49 jobs before ProGPU PR #231 merged. This pins the exact tested
producer head, not its untested merge commit. The WPF consumer and application
behavior still require their own qualification.

Canonical Forms integration uses LibreWinForms commit
`9476d647a68c2cf46a23d0d375ef8b4191d6d23b` from
[PR #139](https://github.com/wieslawsoltes/LibreWinForms/pull/139), which pins the
same engine commit. The integration gate requires this shared dependency identity;
the Forms PR must pass its own Build before the WPF change can merge.

The source `Visual` exporter now preserves local `UIElement.Visibility` as an
optional typed Visible/Hidden/Collapsed descriptor. It never exports effective
`UIElement.IsVisible`: detached visible brush sources are not hidden. Plain
DrawingVisuals remain visible regardless of their alpha. The invalidation tracker
compares visibility independently, including transitions whose opacity remains
zero.

Managed retained owners receive `Scene.Visual.IsVisible`; invisible replay keeps
owner/dependency identity but emits no subtree content. Ordinary flat replay also
excludes the subtree. Native MIL keeps complete shared source records and submits
a sorted visibility snapshot to the native traversal, with session-owned previous
bytes so reused producer arrays cannot conceal a transition. Removal submits an
empty snapshot to restore compatibility-visible defaults.

Explicit BitmapCacheBrush capture retains its existing omission of outer root
state, including local visibility. Ordinary VisualBrush and all descendants use
ordinary local visibility. Neither path tests presentation attachment. Visible
mask/cache/effect input admission and own point-region Empty semantics are not
relaxed; opacity-zero visible content remains input-bearing.

Authored coverage includes four original PresentationFramework source cases,
seven managed replay cases, seven native producer/session cases (including both
native backends), and one tracker case. The existing layout-clip CI runner keeps
all 16 clip cases and separately requires the four source visibility cases with
the same strict skip policy and 60-second invocation bound. No local compilation,
test, GPU, VM or desktop execution was performed. Full dependency-ordered CI and
the original masked-child render/collapse/re-show action remain unqualified.
