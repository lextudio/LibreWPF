# Toolkit empty rectangle and next native input blocker

Acceptance application: ProGPU.Wpf.ToolkitApp. Action: focus its filter after
first native presentation and transient-surface quiescence. The actual source
DrawRectangle record contains (+infinity, +infinity, -infinity, -infinity), the
canonical WPF Rect.Empty. The native builder previously rejected it as width.

ProGPU now retains this static record and consumes it in the shared C++ compiler
as empty drawing, with resource validation and unchanged scopes/visual traversal.
LibreWPF's managed decoder applies the same exact sentinel before brush mapping
in both primitive and ordinary sink routes. Zero area is not empty; zero-width
pen commands still replay. Animated and other rectangle-bearing contracts remain
separate. See ProGPU docs/native-mil-empty-rectangle.md.

Both native providers build and all 19 native suites pass. The diagnostic bridge
build has one existing CS0067 warning; its tests build with 20 existing analyzer
warnings and no errors. All 201 compiler/decoder tests pass, including four new
cases across managed replay, both C++ providers, source point policy and other
visual owners. No test threshold, application action or native-input gate changed.

The unchanged Toolkit diagnostic now advances beyond rectangle translation but
fails recorded hit-index construction with UnsupportedCommand. Temporary native
return-location diagnostics identified the failure of add_recorded_hit_test_index
with source_geometry opacity policy, not append_visual. Those diagnostics were
removed from source immediately after reproduction. Next: locate the rejected
input primitive/scope and repair that shared native contract, then repeat the
same complete live gate. This is not Toolkit success or package qualification.

Artifacts remain under /Volumes/1TB-macOS/progpu-core-release.xtwndj:
toolkit-diagnostic-rectangle-details.log, toolkit-diagnostic-empty-rectangle-native.log,
and toolkit-diagnostic-unsupported-location.log. These runs used the documented
diagnostic source graph and explicit assembly/native overlays, never final packages.

The continuation source fixture was also executed with current source Core and
Interop, using the Interop additional-deps descriptor for this diagnostic graph:
20/22 pass, including the new changed-width/captured-provider regression. Two
existing hidden-only range assertions expose the older terminal-boundary special
case admitting all hidden ranges; distinguish actual newline caret bounds before
claiming the source suite passes. No assembly-loading failure counts as a test pass.

ProGPU Build 34811802371 finished successful at a8afeab6; new local continuation
and empty-rectangle changes require fresh CI. No downstream pins or merges advanced.

## Hidden selection versus terminal caret follow-up

The original terminal-caret fix admitted any hidden-only selection range. Narrowed
it to positive ranges intersecting the actual source newline, preserving native
caret X and retained line height. Hidden formatting edges still participate in
navigation but do not acquire selection rectangles. Added explicit terminal-box
assertions for both a hidden-only paragraph and a continued shaped line; existing
hidden-range assertions remain unchanged. Source Core builds with zero warnings;
the test build has six existing warnings. All 22 PortableTextLine tests now pass,
none skipped, with the current diagnostic source Core/Interop graph. The changed
continuation-width/captured-provider test is included in those 22.
