# Native WebGPU backend configuration for WPF windows

The source dependency now includes ProGPU #205 at merged commit
`bb0b64124dbc9d3107d0419e6c6b35bba75b7c27`. Applications can configure the owned
native instance before adapter/device creation with `PROGPU_WGPU_BACKEND=vulkan`
or the `WGPU_BACKEND` alias. The core parser, precedence, exact backend masks,
adapter verification and failure behavior are unchanged.

Window-host callers can also pass the existing typed core option:

```csharp
var options = new ProGpuWpfWindowOptions
{
    NativeBackendOptions = new WgpuNativeBackendOptions(WgpuNativeBackend.Vulkan)
};
```

Null retains the core startup environment for a new device and inherits a shared
owner's configuration for a borrowing context. Source Window activation copies
the option unchanged. Native popups already resolve their live owner at creation,
so they inherit that configuration without reinterpreting a different default.
An explicit conflicting choice is not discarded: core adapter validation rejects
it before sharing handles. Process sharing policy, renderer selection, compiler
selection and automatic backend defaults remain unchanged. Both managed and
native MIL host branches use this same factory; no new device or renderer
fallback is introduced.

This is startup/lifetime configuration, not per-frame work. It adds O(1) option
selection and no GPU work, native crossing, shader, font processing or mutable
resource ownership. Provenance is the existing ProGPU `WgpuNativeBackendOptions`
contract and LibreWPF's `CreateForWindow`, `CreateHostOptions` and shared-device
host paths. No third-party implementation code is imported.

## Evidence and remaining requirements

Eighteen executable tests cover all five choices, both renderer configurations,
source option copying, default environment policy, inheritance and preservation
of explicit conflicting choices for core validation. The retained/source gate
passes all 669 cases with zero failures/skips on macOS ARM64. The actual
multi-window harness also compiles; neither result creates or qualifies a device.

The Linux hosted job retains its original automatic run and adds independent
explicit OpenGL and Vulkan runs. Each opens/presents the three original windows,
checks unused-client-context ownership, closes the owner and presents a reopened
window on the surviving device. Every explicit run checks each actual adapter
backend, not its name. Failures remain failures; no driver hiding, fallback,
longer deadlines or waived popup assertions are used.

Those native runs and the full exact-head package Build must pass before merge.
The published preview.65 packages do not contain this update. Backend selection
does not convert an EGL non-unwinding panic to an exception; #187 remains open
until forced-GL application/docking and transparent-pixel qualification completes.
Default/native MIL and Windows/macOS release validation remain separate gates.
