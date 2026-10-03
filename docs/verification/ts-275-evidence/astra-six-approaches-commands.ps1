$ErrorActionPreference = 'Stop'
$game = 'C:/Users/orsin/OneDrive/Desktop/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$root = (Get-Location).Path
$output = Join-Path $root '.godot/ts-275/astra'
$commands = Join-Path $root '.godot/ts-275/playtest'
New-Item -ItemType Directory -Path $output,$commands -Force | Out-Null
$process = Start-Process -FilePath $game -ArgumentList @('--path',('"'+$root+'"'),'--resolution','1152x648','res://scenes/verification/handling_playtest.tscn','--','--tunnel-scrape-playtest','--handling-network') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'runtime.log') -RedirectStandardError (Join-Path $output 'runtime-errors.log')
$process.Id | Set-Content (Join-Path $output 'process-id.txt')
$cases = @()
foreach ($trial in @(
    @{side=1;speed=25;angle=0},
    @{side=-1;speed=25;angle=0},
    @{side=1;speed=12;angle=0.15},
    @{side=-1;speed=12;angle=-0.15},
    @{side=1;speed=25;angle=-0.15},
    @{side=-1;speed=25;angle=0.15}
)) {
    $tag = 'approach-' + $trial.side + '-' + $trial.speed + '-' + $trial.angle
    $cases += @{name=$tag;frames=55;steer=0;throttle=1;brake=0;spawn=@(($trial.side * 30),1.145,14);groundSpawn=$true;yaw=($trial.side * 1.5707963 + $trial.angle);speed=$trial.speed}
    $cases += @{name=($tag+'-fall');frames=100;steer=0.35;throttle=1;brake=0}
    $cases += @{name=($tag+'-press');frames=180;steer=0.6;throttle=1;brake=0}
    $cases += @{name=($tag+'-reverse');frames=180;steer=0.2;throttle=0;brake=1}
}
foreach ($case in $cases) {
    $case.id = [Guid]::NewGuid().ToString('N')
    $case | ConvertTo-Json -Compress | Set-Content (Join-Path $commands 'input.tmp')
    Move-Item -LiteralPath (Join-Path $commands 'input.tmp') -Destination (Join-Path $commands 'input.json') -Force
    $deadline = [DateTime]::UtcNow.AddSeconds(50)
    do {
        Start-Sleep -Milliseconds 200
        $done = if (Test-Path (Join-Path $commands 'completed-command.json')) { Get-Content (Join-Path $commands 'completed-command.json') -Raw | ConvertFrom-Json } else { $null }
    } while ($done.id -ne $case.id -and [DateTime]::UtcNow -lt $deadline -and -not $process.HasExited)
    if ($done.id -ne $case.id) { throw ('Playtest timed out: ' + $case.name) }
    Copy-Item (Join-Path $commands 'view.png') (Join-Path $output ($case.name + '.png'))
    Copy-Item (Join-Path $commands 'trace.json') (Join-Path $output ($case.name + '.json'))
    $trace = Get-Content (Join-Path $commands 'trace.json') -Raw | ConvertFrom-Json
    [pscustomobject]@{name=$case.name;frames=$trace.Count;final=$trace[-1].position;hp=$trace[-1].hp;contactFrames=($trace | Where-Object contacts -gt 0).Count} | ConvertTo-Json -Compress | Write-Output
}

Stop-Process -Id $process.Id
