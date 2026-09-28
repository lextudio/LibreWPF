$ErrorActionPreference = "Stop"

# Offline controls only: load the exact production helper, never execute the
# package build, Showcase, renderer, or the full native gate from this test.
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $PSScriptRoot "progpu-wpf-windows-native-mil-showcase.ps1"
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref] $tokens, [ref] $parseErrors)
if ($parseErrors.Count -ne 0) { throw "Native gate PowerShell parse failed: $parseErrors" }
$helpers = @($ast.FindAll({ param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq "Invoke-ShowcaseIdleCheck"
}, $false))
if ($helpers.Count -ne 1) { throw "Expected exactly one production idle helper." }
. ([scriptblock]::Create($helpers[0].Extent.Text))

function Assert-Control {
    param([bool] $Condition, [string] $Message)
    if (!$Condition) { throw $Message }
}

$modes = @(
    "PROGPU_WPF_SHOWCASE_VALIDATE", "PROGPU_WPF_SHOWCASE_RUN_VALIDATE",
    "PROGPU_WPF_SHOWCASE_LIVE_VALIDATE", "PROGPU_WPF_SHOWCASE_PERFORMANCE_VALIDATE"
)
$saved = @{}
foreach ($name in $modes + "PROGPU_WPF_IDLE_CONTROL_SENTINEL") {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "librewpf-idle-launcher-controls-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $testRoot | Out-Null
$expectedApp = Join-Path $testRoot "package with spaces/ProGPU.Wpf.ShowcaseApp.exe"
$caseCalls = 0
$caseCode = 0
$caseThrows = $false
$caseCapture = $false

function Invoke-ControlPython {
    # Deliberately not a renderer or receipt substitute. Observe the launch
    # boundary and inject only native process status/launch exceptions.
    $script:caseCalls++
    Assert-Control ($args.Count -eq $(if ($script:caseCapture) { 6 } else { 5 })) "Unexpected Python runner argument count."
    if ($script:caseCapture) { Assert-Control ($args[5] -eq "--windows-crash-dumps") "Missing explicit crash capture." }
    Assert-Control ($args[0] -eq (Join-Path $repoRoot "eng/progpu-wpf-showcase-idle.py")) "Wrong existing strict runner."
    Assert-Control ($args[1] -eq "--app" -and $args[2] -eq $expectedApp) "Exact package apphost was not preserved."
    Assert-Control ($args[3] -eq "--evidence-parent") "Missing explicit evidence parent."
    Assert-Control (Test-Path -LiteralPath $args[4] -PathType Container) "Fresh evidence directory is missing."
    Assert-Control ((Split-Path -Parent $args[4]) -eq $testRoot) "Evidence escaped the owned parent."
    foreach ($name in $modes) {
        Assert-Control ($null -eq [Environment]::GetEnvironmentVariable($name, "Process")) "Mode not cleared: $name."
    }
    Assert-Control ($env:PROGPU_WPF_IDLE_CONTROL_SENTINEL -eq "untouched") "Unrelated environment changed."
    if ($script:caseThrows) { throw "offline launch error" }
    $global:LASTEXITCODE = $script:caseCode
}

try {
    # Mixed inherited values cover absence, enabled, disabled and nonstandard
    # caller settings. No mode is permanently rewritten by the gate.
    $expected = @("1", "0", $null, "caller-value")
    $cases = @(
        @{ Name = "success"; Code = 0; Throws = $false },
        @{ Name = "child-failure"; Code = 7; Throws = $false },
        @{ Name = "timeout"; Code = 124; Throws = $false },
        @{ Name = "invalid-receipt"; Code = 1; Throws = $false },
        @{ Name = "launch-exception"; Code = 0; Throws = $true },
        @{ Name = "crash-capture-opt-in"; Code = 0; Throws = $false; Capture = $true }
    )
    foreach ($case in $cases) {
        for ($index = 0; $index -lt $modes.Count; $index++) {
            if ($null -eq $expected[$index]) {
                Remove-Item -LiteralPath "Env:$($modes[$index])" -ErrorAction SilentlyContinue
            } else {
                [Environment]::SetEnvironmentVariable($modes[$index], $expected[$index], "Process")
            }
        }
        $env:PROGPU_WPF_IDLE_CONTROL_SENTINEL = "untouched"
        $caseCode = $case.Code
        $caseThrows = $case.Throws
        $caseCapture = [bool] $case.Capture
        $observed = $null
        # The stub deliberately writes a synthetic native-process status. Scope
        # that mutation to this control, including the throwing launch case;
        # GitHub's pwsh wrapper propagates any remaining LASTEXITCODE afterward.
        $previousExitCodeVariable = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
        $hadExitCode = $null -ne $previousExitCodeVariable
        $previousExitCode = if ($hadExitCode) { $previousExitCodeVariable.Value } else { $null }
        try {
            Invoke-ShowcaseIdleCheck $expectedApp $testRoot -PythonCommand Invoke-ControlPython -CaptureCrashDump:$caseCapture
        }
        catch { $observed = $_.Exception.Message }
        finally {
            if ($hadExitCode) {
                $global:LASTEXITCODE = $previousExitCode
            } else {
                Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
            }
        }
        $restoredExitCode = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
        Assert-Control ($hadExitCode -eq ($null -ne $restoredExitCode)) "Caller exit-status presence changed."
        if ($hadExitCode) {
            Assert-Control ($restoredExitCode.Value -eq $previousExitCode) "Caller exit status changed."
        }
        if ($case.Throws) {
            Assert-Control ($observed -eq "offline launch error") "Launch exception was hidden: $observed."
        } elseif ($case.Code -ne 0) {
            Assert-Control ($null -ne $observed -and $observed.Contains("exited $($case.Code);")) "Runner status was hidden: $observed."
        } else {
            Assert-Control ($null -eq $observed) "Successful launcher was rejected: $observed."
        }
        for ($index = 0; $index -lt $modes.Count; $index++) {
            Assert-Control ([Environment]::GetEnvironmentVariable($modes[$index], "Process") -ceq $expected[$index]) "Mode was not restored: $($modes[$index])."
        }
        Assert-Control ($env:PROGPU_WPF_IDLE_CONTROL_SENTINEL -eq "untouched") "Unrelated caller state was not retained."
        Write-Host "PASS offline native idle launcher: $($case.Name)"
    }
    Assert-Control ($caseCalls -eq $cases.Count) "A launcher case did not execute exactly once."
    $directories = @(Get-ChildItem -LiteralPath $testRoot -Directory)
    Assert-Control ($directories.Count -eq $cases.Count) "Evidence directories were reused."
    foreach ($directory in $directories) {
        Assert-Control (Test-Path -LiteralPath (Join-Path $directory.FullName "launcher.log") -PathType Leaf) "Failed launcher transcript was not retained."
    }
    Write-Host "PASS all 6 offline native idle launcher controls; no Showcase was launched."
}
finally {
    foreach ($name in $saved.Keys) {
        if ($null -eq $saved[$name]) {
            Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
        } else {
            [Environment]::SetEnvironmentVariable($name, $saved[$name], "Process")
        }
    }
    # Only the fresh, GUID-owned offline fixture, never native CI evidence.
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
