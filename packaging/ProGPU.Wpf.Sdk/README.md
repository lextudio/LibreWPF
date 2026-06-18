# ProGPU.Wpf.Sdk

`ProGPU.Wpf.Sdk` is the custom MSBuild SDK surface for running WPF applications on the ProGPU/Silk.NET platform. It is intended to let existing WPF applications move from the WindowsDesktop SDK to the portable ProGPU WPF platform by changing the project SDK while preserving normal WPF XAML, BAML, resource, theme, and code-behind behavior.

This initial package skeleton layers on the existing WindowsDesktop SDK so WPF markup compilation remains owned by the real `PresentationBuildTasks` implementation. It then selects the portable ProGPU/Silk.NET platform and redirects WPF framework references through either package references or local artifact roots while the port is still source-built.

Package mode is the intended delivery path. It references the ported managed WPF bundle through `ProGpuWpfManagedPackageId`/`ProGpuWpfManagedPackageVersion`, references the ProGPU runtime packages, injects the non-Windows portable activation bootstrap, and copies resolved managed and native runtime assets to the application output. Local-artifact mode remains available for source-tree validation by setting `ProGpuWpfManagedReferenceRoot` and `ProGpuReferenceRoot`.
