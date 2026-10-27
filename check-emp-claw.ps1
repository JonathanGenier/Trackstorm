param([Parameter(Mandatory)][string]$GodotPath, [switch]$Visual, [switch]$NoBuild, [switch]$Preview)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.Client.csproj -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'EMP Claw build failed.' }
}
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/emp_claw_checks.tscn', '--resolution', '1440x900')
if (-not ($Visual -or $Preview)) { $arguments = @('--headless') + $arguments }
if ($Preview) { $arguments += @('--', '--preview') }
else { $arguments += @('--quit-after', '20000') }
$output = & $GodotPath @arguments 2>&1
$code = $LASTEXITCODE
$output | Write-Output
if ($code -ne 0 -or $output -match 'ERROR:|WARNING:' -or (-not $Preview -and -not ($output -match 'EMP Claw presentation passed:'))) {
    throw 'EMP Claw presentation verification failed.'
}
