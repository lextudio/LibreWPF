# Agent Guidance

## Reflection-Free Port Priority

The ProGPU WPF port must converge on a reflection-free, high-performance implementation. Runtime reflection in the WPF bridge or ProGPU is temporary scaffolding only: keep it limited to compatibility probes, diagnostics, or transitional adapters that are documented with an exit path.

After the current paid Xceed DataGrid/App work, prioritize replacing reflection-heavy retained visual, MIL/resource, invalidation, hit-test, and platform shims with typed APIs, generated accessors, reusable ProGPU scene/vector/text primitives, or source-integrated WPF internals. Do not add new managed WPF workarounds when the correct fix belongs in ProGPU rendering, shaders, layout/cache metadata, input, or DirectX/Silk.NET platform support.

Normal WPF managed code reuse remains the goal. Modify upstream WPF managed code only where necessary to expose portable seams, replace Windows-only calls, or route native rendering/platform work into ProGPU and Silk.NET.
