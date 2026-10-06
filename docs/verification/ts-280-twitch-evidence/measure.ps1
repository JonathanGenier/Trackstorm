param([string]$Label,[int]$Start=180,[int]$End=600)
$rows=foreach($file in Get-ChildItem ('.godot/ts-267/'+$Label) -Filter '*.json' | Where-Object Name -ne 'summary.json') {
    $all=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $trace=@($all|Where-Object {$_.frame -ge $Start -and $_.frame -lt $End})
    $angle=[double]($file.BaseName.Split('-')[-1])
    $sin=[Math]::Sin($angle);$cos=[Math]::Cos($angle)
    $position=@($trace|ForEach-Object {-($_.x*$sin+$_.z*$cos)})
    $velocity=@($trace|ForEach-Object {-($_.vx*$sin+$_.vz*$cos)})
    $step=@(for($i=1;$i -lt $position.Count;$i++){$position[$i]-$position[$i-1]})
    $travel=($step|ForEach-Object {[Math]::Abs($_)}|Measure-Object -Sum).Sum
    $backward=-($step|Where-Object {$_ -lt 0}|Measure-Object -Sum).Sum
    $reversals=0;$sign=0
    foreach($value in $step) {if([Math]::Abs($value) -lt 0.00001){continue};$next=[Math]::Sign($value);if($sign -ne 0 -and $next -ne $sign){$reversals++};$sign=$next}
    [ordered]@{name=$file.BaseName;from=$Start;to=$End;frames=$trace.Count;net=$position[-1]-$position[0];range=($position|Measure-Object -Maximum).Maximum-($position|Measure-Object -Minimum).Minimum;backwardTravel=$backward;repeatedTravel=$travel-[Math]::Abs($position[-1]-$position[0]);reversalsAbove10Microns=$reversals;peakStep=($step|ForEach-Object {[Math]::Abs($_)}|Measure-Object -Maximum).Maximum;minVelocity=($velocity|Measure-Object -Minimum).Minimum;maxVelocity=($velocity|Measure-Object -Maximum).Maximum;contactFrames=@($trace|Where-Object contacts -gt 0).Count}
}
$rows | ConvertTo-Json -Depth 5 | Set-Content ('.godot/ts-280/twitch/'+$Label+'-motion.json')
$rows | ForEach-Object {[pscustomobject]$_} | Select-Object name,net,range,backwardTravel,reversalsAbove10Microns,peakStep,minVelocity,maxVelocity | Format-Table
