#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-11.0.0-dev}"
bundle_output="${PROGPU_WPF_PREVIEW_RELEASE_BUNDLE:-${package_output}/progpu-wpf-preview-${dev_package_version}.tar.gz}"

require_package_cache_entry() {
  local package_id="$1"
  local package_key
  package_key="$(printf '%s' "${package_id}" | tr '[:upper:]' '[:lower:]')"
  local package_dir="${smoke_root}/packages/${package_key}/${dev_package_version}"
  if [[ ! -f "${package_dir}/${package_key}.${dev_package_version}.nupkg" ]]; then
    echo "Expected restored package ${package_id} ${dev_package_version} in ${package_dir}." >&2
    exit 1
  fi
}

"${repo_root}/eng/progpu-preview-release-verify.sh"

if [[ -n "${PROGPU_WPF_PREVIEW_RELEASE_SDK_SMOKE_ROOT:-}" ]]; then
  smoke_root="${PROGPU_WPF_PREVIEW_RELEASE_SDK_SMOKE_ROOT}"
  rm -rf "${smoke_root}"
  mkdir -p "${smoke_root}"
else
  smoke_root="$(mktemp -d "${TMPDIR:-/tmp}/progpu-wpf-preview-sdk-smoke.XXXXXX")"
  trap 'rm -rf "${smoke_root}"' EXIT
fi

feed_dir="${smoke_root}/feed"
mkdir -p "${feed_dir}"
tar -xzf "${bundle_output}" -C "${feed_dir}"
project_dir="${feed_dir}/BundleSdkSmoke"
mkdir -p "${project_dir}"

cat >"${project_dir}/BundleSdkSmoke.csproj" <<PROJECT
<Project Sdk="ProGPU.Wpf.Sdk/${dev_package_version}">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net11.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <AssemblyName>BundleSdkSmoke</AssemblyName>
    <RootNamespace>BundleSdkSmoke</RootNamespace>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
PROJECT

cat >"${project_dir}/App.xaml" <<'XAML'
<Application
    x:Class="BundleSdkSmoke.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:sys="clr-namespace:System;assembly=System.Runtime">
    <Application.Resources>
        <sys:String x:Key="BundleSmokeText">Preview bundle SDK smoke</sys:String>
        <SolidColorBrush x:Key="BundleSmokeBrush" Color="#2B6CB0" />
    </Application.Resources>
</Application>
XAML

cat >"${project_dir}/App.xaml.cs" <<'CS'
using System;
using System.Windows;
using System.Windows.Media;

namespace BundleSdkSmoke;

public partial class App : Application
{
    private const string ExpectedText = "Preview bundle SDK smoke";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (string.Equals(Environment.GetEnvironmentVariable("PROGPU_WPF_BUNDLE_SDK_SMOKE_VALIDATE"), "1", StringComparison.Ordinal))
        {
            MainWindow window = new();
            if (!string.Equals(window.Message.Text, ExpectedText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected TextBlock text '{ExpectedText}', found '{window.Message.Text}'.");
            }

            if (!string.Equals(window.ActionButton.Content as string, ExpectedText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ElementName binding did not update the button content.");
            }

            if (FindResource("BundleSmokeBrush") is not SolidColorBrush brush || brush.Color.R != 0x2B || brush.Color.G != 0x6C || brush.Color.B != 0xB0)
            {
                throw new InvalidOperationException("Application resource lookup did not return the expected brush.");
            }

            Console.WriteLine("ProGPU WPF preview release bundle SDK smoke succeeded.");
            Shutdown(0);
            return;
        }

        new MainWindow().Show();
    }
}
CS

cat >"${project_dir}/MainWindow.xaml" <<'XAML'
<Window
    x:Class="BundleSdkSmoke.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="ProGPU WPF Preview Bundle SDK Smoke"
    Width="360"
    Height="220">
    <StackPanel x:Name="RootPanel" Margin="16">
        <TextBlock
            x:Name="Message"
            Foreground="{StaticResource BundleSmokeBrush}"
            Text="{DynamicResource BundleSmokeText}" />
        <Button
            x:Name="ActionButton"
            Margin="0,12,0,0"
            Content="{Binding ElementName=Message, Path=Text}" />
    </StackPanel>
</Window>
XAML

cat >"${project_dir}/MainWindow.xaml.cs" <<'CS'
using System.Windows;

namespace BundleSdkSmoke;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
CS

NUGET_PACKAGES="${smoke_root}/packages" "${dotnet}" build "${project_dir}/BundleSdkSmoke.csproj" -v:minimal

require_package_cache_entry "Microsoft.DotNet.Wpf.GitHub"
require_package_cache_entry "ProGPU.Wpf"
require_package_cache_entry "ProGPU.Wpf.Sdk"

NUGET_PACKAGES="${smoke_root}/packages" \
PROGPU_WPF_BUNDLE_SDK_SMOKE_VALIDATE=1 \
  "${dotnet}" run --project "${project_dir}/BundleSdkSmoke.csproj" --no-build -v:minimal
