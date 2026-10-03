$ErrorActionPreference = 'Stop'
$game = 'C:/Users/orsin/OneDrive/Desktop/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$root = (Get-Location).Path
$output = Join-Path $root '.godot/ts-274/playtest-results'
$commands = Join-Path $root '.godot/ts-274/playtest'
New-Item -ItemType Directory -Path $output,$commands -Force | Out-Null
$session = [Guid]::NewGuid().ToString('N')
$cases = @(
    @{ name='bank-impact'; frames=55; steer=0; throttle=1; brake=0; spawn=@(0,1.145,0); yaw=-1.5707963; speed=12 },
    @{ name='bank-pressure-turn'; frames=240; steer=1; throttle=1; brake=0 },
    @{ name='bank-reverse'; frames=180; steer=0.4; throttle=0; brake=1 },
    @{ name='bank-recontact'; frames=180; steer=-0.4; throttle=1; brake=0 },
    @{ name='pillar-impact'; frames=90; steer=0; throttle=0; brake=0; spawn=@(0,1.145,-10); yaw=-1.5707963; speed=35 },
    @{ name='pillar-reverse'; frames=160; steer=0.1; throttle=0; brake=1 },
    @{ name='perimeter-impact'; frames=90; steer=0; throttle=0; brake=0; spawn=@(0,1.145,91); groundSpawn=$true; yaw=3.14159265; speed=35 },
    @{ name='perimeter-reverse'; frames=150; steer=0; throttle=0; brake=1 },
    @{ name='rock-impact'; frames=120; steer=0; throttle=1; brake=0; spawn=@(-43,1.145,78); yaw=0; speed=12 },
    @{ name='rock-reverse'; frames=180; steer=0; throttle=0; brake=1 }
)
$process = Start-Process -FilePath $game -ArgumentList @('--path',('"'+$root+'"'),'--resolution','1152x648','res://scenes/verification/handling_playtest.tscn','--','--world-collision-playtest','--handling-network') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'runtime.log') -RedirectStandardError (Join-Path $output 'runtime-errors.log')
try {
    Start-Sleep -Seconds 3
    foreach ($case in $cases) {
        $case.id = $session + '-' + $case.name
        $case | ConvertTo-Json -Compress | Set-Content (Join-Path $commands 'input.tmp')
        Move-Item -LiteralPath (Join-Path $commands 'input.tmp') -Destination (Join-Path $commands 'input.json') -Force
        $deadline = [DateTime]::UtcNow.AddSeconds(35)
        do {
            Start-Sleep -Milliseconds 200
            $done = if (Test-Path (Join-Path $commands 'completed-command.json')) { Get-Content (Join-Path $commands 'completed-command.json') -Raw | ConvertFrom-Json } else { $null }
        } while ($done.id -ne $case.id -and [DateTime]::UtcNow -lt $deadline -and -not $process.HasExited)
        if ($done.id -ne $case.id) { throw ('Playtest timed out: ' + $case.name) }
        Copy-Item (Join-Path $commands 'view.png') (Join-Path $output ($case.name + '.png'))
        Copy-Item (Join-Path $commands 'trace.json') (Join-Path $output ($case.name + '.json'))
        $trace = Get-Content (Join-Path $commands 'trace.json') -Raw | ConvertFrom-Json
        [pscustomobject]@{name=$case.name; frames=$trace.Count; final=$trace[-1].position; hp=$trace[-1].hp; contactFrames=($trace | Where-Object contacts -gt 0).Count} | ConvertTo-Json -Compress | Write-Output
    }
} finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }
