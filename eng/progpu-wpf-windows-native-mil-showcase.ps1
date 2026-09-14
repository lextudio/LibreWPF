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
$nativePackageVersion = $nativePackages[0].BaseName -replace '^ProGPU\.Backend\.Native\.', ''

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

function Invoke-TextLayoutCheck {
    param(
        [string] $Name,
        [string] $AppHost,
        [string] $OutputDirectory
    )

    $env:PROGPU_WPF_TEXT_LAYOUT_REPORT = "1"
    $env:PROGPU_WPF_TEXT_LAYOUT_EXIT_AFTER_REPORT = "1"
    $stdoutPath = Join-Path $OutputDirectory "$Name-text-layout-stdout.log"
    $stderrPath = Join-Path $OutputDirectory "$Name-text-layout-stderr.log"
    $process = Start-Process -FilePath $AppHost -WorkingDirectory (Split-Path -Parent $AppHost) `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
    try {
        if (!$process.WaitForExit(120000)) {
            throw "Windows $Name text-layout check timed out after 120 seconds."
        }
        $process.Refresh()
        $stdout = Get-Content -LiteralPath $stdoutPath -Raw
        $stderr = Get-Content -LiteralPath $stderrPath -Raw
        Write-Host "$Name text layout: $stdout"
        if (![string]::IsNullOrWhiteSpace($stderr)) {
            Write-Warning $stderr
        }
        if ($process.ExitCode -ne 0) {
            throw "Windows $Name text-layout check exited $($process.ExitCode)."
        }
        $match = [regex]::Match($stdout,
            '(?m)^TEXT_LAYOUT width=(?<width>[0-9.]+) height=(?<height>[0-9.]+) font=(?<font>[0-9.]+) lines=(?<lines>[0-9]+) tops=(?<tops>[0-9.,]+) starts=(?<starts>[0-9,]+)\r?$')
        if (!$match.Success) {
            throw "Windows $Name text-layout check did not report the expected metrics."
        }
        $culture = [System.Globalization.CultureInfo]::InvariantCulture
        return [pscustomobject]@{
            Width = [double]::Parse($match.Groups['width'].Value, $culture)
            Height = [double]::Parse($match.Groups['height'].Value, $culture)
            Font = [double]::Parse($match.Groups['font'].Value, $culture)
            Lines = [int]::Parse($match.Groups['lines'].Value, $culture)
            Tops = @($match.Groups['tops'].Value.Split(',') | ForEach-Object { [double]::Parse($_, $culture) })
            Starts = @($match.Groups['starts'].Value.Split(',') | ForEach-Object { [int]::Parse($_, $culture) })
        }
    }
    finally {
        if (!$process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

function Assert-TextMetricNear {
    param([string] $Name, [double] $Native, [double] $Portable, [double] $Tolerance)
    if ([math]::Abs($Native - $Portable) -gt $Tolerance) {
        throw "Windows text-layout $Name differs: native=$Native portable=$Portable tolerance=$Tolerance."
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
    "-p:ProGpuPackageVersion=$nativePackageVersion",
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

# Compile one source-only WPF fixture under both SDKs. The stock Windows WPF
# build is the geometry oracle; both processes must exercise their live text
# views, including the final hidden formatting-edge caret insertion position.
$textProject = Join-Path $repoRoot "samples/ProGPU.Wpf.TextLayoutParityApp/ProGPU.Wpf.TextLayoutParityApp.csproj"
$windowsTextProject = Join-Path $repoRoot "samples/ProGPU.Wpf.TextLayoutParityApp.Windows/ProGPU.Wpf.TextLayoutParityApp.Windows.csproj"
$windowsTextObj = Join-Path $smokeRoot "windows-text-obj"
$windowsTextBin = Join-Path $smokeRoot "windows-text-bin"
New-Item -ItemType Directory -Path $windowsTextObj, $windowsTextBin -Force | Out-Null
Invoke-DotNet -Arguments @(
    "build", $textProject, "-c", "Release", "-r", "win-x64",
    "-p:PlatformTarget=x64",
    "-p:ArtifactsDir=$artifactsProperty",
    "-p:RestorePackagesPath=$packagesProperty",
    "-p:RestoreAdditionalProjectSources=$feedProperty",
    "-p:ProGpuWpfReferenceMode=Package",
    "-p:ProGpuPackageVersion=$nativePackageVersion",
    "-p:ProGpuWpfRendererMode=NativeMilWgpu",
    "-p:ProGpuWpfNativeMilHitTesting=true",
    "-p:RunNetFrameworkApiCompat=false", "-v:minimal"
)
$textDirectory = Join-Path $artifactsRoot "bin/ProGPU.Wpf.TextLayoutParityApp/Release/net10.0-windows"
$textAppHost = Join-Path $textDirectory "ProGPU.Wpf.TextLayoutParityApp.exe"
Assert-ExactPackageAsset (Join-Path $textDirectory "PresentationCore.dll") $transportPackage "runtimes/win-x64/lib/net10.0/PresentationCore.dll"
Assert-ExactPackageAsset (Join-Path $textDirectory "PresentationFramework.dll") $transportPackage "lib/net10.0/PresentationFramework.dll"
Assert-ExactPackageAsset (Join-Path $textDirectory "progpu_native.dll") $nativePackages[0].FullName "runtimes/win-x64/native/progpu_native.dll"
Invoke-DotNet -Arguments @(
    "build", $windowsTextProject, "-c", "Release", "-r", "win-x64",
    "-p:BaseIntermediateOutputPath=$($windowsTextObj.Replace('\', '/'))/",
    "-p:OutputPath=$($windowsTextBin.Replace('\', '/'))/",
    "-p:AppendTargetFrameworkToOutputPath=false",
    "-p:AppendRuntimeIdentifierToOutputPath=false", "-v:minimal"
)
$windowsTextAppHost = Join-Path $windowsTextBin "ProGPU.Wpf.TextLayoutParityApp.Windows.exe"
if (!(Test-Path -LiteralPath $windowsTextAppHost -PathType Leaf)) {
    throw "Windows native WPF text-layout build is missing $windowsTextAppHost."
}
$nativeLayout = Invoke-TextLayoutCheck "native-WPF" $windowsTextAppHost $smokeRoot
$portableLayout = Invoke-TextLayoutCheck "ProGPU-native-MIL" $textAppHost $smokeRoot
if ($nativeLayout.Lines -lt 2 -or $portableLayout.Lines -ne $nativeLayout.Lines -or
    $nativeLayout.Tops.Count -ne $nativeLayout.Lines -or
    $portableLayout.Tops.Count -ne $portableLayout.Lines -or
    ($nativeLayout.Starts -join ',') -ne ($portableLayout.Starts -join ',')) {
    throw "Windows native-WPF and ProGPU text-layout line breaks differ."
}
Assert-TextMetricNear "content width" $nativeLayout.Width $portableLayout.Width 0.01
Assert-TextMetricNear "content height" $nativeLayout.Height $portableLayout.Height 0.05
Assert-TextMetricNear "font size" $nativeLayout.Font $portableLayout.Font 0.001
for ($i = 0; $i -lt $nativeLayout.Tops.Count; $i++) {
    Assert-TextMetricNear "line $i top" $nativeLayout.Tops[$i] $portableLayout.Tops[$i] 0.05
}
Write-Host "Windows x64 package-only native MIL Showcase and same-source text-layout checks succeeded."
