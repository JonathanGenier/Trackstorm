param (
    [Parameter(Mandatory)]
    [string]$GodotPath,
    [switch]$Visual
)

$ErrorActionPreference = 'Stop'
$checkDirectory = Join-Path $PSScriptRoot ('.godot/developer-options-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkDirectory -Force | Out-Null
dotnet build Trackstorm.sln -c Debug -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Developer Options verification build failed.' }
foreach ($phase in @('write', 'read')) {
    $arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/developer_options_checks.tscn', '--quit-after', '4800')
    if (-not $Visual) { $arguments = @('--headless') + $arguments }
    $output = & $GodotPath @arguments -- "--dev-output=$checkDirectory" "--phase=$phase" 2>&1
    $exitCode = $LASTEXITCODE
    $output | Set-Content (Join-Path $checkDirectory "$phase.log")
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:') -or -not ($output -match "Developer Options integration passed: $phase")) {
        throw "Developer Options $phase verification failed."
    }
}
Write-Host "Developer Options verification artifacts: $checkDirectory"
