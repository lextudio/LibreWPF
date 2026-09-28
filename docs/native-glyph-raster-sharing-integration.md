# Exact native glyph raster sharing integration

This graph pins ProGPU `2f47c475cf9143e1943452b40885c37c2de3e849` and
LibreWinForms `f9fb5413273812da06d3edb8d94da320fa97082b`. Forms pins the
same ProGPU commit, preserving canonical source-graph alignment.

The producer pin includes the atlas-growth fixture correction after Build
`36459867439` failed. Distinct unused control-point bytes preserve rendered
coverage while requiring separate raster jobs. Existing growth, warm replay and
pixel checks remain, with an added exact cold job-count assertion. Five local
execution-mode passes do not substitute for the new complete producer Build.

ProGPU shares first-owner raster tiles for bit-identical outline bounds, scale,
phase and all selected segment bytes, including duplicate ranges at different
arena offsets. Original outline indices, source glyphs, positioned draws, styles
and clips remain separate. The same work list reaches native compute, raster,
SIMD and scalar implementations without changing shader quality or defaults.
Forms additionally retains its owned startup-hover popup harness precondition.

## Existing failure and new evidence

The preceding WPF Build `36451685592` at `807bfec3379d83ab3765cdef3e410590711c4c52`
passed 12 of 13 jobs, including the Windows x64 native Showcase gate. Original
ARM64 resize still failed: five presentations retained 744x521 while the live
client and layout requested 884x601. Its separate instrumented replay completed
all four idle phases, but that does not qualify the original run.

The replay recorded 1,968 native glyph dispatches in generation one and 1,316 in
generation two. Its bounded trace truncated during generation four; it does not
identify every later dispatch or prove which shader was active in the failed run.
The new source change removes independently demonstrated duplicate raster work.
ProGPU's local packed-outline fixture reduces three jobs to one, with 5,120 versus
15,360 staging bytes and identical independently referenced pixels. Those native
results do not prove this application now meets its original deadlines.

Full exact-head WPF CI and original Windows ARM64/x64 resize/idle qualification
remain required. The native staging step still waits only for an entire successful
ProGPU Build at the exact pin; failed/canceled producers remain ineligible. The
ordinary acceptance environment stays uninstrumented and the separate failure
replay retains its existing limits. No assertion, timeout, adapter/compiler policy,
package-coherence check or native GPU completion rule is relaxed.
