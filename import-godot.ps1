param (
    [Parameter(Mandatory)]
    [string]$GodotPath
)

$ErrorActionPreference = 'Stop'
$layoutPath = Join-Path $PSScriptRoot '.godot/editor/editor_layout.cfg'
$layout = $null
if (Test-Path -LiteralPath $layoutPath) {
    $layout = [IO.File]::ReadAllBytes($layoutPath)
}

try {
    # Headless imports must not reopen and save the user's script-editor session.
    # Godot 4.7.2 can reformat restored C# scripts and restart its hot-reload timer
    # after that timer has left the tree during shutdown. Plugins still import normally.
    if ($null -ne $layout) {
        [IO.File]::WriteAllText($layoutPath, '')
    }

    # --import waits for resource import and exits itself; --quit is unnecessary.
    $output = & $GodotPath --headless --editor --path $PSScriptRoot --import 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0 -or ($output -match 'ERROR:|WARNING:')) {
        throw 'Godot project import failed or reported runtime diagnostics.'
    }
}
finally {
    if ($null -ne $layout) {
        [IO.File]::WriteAllBytes($layoutPath, $layout)
    }
}
