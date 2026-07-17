param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildTasksProject = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/PresentationBuildTasks.csproj"
$project = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj"
$outputDirectory = Join-Path $repoRoot "artifacts/windows-managed-runtime/net10.0"

Remove-Item -Path $outputDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

dotnet build $buildTasksProject -c $Configuration -f net10.0 -v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Building PresentationBuildTasks for the Windows runtime payload failed."
}

dotnet build $project -c $Configuration -f net10.0 -v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Building the Windows PresentationCore runtime payload failed."
}

$presentationCore = Join-Path $repoRoot "artifacts/bin/PresentationCore/$Configuration/net10.0/PresentationCore.dll"
if (!(Test-Path $presentationCore)) {
    throw "The Windows PresentationCore build did not produce $presentationCore."
}

Copy-Item $presentationCore (Join-Path $outputDirectory "PresentationCore.dll") -Force

$pdb = [System.IO.Path]::ChangeExtension($presentationCore, ".pdb")
if (Test-Path $pdb) {
    Copy-Item $pdb (Join-Path $outputDirectory "PresentationCore.pdb") -Force
}

Write-Host "Staged Windows managed runtime payload at $outputDirectory."
