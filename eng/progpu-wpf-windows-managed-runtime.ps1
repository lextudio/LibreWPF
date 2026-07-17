param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildCommand = Join-Path $repoRoot "build.cmd"
$buildTasksProject = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/PresentationBuildTasks.csproj"
$project = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj"
$outputDirectory = Join-Path $repoRoot "artifacts/windows-managed-runtime"

Remove-Item -Path $outputDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$perlCommand = (Get-Command perl.exe -ErrorAction Stop).Source

function Invoke-WpfProjectBuild([string] $projectPath, [string] $platform) {
    & $buildCommand `
        -ci `
        -configuration $Configuration `
        -platform $platform `
        -projects $projectPath `
        -msbuildEngine vs `
        -nativeToolsOnMachine `
        -excludeCIBinarylog `
        -warnAsError 0 `
        "/p:PerlCommand=$perlCommand" `
        /p:RunNetFrameworkApiCompat=false `
        /p:RunRefApiCompat=false
    if ($LASTEXITCODE -ne 0) {
        throw "Building $projectPath for $platform failed."
    }
}

Invoke-WpfProjectBuild $buildTasksProject "x86"

$runtimePlatforms = [ordered]@{
    "win-x86" = "x86"
    "win-x64" = "x64"
    "win-arm64" = "arm64"
}

foreach ($entry in $runtimePlatforms.GetEnumerator()) {
    $runtimeIdentifier = $entry.Key
    $platform = $entry.Value
    Invoke-WpfProjectBuild $project $platform

    $presentationCore = Join-Path $repoRoot "artifacts/bin/PresentationCore/$platform/$Configuration/net10.0/PresentationCore.dll"
    if (!(Test-Path $presentationCore)) {
        throw "The Windows PresentationCore build did not produce $presentationCore."
    }

    $runtimeOutput = Join-Path $outputDirectory "$runtimeIdentifier/net10.0"
    New-Item -ItemType Directory -Path $runtimeOutput -Force | Out-Null
    Copy-Item $presentationCore (Join-Path $runtimeOutput "PresentationCore.dll") -Force

    $pdb = [System.IO.Path]::ChangeExtension($presentationCore, ".pdb")
    if (Test-Path $pdb) {
        Copy-Item $pdb (Join-Path $runtimeOutput "PresentationCore.pdb") -Force
    }
}

Write-Host "Staged Windows managed runtime payload at $outputDirectory."
