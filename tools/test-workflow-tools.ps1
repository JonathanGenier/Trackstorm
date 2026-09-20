$ErrorActionPreference = "Stop"

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

. "$PSScriptRoot/fast-check-routes.ps1"

# Route-selection regression coverage.
$vehiclePlan = Get-FastCheckPlan -Paths @("code/Core/Vehicles/VehicleSimulation.cs", "code/Client/Vehicles/VehicleController.cs")
Assert-True $vehiclePlan.CoreTests "Vehicle Core changes must route Core tests."
Assert-True $vehiclePlan.ClientBuild "Vehicle Client changes must route a Client build."
Assert-True ($vehiclePlan.RuntimeScripts -contains "check-vehicle.ps1") "Vehicle changes must route check-vehicle.ps1."
Assert-True ($vehiclePlan.ManualScenarios.Count -gt 0) "Vehicle changes must preserve playtest guidance."

$networkPlan = Get-FastCheckPlan -Paths @("code/Client/Networking/VehicleReplicator.cs")
Assert-True $networkPlan.TransportTests "Networking changes must route transport tests."
Assert-True ($networkPlan.RuntimeScripts -contains "check-network-vehicles.ps1") "Networking changes must route network vehicle verification."

$eosPlan = Get-FastCheckPlan -Paths @("code/Client/Online/EosP2pSession.cs", "docs/features/eos-p2p.md")
Assert-True (-not ($eosPlan.ExtendedScripts -contains "check-eos-multiplayer.ps1")) "Real EOS multiplayer verification must not be auto-routed through the GodotPath extended runner."
Assert-True ($eosPlan.ManualScenarios | Where-Object { $_ -match 'check-eos-multiplayer\.ps1' }) "EOS P2P changes must surface the exported-build/distinct-device verification requirement."

$settingsPlan = Get-FastCheckPlan -Paths @("code/Client/Settings/SettingsPanel.cs", "code/Client/Hud/HudController.cs")
Assert-True ($settingsPlan.RuntimeScripts -contains "check-settings.ps1") "Settings changes must route settings verification."
Assert-True ($settingsPlan.RuntimeScripts -contains "check-hud.ps1") "HUD changes must route HUD verification."

$migrationPlan = Get-FastCheckPlan -Paths @("code/Core/Sessions/HostMigrationCoordinator.cs", "docs/features/host-migration.md")
Assert-True ($migrationPlan.RuntimeScripts -contains "check-migration.ps1") "Migration changes must route deterministic migration verification."
Assert-True ($migrationPlan.ExtendedScripts -contains "check-migration-processes.ps1") "Migration changes must identify extended process verification."

$cameraPlan = Get-FastCheckPlan -Paths @("code/Client/Vehicles/ChaseCamera.cs", "docs/features/camera.md")
Assert-True ($cameraPlan.ManualScenarios.Count -gt 0) "Camera changes must preserve manual/playtest verification."
Assert-True ($cameraPlan.RuntimeScripts -contains "check-vehicle.ps1") "Vehicle-mounted camera changes should retain vehicle integration verification."

$versionPlan = Get-FastCheckPlan -Paths @("Directory.Build.props", "export_presets.cfg")
Assert-True $versionPlan.VersionChecks "Version metadata changes must route version regression tests."

$mediaPlan = Get-FastCheckPlan -Paths @("assets/frontend/menu/menu.ogv")
Assert-True $mediaPlan.MediaChecks "Frontend media changes must route media checks."

$docsPlan = Get-FastCheckPlan -Paths @("docs/features/vehicles.md", "docs/features/eos-p2p.md", "docs/features/vehicle-networking.md")
Assert-True (-not $docsPlan.CoreTests -and -not $docsPlan.TransportTests -and -not $docsPlan.ClientBuild -and $docsPlan.RuntimeScripts.Count -eq 0 -and $docsPlan.ExtendedScripts.Count -eq 0) "Feature-doc-only changes must not trigger production/runtime checks."

$servicePlan = Get-FastCheckPlan -Paths @("services/authority-lease/src/worker.js")
Assert-True $servicePlan.ServiceTests "Authority-lease source changes must route service tests."

$simulationPlan = Get-FastCheckPlan -Paths @("code/Core/Simulation/SimulationRunner.cs")
Assert-True ($simulationPlan.RuntimeScripts -contains "check-vehicle.ps1") "Simulation changes must route vehicle runtime verification."
Assert-True ($simulationPlan.RuntimeScripts -contains "check-match.ps1") "Simulation changes must route match runtime verification."

$gdUnitPlan = Get-FastCheckPlan -Paths @("test/Client/Bootstrap/SimulationBootstrapTest.gd")
Assert-True ($gdUnitPlan.RuntimeScripts -contains "check-gdunit.ps1") "GDScript Client test changes must route GdUnit checks."

$allFeaturePaths = @(
    "project.godot",
    "docs/features/startup.md",
    "docs/features/vehicles.md",
    "docs/features/arena.md",
    "docs/features/oval-map.md",
    "docs/features/input.md",
    "docs/features/settings.md",
    "docs/features/hud.md",
    "docs/features/game-menu.md",
    "docs/features/items.md",
    "docs/features/item-spawns.md",
    "docs/features/death-respawn.md",
    "docs/features/matches.md",
    "docs/features/standings.md",
    "docs/features/sessions.md",
    "docs/features/reconnection.md",
    "docs/features/host-migration.md",
    "docs/features/vehicle-networking.md",
    "docs/features/eos-lobbies.md",
    "docs/features/eos-identity.md",
    "docs/features/eos-p2p.md",
    "docs/features/devtools.md",
    "docs/features/developer-options.md",
    "docs/features/statistics.md",
    "docs/features/event-log.md",
    "docs/features/activity-feed.md",
    "docs/features/audio.md"
)
$allPlan = Get-FastCheckPlan -Paths $allFeaturePaths
foreach ($script in @($allPlan.RuntimeScripts) + @($allPlan.ExtendedScripts)) {
    Assert-True (Test-Path (Join-Path (Split-Path -Parent $PSScriptRoot) $script)) "Routed check script '$script' does not exist."
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("trackstorm-workflow-tools-" + [guid]::NewGuid().ToString("N"))

try {
    $toolsDir = Join-Path $tempRoot "tools"
    $verificationDir = Join-Path $tempRoot "docs/verification"
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
    New-Item -ItemType Directory -Path $verificationDir -Force | Out-Null

    Copy-Item "$PSScriptRoot/new-verification-report.ps1" (Join-Path $toolsDir "new-verification-report.ps1")
    Copy-Item "$PSScriptRoot/check-fast.ps1" (Join-Path $toolsDir "check-fast.ps1")
    Copy-Item "$PSScriptRoot/fast-check-routes.ps1" (Join-Path $toolsDir "fast-check-routes.ps1")

    @"
# Historical verification evidence

| Evidence | Reports |
| --- | --- |
| Existing | [Existing](existing.md) |
"@ | Set-Content -Path (Join-Path $verificationDir "README.md") -Encoding utf8

    & (Join-Path $toolsDir "new-verification-report.ps1") TS-999 -Title "Workflow helper regression"

    $reportPath = Join-Path $verificationDir "ts-999.md"
    $indexPath = Join-Path $verificationDir "README.md"

    Assert-True (Test-Path $reportPath) "Verification report scaffolder did not create ts-999.md."

    $report = Get-Content $reportPath -Raw
    Assert-True ($report -match "TODO: record each command/check actually run") "Verification report scaffold lost its evidence placeholder."
    Assert-True ($report -match "Never convert this placeholder into a claimed result") "Verification report scaffold lost its anti-fabrication reminder."

    $index = Get-Content $indexPath -Raw
    Assert-True ($index -match "\[TS-999 verification evidence\]\(ts-999\.md\)") "Verification index was not updated."

    & (Join-Path $toolsDir "new-verification-report.ps1") TS-999 -Title "Workflow helper regression"

    $index = Get-Content $indexPath -Raw
    $linkCount = ([regex]::Matches($index, "\(ts-999\.md\)")).Count
    Assert-True ($linkCount -eq 1) "Verification scaffolder is not idempotent; found $linkCount index links."

    & (Join-Path $toolsDir "check-fast.ps1") -Area Docs

    Write-Host "Workflow helper regression tests passed."
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force
    }
}
