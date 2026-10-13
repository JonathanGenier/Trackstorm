param([string]$GodotPath = $env:GODOT_PATH, [switch]$RequireRuntime, [switch]$Json)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot/agent-checks.ps1"
$checks = [Collections.Generic.List[object]]::new()
function Add-Preflight([string]$Name, [string]$Status, [string]$Detail) {
    $checks.Add([pscustomobject]@{ Name = $Name; Status = $Status; Detail = $Detail })
}
try {
    $context = Get-VerificationContext $root
    Add-Preflight 'Git checkout' 'PASS' ("Branch: {0}; HEAD: {1}; dirty: {2}. No files were stashed, reset or deleted." -f $context.Branch, $context.Head, $context.Dirty)
    if (-not $context.Branch -or $context.Branch -eq 'main') { Add-Preflight 'Implementation branch' 'PENDING' 'Select the authorized Story branch before implementation; detached CI inspection is allowed.' }
    [void](Get-StoryChangedPaths $root)
    Add-Preflight 'Comparison base' 'PASS' 'Local origin/main or main resolves. Freshness still requires an explicit fetch; this preflight does not access the network.'
}
catch { Add-Preflight 'Git context' 'FAIL' $_.Exception.Message }
$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
if ($dotnet) {
    $version = & $dotnet.Source --version 2>&1
    if ($LASTEXITCODE -eq 0 -and "$version" -match '^10\.0\.') { Add-Preflight '.NET SDK' 'PASS' "$version" }
    else { Add-Preflight '.NET SDK' 'FAIL' "Expected .NET 10; selected: $version" }
}
else { Add-Preflight '.NET SDK' 'FAIL' 'Install the repository-required .NET 10 SDK.' }
if ($GodotPath) {
    try {
        $command = Get-Command $GodotPath -CommandType Application -ErrorAction Stop
        $version = & $command.Source --version 2>&1
        $sdk = [xml](Get-Content (Join-Path $root 'Trackstorm.Client.csproj') -Raw)
        $expected = $sdk.Project.Sdk -replace '^Godot.NET.Sdk/', ''
        if ($LASTEXITCODE -ne 0 -or "$version" -notmatch ('^' + [regex]::Escape($expected) + '.*mono')) { throw "Expected Godot $expected .NET; selected: $version" }
        Add-Preflight 'Godot executable' 'PASS' "$version; launch/version only, not project or gameplay verification."
    }
    catch { Add-Preflight 'Godot executable' 'FAIL' $_.Exception.Message }
}
else { Add-Preflight 'Godot executable' $(if ($RequireRuntime) { 'FAIL' } else { 'PENDING' }) 'Supply GODOT_PATH/-GodotPath. tools/setup-godot-ci.ps1 can explicitly prepare and integrity-check the pinned Windows editor.' }
Add-Preflight 'Native and media prerequisites' 'PENDING' 'Use setup-eos.ps1, tools/check-frontend-media.ps1 and import-godot.ps1 as applicable. Their setup/checks were not run by this read-only preflight.'
Add-Preflight 'Environment capabilities' 'UNVERIFIED' 'Package audit/network reachability, user-directory access, renderer, authenticated EOS and physical devices require actual applicable checks. No audit/security settings were changed.'
$result = [pscustomobject]@{ Scope = 'Read-only prerequisite inspection, not final verification'; Checks = $checks.ToArray() }
if ($Json) { $result | ConvertTo-Json -Depth 5 }
else { foreach ($check in $checks) { Write-Host "$($check.Status): $($check.Name): $($check.Detail)" } }
if (@($checks | Where-Object Status -eq 'FAIL').Count -gt 0) { exit 1 }
