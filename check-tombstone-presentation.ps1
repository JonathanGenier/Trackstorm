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
$ground = & $GodotPath --headless --path $PSScriptRoot res://scenes/verification/mounted_shield_ground_checks.tscn 2>&1
$ground | Write-Output
if ($LASTEXITCODE -ne 0 -or $ground -match 'ERROR:|WARNING:|Exception' -or -not ($ground -match 'Mounted shield ground passed:')) { throw 'Mounted shield ground regression failed.' }
$output = Join-Path $PSScriptRoot '.godot/ts-219/playtest'
New-Item -ItemType Directory -Force $output | Out-Null
$run = 'check-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$inputPath = Join-Path $output 'input.json'
if (Test-Path -LiteralPath $inputPath) { Remove-Item -LiteralPath $inputPath }
$arguments = @('--path', ('"{0}"' -f $PSScriptRoot), 'res://scenes/verification/tombstone_playtest.tscn')
if (-not $Visual) { $arguments = @('--headless') + $arguments }
$process = Start-Process -FilePath (Resolve-Path -LiteralPath $GodotPath).Path -ArgumentList $arguments -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $output "$run.log") -RedirectStandardError (Join-Path $output "$run-errors.log")
function Publish([hashtable]$values) {
    $temporary = Join-Path $output 'input.next'
    $values | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temporary
    # Windows file scanners can briefly hold the destination despite the fixture's
    # shared-delete reader. Retry only the atomic handoff, with a bounded deadline.
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try { [System.IO.File]::Move($temporary, $inputPath, $true); return }
        catch {
            if ($attempt -eq 19) { throw }
            Start-Sleep -Milliseconds 50
        }
    }
}
function Command([string]$name, [hashtable]$values) {
    $values.id = "$run-$name"
    Publish $values
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
    foreach ($peer in @('shields', 'remoteShields')) {
        $lifting = @($mounted.trace.$peer | Where-Object { $_.id -eq 1 -and $_.rack -ge .50 -and $_.rack -le .85 })
        Assert ($lifting.Count -ge 5 -and @($lifting | Where-Object { -not $_.visible -or [Math]::Abs($_.scale - .42) -gt .001 -or $_.centerFold -lt .999 -or $_.horizontalFold -lt .999 }).Count -eq 0) "Compact folded shield rides the extending rack on $peer."
    }
    Assert (@($mounted.trace | Where-Object { $_.shields[0].mount -gt 0 -and $_.shields[0].mount -lt 1 }).Count -gt 10 -and $mounted.trace[-1].shields[0].mount -eq 1) 'Selection unfolds progressively from the rack into the rear shield.'
    $centerMotion=@($mounted.trace.shields | Where-Object { $_.visible -and $_.centerFold -gt .01 -and $_.centerFold -lt .99 })
    Assert ($centerMotion.Count -gt 5 -and @($centerMotion | Where-Object { [Math]::Abs($_.scale - 1) -gt .001 }).Count -eq 0) 'The two center leaves hinge open at full constant size.'
    $rowMotion = @($mounted.trace.shields | Where-Object { $_.visible -and $_.horizontalFold -gt .01 -and $_.horizontalFold -lt .99 })
    Assert ($rowMotion.Count -gt 5 -and @($rowMotion | Where-Object { [Math]::Abs($_.scale - 1) -gt .001 -or $_.centerFold -gt .001 }).Count -eq 0) 'All four horizontal hinges unfold at full size after the center opens.'
    Assert ($mounted.trace[-1].shields[0].horizontalFold -eq 0 -and $mounted.trace[-1].remoteShields[0].horizontalFold -eq 0) 'Both peers straighten the top and bottom rows into the full-height shield.'
    $stowing = Command 'stowing' @{select=$true;frames=16;camera=@(6,204,11);look=@(0,202,4)}
    Assert (-not $stowing.trace[-1].shields[0].shield -and $stowing.trace[-1].shields[0].visible -and $stowing.trace[-1].shields[0].mount -gt 0 -and $stowing.trace[-1].shields[0].mount -lt 1) 'Deselection visibly folds the shield while authority deselects immediately.'
    Assert (@($stowing.trace | Where-Object { $_.shields[0].mount -gt 0 -and $_.shields[0].rack -lt .99 }).Count -eq 0) 'Rack stays raised until the shield folds safely back.'
    $stowed = Command 'stowed' @{frames=100}
    Assert (-not $stowed.trace[-1].shields[0].visible -and $stowed.trace[-1].shields[0].rack -eq 0) 'Folded shield and carriage stow with the closed deck.'
    $rackLift = Command 'rack-lift' @{select=$true;frames=22;camera=@(5,205,1);look=@(0,202.3,1.8)}
    $opening = Command 'opening' @{frames=17;camera=@(7,206,4);look=@(0,202.8,2)}
    $swing = Command 'swing' @{frames=12}
    $centerOpening = Command 'center-opening' @{frames=8;camera=@(6,204,11);look=@(0,202,4)}
    $ready = Command 'ready' @{frames=90;camera=@(6,204,-1);look=@(0,202,2)}
    Assert ($ready.trace[-1].shields[0].mount -eq 1 -and $ready.trace[-1].remoteShields[0].mount -eq 1) 'Host and remote peer finish the same selection animation.'
    $rapidOut = Command 'rapid-out' @{select=$true;frames=10}
    $rapidIn = Command 'rapid-in' @{select=$true;frames=230}
    Assert ($rapidIn.trace[-1].shields[0].mount -eq 1 -and $rapidIn.trace[-1].remoteShields[0].mount -eq 1) 'Rapid reselection settles into one coherent mounted shield on both peers.'
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
    $nitro = Command 'switch-to-nitro' @{grant=$true;item='Nitro';select=$true;frames=150}
    Assert (-not $nitro.trace[-1].shields[0].visible -and $nitro.trace[-1].shields[0].mount -eq 0) 'Switching to Boost finishes the shield return before replacing the rack payload.'
    $early = Command 'early-selected' @{owner=2;spawn=@(18,201.7,-12);grant=$true;frames=40;camera=@(24,205,-5);look=@(18,202,-9)}
    $earlyUse = Command 'early-use' @{owner=2;use=$true;frames=12}
    Assert (@($earlyUse.tombstones | Where-Object { $_.Owner -eq 2 -and $_.Stage -eq 2 }).Count -eq 2) 'Use during selection deploys immediately through existing authority.'
    Assert (@($earlyUse.trace.walls | Where-Object { $_.centerFold -gt .01 -and $_.centerFold -lt .99 }).Count -gt 0) 'Early release continues the captured center hinge fold into the world wall.'
    Assert (@($earlyUse.trace.walls | Where-Object { $_.horizontalFold -gt .01 -and $_.horizontalFold -lt .99 }).Count -gt 0) 'Early release continues the captured horizontal fold without snapping the rows open.'
    $earlyDone = Command 'early-finished' @{frames=100}
    Assert ($earlyDone.trace[-1].shields[1].mount -eq 0 -and -not $earlyDone.trace[-1].shields[1].visible) 'Empty carriage returns cleanly after early deployment.'
    $clearances = @($mounted,$stowing,$stowed,$rackLift,$opening,$swing,$centerOpening,$ready,$rapidOut,$rapidIn,$stored,$selected,$repeat,$again,$landing,$drive,$nitro,$early,$earlyUse) | ForEach-Object { $_.trace.shields } | Where-Object visible | ForEach-Object articulationClearance
    Assert (($clearances | Measure-Object -Minimum).Minimum -gt .01) 'Mounted armor clears articulated rear tires and trunk lids during rack motion, landing, driving and steering.'
    $park = Command 'slope-park-other' @{owner=1;spawn=@(25,201.7,0);frames=30}
    $slopeReady = Command 'slope-ready' @{owner=2;ramps=$true;spawn=@(0,201.7,0);grant=$true;frames=120;chase=$true}
    $slopeId = @($slopeReady.tombstones | Where-Object { $_.Owner -eq 2 -and $_.Stage -eq 1 })[0].Id
    $peak = 0; $crossed = $false
    for ($phase=0; $phase -lt 16; $phase++) {
        $slope = Command "slope-drive-$phase" @{owner=2;throttle=.55;frames=60}
        $positions = @($slope.trace.vehicles | Where-Object id -eq 2).position
        $peak = [Math]::Max($peak, ($positions.Y | Measure-Object -Maximum).Maximum)
        Assert (@($slope.tombstones | Where-Object Id -eq $slopeId)[0].HP -eq 1000) "Slope driving phase $phase preserves mounted shield HP."
        if (@($slope.vehicles | Where-Object id -eq 2)[0].pose.Position.Z -lt -76) { $crossed=$true; break }
    }
    Assert ($crossed -and $peak -gt 209) 'Ordinary driving climbs, crests and descends the twenty-degree slope course.'
    $slopeDone = Command 'slope-stop' @{owner=2;brake=1;frames=40}
    Assert (@($slopeDone.remote | Where-Object Id -eq $slopeId)[0].HP -eq 1000) 'Remote peer retains the same undamaged mounted shield after slope traversal.'
    Publish @{id="$run-quit";quit=$true}
    if (-not $process.WaitForExit(10000)) { throw 'Playtest did not exit.' }
    $errors = Get-Content -LiteralPath (Join-Path $output "$run-errors.log") -Raw
    Assert ($process.ExitCode -eq 0 -and -not ($errors -match 'ERROR:|WARNING:|Exception')) 'Runtime exits cleanly without errors, warnings or exceptions.'
    Write-Host "Tombstone presentation passed. Evidence: $output/$run*"
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    $process.Dispose()
}
