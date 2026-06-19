# ProGPU.Wpf.Sdk

`ProGPU.Wpf.Sdk` is the custom MSBuild SDK surface for running WPF applications on the ProGPU/Silk.NET platform. It is intended to let existing WPF applications move from the WindowsDesktop SDK to the portable ProGPU WPF platform by changing the project SDK while preserving normal WPF XAML, BAML, resource, theme, and code-behind behavior.

This initial package skeleton layers on the existing WindowsDesktop SDK so WPF markup compilation remains owned by the real `PresentationBuildTasks` implementation. It then selects the portable ProGPU/Silk.NET platform and redirects WPF framework references through either package references or local artifact roots while the port is still source-built.

Package mode is the intended delivery path. It references the ported managed WPF bundle through `ProGpuWpfManagedPackageId`/`ProGpuWpfManagedPackageVersion`, references the ProGPU runtime packages, injects the non-Windows portable activation bootstrap, and copies resolved managed and native runtime assets to the application output. Local-artifact mode remains available for source-tree validation by setting `ProGpuWpfManagedReferenceRoot` and `ProGpuReferenceRoot`.

The SDK owns the package dependency closure. The WPF transport package supplies the real managed WPF assembly identities and runtime payload, while `ProGPU.Wpf` is the adapter/runtime bridge package and does not publish dependencies on the ProGPU shim `PresentationCore` package.

Existing WPF application projects should keep their normal WPF project shape and switch only the project SDK. The SDK treats `UseWPF=true` as the app's markup intent, then internally redirects framework references to the portable WPF transport and ProGPU/Silk.NET package graph.

The SDK also supplies the WPF markup compiler defaults needed by the portable build lane, so applications do not need ProGPU-specific item includes or PresentationBuildTasks compatibility properties.

```xml
<Project Sdk="ProGPU.Wpf.Sdk/11.0.0-dev">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```
