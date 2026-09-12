param (
    [Parameter(Mandatory)]
    [string]$GodotPath
)

$ErrorActionPreference = "Stop"
$resolvedGodotPath = (Resolve-Path -LiteralPath $GodotPath).Path

function Invoke-Godot {
    param (
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [string]$ExpectedOutput
    )

    Write-Host "`n>> $Name"
    $output = & $resolvedGodotPath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }

    if ($exitCode -ne 0 -or ($output -match 'SCRIPT ERROR:|ERROR:')) {
        throw "$Name failed or reported runtime errors."
    }

    if ($ExpectedOutput -and -not ($output -match $ExpectedOutput)) {
        throw "$Name did not report the expected result."
    }
}

$version = & $resolvedGodotPath --version
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^4\.7\.2.*mono') {
    throw "Godot 4.7.2 .NET is required; found '$version'."
}

Invoke-Godot "GdUnit4 plugin import" @(
    '--headless',
    '--editor',
    '--path', $PSScriptRoot,
    '--import',
    '--quit'
)

Invoke-Godot "GdUnit4 Client tests" @(
    '--headless',
    '--path', $PSScriptRoot,
    '-s',
    'res://addons/gdUnit4/bin/GdUnitCmdTool.gd',
    '-a', 'res://test/Client',
    '-c',
    '--ignoreHeadlessMode',
    '-rd', 'user://trackstorm-gdunit-reports'
) 'Overall Summary'

Write-Host "`nGdUnit4 checks passed."
