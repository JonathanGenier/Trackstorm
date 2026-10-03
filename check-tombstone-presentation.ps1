param(
    [Parameter(Mandatory)][string]$GodotPath,
    [switch]$Visual,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    & "$PSScriptRoot/tools/check-fast.ps1" -Area Client
    if ($LASTEXITCODE -ne 0) { throw 'Tombstone presentation build failed.' }
}
$output = Join-Path $PSScriptRoot '.godot/ts-219/playtest'
New-Item -ItemType Directory -Force $output | Out-Null
$run = 'check-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$inputPath = Join-Path $output 'input.json'
if (Test-Path -LiteralPath $inputPath) { Remove-Item -LiteralPath $inputPath }
$arguments = @('--path', ('"{0}"' -f $PSScriptRoot), 'res://scenes/verification/tombstone_playtest.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$process = Start-Process -FilePath (Resolve-Path -LiteralPath $GodotPath).Path -ArgumentList $arguments -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $output "$run.log") -RedirectStandardError (Join-Path $output "$run-errors.log")
function Command([string]$name, [hashtable]$values) {
    $values.id = "$run-$name"
    $temporary = Join-Path $output 'input.next'
    $values | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temporary
    [System.IO.File]::Move($temporary, $inputPath, $true)
    $result = Join-Path $output ($values.id + '.json')
    $deadline = [DateTime]::UtcNow.AddSeconds(35)
    while (-not (Test-Path -LiteralPath $result)) {
        if ($process.HasExited) { throw "Playtest exited before $name." }
        if ([DateTime]::UtcNow -gt $deadline) { throw "Playtest command $name timed out." }
        Start-Sleep -Milliseconds 100
    }
    # The next physics command is only read after the previous complete capture.
    return Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
}
function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Host "PASS: $message"
}
try {
    $mounted = Command 'mounted' @{grant=$true;frames=120;camera=@(4,203.2,8);look=@(0,201.6,1.8)}
    $first = @($mounted.tombstones)[0].Id
    Assert (@($mounted.tombstones).Count -eq 1 -and $mounted.tombstones[0].Stage -eq 1) 'Selected acquisition presents rear shield.'
    Assert ($mounted.trace[-1].shields[0].rack -gt .99) 'Production rack reaches its raised mounting pose.'
    $stored = Command 'stored' @{grant=$true;select=$true;frames=90}
    Assert (@($stored.tombstones | Where-Object Stage -eq 0).Count -eq 1) 'Second slot retains an independently stored Tombstone.'
    $selected = Command 'selected' @{select=$true;frames=90}
    Assert (@($selected.tombstones | Where-Object Stage -eq 1)[0].Id -eq $first) 'Slot switching restores the original shield identity.'
    $hurt = Command 'shield-damage' @{damage=300;entity=$first;frames=40}
    Assert (@($hurt.tombstones | Where-Object Id -eq $first)[0].HP -eq 700) 'Mounted health presentation follows accepted damage.'
    $transition = Command 'transition' @{use=$true;frames=12;camera=@(5,203.2,10);look=@(0,201.8,3.8)}
    Assert (@($transition.tombstones | Where-Object Id -eq $first)[0].Stage -eq 2) 'Normal input deploys the original entity.'
    Assert (@($transition.trace | Where-Object { $_.walls.Count -gt 0 -and $_.walls[0].expansion -gt 0 -and $_.walls[0].expansion -lt 1 }).Count -gt 0) 'Wing expansion is observed between folded and expanded poses.'
    $wall = Command 'expanded' @{frames=60}
    Assert ($wall.trace[-1].walls[0].expansion -eq 1 -and $wall.trace[-1].walls[0].hp -eq 700) 'Expanded wall retains mounted damage.'
    $remoteMounted = Command 'remote-mounted' @{owner=2;grant=$true;frames=90;camera=@(15,204,10);look=@(10,201.7,2)}
    $remoteId = @($remoteMounted.tombstones | Where-Object Owner -eq 2)[0].Id
    $remoteWall = Command 'remote-deploy' @{owner=2;use=$true;frames=90;camera=@(16,207,15);look=@(5,201.8,4)}
    Assert (@($remoteWall.tombstones | Where-Object Stage -eq 2).Count -eq 2) 'Host and remote input deploy independent simultaneous walls.'
    Assert ((@($remoteWall.tombstones | Sort-Object Id | Select-Object Id,Stage,HP) | ConvertTo-Json -Compress) -eq (@($remoteWall.remote | Sort-Object Id | Select-Object Id,Stage,HP) | ConvertTo-Json -Compress)) 'Remote publication agrees on every live identity, stage and HP.'
    $damaged = Command 'wall-damage' @{damage=450;entity=$first;frames=45;camera=@(4,203,10);look=@(0,201.8,4)}
    Assert (@($damaged.tombstones | Where-Object Id -eq $first)[0].HP -eq 250 -and @($damaged.tombstones | Where-Object Id -eq $remoteId)[0].HP -eq 1000) 'Heavy damage leaves the other wall unchanged.'
    $broken = Command 'destruction' @{damage=300;entity=$first;frames=12}
    Assert (@($broken.tombstones | Where-Object Id -eq $first).Count -eq 0) 'Destroyed wall leaves the live authority set immediately.'
    $cleanup = Command 'cleanup' @{frames=90}
    Assert (@($cleanup.trace[-1].walls).Count -eq 1) 'Destruction cleanup leaves the surviving wall intact.'
    Assert ($broken.effects -gt 0 -and $cleanup.effects -eq 0) 'Non-colliding destruction visuals play and are released after their bounded lifetime.'
    $repeat = Command 'repeat' @{select=$true;spawn=@(-8,201.7,0);frames=90;camera=@(-3,204,10);look=@(-8,201.8,3)}
    $repeatWall = Command 'repeat-deploy' @{use=$true;frames=90}
    Assert (@($repeatWall.tombstones | Where-Object Stage -eq 2).Count -eq 2) 'Stored shield deploys successfully after previous destruction.'
    $again = Command 'reacquire' @{grant=$true;select=$true;frames=90}
    Assert (@($again.tombstones | Where-Object Stage -eq 1).Count -eq 1) 'Reacquisition creates a new mounted shield after repeated use.'
    $chase = Command 'chase-mounted' @{chase=$true;frames=60}
    $landing = Command 'mounted-landing' @{spawn=@(-8,202.5,0);frames=60}
    $drive = Command 'mounted-driving' @{throttle=.7;steer=.25;frames=100}
    Assert (@($drive.tombstones | Where-Object Stage -eq 1).Count -eq 1) 'Mounted presentation survives ordinary driving and steering.'
    $clearances = @($mounted,$stored,$selected,$repeat,$again,$landing,$drive) | ForEach-Object { $_.trace.shields } | Where-Object shield | ForEach-Object articulationClearance
    Assert (($clearances | Measure-Object -Minimum).Minimum -gt .01) 'Mounted armor clears articulated rear tires and trunk lids during rack motion, landing, driving and steering.'
    $temporary = Join-Path $output 'input.next'
    @{id="$run-quit";quit=$true} | ConvertTo-Json | Set-Content -LiteralPath $temporary
    [System.IO.File]::Move($temporary, $inputPath, $true)
    if (-not $process.WaitForExit(10000)) { throw 'Playtest did not exit.' }
    $errors = Get-Content -LiteralPath (Join-Path $output "$run-errors.log") -Raw
    Assert ($process.ExitCode -eq 0 -and -not ($errors -match 'ERROR:|WARNING:|Exception')) 'Runtime exits cleanly without errors, warnings or exceptions.'
    Write-Host "Tombstone presentation passed. Evidence: $output/$run*"
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    $process.Dispose()
}
