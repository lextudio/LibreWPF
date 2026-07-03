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

require_file() {
  local file="$1"
  if [[ ! -f "${file}" ]]; then
    echo "Missing preview release artifact ${file}." >&2
    exit 1
  fi
}

require_file "${bundle_output}"
require_file "${sidecar_output}"

bundle_sha256="$(file_sha256 "${bundle_output}")"
sidecar_sha256="$(awk '{print $1}' "${sidecar_output}")"
sidecar_file="$(awk '{print $2}' "${sidecar_output}")"
if [[ "${sidecar_sha256}" != "${bundle_sha256}" ]]; then
  echo "Preview release bundle checksum sidecar does not match ${bundle_output}." >&2
  exit 1
fi

if [[ "${sidecar_file}" != "$(basename "${bundle_output}")" ]]; then
  echo "Preview release bundle checksum sidecar references '${sidecar_file}' instead of '$(basename "${bundle_output}")'." >&2
  exit 1
fi

archive_entries=()
readme_name="$(basename "${release_readme_path}")"
manifest_name="$(basename "${manifest_path}")"
archive_entries+=("${readme_name}")
archive_entries+=("${manifest_name}")
for package_id in "${package_ids[@]}"; do
  archive_entries+=("${package_id}.${dev_package_version}.nupkg")
done

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

extract_dir="$(mktemp -d "${TMPDIR:-/tmp}/progpu-wpf-preview-release-verify.XXXXXX")"
trap 'rm -rf "${extract_dir}"' EXIT
tar -xzf "${bundle_output}" -C "${extract_dir}"

if [[ -f "${release_readme_path}" ]] && ! cmp -s "${release_readme_path}" "${extract_dir}/${readme_name}"; then
  echo "Preview release bundle README ${readme_name} does not match ${release_readme_path}." >&2
  exit 1
fi

if [[ -f "${manifest_path}" ]] && ! cmp -s "${manifest_path}" "${extract_dir}/${manifest_name}"; then
  echo "Preview release bundle manifest ${manifest_name} does not match ${manifest_path}." >&2
  exit 1
fi

readme_file="${extract_dir}/${readme_name}"
if ! grep -q "ProGPU.Wpf.Sdk/${dev_package_version}" "${readme_file}" \
  || ! grep -q "shasum -a 256 -c progpu-wpf-preview-${dev_package_version}.tar.gz.sha256" "${readme_file}" \
  || ! grep -q "No ProGPU-specific source or XAML changes should be required" "${readme_file}"; then
  echo "Preview release bundle README is missing required SDK switch or verification guidance." >&2
  exit 1
fi

node - "${extract_dir}" "${manifest_name}" "${dev_package_version}" "${package_ids[@]}" <<'NODE'
const fs = require("fs");
const crypto = require("crypto");
const path = require("path");

const [extractDirectory, manifestName, devPackageVersion, ...packageIds] = process.argv.slice(2);
const manifestPath = path.join(extractDirectory, manifestName);
const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));

function fail(message) {
  console.error(message);
  process.exit(1);
}

if (manifest.schemaVersion !== 2) {
  fail(`Expected preview manifest schemaVersion 2, found ${manifest.schemaVersion}.`);
}

if (manifest.version !== devPackageVersion) {
  fail(`Expected preview manifest version ${devPackageVersion}, found ${manifest.version}.`);
}

if (!manifest.source || !manifest.source.wpfCommit || !manifest.source.progpuCommit) {
  fail("Preview manifest source provenance is missing WPF or ProGPU commit information.");
}

if (manifest.packageDirectory !== ".") {
  fail(`Expected preview manifest packageDirectory '.', found ${manifest.packageDirectory}.`);
}

if (!Array.isArray(manifest.packages) || manifest.packages.length !== packageIds.length) {
  fail(`Expected ${packageIds.length} preview manifest package entries, found ${manifest.packages?.length}.`);
}

const expectedIds = new Set(packageIds);
for (const [index, packageId] of packageIds.entries()) {
  const entry = manifest.packages[index];
  if (!entry || entry.id !== packageId) {
    fail(`Expected preview package entry ${index} to be ${packageId}, found ${entry?.id}.`);
  }

  if (!expectedIds.delete(entry.id)) {
    fail(`Unexpected or duplicate preview package id ${entry.id}.`);
  }

  const expectedFile = `${packageId}.${devPackageVersion}.nupkg`;
  if (entry.file !== expectedFile) {
    fail(`Expected preview package ${packageId} file ${expectedFile}, found ${entry.file}.`);
  }

  const packagePath = path.join(extractDirectory, expectedFile);
  if (!fs.existsSync(packagePath)) {
    fail(`Missing preview package ${packagePath}.`);
  }

  const bytes = fs.readFileSync(packagePath);
  if (entry.sizeBytes !== bytes.length) {
    fail(`Preview package ${expectedFile} size mismatch: manifest ${entry.sizeBytes}, actual ${bytes.length}.`);
  }

  const sha256 = crypto.createHash("sha256").update(bytes).digest("hex");
  if (entry.sha256 !== sha256) {
    fail(`Preview package ${expectedFile} SHA-256 mismatch.`);
  }
}

if (expectedIds.size !== 0) {
  fail(`Missing preview package ids: ${Array.from(expectedIds).join(", ")}.`);
}
NODE

echo "ProGPU WPF preview release bundle verification succeeded for ${bundle_output}."
