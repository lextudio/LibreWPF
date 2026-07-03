#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-11.0.0-dev}"
manifest_path="${PROGPU_WPF_PREVIEW_PACKAGE_MANIFEST:-${package_output}/progpu-wpf-preview-packages-${dev_package_version}.json}"
bundle_output="${PROGPU_WPF_PREVIEW_RELEASE_BUNDLE:-${package_output}/progpu-wpf-preview-${dev_package_version}.tar.gz}"

package_ids=(
  Microsoft.DotNet.Wpf.GitHub
  ProGPU.Backend
  ProGPU.DirectX
  ProGPU.Transpiler
  ProGPU.Compute
  ProGPU.Vector
  ProGPU.Text
  ProGPU.Scene
  ProGPU.Layout
  ProGPU.Virtualization
  ProGPU.WinUI
  ProGPU.Avalonia
  ProGPU.Wpf.Interop
  ProGPU.Wpf
  ProGPU.Wpf.Sdk
)

"${repo_root}/eng/progpu-preview-package-manifest.sh"

bundle_dir="$(dirname "${bundle_output}")"
mkdir -p "${bundle_dir}"
rm -f "${bundle_output}"

archive_entries=()
manifest_name="$(basename "${manifest_path}")"
archive_entries+=("${manifest_name}")

if [[ ! -f "${manifest_path}" ]]; then
  echo "Missing preview package manifest ${manifest_path}." >&2
  exit 1
fi

for package_id in "${package_ids[@]}"; do
  package_name="${package_id}.${dev_package_version}.nupkg"
  package_file="${package_output}/${package_name}"
  if [[ ! -f "${package_file}" ]]; then
    echo "Missing package ${package_file}." >&2
    exit 1
  fi
  archive_entries+=("${package_name}")
done

(
  cd "${package_output}"
  COPYFILE_DISABLE=1 tar -czf "${bundle_output}" "${archive_entries[@]}"
)

echo "ProGPU WPF preview release bundle written to ${bundle_output}."
