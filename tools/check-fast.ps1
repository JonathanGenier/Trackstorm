param(
    [ValidateSet("Auto", "Core", "Client", "Transport", "Docs")]
    [string]$Area = "Auto",

    [string]$GodotPath = $env:GODOT_PATH,

    [switch]$IncludeExtended,

    [switch]$RequireRuntime
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

. "$PSScriptRoot/fast-check-routes.ps1"

function Invoke-Check {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    Write-Host "`n>> $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Get-ChangedPaths {
    $base = "origin/main"
    git -C "$root" rev-parse --verify $base *> $null
    if ($LASTEXITCODE -ne 0) {
        $base = "main"
        git -C "$root" rev-parse --verify $base *> $null
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Cannot resolve origin/main or main. Fetch main or pass -Area explicitly."
    }

    $paths = @(git -C "$root" diff --name-only "$base...HEAD")
    if ($LASTEXITCODE -ne 0) {
        throw "Cannot determine changed paths against $base."
    }

    return $paths
}

function Resolve-GodotExecutable {
    param([string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $null
    }

    if (Test-Path -LiteralPath $Candidate) {
        return (Resolve-Path -LiteralPath $Candidate).Path
    }

    $command = Get-Command $Candidate -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    throw "Godot executable '$Candidate' was not found."
}

function Invoke-RoutedRuntimeCheck {
    param(
        [Parameter(Mandatory)][string]$Script,
        [Parameter(Mandatory)][string]$ResolvedGodotPath
    )

    $scriptPath = Join-Path $root $Script
    if (-not (Test-Path -LiteralPath $scriptPath)) {
        throw "Routed runtime check '$Script' does not exist."
    }

    Invoke-Check $Script {
        & $scriptPath -GodotPath $ResolvedGodotPath
    }
}

if ($Area -ne "Auto") {
    switch ($Area) {
        "Core" {
            Invoke-Check "Core tests" {
                dotnet test "$root/code/Tests/Trackstorm.Core.Tests.csproj" -c Release
            }
        }
        "Client" {
            Invoke-Check "Client Debug build" {
                dotnet build "$root/Trackstorm.Client.csproj" -c Debug -warnaserror
            }
        }
        "Transport" {
            Invoke-Check "Transport tests" {
                dotnet test "$root/code/TransportTests/Trackstorm.Transport.Tests.csproj" -c Release --filter 'TestCategory!=Native'
            }
        }
        "Docs" {
            Write-Host "Docs-only iteration selected; no build/test command required."
        }
    }

    Write-Host "`nFast targeted checks passed. Run ./check.ps1 before final Story handoff."
    exit 0
}

$paths = @(Get-ChangedPaths)
if ($paths.Count -eq 0) {
    Write-Host "No committed changes against main. Nothing to check."
    exit 0
}

$plan = Get-FastCheckPlan -Paths $paths
$resolvedGodot = Resolve-GodotExecutable -Candidate $GodotPath
$runtimeWillRun = $resolvedGodot -and $plan.RuntimeScripts.Count -gt 0

Write-Host "Fast-check plan for $($paths.Count) changed path(s):"
if ($plan.CoreTests) { Write-Host "  - Core tests" }
if ($plan.TransportTests) { Write-Host "  - Non-native transport tests" }
if ($plan.ServiceTests) { Write-Host "  - Authority-lease service tests" }
if ($plan.ClientBuild -and -not $runtimeWillRun) { Write-Host "  - Client Debug build" }
if ($plan.VersionChecks) { Write-Host "  - Version rule regression tests" }
if ($plan.MediaChecks) { Write-Host "  - Frontend media checks" }
foreach ($script in $plan.RuntimeScripts) { Write-Host "  - Runtime: $script" }
foreach ($script in $plan.ExtendedScripts) { Write-Host "  - Extended/native: $script" }
foreach ($scenario in $plan.ManualScenarios) { Write-Host "  - Playtest/manual: $scenario" }

if ($plan.VersionChecks) {
    Invoke-Check "Version rule regression tests" {
        & "$root/tools/test-version.ps1"
    }
}

if ($plan.MediaChecks) {
    Invoke-Check "Frontend media verifier regression tests" {
        & "$root/tools/test-frontend-media.ps1"
    }
    Invoke-Check "Frontend media materialization and checksums" {
        & "$root/tools/check-frontend-media.ps1"
    }
}

if ($plan.CoreTests) {
    Invoke-Check "Core tests" {
        dotnet test "$root/code/Tests/Trackstorm.Core.Tests.csproj" -c Release
    }
}

if ($plan.TransportTests) {
    Invoke-Check "Transport tests" {
        dotnet test "$root/code/TransportTests/Trackstorm.Transport.Tests.csproj" -c Release --filter 'TestCategory!=Native'
    }
}

if ($plan.ServiceTests) {
    $serviceRoot = Join-Path $root "services/authority-lease"
    if (-not (Test-Path (Join-Path $serviceRoot "node_modules"))) {
        Invoke-Check "Authority-lease npm install" {
            npm ci --prefix $serviceRoot
        }
    }

    Invoke-Check "Authority-lease service tests" {
        npm test --prefix $serviceRoot
    }
}

# Routed runtime scripts already compile the affected solution. Avoid an extra Client build
# when at least one runtime harness is going to run.
if ($plan.ClientBuild -and -not $runtimeWillRun) {
    Invoke-Check "Client Debug build" {
        dotnet build "$root/Trackstorm.Client.csproj" -c Debug -warnaserror
    }
}

if ($plan.RuntimeScripts.Count -gt 0) {
    if ($resolvedGodot) {
        foreach ($script in $plan.RuntimeScripts) {
            Invoke-RoutedRuntimeCheck -Script $script -ResolvedGodotPath $resolvedGodot
        }
    }
    else {
        Write-Warning "Runtime checks were routed but no Godot executable was supplied. Pass -GodotPath <path> or set GODOT_PATH."
        foreach ($script in $plan.RuntimeScripts) {
            Write-Host "  pending runtime check: ./$script -GodotPath <path>"
        }

        if ($RequireRuntime) {
            throw "Runtime checks are required for this iteration but GodotPath is unavailable."
        }
    }
}

if ($plan.ExtendedScripts.Count -gt 0) {
    if ($IncludeExtended) {
        if (-not $resolvedGodot) {
            throw "-IncludeExtended requires -GodotPath or GODOT_PATH."
        }

        foreach ($script in $plan.ExtendedScripts) {
            Invoke-RoutedRuntimeCheck -Script $script -ResolvedGodotPath $resolvedGodot
        }
    }
    else {
        Write-Host "`nExtended/native checks were identified but not auto-run:"
        foreach ($script in $plan.ExtendedScripts) {
            Write-Host "  pending extended check: ./$script -GodotPath <path>"
        }
    }
}

if ($plan.ManualScenarios.Count -gt 0) {
    Write-Host "`nRequired playtest/manual scenarios for this change:"
    foreach ($scenario in $plan.ManualScenarios) {
        Write-Host "  - $scenario"
    }
}

$selected = $plan.CoreTests -or $plan.TransportTests -or $plan.ServiceTests -or $plan.ClientBuild -or
    $plan.VersionChecks -or $plan.MediaChecks -or $plan.RuntimeScripts.Count -gt 0 -or
    $plan.ExtendedScripts.Count -gt 0 -or $plan.ManualScenarios.Count -gt 0

if (-not $selected) {
    Write-Host "Only documentation/workflow or unclassified non-production files changed; no iteration check selected."
}

Write-Host "`nFast targeted checks completed. Run ./check.ps1 before final Story handoff."
