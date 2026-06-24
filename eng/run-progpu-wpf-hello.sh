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
hello_project="${repo_root}/samples/ProGPU.Wpf.HelloApp/ProGPU.Wpf.HelloApp.csproj"
hello_output="${repo_root}/artifacts/bin/ProGPU.Wpf.HelloApp/Debug/net11.0-windows"

apphost_name="ProGPU.Wpf.HelloApp"
case "$(uname -s 2>/dev/null || echo unknown)" in
  MINGW*|MSYS*|CYGWIN*)
    apphost_name="${apphost_name}.exe"
    ;;
esac

if [[ "${PROGPU_WPF_HELLO_REBUILD_PACKAGES:-0}" == "1" || ! -f "${sdk_package}" ]]; then
  echo "Building ProGPU WPF SDK packages before launching Hello app..."
  "${repo_root}/eng/progpu-wpf-sdk-ci.sh"
fi

rm -rf \
  "${repo_root}/artifacts/bin/ProGPU.Wpf.HelloApp" \
  "${repo_root}/artifacts/obj/ProGPU.Wpf.HelloApp"

echo "Building ProGPU WPF Hello app..."
"${dotnet}" build "${hello_project}" -v:minimal

launch_args=("$@")
if [[ "${PROGPU_WPF_HELLO_RUN_VALIDATE:-0}" == "1" && "${#launch_args[@]}" == "0" ]]; then
  launch_args=("hello-alpha" "hello beta")
fi

echo "Launching ProGPU WPF Hello apphost..."
(
  cd "${hello_output}"
  "./${apphost_name}" "${launch_args[@]}"
)
