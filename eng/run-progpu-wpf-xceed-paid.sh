#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

hydrate_env_from_launchctl() {
  local name="$1"
  if [[ -n "${!name:-}" ]]; then
    return 0
  fi

  if ! command -v launchctl >/dev/null 2>&1; then
    return 0
  fi

  local value
  value="$(launchctl getenv "${name}" 2>/dev/null || true)"
  if [[ -n "${value}" ]]; then
    export "${name}=${value}"
  fi
}

hydrate_env_from_launchctl "XCEED_TOOLKIT_LICENSE_KEY"
hydrate_env_from_launchctl "XCEED_DATAGRID_LICENSE_KEY"

package_output="${PROGPU_WPF_PACKAGE_OUTPUT:-${repo_root}/artifacts/packages/Release/NonShipping}"
sdk_package="${package_output}/ProGPU.Wpf.Sdk.11.0.0-dev.nupkg"
xceed_project="${repo_root}/samples/ProGPU.Wpf.XceedPaidApp/ProGPU.Wpf.XceedPaidApp.csproj"
xceed_output="${repo_root}/artifacts/bin/ProGPU.Wpf.XceedPaidApp/Debug/net11.0-windows"

apphost_name="ProGPU.Wpf.XceedPaidApp"
case "$(uname -s 2>/dev/null || echo unknown)" in
  MINGW*|MSYS*|CYGWIN*)
    apphost_name="${apphost_name}.exe"
    ;;
esac

if [[ "${PROGPU_WPF_XCEED_PAID_SKIP_REBUILD_PACKAGES:-0}" == "1" ]]; then
  rebuild_packages=0
elif [[ "${PROGPU_WPF_XCEED_PAID_REBUILD_PACKAGES:-0}" == "1" || ! -f "${sdk_package}" ]]; then
  rebuild_packages=1
else
  rebuild_packages=0
  for source_path in \
    "${repo_root}/src/ProGPU.Wpf" \
    "${repo_root}/packaging/ProGPU.Wpf.Sdk" \
    "${repo_root}/external/ProGPU/src/ProGPU.Backend" \
    "${repo_root}/external/ProGPU/src/ProGPU.Compute" \
    "${repo_root}/external/ProGPU/src/ProGPU.DirectX" \
    "${repo_root}/external/ProGPU/src/ProGPU.Layout" \
    "${repo_root}/external/ProGPU/src/ProGPU.Scene" \
    "${repo_root}/external/ProGPU/src/ProGPU.Text" \
    "${repo_root}/external/ProGPU/src/ProGPU.Transpiler" \
    "${repo_root}/external/ProGPU/src/ProGPU.Vector" \
    "${repo_root}/external/ProGPU/src/ProGPU.Wpf.Interop" \
    "${repo_root}/external/ProGPU/src/PresentationCore" \
    "${repo_root}/external/ProGPU/src/WindowsBase"; do
    if find "${source_path}" \
      \( -path '*/bin' -o -path '*/obj' \) -prune -o \
      -type f \( -name '*.cs' -o -name '*.props' -o -name '*.targets' -o -name '*.csproj' \) \
      -newer "${sdk_package}" -print -quit | grep -q .; then
      rebuild_packages=1
      break
    fi
  done
fi

if [[ "${rebuild_packages}" == "1" ]]; then
  echo "Building ProGPU WPF SDK packages before launching paid Xceed app..."
  PROGPU_WPF_HELLO_REBUILD_PACKAGES=0 \
  PROGPU_WPF_HELLO_RUN_VALIDATE=0 \
  PROGPU_WPF_HELLO_LIVE_VALIDATE=0 \
  PROGPU_WPF_MVP_REBUILD_PACKAGES=0 \
  PROGPU_WPF_MVP_VALIDATE=0 \
  PROGPU_WPF_MVP_RUN_VALIDATE=0 \
  PROGPU_WPF_MVP_LIVE_VALIDATE=0 \
  PROGPU_WPF_TOOLKIT_REBUILD_PACKAGES=0 \
  PROGPU_WPF_TOOLKIT_VALIDATE=0 \
  PROGPU_WPF_TOOLKIT_RUN_VALIDATE=0 \
  PROGPU_WPF_TOOLKIT_LIVE_VALIDATE=0 \
    "${repo_root}/eng/progpu-wpf-sdk-ci.sh"
fi

rm -rf \
  "${repo_root}/artifacts/bin/ProGPU.Wpf.XceedPaidApp" \
  "${repo_root}/artifacts/obj/ProGPU.Wpf.XceedPaidApp" \
  "${repo_root}/artifacts/nuget/ProGPU.Wpf.XceedPaidApp"

echo "Building ProGPU WPF paid Xceed app..."
"${dotnet}" build "${xceed_project}" -v:minimal

if [[ "${PROGPU_WPF_XCEED_PAID_VALIDATE:-0}" == "1" || "${PROGPU_WPF_XCEED_PAID_RUN_VALIDATE:-0}" == "1" ]]; then
  echo "Running ProGPU WPF paid Xceed apphost validation..."
  (
    cd "${xceed_output}"
    "./${apphost_name}" "$@"
  )
  exit 0
fi

echo "Launching ProGPU WPF paid Xceed apphost..."
(
  cd "${xceed_output}"
  "./${apphost_name}" "$@"
)
