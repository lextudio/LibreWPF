#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${repo_root}/.dotnet/dotnet"
if [[ ! -x "${dotnet}" ]]; then
  dotnet="dotnet"
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export DOTNET_ROLL_FORWARD_TO_PRERELEASE="${DOTNET_ROLL_FORWARD_TO_PRERELEASE:-1}"

echo "Running external no-source-change ProGPU WPF SDK smoke..."
"${dotnet}" run \
  --project "${repo_root}/src/ProGPU.Wpf.SdkExternalSmokeHarness/ProGPU.Wpf.SdkExternalSmokeHarness.csproj" \
  -v:minimal

echo "Running Hello SDK apphost Application.Run self-test..."
PROGPU_WPF_HELLO_RUN_VALIDATE=1 "${repo_root}/eng/run-progpu-wpf-hello.sh"

echo "Running Hello SDK apphost live geometry probe..."
PROGPU_WPF_HELLO_LIVE_VALIDATE=1 "${repo_root}/eng/run-progpu-wpf-hello.sh"

echo "Running MVP SDK apphost Application.Run self-test..."
PROGPU_WPF_MVP_RUN_VALIDATE=1 "${repo_root}/eng/run-progpu-wpf-mvp.sh"

echo "Running MVP SDK apphost live geometry probe..."
PROGPU_WPF_MVP_LIVE_VALIDATE=1 "${repo_root}/eng/run-progpu-wpf-mvp.sh"

echo "ProGPU WPF MVP quickcheck succeeded."
