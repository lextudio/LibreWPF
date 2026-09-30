#!/usr/bin/env bash
set -euo pipefail

# The preceding source MessageBox gate builds this original test project and
# its source dependency graph. Reuse that output, not another WPF ABI or package
# fixture. This checks actual host ownership, not AvalonDock application rendering.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -n "${PROGPU_WPF_VISUAL_HOST_SOURCE_DOTNET+set}" ]]; then
  dotnet_command="${PROGPU_WPF_VISUAL_HOST_SOURCE_DOTNET}"
  if [[ ! -x "${dotnet_command}" || -d "${dotnet_command}" ]]; then
    echo "PROGPU_WPF_VISUAL_HOST_SOURCE_DOTNET must name an executable file: ${dotnet_command}" >&2
    exit 2
  fi
elif [[ -x "${repo_root}/.dotnet/dotnet" ]]; then
  dotnet_command="${repo_root}/.dotnet/dotnet"
else
  dotnet_command="$(command -v dotnet)"
fi
configuration="${CONFIGURATION:-Release}"
assembly="${repo_root}/artifacts/bin/PresentationFramework.Tests/${configuration}/net10.0-windows/PresentationFramework.Tests.dll"
if [[ ! -f "${assembly}" ]]; then
  echo "Build the original PresentationFramework.Tests source project before running portable visual host contracts: ${assembly}" >&2
  exit 1
fi

export LIBREWPF_TEST_MEDIA_BACKEND=Portable
exec "${dotnet_command}" "${assembly}" \
  --filter-class System.Windows.PortableVisualHwndHostTests \
  --minimum-expected-tests 8 --fail-skips on --timeout 60s --no-progress
