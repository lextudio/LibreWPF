#!/usr/bin/env bash
# Exercises ProGPU.Wpf.Sdk.XamlSourceGeneratorResources.targets with plain MSBuild (no WPF SDK needed).
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
targets="$here/targets/ProGPU.Wpf.Sdk.XamlSourceGeneratorResources.targets"
dotnet_cmd="${DOTNET:-dotnet}"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
mkdir -p "$work/a"; echo one > "$work/a/One.txt"; echo two > "$work/a/Two.txt"

# project <flag> <items>
project() {
cat > "$work/t.proj" <<P
<Project DefaultTargets="Run">
  <PropertyGroup><UseLibreWpfXamlSourceGenerator>$1</UseLibreWpfXamlSourceGenerator><AssemblyName>T</AssemblyName><IntermediateOutputPath>obj/</IntermediateOutputPath></PropertyGroup>
  <ItemGroup>$2</ItemGroup>
  <Target Name="AssignTargetPaths" />
  <Import Project="$targets" />
  <Target Name="Run" DependsOnTargets="AssignTargetPaths"><Message Importance="high" Text="EMB=@(EmbeddedResource->'%(LogicalName)')" /></Target>
</Project>
P
}
run() { (cd "$work" && "$dotnet_cmd" msbuild t.proj -nologo 2>&1) || true; }
fail() { echo "FAIL: $*"; exit 1; }

project false '<Resource Include="a/One.txt" />'
run | grep -q 'EMB=$' || fail "flag off must not embed"
project true ''
run | grep -q 'EMB=$' || fail "no Resource items must not embed"

project true '<Resource Include="a/One.txt" Link="Assets\Logo.TXT" /><Resource Include="a/Two.txt" />'
run | grep -q 'EMB=T.g.resources' || fail "Resource items must embed T.g.resources"
python3 - "$work/obj/T.g.resources" <<'PY' || fail "keys must be lower-case and '/'-separated, Link honoured"
import sys
d = open(sys.argv[1], 'rb').read()
for k in ("assets/logo.txt", "a/two.txt"):
    assert k.encode('utf-16-le') in d or k.encode() in d, k
PY
before=$(stat -f %m "$work/obj/T.g.resources" 2>/dev/null || stat -c %Y "$work/obj/T.g.resources")
sleep 2; run >/dev/null
after=$(stat -f %m "$work/obj/T.g.resources" 2>/dev/null || stat -c %Y "$work/obj/T.g.resources")
[ "$before" = "$after" ] || fail "unchanged inputs must not rewrite the file"

project true '<Resource Include="a/One.txt" Link="X" /><Resource Include="a/Two.txt" Link="x" />'
run | grep -q "Two Resource items have the resource name 'x'" || fail "duplicate key must be an error"


# NativeAOT targets (the ILC target the SDK reads TrimmerRootAssembly in is WriteIlcRspFileForCompilation; PrepareForILLink is not on that path): trim error lifted and project references rooted, only for XSG + PublishAot.
aot="$here/targets/ProGPU.Wpf.Sdk.XamlSourceGeneratorNativeAot.targets"
mkdir -p "$work/lib"
aotproj() {
cat > "$work/aot.proj" <<P
<Project DefaultTargets="Run">
  <PropertyGroup><UseLibreWpfXamlSourceGenerator>$1</UseLibreWpfXamlSourceGenerator><PublishAot>$2</PublishAot></PropertyGroup>
  <ItemGroup><ReferencePath Include="/x/Theme.dll" ReferenceSourceTarget="ProjectReference" /><ReferencePath Include="/x/Other.dll" ReferenceSourceTarget="ResolveAssemblyReference" /></ItemGroup>
  <Target Name="WriteIlcRspFileForCompilation" />
  <Import Project="$aot" />
  <Target Name="Run" DependsOnTargets="WriteIlcRspFileForCompilation"><Message Importance="high" Text="TRIM=\$(_SuppressWpfTrimError) ROOT=@(TrimmerRootAssembly) RD=@(RdXmlFile->'%(Filename)') STRICT=\$(LibreWpfXsgAotStrict) VISIBLE=@(CompilerVisibleProperty)" /></Target>
</Project>
P
(cd "$work" && "$dotnet_cmd" msbuild aot.proj -nologo 2>&1)
}
aotproj true true | grep -q 'TRIM=true ROOT=Theme RD=ProGPU.Wpf.Sdk.XamlSourceGeneratorNativeAotRoots.rd STRICT=true VISIBLE=LibreWpfXsgAotStrict$' || fail "XSG + PublishAot must lift the trim error and root only project references"
aotproj false true | grep -q 'TRIM= ROOT= RD= STRICT= VISIBLE=LibreWpfXsgAotStrict$' || fail "BAML project must be untouched"
aotproj true false | grep -q 'TRIM= ROOT= RD= STRICT= VISIBLE=LibreWpfXsgAotStrict$' || fail "no PublishAot must be untouched"
echo PASS-AOT

# ReadyToRun publish: the IL entry that shadows a ReadyToRun image of the same file name is dropped before the copy; without PublishReadyToRun nothing is touched.
r2r="$here/targets/ProGPU.Wpf.Sdk.PublishReadyToRun.targets"
r2rproj() {
cat > "$work/r2r.proj" <<P
<Project DefaultTargets="_CopyResolvedFilesToPublishPreserveNewest">
  <PropertyGroup><PublishReadyToRun>$1</PublishReadyToRun></PropertyGroup>
  <ItemGroup>
    <ResolvedFileToPublish Include="/x/lib/A.dll"><RelativePath>A.dll</RelativePath></ResolvedFileToPublish>
    <ResolvedFileToPublish Include="/x/obj/R2R/A.dll"><RelativePath>A.dll</RelativePath></ResolvedFileToPublish>
    <ResolvedFileToPublish Include="/x/lib/B.dll"><RelativePath>B.dll</RelativePath></ResolvedFileToPublish>
    <_ResolvedFileToPublishPreserveNewest Include="@(ResolvedFileToPublish)" />
    <_ResolvedFileToPublishAlways Include="@(ResolvedFileToPublish)" />
  </ItemGroup>
  <Import Project="$r2r" />
  <Target Name="_CopyResolvedFilesToPublishPreserveNewest"><Message Importance="high" Text="COPY=@(_ResolvedFileToPublishPreserveNewest) ALWAYS=@(_ResolvedFileToPublishAlways) ALL=@(ResolvedFileToPublish)" /></Target>
  <Target Name="_CopyResolvedFilesToPublishAlways" />
</Project>
P
(cd "$work" && "$dotnet_cmd" msbuild r2r.proj -nologo 2>&1)
}
r2rproj true | grep -q 'COPY=/x/obj/R2R/A.dll;/x/lib/B.dll ALWAYS=/x/obj/R2R/A.dll;/x/lib/B.dll ALL=/x/obj/R2R/A.dll;/x/lib/B.dll' || fail "ReadyToRun: the shadowing IL entry must be dropped from every list"
r2rproj false | grep -q 'COPY=/x/lib/A.dll;/x/obj/R2R/A.dll;/x/lib/B.dll' || fail "without PublishReadyToRun nothing is dropped"
echo PASS-R2R
