param (
    [Parameter(Mandatory)]
    [string]$GodotPath
)

$ErrorActionPreference = "Stop"

function Invoke-GodotCheck {
    param ([string[]]$Arguments, [string]$ExpectedOutput)

    $output = & $GodotPath --headless --path $PSScriptRoot @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:')) {
        throw "Godot verification failed or reported runtime diagnostics."
    }
    if ($ExpectedOutput -and -not ($output -match $ExpectedOutput)) {
        throw "Godot exited without the expected verification result."
    }
}

dotnet build Trackstorm.sln -c Debug -warnaserror
if ($LASTEXITCODE -ne 0) { throw "Input verification build failed." }
& (Join-Path $PSScriptRoot 'import-godot.ps1') -GodotPath $GodotPath
Invoke-GodotCheck -Arguments @('res://scenes/verification/input_checks.tscn', '--quit-after', '600') -ExpectedOutput 'Input integration passed:'
# Allow asynchronous frontend media loading to finish; this is a startup smoke,
# not an abrupt-shutdown test of an in-flight resource loader.
Invoke-GodotCheck -Arguments @('--quit-after', '120')
