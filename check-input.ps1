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
Invoke-GodotCheck -Arguments @('--editor', '--import')
Invoke-GodotCheck -Arguments @('res://scenes/verification/input_checks.tscn', '--quit-after', '600') -ExpectedOutput 'Input integration passed:'
Invoke-GodotCheck -Arguments @('--quit-after', '5')
