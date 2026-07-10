#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-0.1.0-preview.1}"
transport_target_framework="${PROGPU_WPF_TRANSPORT_TARGET_FRAMEWORK:-net10.0}"
source "${repo_root}/eng/progpu-preview-package-list.sh"

package_path() {
  local package_id="$1"
  echo "${package_output}/${package_id}.${dev_package_version}.nupkg"
}

package_assembly_name() {
  local package_id="$1"
  case "${package_id}" in
    LibreWPF.Interop)
      echo "ProGPU.Wpf.Interop"
      ;;
    LibreWPF.ProGPU)
      echo "ProGPU.Wpf"
      ;;
    ProGPU.System.Drawing.Common)
      echo "System.Drawing.Common"
      ;;
    ProGPU.SkiaSharp)
      echo "SkiaSharp"
      ;;
    *)
      echo "${package_id}"
      ;;
  esac
}

is_expected_package_artifact() {
  local file_name="$1"
  local package_id
  for package_id in "${all_packages[@]}"; do
    if [[ "${file_name}" == "${package_id}.${dev_package_version}.nupkg" ]]; then
      return 0
    fi
  done

  return 1
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

require_entry_contains() {
  local package_id="$1"
  local entry="$2"
  local expected="$3"
  local package_file
  package_file="$(package_path "${package_id}")"
  if ! unzip -p "${package_file}" "${entry}" | grep -Fq "${expected}"; then
    echo "Package ${package_id} entry '${entry}' is missing '${expected}'." >&2
    exit 1
  fi
}

reject_entry() {
  local package_id="$1"
  local entry="$2"
  local package_file
  local entries
  package_file="$(package_path "${package_id}")"
  entries="$(unzip -Z -1 "${package_file}")"
  if grep -Fxq "${entry}" <<<"${entries}"; then
    echo "Package ${package_id} should not contain '${entry}'." >&2
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

unexpected_package_found=0
while IFS= read -r -d '' artifact; do
  file_name="$(basename "${artifact}")"
  if ! is_expected_package_artifact "${file_name}"; then
    echo "Unexpected preview package artifact in output: ${artifact}" >&2
    unexpected_package_found=1
  fi
done < <(find "${package_output}" -maxdepth 1 -type f \( -name "*.nupkg" -o -name "*.snupkg" \) -print0)

if [[ "${unexpected_package_found}" -ne 0 ]]; then
  exit 1
fi

for package_id in "${all_packages[@]}"; do
  require_package "${package_id}"
  require_entry "${package_id}" "README.md"
  require_nuspec_contains "${package_id}" "<readme>README.md</readme>"
done

for package_id in "${runtime_packages[@]}"; do
  require_entry "${package_id}" "lib/net10.0/$(package_assembly_name "${package_id}").dll"
done

require_entry LibreWPF.ProGPU "lib/net10.0/ProGPU.Wpf.dll"
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"ProGPU.Backend\" version=\"${dev_package_version}\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"ProGPU.DirectX\" version=\"${dev_package_version}\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"ProGPU.Scene\" version=\"${dev_package_version}\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"LibreWPF.Interop\" version=\"${dev_package_version}\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"Silk.NET.Input\" version=\"2.23.0\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"Silk.NET.WebGPU\" version=\"2.23.0\""
require_nuspec_contains LibreWPF.ProGPU "dependency id=\"Silk.NET.Windowing\" version=\"2.23.0\""

require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.Backend\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.Layout\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.Scene\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"ProGPU.WinUI\" version=\"${dev_package_version}\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"Avalonia\" version=\""
require_nuspec_contains ProGPU.Avalonia "dependency id=\"Silk.NET.WebGPU\" version=\"2.23.0\""

require_nuspec_contains LibreWPF.Sdk "<packageType name=\"MSBuildSdk\" />"
require_entry LibreWPF.Sdk "Sdk/Sdk.props"
require_entry LibreWPF.Sdk "Sdk/LibreWPF.Sdk.Version.props"
require_entry_contains LibreWPF.Sdk "Sdk/LibreWPF.Sdk.Version.props" "<_LibreWpfSdkPackageVersion>${dev_package_version}</_LibreWpfSdkPackageVersion>"
require_entry LibreWPF.Sdk "Sdk/Sdk.targets"
require_entry LibreWPF.Sdk "targets/ProGPU.Wpf.Sdk.props"
require_entry LibreWPF.Sdk "targets/ProGPU.Wpf.Sdk.targets"
require_entry LibreWPF.Sdk "targets/ProGPU.Wpf.Sdk.PortableBootstrap.cs"
require_entry LibreWPF.Sdk "targets/ProGPU.Wpf.Sdk.Win32Compat.c"

transport_entries=(
  "lib/${transport_target_framework}/WindowsBase.dll"
  "lib/${transport_target_framework}/PresentationCore.dll"
  "lib/${transport_target_framework}/PresentationFramework.dll"
  "lib/${transport_target_framework}/PresentationFramework.Fluent.dll"
  "lib/${transport_target_framework}/System.Xaml.dll"
  "lib/${transport_target_framework}/System.Windows.Controls.Ribbon.dll"
  "lib/${transport_target_framework}/System.Printing.dll"
  "lib/${transport_target_framework}/UIAutomationTypes.dll"
  "lib/${transport_target_framework}/System.Private.Windows.Core.dll"
  "ref/${transport_target_framework}/WindowsBase.dll"
  "ref/${transport_target_framework}/PresentationCore.dll"
  "ref/${transport_target_framework}/PresentationFramework.dll"
  "ref/${transport_target_framework}/PresentationFramework.Fluent.dll"
  "ref/${transport_target_framework}/System.Xaml.dll"
  "ref/${transport_target_framework}/System.Windows.Controls.Ribbon.dll"
  "ref/${transport_target_framework}/System.Printing.dll"
  "ref/${transport_target_framework}/UIAutomationTypes.dll"
)

for entry in "${transport_entries[@]}"; do
  require_entry LibreWPF.Transport "${entry}"
done

reject_entry LibreWPF.Transport "lib/${transport_target_framework}/WindowsFormsIntegration.dll"
reject_entry LibreWPF.Transport "ref/${transport_target_framework}/WindowsFormsIntegration.dll"
require_entry LibreWPF.Transport "runtime.json"
require_nuspec_contains LibreWPF.Transport "<group targetFramework=\"${transport_target_framework}\" />"

echo "LibreWPF preview package audit succeeded for ${dev_package_version}."
