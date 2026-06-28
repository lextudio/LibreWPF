# Agent Guidance

## Reflection-Free Port Priority

The ProGPU WPF port must now prioritize a reflection-free, high-performance implementation. Runtime reflection in the WPF bridge or ProGPU is temporary scaffolding only: keep it limited to compatibility probes, diagnostics, or transitional adapters that are documented with an exit path, and replace product hot-path reflection with typed/source-integrated seams as soon as the local blocker is handled.

After the current paid Xceed DataGrid/App work, prioritize replacing reflection-heavy retained visual, MIL/resource, invalidation, hit-test, and platform shims with typed APIs, generated accessors, reusable ProGPU scene/vector/text primitives, or source-integrated WPF internals. Do not add new managed WPF workarounds when the correct fix belongs in ProGPU rendering, shaders, layout/cache metadata, input, or DirectX/Silk.NET platform support.

Bridge contracts used by package-mode apps must avoid shim-owned WPF structs/classes in public callback signatures. Prefer primitives, neutral DTOs, or source-integrated WPF interfaces/factories over runtime type lookup, reflected properties/events, or expression-built adapter delegates.

Use the existing `IPortableGeometryPathSource` and `IPortableGuidelineSetSource` seams as the pattern for future cleanup: expose narrow typed contracts from source-built WPF internals, keep DTOs package-neutral, and update tests to assert that hot-path readers do not use `System.Reflection`, `BindingFlags`, property probing, or duck-typed fake shapes.

Normal WPF managed code reuse remains the goal. Modify upstream WPF managed code only where necessary to expose portable seams, replace Windows-only calls, or route native rendering/platform work into ProGPU and Silk.NET.
