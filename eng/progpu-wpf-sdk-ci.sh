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
dev_package_version="${PROGPU_WPF_DEV_PACKAGE_VERSION:-11.0.0-dev}"
mkdir -p "${package_output}"

pack_project() {
  local project="$1"
  local package_id="$2"
  rm -f \
    "${package_output}/${package_id}.${dev_package_version}.nupkg" \
    "${package_output}/${package_id}.${dev_package_version}.snupkg"
  "${dotnet}" pack "${repo_root}/${project}" -c Release -o "${package_output}" -v:minimal
}

build_project() {
  local project="$1"
  "${dotnet}" build "${repo_root}/${project}" -c Release -v:minimal
}

run_dotnet() {
  "${dotnet}" "$@"
}

apphost_name() {
  local assembly_name="$1"
  case "$(uname -s 2>/dev/null || echo unknown)" in
    MINGW*|MSYS*|CYGWIN*)
      echo "${assembly_name}.exe"
      ;;
    *)
      echo "${assembly_name}"
      ;;
  esac
}

clean_sdk_smoke_outputs() {
  local project
  for project in \
    "ProGPU.Wpf.SdkSwitchLibrary" \
    "ProGPU.Wpf.SdkSwitchSmoke" \
    "ProGPU.Wpf.SdkSwitchRuntimeHarness" \
    "ProGPU.Wpf.SdkExternalSmokeHarness" \
    "ProGPU.Wpf.HelloApp" \
    "ProGPU.Wpf.MvpApp"
  do
    rm -rf \
      "${repo_root}/artifacts/bin/${project}" \
      "${repo_root}/artifacts/obj/${project}"
  done

  rm -rf \
    "${repo_root}/artifacts/nuget/ProGPU.Wpf.SdkSwitchSmoke" \
    "${repo_root}/artifacts/nuget/ProGPU.Wpf.MvpApp"
}

echo "Packing ProGPU packages for ProGPU.Wpf.Sdk feed..."
pack_project "external/ProGPU/src/ProGPU.Backend/ProGPU.Backend.csproj" "ProGPU.Backend"
pack_project "external/ProGPU/src/ProGPU.Transpiler/ProGPU.Transpiler.csproj" "ProGPU.Transpiler"
pack_project "external/ProGPU/src/ProGPU.Compute/ProGPU.Compute.csproj" "ProGPU.Compute"
pack_project "external/ProGPU/src/ProGPU.Vector/ProGPU.Vector.csproj" "ProGPU.Vector"
pack_project "external/ProGPU/src/ProGPU.Text/ProGPU.Text.csproj" "ProGPU.Text"
pack_project "external/ProGPU/src/ProGPU.Scene/ProGPU.Scene.csproj" "ProGPU.Scene"

echo "Building managed WPF transport payload..."
build_project "src/Microsoft.DotNet.Wpf/src/WindowsBase/WindowsBase.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/System.Xaml/System.Xaml.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/UIAutomation/UIAutomationTypes/UIAutomationTypes.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/UIAutomation/UIAutomationProvider/UIAutomationProvider.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/System.Windows.Input.Manipulations/System.Windows.Input.Manipulations.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/System.Windows.Primitives/System.Windows.Primitives.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/ReachFramework/ReachFramework.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/PresentationUI/PresentationUI.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/PresentationFramework/PresentationFramework.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Aero/PresentationFramework.Aero.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Aero2/PresentationFramework.Aero2.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.AeroLite/PresentationFramework.AeroLite.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Classic/PresentationFramework.Classic.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/PresentationFramework.Fluent.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Luna/PresentationFramework.Luna.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Royale/PresentationFramework.Royale.csproj"
build_project "src/Microsoft.DotNet.Wpf/src/System.Windows.Controls.Ribbon/System.Windows.Controls.Ribbon.csproj"

echo "Packing WPF transport, ProGPU bridge, and custom SDK..."
pack_project "packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj" "Microsoft.DotNet.Wpf.GitHub"
pack_project "src/ProGPU.Wpf/ProGPU.Wpf.csproj" "ProGPU.Wpf"
pack_project "packaging/ProGPU.Wpf.Sdk/ProGPU.Wpf.Sdk.ArchNeutral.csproj" "ProGPU.Wpf.Sdk"

echo "Cleaning package-mode SDK smoke outputs..."
clean_sdk_smoke_outputs

echo "Building package-mode SDK switch smoke..."
run_dotnet build "${repo_root}/src/ProGPU.Wpf.SdkSwitchSmoke/ProGPU.Wpf.SdkSwitchSmoke.csproj" -v:minimal

echo "Running SDK switch runtime smoke..."
run_dotnet run --project "${repo_root}/src/ProGPU.Wpf.SdkSwitchRuntimeHarness/ProGPU.Wpf.SdkSwitchRuntimeHarness.csproj" -v:minimal

echo "Running external no-source-change SDK smoke..."
run_dotnet run --project "${repo_root}/src/ProGPU.Wpf.SdkExternalSmokeHarness/ProGPU.Wpf.SdkExternalSmokeHarness.csproj" -v:minimal

echo "Building Hello SDK app..."
run_dotnet build "${repo_root}/samples/ProGPU.Wpf.HelloApp/ProGPU.Wpf.HelloApp.csproj" -v:minimal

hello_output="${repo_root}/artifacts/bin/ProGPU.Wpf.HelloApp/Debug/net11.0-windows"
hello_apphost_name="$(apphost_name "ProGPU.Wpf.HelloApp")"
if [[ ! -x "${hello_output}/${hello_apphost_name}" ]]; then
  echo "Expected Hello SDK apphost at ${hello_output}/${hello_apphost_name}" >&2
  exit 1
fi

echo "Running Hello SDK app apphost Application.Run validation..."
(
  cd "${hello_output}"
  PROGPU_WPF_HELLO_RUN_VALIDATE=1 "./${hello_apphost_name}" "hello-alpha" "hello beta"
)

echo "Building MVP SDK app..."
run_dotnet build "${repo_root}/samples/ProGPU.Wpf.MvpApp/ProGPU.Wpf.MvpApp.csproj" -v:minimal

echo "Running MVP SDK app validation..."
PROGPU_WPF_MVP_VALIDATE=1 run_dotnet run --project "${repo_root}/samples/ProGPU.Wpf.MvpApp/ProGPU.Wpf.MvpApp.csproj" -v:minimal

echo "Running MVP SDK app Application.Run validation..."
PROGPU_WPF_MVP_RUN_VALIDATE=1 run_dotnet run --project "${repo_root}/samples/ProGPU.Wpf.MvpApp/ProGPU.Wpf.MvpApp.csproj" -v:minimal

mvp_output="${repo_root}/artifacts/bin/ProGPU.Wpf.MvpApp/Debug/net11.0-windows"
mvp_apphost_name="$(apphost_name "ProGPU.Wpf.MvpApp")"
if [[ ! -x "${mvp_output}/${mvp_apphost_name}" ]]; then
  echo "Expected MVP SDK apphost at ${mvp_output}/${mvp_apphost_name}" >&2
  exit 1
fi

echo "Running MVP SDK app apphost Application.Run validation..."
(
  cd "${mvp_output}"
  PROGPU_WPF_MVP_RUN_VALIDATE=1 "./${mvp_apphost_name}"
)

echo "Building focused WPF graph tests..."
run_dotnet build "${repo_root}/src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj" -v:minimal

echo "Running focused SDK graph guard..."
run_dotnet vstest \
  "${repo_root}/src/ProGPU.Wpf.Tests/bin/Debug/net10.0/ProGPU.Wpf.Tests.dll" \
  --Tests:ProGPU.Wpf.Tests.Composition.WpfManagedProjectGraphTests.ProGpuWpfSdkProvidesSwitchOnlyPackagingSurface
