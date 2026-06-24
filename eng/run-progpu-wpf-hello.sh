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
if [[ "${#launch_args[@]}" == "0" ]] &&
   [[ "${PROGPU_WPF_HELLO_RUN_VALIDATE:-0}" == "1" || "${PROGPU_WPF_HELLO_LIVE_VALIDATE:-0}" == "1" ]]; then
  launch_args=("hello-alpha" "hello beta")
fi

if [[ "${PROGPU_WPF_HELLO_LIVE_VALIDATE:-0}" == "1" ]]; then
  live_log="$(mktemp "${TMPDIR:-/tmp}/progpu-wpf-hello-live.XXXXXX")"
  apphost_pid=""
  cleanup_live_probe() {
    if [[ -n "${apphost_pid}" ]] && kill -0 "${apphost_pid}" 2>/dev/null; then
      kill "${apphost_pid}" 2>/dev/null || true
      sleep 0.5
      if kill -0 "${apphost_pid}" 2>/dev/null; then
        kill -9 "${apphost_pid}" 2>/dev/null || true
      fi
      wait "${apphost_pid}" 2>/dev/null || true
    fi
    rm -f "${live_log}"
  }
  trap cleanup_live_probe EXIT

  echo "Launching ProGPU WPF Hello apphost live geometry probe..."
  (
    cd "${hello_output}"
    "./${apphost_name}" "${launch_args[@]}"
  ) >"${live_log}" 2>&1 &
  apphost_pid="$!"

  swapchain_line=""
  for _ in {1..200}; do
    if ! kill -0 "${apphost_pid}" 2>/dev/null; then
      echo "Hello apphost exited before configuring a ProGPU swapchain." >&2
      cat "${live_log}" >&2
      exit 1
    fi

    swapchain_line="$(grep -E "Configuring SwapChain: [0-9]+x[0-9]+" "${live_log}" | tail -n 1 || true)"
    if [[ -n "${swapchain_line}" ]]; then
      break
    fi

    sleep 0.05
  done

  if [[ -z "${swapchain_line}" ]]; then
    echo "Expected Hello apphost to configure a ProGPU swapchain." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  if [[ ! "${swapchain_line}" =~ Configuring[[:space:]]SwapChain:[[:space:]]([0-9]+)x([0-9]+) ]]; then
    echo "Could not parse Hello apphost swapchain line: ${swapchain_line}" >&2
    cat "${live_log}" >&2
    exit 1
  fi

  pixel_width="${BASH_REMATCH[1]}"
  pixel_height="${BASH_REMATCH[2]}"
  logical_width=520
  logical_height=360
  if (( pixel_width < logical_width || pixel_height < logical_height )); then
    echo "Expected Hello apphost pixels to cover ${logical_width}x${logical_height} logical content, but got ${pixel_width}x${pixel_height}." >&2
    cat "${live_log}" >&2
    exit 1
  fi

  trap - EXIT
  cleanup_live_probe >/dev/null 2>&1
  echo "ProGPU WPF HelloApp live geometry validation succeeded: logical ${logical_width}x${logical_height}, pixels ${pixel_width}x${pixel_height}."
  exit 0
fi

echo "Launching ProGPU WPF Hello apphost..."
(
  cd "${hello_output}"
  "./${apphost_name}" "${launch_args[@]}"
)
