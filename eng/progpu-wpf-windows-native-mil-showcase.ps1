param(
    [string] $PackageDirectory = "",
    [string] $Version = "0.1.0-preview.45",
    [switch] $AllowEmulatedX64
)

$ErrorActionPreference = "Stop"

$osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($osArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64 -and !$AllowEmulatedX64) {
    throw "The Windows x64 native MIL CI gate requires an x64 Windows host; detected $osArchitecture."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot "artifacts/packages/Release/NonShipping"
}
$PackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)

$sdkPackage = Join-Path $PackageDirectory "LibreWPF.Sdk.$Version.nupkg"
$transportPackage = Join-Path $PackageDirectory "LibreWPF.Transport.$Version.nupkg"
$bridgePackage = Join-Path $PackageDirectory "LibreWPF.ProGPU.$Version.nupkg"
$nativePackages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter "ProGPU.Backend.Native.*.nupkg" -File)
foreach ($package in @($sdkPackage, $transportPackage, $bridgePackage)) {
    if (!(Test-Path -LiteralPath $package -PathType Leaf)) {
        throw "Windows native MIL Showcase requires the package $package."
    }
}
if ($nativePackages.Count -ne 1) {
    throw "Windows native MIL Showcase requires exactly one ProGPU.Backend.Native package; found $($nativePackages.Count)."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-PackageEntryHash {
    param([string] $PackagePath, [string] $EntryPath)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entry = $archive.GetEntry($EntryPath)
        if ($null -eq $entry) {
            throw "Package $PackagePath is missing $EntryPath."
        }
        $stream = $entry.Open()
        try {
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try {
                return [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace("-", "")
            }
            finally {
                $sha.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-ExactPackageAsset {
    param(
        [string] $OutputPath,
        [string] $PackagePath,
        [string] $EntryPath
    )

    if (!(Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
        throw "Windows native MIL Showcase output is missing $OutputPath."
    }
    $expected = Get-PackageEntryHash $PackagePath $EntryPath
    $actual = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash
    if (![string]::Equals($expected, $actual, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Windows native MIL Showcase output $OutputPath does not match $EntryPath in $PackagePath."
    }
    Write-Host "Exact package asset: $OutputPath ($actual)"
}

function Invoke-DotNet {
    param([string[]] $Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-ShowcaseCheck {
    param(
        [string] $Name,
        [string] $ExpectedMarker,
        [string] $AppHost,
        [string] $OutputDirectory
    )

    $env:PROGPU_WPF_SHOWCASE_VALIDATE = if ($Name -eq "pre-display") { "1" } else { "0" }
    $env:PROGPU_WPF_SHOWCASE_RUN_VALIDATE = if ($Name -eq "displayed") { "1" } else { "0" }
    $env:PROGPU_WPF_SHOWCASE_LIVE_VALIDATE = "0"
    $stdoutPath = Join-Path $OutputDirectory "$Name-stdout.log"
    $stderrPath = Join-Path $OutputDirectory "$Name-stderr.log"

    $process = Start-Process -FilePath $AppHost -WorkingDirectory (Split-Path -Parent $AppHost) `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
    try {
        if (!$process.WaitForExit(180000)) {
            throw "Windows native MIL Showcase $Name check timed out after 180 seconds."
        }
        $process.Refresh()
        $stdout = Get-Content -LiteralPath $stdoutPath -Raw
        $stderr = Get-Content -LiteralPath $stderrPath -Raw
        Write-Host $stdout
        if (![string]::IsNullOrWhiteSpace($stderr)) {
            Write-Warning $stderr
        }
        if ($process.ExitCode -ne 0) {
            throw "Windows native MIL Showcase $Name check exited $($process.ExitCode)."
        }
        if ($stdout.IndexOf($ExpectedMarker, [System.StringComparison]::Ordinal) -lt 0) {
            throw "Windows native MIL Showcase $Name check did not print its success marker."
        }
    }
    finally {
        if (!$process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "librewpf-native-mil-win-x64-$([guid]::NewGuid().ToString('N'))"
$artifactsRoot = Join-Path $smokeRoot "artifacts"
$packagesRoot = Join-Path $smokeRoot "nuget"
New-Item -ItemType Directory -Path $artifactsRoot, $packagesRoot -Force | Out-Null
$artifactsProperty = $artifactsRoot.Replace('\', '/') + '/'
$packagesProperty = $packagesRoot.Replace('\', '/')
$feedProperty = $PackageDirectory.Replace('\', '/')
$buildTasksProject = Join-Path $repoRoot "src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/PresentationBuildTasks.csproj"
$showcaseProject = Join-Path $repoRoot "samples/ProGPU.Wpf.ShowcaseApp/ProGPU.Wpf.ShowcaseApp.csproj"

Write-Host "Building unchanged SDK Showcase for win-x64 from $PackageDirectory."
Write-Host "Private test output: $smokeRoot"
Invoke-DotNet -Arguments @(
    "build", $buildTasksProject, "-f", "net10.0", "-c", "Release",
    "-p:ArtifactsDir=$artifactsProperty",
    "-p:RestorePackagesPath=$packagesProperty",
    "-p:RestoreAdditionalProjectSources=$feedProperty",
    "-p:RunNetFrameworkApiCompat=false", "-v:minimal"
)
Invoke-DotNet -Arguments @(
    "build", $showcaseProject, "-c", "Release", "-r", "win-x64",
    "-p:PlatformTarget=x64",
    "-p:ArtifactsDir=$artifactsProperty",
    "-p:RestorePackagesPath=$packagesProperty",
    "-p:RestoreAdditionalProjectSources=$feedProperty",
    "-p:ProGpuWpfReferenceMode=Package",
    "-p:ProGpuWpfRendererMode=NativeMilWgpu",
    "-p:ProGpuWpfNativeMilHitTesting=true",
    "-p:RunNetFrameworkApiCompat=false", "-v:minimal"
)

$appDirectory = Join-Path $artifactsRoot "bin/ProGPU.Wpf.ShowcaseApp/Release/net10.0-windows"
$appHost = Join-Path $appDirectory "ProGPU.Wpf.ShowcaseApp.exe"
if (!(Test-Path -LiteralPath $appHost -PathType Leaf)) {
    throw "Windows native MIL Showcase build is missing the x64 apphost $appHost."
}
$appHostBytes = [System.IO.File]::ReadAllBytes($appHost)
$peOffset = [System.BitConverter]::ToInt32($appHostBytes, 60)
$machine = [System.BitConverter]::ToUInt16($appHostBytes, $peOffset + 4)
if ($machine -ne 0x8664) {
    throw "Windows native MIL Showcase apphost is not AMD64 (PE machine $machine)."
}

Assert-ExactPackageAsset (Join-Path $appDirectory "PresentationCore.dll") $transportPackage "runtimes/win-x64/lib/net10.0/PresentationCore.dll"
Assert-ExactPackageAsset (Join-Path $appDirectory "PresentationFramework.dll") $transportPackage "lib/net10.0/PresentationFramework.dll"
Assert-ExactPackageAsset (Join-Path $appDirectory "ProGPU.Wpf.dll") $bridgePackage "lib/net10.0/ProGPU.Wpf.dll"
Assert-ExactPackageAsset (Join-Path $appDirectory "progpu_native.dll") $nativePackages[0].FullName "runtimes/win-x64/native/progpu_native.dll"

Invoke-ShowcaseCheck "pre-display" "ProGPU WPF Showcase validation succeeded." $appHost $smokeRoot
Invoke-ShowcaseCheck "displayed" "ProGPU WPF Showcase Application.Run validation succeeded." $appHost $smokeRoot
Write-Host "Windows x64 package-only native MIL Showcase checks succeeded."
