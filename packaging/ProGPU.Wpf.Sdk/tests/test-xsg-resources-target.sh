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
echo PASS
