param (
    [Parameter(Mandatory)] [string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild,
    [ValidateRange(3, 50)] [int]$Cycles = 3,
    [switch]$CollectDuringLoading,
    [switch]$ResourcesOnly
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    dotnet build Trackstorm.sln -c Debug -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Post-match build failed.' }
}
$postMatchOutput = Join-Path $PSScriptRoot ('.godot/post-match-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $postMatchOutput -Force | Out-Null
$arguments = @('--path', $PSScriptRoot, 'res://scenes/verification/post_match_checks.tscn', '--', "--post-match-output=$postMatchOutput", "--post-match-cycles=$Cycles")
if (-not $Visual) { $arguments = @('--headless') + $arguments }
if ($CollectDuringLoading) { $arguments += '--post-match-gc' }
if ($ResourcesOnly) { $arguments += '--post-match-resources-only' }
$log = & $GodotPath @arguments 2>&1
$exitCode = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $postMatchOutput 'runtime.log')
$log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0 -or $log -match 'ERROR:|WARNING:' -or -not ($log -match 'Post-match integration passed.')) {
    throw "Post-match integration failed. Artifacts: $postMatchOutput"
}
Write-Host "Post-match verification artifacts: $postMatchOutput"
