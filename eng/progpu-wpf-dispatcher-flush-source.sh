#!/usr/bin/env bash
set -euo pipefail

# The preceding MessageBox gate builds the real source framework graph. Add
# the original WindowsBase tests and reuse that graph; never substitute an
# SDK/framework-reference Dispatcher or run a desktop acceptance application.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${PROGPU_WPF_DISPATCHER_FLUSH_DOTNET:-${repo_root}/.dotnet/dotnet}"
if [[ ! -x "${dotnet_command}" || -d "${dotnet_command}" ]]; then
  if [[ -n "${PROGPU_WPF_DISPATCHER_FLUSH_DOTNET+set}" ]]; then
    echo "PROGPU_WPF_DISPATCHER_FLUSH_DOTNET must name an executable file." >&2
    exit 2
  fi
  dotnet_command="$(command -v dotnet)"
fi
configuration="${CONFIGURATION:-Release}"
"${dotnet_command}" build \
  "${repo_root}/src/Microsoft.DotNet.Wpf/tests/UnitTests/WindowsBase.Tests/WindowsBase.Tests.csproj" \
  --configuration "${configuration}" -m:1 -p:UseSharedCompilation=false --verbosity minimal

base_assembly="${repo_root}/artifacts/bin/WindowsBase.Tests/${configuration}/net10.0-windows/WindowsBase.Tests.dll"
framework_assembly="${repo_root}/artifacts/bin/PresentationFramework.Tests/${configuration}/net10.0-windows/PresentationFramework.Tests.dll"
if [[ ! -f "${base_assembly}" || ! -f "${framework_assembly}" ]]; then
  echo "Both original source test assemblies are required for dispatcher flush contracts." >&2
  exit 1
fi
export LIBREWPF_TEST_MEDIA_BACKEND=Portable
"${dotnet_command}" "${base_assembly}" \
  --filter-class System.Windows.Threading.Tests.PortableDispatcherFlushTests \
  --minimum-expected-tests 18 --fail-skips on --timeout 60s --no-progress
"${dotnet_command}" "${framework_assembly}" \
  --filter-class System.Windows.PortableDispatcherFlushTests \
  --minimum-expected-tests 11 --fail-skips on --timeout 60s --no-progress
