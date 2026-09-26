# Windows clipboard BitmapSource transport

Acceptance application: `ProGPU.Wpf.ShowcaseApp`, Editors → Data transfer →
Copy image (Windows), then Paste image (Windows). The pasted source is assigned
to a real WPF Image. The historical issue117 WPFGallery report describes a hang
and subsequent exit; no stack trace proves the cause of that hang.

## Source-backed blocker and implementation

The default net10 graph defines `PROGPU_WPF_ALIAS_WINCORE`. Its shipped
preview.45 and preview.65 PresentationCore assemblies contain the
`WpfOleServices.GetDataHere = E_NOTIMPL` and failed native-object-read branches.
Windows clipboard routing already uses real OLE; the non-Windows clipboard
registry intentionally does not replace it.

Restoring the alternate branch alone is insufficient. Canonical ProGPU
`Bitmap.GetHbitmap` explicitly rejects export without a native image adapter,
and the existing InteropBitmap HBITMAP constructor enters the legacy MIL/WIC
factory. The implementation therefore uses the shared lightweight
`WindowsGdiBitmap` helper from [ProGPU PR188](https://github.com/wieslawsoltes/ProGPU/pull/188).
The source submodule pins its exact committed implementation, not copied files.

## Aligned source and CI dependency

ProGPU PR188 is merged at `bd9d034a72e79ce6bced330a30780e970b07c89a`.
The dependency remains pinned to its exact qualified producer head
`08f4343ef15328ba742cdcf11f8eb2daeefb5f7b`, whose complete
[Build 36248366666](https://github.com/wieslawsoltes/ProGPU/actions/runs/36248366666)
passed all 43 jobs. The native package staging script requires that successful
whole Build and its live `progpu-native-package` artifact; a merge commit or an
individual successful job does not substitute for that producer identity.

The LibreWinForms dependency is the merged
[PR62](https://github.com/wieslawsoltes/LibreWinForms/pull/62) commit
`0d9145f7e6429a941261dcd96d1293e4b7633097`. Its producer head
`d9f058d4e8b7b33b83ae4844c39adced03f96587` passed all nine jobs in
[Build 36259003664](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/36259003664).
Its nested ProGPU pin is the same
`08f4343ef15328ba742cdcf11f8eb2daeefb5f7b`, preserving the canonical graph's
exact-equality check. Current LibreWPF main is integrated without dropping the
clipboard consumer copy-local fix, RID-aligned output, four STA contracts, or
exact native-payload hash checks.
This aligned LibreWPF head still requires its own complete source/package CI,
including actual Windows x64 and ARM64 clipboard execution. No local heavy build
or additional VM validation was performed for this integration.

The WPF adapter validates CF_BITMAP, content aspect, lindex and the GDI medium
before encoding. The existing BMP encoder retains source pixel-format/palette
conversion. GDI receives only a validated complete BI_RGB BMP and publishes an
owned real HBITMAP after success. Caller-owned media are not overwritten.
Portable import copies native pixels while the OLE medium is alive, constructs
an independently owned BitmapSource, then releases the medium in finally.
Unexpected media and failed HRESULTs also retain release ownership. There is no
System.Drawing conversion, private fake handle or Windows portable-clipboard
reroute. Explicit native-media import retains its existing InteropBitmap path;
the untouched non-alias implementation and text/non-Windows routes stay separate.

The admitted BMP encoder formats and strict native ingress do not qualify every
WIC output format: 16-bit/bitfield/compressed BMP inputs remain rejected. CF_BITMAP
carries opaque RGB, not an alpha- or source-DPI-preserving image contract.

## Authored regression and qualification boundary

`ShowcaseClipboardImage` is compiled unchanged in the actual SDK Showcase and
source PresentationCore tests. Opaque asymmetric 3×2 pixels, padded source rows
and a partial Indexed8 palette detect row flips, channel swaps and palette loss.
The lifetime cycle flushes OLE, mutates the source, reads, clears the clipboard,
checks the retained image, freezes it, republishes and checks again. Showcase's
existing Windows package self-test invokes the actual copy/paste buttons and both
lifetime cases. Non-Windows does not claim this Windows gate or change clipboard
behavior; the added image controls are hidden there.

Source tests also construct an actual native COM data producer with real GDI or
global-memory media and a custom IUnknown release owner. They cover success,
failure with owned output, wrong TYMED, invalid export descriptors and preservation
of a caller-owned output bitmap. These are typed COM boundary tests, not fake WPF
objects.

The existing Windows x64 and ARM64 package Showcase jobs additionally compile
`eng/WindowsClipboardConsumer` against the exact package implementation assets
already staged for Showcase. It reuses the existing signed source-test friend
identity and links all four test bodies unchanged. An STA entry thread selects
portable pixel storage, invokes every body directly (no discovery or skip path),
and must finish within 60 seconds with four passed, zero skipped and the expected
process architecture. PresentationCore, WindowsBase, System.Private.Windows.Core,
PresentationNative_cor3 and shared Interop output hashes must match their exact
packages and selected RID. This is a real Windows OLE/GDI transport
gate, not a renderer fallback or a new public source API.

PresentationCore Release compiled with the repository SDK on macOS ARM64:
zero warnings and zero errors. The complete PresentationCore.Tests source project
including the four new Windows clipboard cases compiles with seven existing
warnings and zero errors. Showcase XAML/code and the small package-consumer
executable also compile with zero warnings and zero errors. These local checks
use explicit current source-built core assemblies and the existing source-built
bridge closure, not a freshly produced Windows package. They are compile-only;
the CI consumer instead uses the exact downloaded package implementation bytes.
An additional isolated Windows ARM64 source diagnostic launched the unchanged
four-body executable on .NET 10.0.5, with copied managed PE files checked by SHA-256 and
macOS native shims excluded. It failed during WindowsBase window-procedure setup
because that source-only closure lacks `PresentationNative_cor3.dll`; no test
completion marker was produced. The normal transport explicitly repacks native
WindowsDesktop runtime assets from the declared `10.0.11` packages, alongside the
separately Windows-built PresentationCore/DirectWriteForwarder and IJW host. The
SDK copies selected native runtime assets to Showcase; the clipboard consumer
then copies that declared DLL closure and verifies the selected PresentationNative
bytes before execution. No ambient WindowsDesktop DLL graft or stock managed WPF
replacement is used. The failed source probe is incomplete native staging, not
Windows image transport qualification or evidence about the reported hang. Preflight also
found and corrected the standalone consumer's inherited copy-local suppression:
its declared xUnit assertion DLL now accompanies the executable. Existing exact-head
CI and Windows x64/ARM64 package/runtime gates remain required; source compilation
and this failed diagnostic do not close issue117.

## Preserved SDK failure and main integration

Exact head `1b971273206f45f29a35e382cea1c0de559a3f33` failed the macOS SDK
job in [Build 36267516062](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36267516062/job/108477431838),
before Toolkit or the downstream Windows clipboard jobs ran. Showcase's real
live Thumb precondition exhausted its existing attempts: the target was visible,
enabled and hit-test-visible at 32×20, but source `InputHitTest` at `(16,470.087)`
returned a `Border` outside the target's ancestry. This happened before that
attempt injected drag input. The app exited after approximately94 seconds,
within the unchanged180-second outer deadline; this was an assertion failure,
not a Toolkit Splitter timeout.

The full retained job log has SHA-256
`cef21b466b1a75e048ba4e362b5d6aac32a147b3d844d83dd2723444135a0787`.
It does not identify the returned Border's ancestors, templated owner or clip.
Toolkit had not started, so its always-upload step explicitly found no diagnostic
files and no Toolkit screenshot/receipt exists for this run. No renderer,
runtime, clipboard or historical issue117 cause is inferred from that failure.

Main `915df76c1b19889ee7ab8992d4313c1568dbd645` is now integrated, retaining
all clipboard product/consumer contracts and Toolkit failure diagnostics, plus
the separately merged #181/#182 work. The incoming #181 final-failed-attempt
Thumb diagnostic adds selected-tab, presentation-source, ancestor/templated-owner,
layout-validity, transform and clip evidence. It neither repairs layout nor changes
input admission, deadlines or assertions. This integration is not a fix for the
recorded failure and not retry-as-qualification: the new exact combined head must
pass the entire source/package Build and both actual Windows clipboard jobs.
Only lightweight syntax/offline integration checks were run locally; no new
native application, heavy build or VM execution was performed.

The integrated `1170b29afd90ffb98d368950e3e151d0ca4ec695` failed the same
precondition in [Build 36269872346](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36269872346/job/108484453658).
Both target and Window resolve to the same presentation source; layout is valid.
The selected hit belongs to the outer TabControl template Border, not the Thumb.
The recorded offsets place the Thumb center at `(16,226.4931640625)` in the
ancestor with reported rectangle `(0,0,506.66666666666663,254.607421875)`.
Those coordinates alone do not establish the geometry's transform, actual
containment, template drawing admission or retained GPU owner selection. The
source `InputHitTest` is connected to the portable GPU owner-query override.
The full job log SHA-256 is
`075b5a44dc324b67ec1ba6971f6c7f0e7d6326a6e7874d9a3edc647db491ec96`.
Toolkit and downstream native clipboard jobs again did not run.

The next final-failure diagnostic records the actual clip-local point, clip
affine and source containment; existing Thumb template/drawing descendants
(at most64 visual nodes and depth8); and at most128 raw GPU owners through the
existing typed query. This is explicitly a **post-failure** query: it may refresh
the index, so before/after presented-frame identities are retained and it is not
claimed to be the original rejected query. Truncation and diagnostic errors are
reported. No layout/template repair, input, retry, fallback, assertion or deadline
change is made. The checked-in focused source guard and Roslyn syntax check pass
without building/running Showcase; actual compilation and full CI remain required.

The SDK workflow now prepares a separate failure-only Showcase archive after a
failed SDK step. `eng/progpu-wpf-preserve-showcase.py` retains the actual built
Debug app directory (apphost, managed assemblies, dependency/runtime manifests,
fonts and content) and the two already-staged native input directories. Its fresh
private receipt records source commit, recursive submodule state, workflow run
and attempt, every archived file's SHA-256/size/mode and the archive SHA-256.
Capture rejects missing required payloads, links, changed inputs and exceeded
budgets (4096 files, 256 MiB source bytes, 272 MiB compressed, 90 seconds inside
a two-minute step). An incomplete capture stays explicitly incomplete, with its
original failed job still failed. No app is launched or rebuilt by this step.

`showcase-failure-diagnostics-<source SHA>` is intentionally separate from the
successful CI package bundle and is **never a qualified package producer**.
Staged native files are not proof of which modules the failed process loaded;
the .NET runtime and operating-system dependencies are not distributed. The
runtimeconfig, dependency manifest and exact retained bytes enable a later
controlled diagnostic launch, not a rendering or clipboard qualification claim.
The 15 offline archive controls exercise inert byte fixtures only; they do not
run Showcase or replace any existing SDK, input or Windows gate.

Exact `b3e88a5c90e5b3acd0fbe9abc02bf0b0e31af4d2` reached and passed the actual
Showcase Thumb capture/drag/release and Toolkit live gates in
[Build 36272431104](https://github.com/wieslawsoltes/LibreWPF/actions/runs/36272431104/job/108491679689).
Consequently its failure-only Thumb diagnostic did not execute. The SDK job then
failed the unchanged packaging graph guard: it expected 22 exact-head expressions,
but the already-added Toolkit diagnostic artifact made 23. The new Showcase
archive makes 24: ten checkout refs plus fourteen artifact names, now explicitly
checked by the focused executable source guard as well as the original full guard.
No original job, input assertion or deadline is removed. This observed live pass
does not explain or repair the retained earlier Thumb failures and the failed
producer is not qualified. The full job log SHA-256 is
`e3d43eb9427be9bf941c37282106cff17935fd3fd0a9e4e08aa9e298cec00253`.
