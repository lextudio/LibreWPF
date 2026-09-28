# Native pipeline startup integration

This graph pins ProGPU `9e471863b351770ad59a23bdfe79579bb3c3a9f2` and
LibreWinForms `906953405013f987baa88ffc968204dd6412a2ce`. LibreWinForms pins
that same ProGPU revision. The integration carries original ProGPU all-line,
operation-zero path raster specialization in both native providers and managed
rendering, exact managed single-sample pipeline reuse, opt-in pipeline timing,
and the Forms original-grid source/painting changes. No WPF-local rendering
algorithm or source workaround is added.

The specialization retains the canonical winding/sampling/packing algorithm,
with full curved/Boolean paths for all other inputs. Managed cache reuse retains
actual layout identity, target format, sample count, blend, alpha and mask keys.
Compiler/adapter defaults, source font/layout state, GPU completion semantics,
source/native input ownership and all existing deadlines remain unchanged.

The existing successful-Build selector must stage native runtime packages only
from the complete successful ProGPU producer at the exact pinned commit. This
source update does not itself stage any artifact or waive that prerequisite.
The complete WPF Build and package/application gates still qualify the combined
graph; source or standalone shader passes cannot substitute for them.

## Evidence boundaries

The preceding ProGPU change passed independent complete-output linear/full-path
GPU comparisons, all six Windows path cases, both native provider builds and
23 native CTests. Exact single-sample reuse passed all 12 new cases on actual
Windows ARM64, checking pixels, distinct four-sample targets, repeat selection
and disposal. Forms' original native-input diagnostic passed with a private
updated renderer copy under unchanged startup/interaction deadlines. That copy
is not a qualified package and does not establish WPF behavior.

The prior WPF Windows ARM64 native Showcase failure remains authoritative until
the unchanged gate passes on this complete graph. Its captured WARP worker
stack identified compute execution, not a particular shader or a proven driver
fault. The new path specialization must not be described as fixing that stall
without actual application evidence. Preserve failure traces, live-stack
capture, x64 controls and passive resize/input assertions.

The independent visual-only HwndHost change remains in PR #200; it is not
implicitly included or qualified by this dependency-only stack. AvalonDock,
popup/modal UX and the remaining native MIL/DirectX/Direct2D requirements retain
their separate application and release gates.
