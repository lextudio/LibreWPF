param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildCommand = Join-Path $repoRoot "build.cmd"
$buildTasksProject = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/PresentationBuildTasks.csproj"
$project = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj"
$outputDirectory = Join-Path $repoRoot "artifacts/windows-managed-runtime"
$versionDetailsPath = Join-Path $repoRoot "eng/Version.Details.props"
$packagesDirectory = Join-Path $repoRoot ".packages"

Remove-Item -Path $outputDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$perlCommand = (Get-Command perl.exe -ErrorAction Stop).Source

$versionDetails = [xml](Get-Content -Path $versionDetailsPath -Raw)
$netCoreAppVersion = [string]($versionDetails.Project.PropertyGroup.MicrosoftNETCoreAppRefPackageVersion | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($netCoreAppVersion)) {
    throw "MicrosoftNETCoreAppRefPackageVersion is missing from $versionDetailsPath."
}

$runtimeIdentifiers = @("win-x86", "win-x64", "win-arm64")
$restoreRoot = Join-Path ([System.IO.Path]::GetTempPath()) "librewpf-ijw-host-$([guid]::NewGuid().ToString('N'))"
$restoreProject = Join-Path $restoreRoot "IjwHostRestore.csproj"
New-Item -ItemType Directory -Path $restoreRoot -Force | Out-Null
try {
    $packageDownloads = ($runtimeIdentifiers | ForEach-Object {
        "    <PackageDownload Include=`"Microsoft.NETCore.App.Host.$_`" Version=`"[$netCoreAppVersion]`" />"
    }) -join [Environment]::NewLine

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RestorePackagesPath>$packagesDirectory</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
$packageDownloads
  </ItemGroup>
</Project>
"@ | Set-Content -Path $restoreProject -Encoding utf8

    dotnet restore $restoreProject --configfile (Join-Path $repoRoot "NuGet.config") --force --no-cache
    if ($LASTEXITCODE -ne 0) {
        throw "Restoring the Windows IJW host packs failed."
    }
}
finally {
    Remove-Item -Path $restoreRoot -Recurse -Force -ErrorAction SilentlyContinue
}

function Invoke-WpfProjectBuild([string] $projectPath, [string] $platform, [string] $runtimeIdentifier, [string] $ijwHostSourcePath = "") {
    $ijwHostArgument = @()
    if (![string]::IsNullOrWhiteSpace($ijwHostSourcePath)) {
        $ijwHostArgument = "/p:IjwHostSourcePath=$ijwHostSourcePath"
    }

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
        "/p:RuntimeIdentifier=$runtimeIdentifier" `
        $ijwHostArgument `
        /p:RunNetFrameworkApiCompat=false `
        /p:RunRefApiCompat=false
    if ($LASTEXITCODE -ne 0) {
        throw "Building $projectPath for $platform failed."
    }
}

Invoke-WpfProjectBuild $buildTasksProject "x86" "win-x86"

$runtimePlatforms = [ordered]@{
    "win-x86" = "x86"
    "win-x64" = "x64"
    "win-arm64" = "arm64"
}

foreach ($entry in $runtimePlatforms.GetEnumerator()) {
    $runtimeIdentifier = $entry.Key
    $platform = $entry.Value
    $ijwHost = Join-Path $packagesDirectory "microsoft.netcore.app.host.$runtimeIdentifier/$netCoreAppVersion/runtimes/$runtimeIdentifier/native/ijwhost.dll"
    if (!(Test-Path $ijwHost)) {
        throw "The $runtimeIdentifier IJW host was not restored at $ijwHost."
    }

    Invoke-WpfProjectBuild $project $platform $runtimeIdentifier $ijwHost

    $presentationCore = Join-Path $repoRoot "artifacts/bin/PresentationCore/$platform/$Configuration/net10.0/$runtimeIdentifier/PresentationCore.dll"
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
