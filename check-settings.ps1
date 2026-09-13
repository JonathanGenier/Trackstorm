param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual
)

$ErrorActionPreference = 'Stop'
$checkDirectory = Join-Path $PSScriptRoot ('.godot/settings-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkDirectory -Force | Out-Null

dotnet build Trackstorm.sln -c Debug -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Settings verification build failed.' }

foreach ($phase in @('write', 'read') + $(if ($Visual) { @('visual') } else { @() })) {
    $settingsPath = Join-Path $checkDirectory $(if ($phase -eq 'visual') { 'visual.json' } else { 'restart.json' })
    $arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/settings_checks.tscn', '--quit-after', '1200')
    if ($phase -ne 'visual') { $arguments = @('--headless') + $arguments }
    $output = & $GodotPath @arguments -- "--settings-path=$settingsPath" "--phase=$phase" 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match "Settings integration passed: $phase,")) {
        throw "Settings $phase verification failed or did not complete."
    }
}

Write-Host "Settings verification artifacts: $checkDirectory"
