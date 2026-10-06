param([string]$Phase)
$results=foreach($kind in @('large','barrier','dirt','tunnel')) {
    $suffix=if($Phase -eq 'before' -and $kind -in @('large','barrier')){'-v2'}else{''}
    $trace=Get-Content ('.godot/ts-280/playtest/twitch-'+$Phase+'-'+$kind+'-held'+$suffix+'-trace.json') -Raw|ConvertFrom-Json
    $window=@($trace|Select-Object -Skip 180)
    $axis=if($kind -in @('dirt','tunnel')){0}else{2}
    $sign=if($kind -eq 'large'){-1}else{1}
    $position=@($window|ForEach-Object {$sign*$_.position[$axis]})
    $steps=@(for($i=1;$i -lt $position.Count;$i++){$position[$i]-$position[$i-1]})
    $previous=0;$reversals=0
    foreach($step in $steps) {if([Math]::Abs($step)-lt 0.00001){continue};$direction=[Math]::Sign($step);if($previous -ne 0 -and $previous -ne $direction){$reversals++};$previous=$direction}
    [ordered]@{kind=$kind;frames=$window.Count;contacts=@($window|Where-Object contacts -gt 0).Count;net=$position[-1]-$position[0];range=($position|Measure-Object -Maximum).Maximum-($position|Measure-Object -Minimum).Minimum;backward=-($steps|Where-Object {$_ -lt 0}|Measure-Object -Sum).Sum;repeated=($steps|ForEach-Object {[Math]::Abs($_)}|Measure-Object -Sum).Sum-[Math]::Abs($position[-1]-$position[0]);reversalsAbove10Microns=$reversals;peakStep=($steps|ForEach-Object {[Math]::Abs($_)}|Measure-Object -Maximum).Maximum;minObservedVelocity=($window|ForEach-Object {$sign*$_.observedVelocity[$axis]}|Measure-Object -Minimum).Minimum;maxObservedVelocity=($window|ForEach-Object {$sign*$_.observedVelocity[$axis]}|Measure-Object -Maximum).Maximum;hp=$window[-1].hp;up=$window[-1].up}
}
$results|ConvertTo-Json -Depth 6|Set-Content ('.godot/ts-280/twitch/playtest-'+$Phase+'-motion.json')
$results|ConvertTo-Json -Depth 6
