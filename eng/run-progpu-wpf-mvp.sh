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
sdk_package="${package_output}/ProGPU.Wpf.Sdk.11.0.0-dev.nupkg"
mvp_project="${repo_root}/samples/ProGPU.Wpf.MvpApp/ProGPU.Wpf.MvpApp.csproj"
mvp_output="${repo_root}/artifacts/bin/ProGPU.Wpf.MvpApp/Debug/net11.0-windows"

apphost_name="ProGPU.Wpf.MvpApp"
case "$(uname -s 2>/dev/null || echo unknown)" in
  MINGW*|MSYS*|CYGWIN*)
    apphost_name="${apphost_name}.exe"
    ;;
esac

if [[ "${PROGPU_WPF_MVP_REBUILD_PACKAGES:-0}" == "1" || ! -f "${sdk_package}" ]]; then
  echo "Building ProGPU WPF SDK packages before launching MVP app..."
  "${repo_root}/eng/progpu-wpf-sdk-ci.sh"
fi

rm -rf \
  "${repo_root}/artifacts/bin/ProGPU.Wpf.MvpApp" \
  "${repo_root}/artifacts/obj/ProGPU.Wpf.MvpApp"

echo "Building ProGPU WPF MVP app..."
"${dotnet}" build "${mvp_project}" -v:minimal

echo "Launching ProGPU WPF MVP apphost..."
(
  cd "${mvp_output}"
  "./${apphost_name}"
)
