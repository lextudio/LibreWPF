#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-11.0.0-dev}"
source "${repo_root}/eng/progpu-preview-package-list.sh"

package_path() {
  local package_id="$1"
  echo "${package_output}/${package_id}.${dev_package_version}.nupkg"
}

require_package() {
  local package_id="$1"
  local package_file
  package_file="$(package_path "${package_id}")"
  if [[ ! -f "${package_file}" ]]; then
    echo "Missing package ${package_file}." >&2
    exit 1
  fi
}

require_entry() {
  local package_id="$1"
  local entry="$2"
  local package_file
  local entries
  package_file="$(package_path "${package_id}")"
  entries="$(unzip -Z -1 "${package_file}")"
  if ! grep -Fxq "${entry}" <<<"${entries}"; then
    echo "Package ${package_id} is missing '${entry}'." >&2
    exit 1
  fi
}

require_nuspec_contains() {
  local package_id="$1"
  local expected="$2"
  local package_file
  package_file="$(package_path "${package_id}")"
  if ! unzip -p "${package_file}" "${package_id}.nuspec" | grep -Fq "${expected}"; then
    echo "Package ${package_id} nuspec is missing '${expected}'." >&2
    exit 1
  fi
}

runtime_packages=("${progpu_preview_runtime_package_ids[@]}")
all_packages=("${progpu_preview_package_ids[@]}")

for package_id in "${all_packages[@]}"; do
  require_package "${package_id}"
  require_entry "${package_id}" "README.md"
  require_nuspec_contains "${package_id}" "<readme>README.md</readme>"
done

for package_id in "${runtime_packages[@]}"; do
  require_entry "${package_id}" "lib/net10.0/${package_id}.dll"
done

require_entry ProGPU.Wpf "lib/net10.0/ProGPU.Wpf.dll"
require_nuspec_contains ProGPU.Wpf "dependency id=\"ProGPU.Backend\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Wpf "dependency id=\"ProGPU.DirectX\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Wpf "dependency id=\"ProGPU.Scene\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Wpf "dependency id=\"ProGPU.Wpf.Interop\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Wpf "dependency id=\"Silk.NET.Input\" version=\"2.23.0\""
require_nuspec_contains ProGPU.Wpf "dependency id=\"Silk.NET.WebGPU\" version=\"2.23.0\""
require_nuspec_contains ProGPU.Wpf "dependency id=\"Silk.NET.Windowing\" version=\"2.23.0\""

require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.Backend\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.Layout\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.Scene\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.WinUI\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"Avalonia\" version=\"12.0.3\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"Silk.NET.WebGPU\" version=\"2.23.0\""

require_nuspec_contains ProGPU.Wpf.Sdk "<packageType name=\"MSBuildSdk\" />"
require_entry ProGPU.Wpf.Sdk "Sdk/Sdk.props"
require_entry ProGPU.Wpf.Sdk "Sdk/Sdk.targets"
require_entry ProGPU.Wpf.Sdk "targets/ProGPU.Wpf.Sdk.props"
require_entry ProGPU.Wpf.Sdk "targets/ProGPU.Wpf.Sdk.targets"
require_entry ProGPU.Wpf.Sdk "targets/ProGPU.Wpf.Sdk.PortableBootstrap.cs"
require_entry ProGPU.Wpf.Sdk "targets/ProGPU.Wpf.Sdk.Win32Compat.c"

transport_entries=(
  lib/net11.0/WindowsBase.dll
  lib/net11.0/PresentationCore.dll
  lib/net11.0/PresentationFramework.dll
  lib/net11.0/PresentationFramework.Fluent.dll
  lib/net11.0/System.Xaml.dll
  lib/net11.0/System.Windows.Controls.Ribbon.dll
  lib/net11.0/System.Printing.dll
  lib/net11.0/UIAutomationTypes.dll
  ref/net11.0/WindowsBase.dll
  ref/net11.0/PresentationCore.dll
  ref/net11.0/PresentationFramework.dll
  ref/net11.0/PresentationFramework.Fluent.dll
  ref/net11.0/System.Xaml.dll
  ref/net11.0/System.Windows.Controls.Ribbon.dll
  ref/net11.0/System.Printing.dll
  ref/net11.0/UIAutomationTypes.dll
)

for entry in "${transport_entries[@]}"; do
  require_entry Microsoft.DotNet.Wpf.GitHub "${entry}"
done

require_entry Microsoft.DotNet.Wpf.GitHub "runtime.json"
require_nuspec_contains Microsoft.DotNet.Wpf.GitHub "<group targetFramework=\"net11.0\" />"

echo "ProGPU WPF preview package audit succeeded for ${dev_package_version}."
