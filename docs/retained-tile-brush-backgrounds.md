# Retained tile-brush rectangle backgrounds

Issue [#220](https://github.com/wieslawsoltes/LibreWPF/issues/220) reports invisible
DrawingBrush and ImageBrush Panel/Border backgrounds in the nine-panel macOS
TileBrushTest, while solid fills and Image.Source work. The acceptance target is
that original application/action, not a replacement control or a solid fallback.

## Concrete source path

Panel.OnRender and the square Border background path record DrawRectangle.
Canonical RenderData exports that record with its original dependent Brush.
WpfVisualTreeRenderer replays the snapshot through WpfRenderDataBridge and
WpfMilRenderDataDecoder; it does not call WpfObjectRenderDataDrawingContext.
The earlier object-context tile-rectangle fix therefore did not cover this path.

The decoder previously passed the rectangle's raw resource to ResolveBrush,
which only adapts an existing media brush or IPortableBrushSource. Canonical
TileBrush instead implements IPortableTileBrushSource. The fill became null,
and the decoder nevertheless counted the rectangle as applied. DrawingBrush and
ImageBrush were lost alike, before their tiled versus untiled mapping mattered.

The retained decoder now recognizes the raw tile source before generic brush
adaptation in both its typed and native-primitive command sinks. Both use the
existing WpfDrawingReplay tile-fill algorithm: actual source rectangle hit scope,
destination clip, image adapter, drawing replay, transforms and tile placement.
The separate pen is replayed after the fill scopes close, using the native
rectangle command when available. Recognized unavailable sources remain
unsupported; a successful pen alone is partial application, not fill success.
An unavailable requested pen likewise remains unsupported. Existing animated
base-value replay still counts nonzero animation handles as unsupported, and
the exact source Rect.Empty sentinel still bypasses brush access.
An admitted empty drawing remains skipped without a pen, or applied when its
independent pen draws; it is distinct from an unavailable drawing descriptor.

The source tree's SDK default is explicitly ManagedPortable in
packaging/ProGPU.Wpf.Sdk/Sdk/Sdk.props. The non-native portable bootstrap registers
the ordinary activation service; its default ProGpuWpfWindowOptions also selects
ManagedPortable. Explicit NativeMilWgpu selects a separate transport/compiler
which already preserves native tile brush descriptors; this change does not
switch renderer modes or alter that compiler. The issue's external dist.local.sh
is not checked into this repository, so its effective project overrides have not
been independently established.

## Coverage and remaining admission

Authored decoder controls cover both sinks, source DrawingBrush/ImageBrush,
untiled and absolute/relative tiled viewports, exact rectangular source coverage,
balanced clipping/mapping and independent pen order. Negative/control cases keep
unavailable sources and pens, resource-owned image adaptation, animated-handle
accounting and Rect.Empty/following-draw behavior explicit. A source-graph guard
connects the canonical Panel/Border and RenderData entry points to both decoder
branches. Existing solid rectangle and other decoder tests remain unchanged.

The 30 decoder cases and one source-graph guard are authored only. No local
compilation, tests, native execution or pixel capture was performed for
this change. These are authored structural/replay regressions, not measured
rendering results. The original nine-panel reproduction on macOS, installed
package/renderer identity and complete required platform CI remain pending;
issue #220 must not be considered closed from source inspection alone.

## Curved retained backgrounds

The square-background correction did not cover `DrawRoundedRectangle` or
`DrawEllipse`. The uniform-corner `Border.OnRender` background emits the former.
Both decoder branches still adapted those brushes before tile replay, losing
canonical `IPortableTileBrushSource`; the native ellipse check happened only
after that lossy adaptation. A null fill was incorrectly counted as applied.

Both sinks now recognize raw tile sources for these records too. Shared tile
replay retains the original rounded-rectangle or ellipse geometry as the clip,
not its bounds, and does not add square source-hit metadata to curved fills.
Independent strokes follow closed fill scopes through the original primitive
sink. A shared status combiner preserves the rectangle behavior for unavailable
sources/pens and admitted empty drawings. Nonzero animation handles retain their
unsupported counts independently of successful base-value replay.

Authored controls cover both sinks, Image/Drawing sources, untiled and absolute/
relative tiled viewports, exact curves, pen ordering, empty/unavailable content,
missing pens, source-owned image adaptation and animated records. This change
does not alter native MIL, renderer defaults,
or original application/package qualification. Focused execution follows the
implementation commit; no desktop or GPU validation is claimed.

## Nonuniform-corner retained backgrounds

The complex `Border.OnRender` branch records its cached `StreamGeometry` with
`DrawGeometry`, including nonuniform corners. Both decoder sinks now examine the
raw tile source before generic adaptation for this record too. A portable geometry
publisher is read once, then the same exact path owns the fill clip and pen;
brush replay cannot trigger a second source publication. Source curves, holes,
fill rules and geometry-local transforms stay on that path.

The path-specific tile replay uses the existing mapping and drawing/image replay,
with a required exact native geometry clip. A proven rectangle may retain the
existing exact rectangle path, but failed curved-clip admission cannot become a
media fallback, flattened geometry or bounds-only clip. A failed native pen draw
is likewise reported as unavailable even if its pen descriptor resolved. Existing
non-tile and legacy media-brush records retain their original route.

Twenty-six additional authored cases cover both decoder sinks, original image and
drawing identities, tiled/untiled placement, nonuniform arcs, a transformed curved
hole, single publication, declined clips/strokes, missing pens and unavailable
source geometry/content. Empty drawing content remains distinct from failure,
and independent pen/following-record order is retained. These checks join the 52
curved-primitive cases; neither set has been executed locally. The previous
mixed-cache diagnostic harness is not a coherent validation graph and is not
expanded or relaxed for this work. Diff checks are the bounded local validation;
coherent CI and the original macOS application/package reproduction remain open.
