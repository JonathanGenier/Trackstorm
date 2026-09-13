param (
    [Parameter(Mandatory)]
    [string]$GodotPath
)

$ErrorActionPreference = 'Stop'
$layoutPath = Join-Path $PSScriptRoot '.godot/editor/editor_layout.cfg'
$originalLayout = if (Test-Path -LiteralPath $layoutPath) { [IO.File]::ReadAllBytes($layoutPath) } else { $null }
$scriptPath = Join-Path $PSScriptRoot 'code/Core/Input/InputButtons.cs'
$scriptHash = (Get-FileHash -LiteralPath $scriptPath).Hash
New-Item -ItemType Directory -Path (Split-Path $layoutPath) -Force | Out-Null

try {
    # Reproduce the saved C# editor session that triggered shutdown hot reload.
    [IO.File]::WriteAllText($layoutPath, @'
[ScriptEditor]
open_scripts=["res://code/Core/Input/InputButtons.cs"]
selected_script="res://code/Core/Input/InputButtons.cs"
open_help=[]
'@)
    $layoutHash = (Get-FileHash -LiteralPath $layoutPath).Hash

    foreach ($run in 1..2) {
        Write-Host "`n>> Saved-session regression run $run"
        & (Join-Path $PSScriptRoot 'check-gdunit.ps1') -GodotPath $GodotPath
        if ((Get-FileHash -LiteralPath $scriptPath).Hash -ne $scriptHash) {
            throw 'Headless import modified the restored C# script.'
        }
        if ((Get-FileHash -LiteralPath $layoutPath).Hash -ne $layoutHash) {
            throw 'Headless import did not preserve the editor session.'
        }
    }

    # A launch failure must also restore the session and remain a failure.
    $failed = $false
    try {
        & (Join-Path $PSScriptRoot 'import-godot.ps1') -GodotPath (Join-Path $PSScriptRoot ([Guid]::NewGuid().ToString('N') + '.missing.exe'))
    }
    catch {
        $failed = $true
    }
    if (-not $failed -or (Get-FileHash -LiteralPath $layoutPath).Hash -ne $layoutHash) {
        throw 'Import failure did not propagate and preserve the editor session.'
    }

    Write-Host 'GdUnit wrapper regression passed: repeated imports, unchanged C# source, preserved session, and failure cleanup.'
}
finally {
    if ($null -ne $originalLayout) {
        [IO.File]::WriteAllBytes($layoutPath, $originalLayout)
    }
    else {
        Remove-Item -LiteralPath $layoutPath
    }
}
