$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/agent-checks.ps1"
. "$PSScriptRoot/fast-check-routes.ps1"
$assertions = 0
function Assert-AgentTool([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:assertions++
}
$root = Split-Path -Parent $PSScriptRoot
foreach ($path in @(Get-ChildItem $PSScriptRoot -Filter '*.ps1') + @(Get-ChildItem $root -Filter 'check-*.ps1')) {
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($path.FullName, [ref]$tokens, [ref]$errors)
    Assert-AgentTool ($errors.Count -eq 0) "PowerShell parse error in $($path.Name): $errors"
}
$docs = @(Get-ChildItem (Join-Path $root 'docs/features') -Filter '*.md' -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
foreach ($path in $docs + @('docs/features/water.md', 'docs/features/surfaces.md', 'code/Client/AGENTS.md')) {
    $plan = Get-FastCheckPlan -Paths @($path)
    Assert-AgentTool (@(Get-PlanCheckNames -Plan $plan).Count -eq 0) "Documentation-only path selected checks: $path"
}
Assert-AgentTool (@(Get-PlanCheckNames (Get-FastCheckPlan -Paths @())).Count -eq 0) 'Empty plans must select no checks.'
$mixed = Get-FastCheckPlan -Paths @('code/Core/Input/InputFrame.cs', 'docs/features/water.md')
Assert-AgentTool ($mixed.CoreTests -and $mixed.RuntimeScripts -contains 'check-water.ps1') 'Mixed production/document changes retain integration hints.'
$why = Get-FastCheckExplanation -Paths @('code/Core/Input/InputFrame.cs', 'docs/features/water.md')
Assert-AgentTool ($why['runtime:check-water.ps1'] -contains 'docs/features/water.md') 'Explain must identify mixed documentation hints.'
Assert-AgentTool ((Get-FastCheckPlan -Paths @('tools/agent-checks.ps1')).WorkflowTests) 'Workflow tooling must test itself.'
Assert-AgentTool ((Get-RuntimeBuildSwitch (Join-Path $root 'check-camera.ps1')) -eq 'NoBuild') 'Debug camera build can be shared.'
Assert-AgentTool ((Get-RuntimeBuildSwitch (Join-Path $root 'check-startup.ps1')) -eq 'SkipBuild') 'Startup retains its supported switch.'
Assert-AgentTool ($null -eq (Get-RuntimeBuildSwitch (Join-Path $root 'check-network-soak.ps1'))) 'Mixed-configuration soak retains its own fresh build.'
$temp = Join-Path ([IO.Path]::GetTempPath()) ('trackstorm-agent-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
function Git-Test([string[]]$Arguments) { [void](Invoke-ContextGit -Root $temp -Arguments $Arguments) }
function Write-TestFile([string]$Path, [string]$Text) {
    $target = Join-Path $temp $Path
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    [IO.File]::WriteAllText($target, $Text)
}
try {
    Git-Test @('init', '-q', '-b', 'main')
    Git-Test @('config', 'user.name', 'Trackstorm verification')
    Git-Test @('config', 'user.email', 'verification@example.invalid')
    Git-Test @('config', 'core.autocrlf', 'false')
    Write-TestFile '.gitignore' ".godot/`nlogs/`nignored/`n"
    foreach ($file in @('committed.cs', 'unstaged.cs', 'staged.cs', 'deleted.cs', 'before-name.cs')) { Write-TestFile $file 'original' }
    foreach ($file in @('agent-checks.ps1', 'check-fast.ps1', 'fast-check-routes.ps1')) { Write-TestFile "tools/$file" (Get-Content -Raw (Join-Path $PSScriptRoot $file)) }
    Git-Test @('add', '--all'); Git-Test @('commit', '-qm', 'initial')
    Git-Test @('checkout', '-qb', 'ts-999-jg')
    Assert-AgentTool (@(Get-StoryChangedPaths $temp).Count -eq 0) 'A clean identical branch has no changes.'
    Write-TestFile 'unstaged.cs' 'uncommitted first edit'
    Assert-AgentTool ((@(Get-StoryChangedPaths $temp) -join '|') -eq 'unstaged.cs') 'Uncommitted-only work must be discovered.'
    Write-TestFile 'committed.cs' 'committed edit'
    Git-Test @('add', '--', 'committed.cs'); Git-Test @('commit', '-qm', 'story edit')
    Write-TestFile 'staged.cs' 'staged edit'; Git-Test @('add', '--', 'staged.cs')
    Remove-Item -LiteralPath (Join-Path $temp 'deleted.cs')
    Git-Test @('mv', '--', 'before-name.cs', 'after name.cs')
    Write-TestFile 'new é file.cs' 'untracked edit'
    Write-TestFile 'ignored/not-source.cs' 'must remain ignored'
    $actual = @(Get-StoryChangedPaths $temp)
    $expected = @('committed.cs', 'unstaged.cs', 'staged.cs', 'deleted.cs', 'before-name.cs', 'after name.cs', 'new é file.cs')
    Assert-AgentTool ($actual.Count -eq $expected.Count -and @($expected | Where-Object { $_ -cnotin $actual }).Count -eq 0) "Changed paths differ: $actual"
    $context = Get-VerificationContext $temp
    Assert-AgentTool ($context.Dirty -and $context.Branch -eq 'ts-999-jg') 'Context records dirty source and branch.'
    Assert-AgentTool ((Get-VerificationContext $temp).Fingerprint -eq $context.Fingerprint) 'Unchanged context is stable.'
    Write-TestFile 'new é file.cs' 'changed untracked content'
    Assert-AgentTool ((Get-VerificationContext $temp).Fingerprint -ne $context.Fingerprint) 'Untracked content changes invalidate evidence.'
    $rejected = $false
    try { Assert-VerificationContext $temp $context.Fingerprint } catch { $rejected = $true }
    Assert-AgentTool $rejected 'Stale build context must fail closed.'
    $logs = Join-Path $temp 'logs'
    $planner = Invoke-AgentCheck -Name 'plan only' -Executable (Join-Path $temp 'tools/check-fast.ps1') -Parameters @{ Plan = $true; Explain = $true; Json = $true; GodotPath = 'not-an-installed-engine' } -Root $temp -LogDirectory $logs
    Assert-AgentTool ($planner.Status -eq 'PASS') 'Plan mode must need neither a compiler nor Godot.'
    $view = Get-Content -Raw $planner.StdoutLog | ConvertFrom-Json
    Assert-AgentTool (-not $view.Executed -and $view.Paths.Count -eq $expected.Count) 'Plan JSON must not claim execution.'
    Assert-AgentTool (-not (Test-Path (Join-Path $temp '.godot'))) 'Plan mode must not create verification artifacts.'
    Write-TestFile 'ignored/parameters.ps1' 'param([string]$Text, [switch]$NoBuild) if ($Text -cne "literal ''quote'' ; `$value" -or -not $NoBuild) { throw "Parameter binding changed." }; Write-Output "parameters passed"'
    $pass = Invoke-AgentCheck -Name 'parameters' -Executable (Join-Path $temp 'ignored/parameters.ps1') -Parameters @{ Text = 'literal ''quote'' ; $value'; NoBuild = $true } -Root $temp -LogDirectory $logs
    Assert-AgentTool ($pass.Status -eq 'PASS' -and $pass.ExitCode -eq 0) 'Named parameters and switches must bind literally.'
    Write-TestFile 'ignored/failed.ps1' 'Write-Output "complete diagnostic"; Write-Output "WARNING: fixture"; throw "expected harness failure"'
    $fail = Invoke-AgentCheck -Name 'failure' -Executable (Join-Path $temp 'ignored/failed.ps1') -Root $temp -LogDirectory $logs
    Assert-AgentTool ($fail.Status -eq 'FAIL' -and $fail.ExitCode -ne 0) 'A failed warning-sensitive harness cannot pass.'
    Assert-AgentTool ((Get-Content -Raw $fail.StdoutLog) -match 'complete diagnostic') 'Full diagnostic output must survive failure.'
    Write-TestFile 'ignored/exit.ps1' 'exit 7'
    $exit = Invoke-AgentCheck -Name 'explicit exit' -Executable (Join-Path $temp 'ignored/exit.ps1') -Root $temp -LogDirectory $logs
    Assert-AgentTool ($exit.Status -eq 'FAIL' -and $exit.ExitCode -eq 7) 'Explicit exits must be contained and preserved.'
    Write-TestFile 'ignored/timeout.ps1' 'Start-Sleep -Seconds 10'
    $timeout = Invoke-AgentCheck -Name 'timeout' -Executable (Join-Path $temp 'ignored/timeout.ps1') -Root $temp -LogDirectory $logs -TimeoutSeconds 1
    Assert-AgentTool ($timeout.Status -eq 'FAIL' -and $timeout.Error -match 'Timed out') 'Timeouts must not produce passing evidence.'
    $missing = Invoke-AgentCheck -Name 'missing command' -Executable 'trackstorm-command-that-does-not-exist' -Root $temp -LogDirectory $logs
    Assert-AgentTool ($missing.Status -eq 'FAIL') 'Unavailable commands must fail.'
    Write-TestFile 'code/Client/Vehicles/VehicleMovement.cs' 'fixture path only; never compiled'
    $required = Invoke-AgentCheck -Name 'required runtime unavailable' -Executable (Join-Path $temp 'tools/check-fast.ps1') -Parameters @{ RequireRuntime = $true; GodotPath = ''; OutputDirectory = (Join-Path $logs 'required') } -Root $temp -LogDirectory $logs
    Assert-AgentTool ($required.Status -eq 'FAIL') 'Required missing runtime must fail before expensive execution.'
    $report = Get-Content -Raw (Join-Path $logs 'required/results.json') | ConvertFrom-Json
    Assert-AgentTool ($report.Status -eq 'FAIL' -and $report.Checks.Count -eq 0 -and $report.Pending.Count -gt 0) 'Missing runtime report must retain pending work and no passing tests.'
    Assert-AgentTool ($report.Unexecuted.Count -gt 0) 'Unexecuted deterministic checks must remain explicit after failure.'
}
finally { Remove-Item -LiteralPath $temp -Recurse -Force }
Write-Host "Agent tooling regression tests passed: $assertions assertions. Expected failure/timeout fixtures above are negative tests."
