#!/usr/bin/env bash
# Exercises the generator auto-reference of ProGPU.Wpf.Sdk.XamlSourceGeneratorFeatures.targets with plain MSBuild (no WPF SDK needed).
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
targets="$here/targets/ProGPU.Wpf.Sdk.XamlSourceGeneratorFeatures.targets"
dotnet_cmd="${DOTNET:-dotnet}"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT

# project <file name> <properties> <items>
project() {
cat > "$work/$1" <<P
<Project DefaultTargets="Run">
  <PropertyGroup><ProGpuPackageVersion>1.2.3</ProGpuPackageVersion>$2</PropertyGroup>
  <ItemGroup>$3</ItemGroup>
  <Import Project="$targets" />
  <Target Name="Run"><Message Importance="high" Text="REF=@(PackageReference->'%(Identity)|%(Version)|%(VersionOverride)')" /></Target>
</Project>
P
}
run() { (cd "$work" && "$dotnet_cmd" msbuild "$1" -nologo 2>&1) || true; }
fail() { echo "FAIL: $*"; exit 1; }

project a.csproj '<UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>' ''
run a.csproj | grep -q 'REF=LibreWPF.Xaml.SourceGenerator|1.2.3|' || fail "C# project with the switch on gets the package"
project a.vbproj '<UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>' ''
run a.vbproj | grep -q 'REF=LibreWPF.Xaml.SourceGenerator|1.2.3|' || fail "VB project with the switch on gets the package"
project b.csproj '<UseLibreWpfXamlSourceGenerator>false</UseLibreWpfXamlSourceGenerator>' ''
run b.csproj | grep -q 'REF=$' || fail "switch off adds nothing"
project c.csproj '<UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator><LibreWpfXsgAutoReference>false</LibreWpfXsgAutoReference>' ''
run c.csproj | grep -q 'REF=$' || fail "LibreWpfXsgAutoReference=false adds nothing"
project d.csproj '<UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>' '<PackageReference Include="LibreWPF.Xaml.SourceGenerator" Version="9.9.9" />'
run d.csproj | grep -q 'REF=LibreWPF.Xaml.SourceGenerator|9.9.9|$' || fail "an existing reference is left alone (one entry, its version)"
project d2.csproj '<UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>' '<PackageReference Include="librewpf.xaml.sourcegenerator" Version="9.9.9" />'
run d2.csproj | grep -q 'REF=librewpf.xaml.sourcegenerator|9.9.9|$' || fail "the existing-reference guard ignores the case of the id"
project d3.csproj '<UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>' '<ProjectReference Include="../src/LibreWPF.Xaml.SourceGenerator/LibreWPF.Xaml.SourceGenerator.csproj" />'
run d3.csproj | grep -q 'REF=$' || fail "a ProjectReference to the generator is left alone"
project d4.csproj '<UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>' '<Analyzer Include="/x/LibreWPF.Xaml.SourceGenerator.dll" />'
run d4.csproj | grep -q 'REF=$' || fail "an Analyzer item for the generator is left alone"
project e.csproj '<UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>' ''
run e.csproj | grep -q 'REF=LibreWPF.Xaml.SourceGenerator||1.2.3' || fail "central package management uses VersionOverride"
project f.fsproj '<UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator>' ''
run f.fsproj | grep -q 'REF=$' || fail "F# has no Roslyn generator: no package reference here"

# Theme duplicates: only the Aero reference stays in the compile references.
themes() {
cat > "$work/$1" <<P
<Project DefaultTargets="Run">
  <PropertyGroup><UseLibreWpfXamlSourceGenerator>$2</UseLibreWpfXamlSourceGenerator>$3</PropertyGroup>
  <ItemGroup>
    <ReferencePathWithRefAssemblies Include="/r/PresentationFramework.dll;/r/PresentationFramework.Aero.dll;/r/PresentationFramework.Aero2.dll;/r/PresentationFramework.AeroLite.dll;/r/PresentationFramework.Luna.dll" />
    <ReferencePath Include="/r/PresentationFramework.dll;/r/PresentationFramework.Aero.dll;/r/PresentationFramework.Aero2.dll;/r/PresentationFramework.AeroLite.dll;/r/PresentationFramework.Luna.dll" />
  </ItemGroup>
  <Import Project="$targets" />
  <Target Name="CoreCompile" />
  <Target Name="Run" DependsOnTargets="_LibreWpfXsgDropDuplicateThemeReferences"><Message Importance="high" Text="REFS=@(ReferencePathWithRefAssemblies->'%(Filename)')" /><Message Importance="high" Text="PATHS=@(ReferencePath->'%(Filename)')" /></Target>
</Project>
P
}
themes t1.csproj true ''
run t1.csproj | grep -q 'REFS=PresentationFramework;PresentationFramework.Aero$' || fail "duplicates are dropped under the generator"
run t1.csproj | grep -q 'PATHS=PresentationFramework;PresentationFramework.Aero$' || fail "duplicates are dropped from ReferencePath too"
themes t2.csproj false ''
run t2.csproj | grep -q 'REFS=PresentationFramework;PresentationFramework.Aero;PresentationFramework.Aero2;PresentationFramework.AeroLite;PresentationFramework.Luna$' || fail "BAML projects keep every reference"
themes t3.csproj true '<LibreWpfXsgKeepThemeReferences>true</LibreWpfXsgKeepThemeReferences>'
run t3.csproj | grep -q 'PresentationFramework.Aero2' || fail "LibreWpfXsgKeepThemeReferences=true keeps them"
# Without Aero nothing is dropped: the project keeps the theme types it can reach.
cat > "$work/t5.csproj" <<P
<Project DefaultTargets="Run">
  <PropertyGroup><UseLibreWpfXamlSourceGenerator>true</UseLibreWpfXamlSourceGenerator></PropertyGroup>
  <ItemGroup><ReferencePathWithRefAssemblies Include="/r/PresentationFramework.dll;/r/PresentationFramework.Aero2.dll" /></ItemGroup>
  <Import Project="$targets" />
  <Target Name="CoreCompile" />
  <Target Name="Run" DependsOnTargets="_LibreWpfXsgDropDuplicateThemeReferences"><Message Importance="high" Text="REFS=@(ReferencePathWithRefAssemblies->'%(Filename)')" /></Target>
</Project>
P
run t5.csproj | grep -q 'REFS=PresentationFramework;PresentationFramework.Aero2$' || fail "no Aero reference: nothing is dropped"
echo "ok"
