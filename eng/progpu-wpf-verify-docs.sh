#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${repo_root}/eng/progpu-preview-package-list.sh"

require_text() {
  local file="$1"
  local text="$2"
  if ! grep -Fq "${text}" "${repo_root}/${file}"; then
    echo "Missing '${text}' in ${file}." >&2
    exit 1
  fi
}

require_text ".github/workflows/progpu-wpf-sdk.yml" "./eng/progpu-wpf-sdk-ci.sh"
require_text ".github/workflows/progpu-wpf-release.yml" "NUGET_API_KEY"
require_text ".github/workflows/progpu-wpf-release.yml" "librewpf-v*"
require_text ".github/workflows/progpu-wpf-release.yml" "refs/tags/librewpf-v"
require_text ".github/workflows/progpu-wpf-release.yml" "librewpf-packages-"
require_text ".github/workflows/progpu-wpf-docs.yml" "librewpf-docs"
require_text "docs/progpu-wpf-release.md" "LibreWPF.Sdk"

for package_id in "${progpu_preview_package_ids[@]}"; do
  require_text "README.md" "| \`${package_id}\` |"
  require_text "docs/progpu-wpf-release.md" "\`${package_id}\`"
done

echo "LibreWPF documentation/package table verification succeeded."
