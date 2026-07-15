param(
    [string] $PackageDirectory = "",
    [string] $Version = "",
    [string] $RuntimeIdentifier = "",
    [switch] $BuildOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot "artifacts/packages/Release/NonShipping"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $env:PROGPU_WPF_DEV_PACKAGE_VERSION
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.1.0-preview.15"
}

$PackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
$sdkPackage = Join-Path $PackageDirectory "LibreWPF.Sdk.$Version.nupkg"
$transportPackage = Join-Path $PackageDirectory "LibreWPF.Transport.$Version.nupkg"
if (!(Test-Path $sdkPackage) -or !(Test-Path $transportPackage)) {
    throw "LibreWPF AnyCPU smoke requires $sdkPackage and $transportPackage."
}

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) "librewpf-windows-anycpu-$([guid]::NewGuid().ToString('N'))"
$projectRoot = Join-Path $smokeRoot "App"
$packagesRoot = Join-Path $smokeRoot "packages"
New-Item -ItemType Directory -Path $projectRoot -Force | Out-Null

try {
    $runtimeIdentifierProperty = ""
    if (![string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $runtimeIdentifierProperty = "    <RuntimeIdentifier>$RuntimeIdentifier</RuntimeIdentifier>"
    }

    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="librewpf-local" value="$PackageDirectory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="dotnet11" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/index.json" />
    <add key="dotnet11-transport" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11-transport/nuget/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content -Path (Join-Path $smokeRoot "NuGet.config") -Encoding utf8

    @"
<Project Sdk="LibreWPF.Sdk/$Version">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
$runtimeIdentifierProperty
  </PropertyGroup>
</Project>
"@ | Set-Content -Path (Join-Path $projectRoot "AnyCpuSmoke.csproj") -Encoding utf8

    @'
<Application x:Class="AnyCpuSmoke.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml" />
'@ | Set-Content -Path (Join-Path $projectRoot "App.xaml") -Encoding utf8

    @'
using System.Windows;

namespace AnyCpuSmoke;

public partial class App : Application
{
}
'@ | Set-Content -Path (Join-Path $projectRoot "App.xaml.cs") -Encoding utf8

    @'
<Window x:Class="AnyCpuSmoke.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="LibreWPF AnyCPU Smoke"
        Width="320"
        Height="180">
  <TextBlock Text="LibreWPF AnyCPU native runtime smoke" />
</Window>
'@ | Set-Content -Path (Join-Path $projectRoot "MainWindow.xaml") -Encoding utf8

    @'
using System;
using System.IO;
using System.Windows;

namespace AnyCpuSmoke;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        string nativePath = Path.Combine(AppContext.BaseDirectory, "PresentationNative_cor3.dll");
        if (!File.Exists(nativePath))
        {
            throw new FileNotFoundException("LibreWPF did not select the native WPF runtime for the current AnyCPU process.", nativePath);
        }

        Console.WriteLine($"LibreWPF Windows AnyCPU smoke succeeded with {nativePath}.");
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() => Application.Current.Shutdown(0)));
    }
}
'@ | Set-Content -Path (Join-Path $projectRoot "MainWindow.xaml.cs") -Encoding utf8

    $oldPackages = $env:NUGET_PACKAGES
    $env:NUGET_PACKAGES = $packagesRoot
    try {
        dotnet restore (Join-Path $projectRoot "AnyCpuSmoke.csproj") --configfile (Join-Path $smokeRoot "NuGet.config") --force --no-cache
        if ($LASTEXITCODE -ne 0) { throw "LibreWPF Windows AnyCPU restore failed." }

        dotnet build (Join-Path $projectRoot "AnyCpuSmoke.csproj") --no-restore -c Release
        if ($LASTEXITCODE -ne 0) { throw "LibreWPF Windows AnyCPU build failed." }

        $nativeAsset = Get-ChildItem -Path (Join-Path $projectRoot "bin/Release") -Filter "PresentationNative_cor3.dll" -Recurse | Select-Object -First 1
        if ($null -eq $nativeAsset) {
            throw "LibreWPF Windows AnyCPU build output is missing PresentationNative_cor3.dll."
        }

        if (!$BuildOnly) {
            $appHost = Get-ChildItem -Path (Join-Path $projectRoot "bin/Release") -Filter "AnyCpuSmoke.exe" -Recurse | Select-Object -First 1
            if ($null -eq $appHost) {
                throw "LibreWPF Windows AnyCPU build output is missing the AnyCPU app host."
            }

            & $appHost.FullName
            if ($LASTEXITCODE -ne 0) { throw "LibreWPF Windows AnyCPU launch failed." }
        }
    }
    finally {
        $env:NUGET_PACKAGES = $oldPackages
    }
}
finally {
    Remove-Item -Path $smokeRoot -Recurse -Force -ErrorAction SilentlyContinue
}
