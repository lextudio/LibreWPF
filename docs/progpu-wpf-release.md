# ProGPU WPF Preview Release Workflow

The ProGPU WPF preview release uses the package list in `eng/progpu-preview-package-list.sh`.
That package set is what users need to consume the custom `ProGPU.Wpf.Sdk` and run normal WPF
projects on the ProGPU/Silk.NET platform.

## NuGet Packages

- `Microsoft.DotNet.Wpf.GitHub`
- `ProGPU.Backend`
- `ProGPU.DirectX`
- `ProGPU.Transpiler`
- `ProGPU.Compute`
- `ProGPU.Vector`
- `ProGPU.Text`
- `ProGPU.Scene`
- `ProGPU.Layout`
- `ProGPU.Virtualization`
- `ProGPU.WinUI`
- `ProGPU.Avalonia`
- `ProGPU.Wpf.Interop`
- `ProGPU.Wpf`
- `ProGPU.Wpf.Sdk`

## Local Preview Build

```bash
PROGPU_WPF_DEV_PACKAGE_VERSION=11.0.0-dev ./eng/progpu-wpf-sdk-ci.sh
```

The SDK CI script builds the ProGPU runtime packages, managed WPF transport assemblies, `ProGPU.Wpf`,
and `ProGPU.Wpf.Sdk`, then audits the packages, writes the preview manifest, creates a release bundle,
verifies the bundle, and runs package-mode SDK smoke tests.

## GitHub Actions

- `ProGPU WPF Build` runs the SDK package/no-source-change smoke on macOS with submodules checked out.
- `ProGPU WPF Docs` verifies that this document and README stay aligned with the preview package list.
- `ProGPU WPF Release` runs the same SDK CI gate, uploads packages/bundle artifacts, and can publish to NuGet.org.

## NuGet Publishing

Publishing is gated by repository secret `NUGET_API_KEY`.

- Manual workflow runs publish only when the `publish` input is true.
- Tags named `progpu-wpf-v*` publish after validation.
- The release job pushes all preview `.nupkg` files from `artifacts/packages/Release/NonShipping`.

## SDK Switch Contract

Existing WPF applications should be able to switch only the project SDK:

```xml
<Project Sdk="ProGPU.Wpf.Sdk/11.0.0-dev">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net11.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

No application source or XAML changes should be required for normal WPF code. Windows-specific interop,
unsupported DirectX features, and native-hosting edge cases remain tracked in `reports/`.
