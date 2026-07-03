#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-11.0.0-dev}"
manifest_path="${PROGPU_WPF_PREVIEW_PACKAGE_MANIFEST:-${package_output}/progpu-wpf-preview-packages-${dev_package_version}.json}"
bundle_output="${PROGPU_WPF_PREVIEW_RELEASE_BUNDLE:-${package_output}/progpu-wpf-preview-${dev_package_version}.tar.gz}"
sidecar_output="${PROGPU_WPF_PREVIEW_RELEASE_BUNDLE_SHA256:-${bundle_output}.sha256}"
release_readme_path="${PROGPU_WPF_PREVIEW_RELEASE_README:-${package_output}/README.md}"
source "${repo_root}/eng/progpu-preview-package-list.sh"

package_ids=("${progpu_preview_package_ids[@]}")

file_sha256() {
  local file="$1"
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "${file}" | awk '{print $1}'
  else
    sha256sum "${file}" | awk '{print $1}'
  fi
}

"${repo_root}/eng/progpu-preview-package-manifest.sh"

bundle_dir="$(dirname "${bundle_output}")"
sidecar_dir="$(dirname "${sidecar_output}")"
readme_dir="$(dirname "${release_readme_path}")"
mkdir -p "${bundle_dir}" "${sidecar_dir}" "${readme_dir}"
rm -f "${bundle_output}" "${sidecar_output}"

cat >"${release_readme_path}" <<README
# ProGPU WPF Preview ${dev_package_version}

This preview bundle contains the package set for running WPF applications on the ProGPU/Silk.NET platform through the custom \`ProGPU.Wpf.Sdk\`.

## Contents

- \`progpu-wpf-preview-packages-${dev_package_version}.json\` records the exact package list, source commits, package sizes, and SHA-256 hashes.
- \`Microsoft.DotNet.Wpf.GitHub.${dev_package_version}.nupkg\` contains the ported managed WPF transport assemblies.
- \`ProGPU.Wpf.Sdk.${dev_package_version}.nupkg\` is the custom MSBuild SDK package.
- \`ProGPU.Wpf.${dev_package_version}.nupkg\` and the \`ProGPU.*.${dev_package_version}.nupkg\` packages contain the bridge, compositor, rendering, DirectX, WinUI, Avalonia, and Silk.NET-backed runtime dependencies.

Verify the archive with the adjacent checksum file:

\`\`\`bash
shasum -a 256 -c progpu-wpf-preview-${dev_package_version}.tar.gz.sha256
\`\`\`

Use the extracted directory as a local NuGet source, then switch an existing WPF project to the custom SDK:

\`\`\`xml
<Project Sdk="ProGPU.Wpf.Sdk/${dev_package_version}">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net11.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
\`\`\`

No ProGPU-specific source or XAML changes should be required for normal WPF application code. Windows-only interop remains the expected exception while the portable platform layer is still being completed.

For repository validation, run:

\`\`\`bash
./eng/progpu-preview-release-verify.sh
./eng/progpu-preview-release-sdk-smoke.sh
\`\`\`
README

archive_entries=()
readme_name="$(basename "${release_readme_path}")"
manifest_name="$(basename "${manifest_path}")"
archive_entries+=("${readme_name}")
archive_entries+=("${manifest_name}")

if [[ ! -f "${manifest_path}" ]]; then
  echo "Missing preview package manifest ${manifest_path}." >&2
  exit 1
fi

if [[ ! -f "${release_readme_path}" ]]; then
  echo "Missing preview release README ${release_readme_path}." >&2
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

expected_entries="$(printf '%s\n' "${archive_entries[@]}")"
actual_entries="$(tar -tzf "${bundle_output}")"
if [[ "${actual_entries}" != "${expected_entries}" ]]; then
  echo "Preview release bundle entries do not match the expected manifest/package set." >&2
  echo "Expected entries:" >&2
  printf '%s\n' "${archive_entries[@]}" >&2
  echo "Actual entries:" >&2
  tar -tzf "${bundle_output}" >&2
  exit 1
fi

bundle_sha256="$(file_sha256 "${bundle_output}")"
printf '%s  %s\n' "${bundle_sha256}" "$(basename "${bundle_output}")" >"${sidecar_output}"

echo "ProGPU WPF preview release bundle written to ${bundle_output}."
echo "ProGPU WPF preview release bundle SHA-256 written to ${sidecar_output}."
