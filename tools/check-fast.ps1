param(
    [ValidateSet('Auto', 'Core', 'Client', 'Transport', 'Docs')][string]$Area = 'Auto',
    [string]$GodotPath = $env:GODOT_PATH,
    [switch]$IncludeExtended,
    [switch]$RequireRuntime,
    [switch]$Plan,
    [switch]$Explain,
    [switch]$Json,
    [string]$OutputDirectory = '',
    [switch]$DetailedOutput
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot/fast-check-routes.ps1"
. "$PSScriptRoot/agent-checks.ps1"
if ($Json -and -not $Plan) { throw '-Json requires -Plan; execution evidence is written to results.json.' }
$paths = @()
if ($Area -eq 'Auto') {
    $paths = @(Get-StoryChangedPaths -Root $root)
    $checkPlan = Get-FastCheckPlan -Paths $paths
}
else {
    $checkPlan = Get-FastCheckPlan -Paths @()
    switch ($Area) {
        'Core' { $checkPlan.CoreTests = $true }
        'Client' { $checkPlan.ClientBuild = $true }
        'Transport' { $checkPlan.TransportTests = $true }
    }
}
$explanation = if ($Explain -and $Area -eq 'Auto') { Get-FastCheckExplanation -Paths $paths } else { $null }
if ($Plan) {
    $view = [pscustomobject]@{ Area = $Area; Paths = $paths; Checks = $checkPlan; Reasons = $explanation; Executed = $false }
    if ($Json) { $view | ConvertTo-Json -Depth 10 }
    else {
        Write-Host "Plan only; no verification executed. Area: $Area; changed paths: $($paths.Count)"
        Get-PlanCheckNames -Plan $checkPlan | ForEach-Object { Write-Host "  $_" }
        if ($explanation) { foreach ($key in $explanation.Keys) { Write-Host "$key <- $($explanation[$key] -join ', ')" } }
    }
    exit 0
}
$selected = @(Get-PlanCheckNames -Plan $checkPlan)
if ($selected.Count -eq 0) {
    Write-Host 'No iteration checks selected. No tests were executed. Final Story verification remains required.'
    exit 0
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('.godot/fast-checks/' + [guid]::NewGuid().ToString('N')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$relativeOutput = [IO.Path]::GetRelativePath($root, $OutputDirectory).Replace('\', '/')
if ($relativeOutput -ne '..' -and -not $relativeOutput.StartsWith('../') -and -not [IO.Path]::IsPathRooted($relativeOutput)) {
    $ignored = Invoke-ContextGit -Root $root -Arguments @('check-ignore', '-q', '--', "$relativeOutput/results.json") -AllowFailure
    if ($ignored.ExitCode -ne 0) { throw 'Evidence inside the repository must use a Git-ignored directory, such as .godot/fast-checks.' }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$reportPath = Join-Path $OutputDirectory 'results.json'
$report = [ordered]@{
    SchemaVersion = 1; Area = $Area; Status = 'RUNNING'; Scope = 'Targeted iteration only; not final Story verification'
    Context = $null; GodotPath = $GodotPath; Paths = $paths; Plan = $checkPlan; Reasons = $explanation
    Checks = [Collections.Generic.List[object]]::new(); Pending = [Collections.Generic.List[string]]::new()
    Unexecuted = [Collections.Generic.List[string]]::new(); Error = $null
}
function Save-Report { [IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 12)) }
function Run-Check {
    param([string]$Name, [string]$Executable, [string[]]$Arguments = @(), [hashtable]$Parameters = @{})
    $result = Invoke-AgentCheck -Name $Name -Executable $Executable -Arguments $Arguments -Parameters $Parameters -Root $root -LogDirectory $OutputDirectory -DetailedOutput:$DetailedOutput
    $report.Checks.Add($result)
    [void]$report.Unexecuted.Remove($Name)
    Save-Report
    if ($result.Status -ne 'PASS') { throw "$Name failed; inspect $($result.StdoutLog) and $($result.StderrLog)." }
}
function Run-Script {
    param([string]$Script, [hashtable]$Parameters = @{})
    Run-Check -Name $Script -Executable (Join-Path $root $Script) -Parameters $Parameters
}
try {
    $report.Context = Get-VerificationContext -Root $root
    $resolvedGodot = $null
    $runtimeNames = @($checkPlan.RuntimeScripts)
    $extendedNames = @($checkPlan.ExtendedScripts | Where-Object { $_ -notin $runtimeNames })
    if ($runtimeNames.Count -gt 0 -or ($IncludeExtended -and $extendedNames.Count -gt 0)) {
        if ($GodotPath) {
            if (Test-Path -LiteralPath $GodotPath -PathType Leaf) { $resolvedGodot = (Resolve-Path -LiteralPath $GodotPath).Path }
            else { $resolvedGodot = (Get-Command -Name $GodotPath -CommandType Application -ErrorAction Stop).Source }
        }
    }
    $report.GodotPath = $resolvedGodot
    foreach ($scenario in $checkPlan.ManualScenarios) { $report.Pending.Add("Manual: $scenario") }
    if (-not $IncludeExtended) { foreach ($script in $extendedNames) { $report.Pending.Add("Extended: $script") } }
    if (-not $resolvedGodot) {
        foreach ($script in $runtimeNames) { $report.Pending.Add("Runtime: $script (Godot unavailable)") }
        if ($IncludeExtended) { foreach ($script in $extendedNames) { $report.Pending.Add("Extended: $script (Godot unavailable)") } }
    }
    $runtimeToRun = @()
    if ($resolvedGodot) {
        $runtimeToRun = $runtimeNames
        if ($IncludeExtended) { $runtimeToRun += $extendedNames }
    }
    if ($checkPlan.WorkflowTests) { $report.Unexecuted.Add('tools/test-workflow-tools.ps1') }
    if ($checkPlan.VersionChecks) { $report.Unexecuted.Add('tools/test-version.ps1') }
    if ($checkPlan.MediaChecks) { $report.Unexecuted.Add('tools/test-frontend-media.ps1'); $report.Unexecuted.Add('tools/check-frontend-media.ps1') }
    if ($checkPlan.CoreTests) { $report.Unexecuted.Add('Core tests') }
    if ($checkPlan.TransportTests) { $report.Unexecuted.Add('Transport tests') }
    if ($checkPlan.ServiceTests) { $report.Unexecuted.Add('Authority-lease service tests') }
    if ($runtimeToRun.Count -gt 0) { $report.Unexecuted.Add('Runtime Debug build') }
    elseif ($checkPlan.ClientBuild) { $report.Unexecuted.Add('Client Debug build') }
    foreach ($script in $runtimeToRun) { $report.Unexecuted.Add($script) }
    Save-Report
    if (($RequireRuntime -and $runtimeNames.Count -gt 0 -and -not $resolvedGodot) -or
        ($IncludeExtended -and $extendedNames.Count -gt 0 -and -not $resolvedGodot)) {
        throw 'Required runtime is unavailable. Supply -GodotPath or GODOT_PATH. No runtime check passed.'
    }
    if ($checkPlan.WorkflowTests) { Run-Script 'tools/test-workflow-tools.ps1' }
    if ($checkPlan.VersionChecks) { Run-Script 'tools/test-version.ps1' }
    if ($checkPlan.MediaChecks) { Run-Script 'tools/test-frontend-media.ps1'; Run-Script 'tools/check-frontend-media.ps1' }
    if ($checkPlan.CoreTests) { Run-Check 'Core tests' 'dotnet' @('test', 'code/Tests/Trackstorm.Core.Tests.csproj', '-c', 'Release') }
    if ($checkPlan.TransportTests) { Run-Check 'Transport tests' 'dotnet' @('test', 'code/TransportTests/Trackstorm.Transport.Tests.csproj', '-c', 'Release', '--filter', 'TestCategory!=Native') }
    if ($checkPlan.ServiceTests) {
        $serviceRoot = Join-Path $root 'services/authority-lease'
        if (-not (Test-Path (Join-Path $serviceRoot 'node_modules'))) { Run-Check 'Authority-lease npm install' 'npm' @('ci', '--prefix', $serviceRoot) }
        Run-Check 'Authority-lease service tests' 'npm' @('test', '--prefix', $serviceRoot)
    }
    # Build once within this invocation; no persistent build cache or prior-run pass is trusted.
    $buildFingerprint = $null
    if ($runtimeToRun.Count -gt 0) {
        Assert-VerificationContext -Root $root -Fingerprint $report.Context.Fingerprint
        Run-Check 'Runtime Debug build' 'dotnet' @('build', 'Trackstorm.sln', '-c', 'Debug', '-warnaserror')
        Assert-VerificationContext -Root $root -Fingerprint $report.Context.Fingerprint
        $buildFingerprint = $report.Context.Fingerprint
    }
    elseif ($checkPlan.ClientBuild) { Run-Check 'Client Debug build' 'dotnet' @('build', 'Trackstorm.Client.csproj', '-c', 'Debug', '-warnaserror') }
    foreach ($script in $runtimeToRun) {
        Assert-VerificationContext -Root $root -Fingerprint $buildFingerprint
        $scriptPath = Join-Path $root $script
        $parameters = @{ GodotPath = $resolvedGodot }
        $buildSwitch = Get-RuntimeBuildSwitch -ScriptPath $scriptPath
        if ($buildSwitch) { $parameters[$buildSwitch] = $true }
        Run-Script -Script $script -Parameters $parameters
    }
    Assert-VerificationContext -Root $root -Fingerprint $report.Context.Fingerprint
    $report.Status = if ($report.Pending.Count -gt 0) { 'COMPLETED_WITH_PENDING' } else { 'PASS' }
}
catch { $report.Status = 'FAIL'; $report.Error = $_.Exception.Message; throw }
finally {
    Save-Report
    foreach ($pending in $report.Pending) { Write-Host "PENDING: $pending" }
    Write-Host "Targeted result: $($report.Status). Evidence: $reportPath"
    Write-Host 'Run ./check.ps1 and applicable runtime/native checks before final Story handoff.'
}
