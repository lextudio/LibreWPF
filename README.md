# LibreWPF ProGPU Port

This branch ports WPF onto the ProGPU/Silk.NET platform while reusing as much managed WPF code as possible. The public package brand is LibreWPF, with the custom SDK package `LibreWPF.Sdk`, so an existing WPF app can switch the project SDK and keep normal WPF source and XAML unchanged.

Current focus areas:

- Reuse WPF managed code for application model, dependency properties, layout, controls, data binding, documents, XAML, resources, themes, and the XAML compiler.
- Replace Windows-only MIL/D3D rendering with ProGPU WebGPU composition, shaders, DirectX-compatible shims, GPU hit testing, and Silk.NET windowing/input.
- Package the runtime as a preview SDK and NuGet set that can be consumed from a local feed or NuGet.org.
- Keep third-party validation active through basic WPF apps, Xceed Toolkit/AvalonDock, Xceed paid Toolkit/DataGrid, SciChart MVP, ProGPU Avalonia package smoke, and no-source-change SDK smoke tests.

## SDK Switch

After adding the preview feed, existing WPF projects should only need the SDK change:

```xml
<Project Sdk="LibreWPF.Sdk/0.1.0-preview.1">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

Windows-only interop and unsupported native/DirectX APIs remain the expected exceptions while the portable platform layer is completed.

## NuGet Packages

The preview package set is defined in `eng/progpu-preview-package-list.sh` and validated by the release workflow.

| Package | Purpose | Source |
| --- | --- | --- |
| `LibreWPF.Transport` | Ported managed WPF transport assemblies, refs, themes, XAML build tasks, and runtime metadata. | `packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj` |
| `ProGPU.Backend` | WebGPU device, swapchain, Silk.NET windowing, and platform backend services. | `external/ProGPU/src/ProGPU.Backend/ProGPU.Backend.csproj` |
| `ProGPU.DirectX` | DirectX-compatible facade for SciChart and future D3D-style interop on ProGPU/WebGPU. | `external/ProGPU/src/ProGPU.DirectX/ProGPU.DirectX.csproj` |
| `ProGPU.Transpiler` | Shader/source transformation helpers used by generated GPU pipelines. | `external/ProGPU/src/ProGPU.Transpiler/ProGPU.Transpiler.csproj` |
| `ProGPU.Compute` | Compute pipeline helpers for GPU effects, indexes, and acceleration structures. | `external/ProGPU/src/ProGPU.Compute/ProGPU.Compute.csproj` |
| `ProGPU.Vector` | Vector paths, geometry, brushes, pens, and rasterization data models. | `external/ProGPU/src/ProGPU.Vector/ProGPU.Vector.csproj` |
| `ProGPU.Text` | Text layout, glyph metrics, and GPU-ready text rendering helpers. | `external/ProGPU/src/ProGPU.Text/ProGPU.Text.csproj` |
| `ProGPU.Scene` | Scene graph, compositor commands, retained visuals, effects, and presentation primitives. | `external/ProGPU/src/ProGPU.Scene/ProGPU.Scene.csproj` |
| `ProGPU.Layout` | Measure/arrange layout substrate shared by ProGPU UI adapters. | `external/ProGPU/src/ProGPU.Layout/ProGPU.Layout.csproj` |
| `ProGPU.Virtualization` | Virtualization helpers for large retained visual and item surfaces. | `external/ProGPU/src/ProGPU.Virtualization/ProGPU.Virtualization.csproj` |
| `ProGPU.WinUI` | WinUI-shaped controls and app model implemented on ProGPU. | `external/ProGPU/src/ProGPU.WinUI/ProGPU.WinUI.csproj` |
| `ProGPU.Avalonia` | Avalonia integration and compositor backend adapter used by package smoke validation. | `external/ProGPU/src/ProGPU.Avalonia/ProGPU.Avalonia.csproj` |
| `LibreWPF.Interop` | Shared WPF interop contracts consumed by the WPF bridge and ProGPU runtime. | `external/ProGPU/src/ProGPU.Wpf.Interop/ProGPU.Wpf.Interop.csproj` |
| `LibreWPF.ProGPU` | WPF-to-ProGPU host, retained/source replay bridge, Silk.NET input/windowing, and compositor adapter. | `src/ProGPU.Wpf/ProGPU.Wpf.csproj` |
| `LibreWPF.Sdk` | Custom MSBuild SDK that redirects WPF apps to the ProGPU/Silk.NET platform. | `packaging/ProGPU.Wpf.Sdk/ProGPU.Wpf.Sdk.ArchNeutral.csproj` |

## Build And Release

```bash
PROGPU_WPF_DEV_PACKAGE_VERSION=0.1.0-preview.1 ./eng/progpu-wpf-sdk-ci.sh
```

The SDK CI script builds ProGPU runtime packages, managed WPF transport assemblies, `LibreWPF.ProGPU`, and `LibreWPF.Sdk`, then audits the packages, writes the preview manifest, creates and verifies the release bundle, and runs package-mode SDK smoke tests.

GitHub workflows:

- `LibreWPF Build` runs the SDK package/no-source-change smoke on macOS.
- `LibreWPF Docs` verifies README and release docs against the preview package list.
- `LibreWPF Release` builds preview packages/bundle artifacts and can publish to NuGet.org with `NUGET_API_KEY`.

See [docs/progpu-wpf-release.md](docs/progpu-wpf-release.md) and the ongoing porting reports in [reports/](reports/).

## Original Upstream README

# Windows Presentation Foundation (WPF)
[![.NET Foundation](https://img.shields.io/badge/.NET%20Foundation-blueviolet.svg)](https://www.dotnetfoundation.org/)
[![Build Status](https://dnceng.visualstudio.com/public/_apis/build/status/dotnet/wpf/dotnet-wpf%20CI)](https://dnceng.visualstudio.com/public/_build/latest?definitionId=270)
[![codecov](https://codecov.io/gh/dotnet/wpf/branch/main/graph/badge.svg?flag=production)](https://codecov.io/gh/dotnet/wpf)
[![MIT License](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)

Windows Presentation Foundation (WPF) is a UI framework for building Windows desktop applications. 

WPF supports a broad set of application development features, including an application model, resources, controls, graphics, layout, data binding and documents. WPF uses the Extensible Application Markup Language (XAML) to provide a declarative model for application programming.

WPF's rendering is vector-based, which enables applications to look great on high DPI monitors, as they can be infinitely scaled. WPF also includes a flexible hosting model, which makes it straightforward to host a video in a button, for example.

Visual Studio's designer, as well as Visual Studio Blend, make it easy to build WPF applications, with drag-and-drop and/or direct editing of XAML markup.

As of .NET 6.0, WPF supports ARM64. 

See the [WPF Roadmap](roadmap.md) to learn about project priorities, status and ship dates.

[WinForms](https://github.com/dotnet/winforms) is another UI framework for building Windows desktop applications that is supported on .NET (7.0.x/6.0.x). WPF and WinForms applications only run on Windows. They are part of the `Microsoft.NET.Sdk.WindowsDesktop` SDK. You are recommended to use the most recent version of [Visual Studio](https://visualstudio.microsoft.com/downloads/) to develop WPF and WinForms applications for .NET.  

To build the WPF repo and contribute features and fixes for .NET 8.0, [Visual Studio 2022 Preview](https://visualstudio.microsoft.com/vs/preview/) is required.

## Getting started

* [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0), [.NET 7.0 SDK](https://dotnet.microsoft.com/download/dotnet/7.0), [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
* [.NET Preview SDKs](https://github.com/dotnet/dotnet/blob/main/docs/builds-table.md)
* [Getting started instructions](Documentation/getting-started.md)
* [Contributing guide](Documentation/contributing.md)
* [Migrating .NET Framework WPF Apps to .NET Core](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/migration/)

## Status

- We are currently developing WPF for .NET 10. 

See the [WPF roadmap](roadmap.md) to learn about the schedule for specific WPF components.

Test published at [separate repo](https://github.com/dotnet/wpf-test) Tests and have limited coverage at this time. We will add more tests, however, it will be a progressive process.

The Visual Studio WPF designer is now available as part of Visual Studio 2019. 

## How to Engage, Contribute and Provide Feedback

Some of the best ways to contribute are to try things out, file bugs, join in design conversations, and fix issues.

* This repo defines [contributing guidelines](Documentation/contributing.md) and also follows the more general [.NET Core contributing guide](https://github.com/dotnet/runtime/blob/main/CONTRIBUTING.md).
* If you have a question or have found a bug, [file an issue](https://github.com/dotnet/wpf/issues/new).
* Use [daily builds](Documentation/getting-started.md#installation) if you want to contribute and stay up to date with the team.

### .NET Framework issues

Issues with .NET Framework, including WPF, should be filed on [VS developer community](https://developercommunity.visualstudio.com/spaces/61/index.html), 
or [Product Support](https://support.microsoft.com/en-us/contactus?ws=support).
They should not be filed on this repo.

## Relationship to .NET Framework

This code base is a fork of the WPF code in the .NET Framework. .NET Core 3.0 was released with a goal of WPF having parity with the .NET Framework version. Over time, the two implementations may diverge.

The [Update on .NET Core 3.0 and .NET Framework 4.8](https://devblogs.microsoft.com/dotnet/update-on-net-core-3-0-and-net-framework-4-8/) provides a good description of the forward-looking differences between .NET Core and .NET Framework.

This [update](https://devblogs.microsoft.com/dotnet/net-core-is-the-future-of-net/) states how going forward .NET Core is the future of .NET. and .NET Framework 4.8 will be the last major version of .NET Framework.


## Code of Conduct

This project uses the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct) to define expected conduct in our community. Instances of abusive, harassing, or otherwise unacceptable behavior may be reported by contacting a project maintainer at conduct@dotnetfoundation.org.

## Reporting security issues and security bugs

Security issues and bugs should be reported privately, via email, to the Microsoft Security Response Center (MSRC) <secure@microsoft.com>. You should receive a response within 24 hours. If for some reason you do not, please follow up via email to ensure we received your original message. Further information, including the MSRC PGP key, can be found in the [Security TechCenter](https://www.microsoft.com/msrc/faqs-report-an-issue).

Also see info about related [Microsoft .NET Core and ASP.NET Core Bug Bounty Program](https://www.microsoft.com/msrc/bounty-dot-net-core).

## License

.NET Core (including the WPF repo) is licensed under the [MIT license](LICENSE.TXT).

## .NET Foundation

.NET Core WPF is a [.NET Foundation](https://www.dotnetfoundation.org/projects) project.

See the [.NET home repo](https://github.com/Microsoft/dotnet) to find other .NET-related projects.
