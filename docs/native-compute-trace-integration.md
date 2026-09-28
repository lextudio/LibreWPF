# Native compute trace integration

The source graph pins ProGPU `5991fce2c61a3e98b5ff5e47f2b5473c5b4a9a83`
and LibreWinForms `cf6bbc14539533055bae50bbac4bd7b413630cbf`. Forms pins
that exact ProGPU revision, preserving the canonical integration alignment gate.

This carries the captured Drawing clip and exact atlas/device mapping fixes,
Forms popup device sharing, menu measurement/hover/dismissal, physical-key test
identity and tooltip text margins. It retains the original source controls,
compiler/adapter defaults, input sequence and application deadlines. The native
compute diagnostics include the MSVC member-shadowing correction in ProGPU #221.

Only the separate failed-Showcase replay requests native compute tracing. The
ordinary acceptance launcher rejects an inherited enabled opt-in before payload
inspection or child launch. The receipt distinguishes requested tracing from
actual emitted records; encoding and submission are not GPU completion. See
[failure evidence](showcase-idle-failure-evidence.md).

The preceding Forms drawing-clip Build `36446248684` passed all nine jobs on its
tested graph, including visible Windows, Ubuntu and macOS applications. WPF Build
`36411654709` still failed its Windows ARM64 native Showcase resize. The new
diagnostic does not fix that stall or qualify the new dependency combination.

No package is staged by this source update. Native runtime staging still requires
the complete successful ProGPU Build at the exact pinned commit; the failed
`b3f221d3` producer is ineligible. Complete WPF source/package/application gates
remain required, with no fallback, test suppression or deadline extension.
The visual-only HwndHost change in PR #200 remains independent of this stack.
