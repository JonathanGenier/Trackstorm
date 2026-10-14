# Shared mechanics for targeted verification; feature routing remains in fast-check-routes.ps1.
function Invoke-ContextGit {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string[]]$Arguments, [switch]$AllowFailure)
    $start = [Diagnostics.ProcessStartInfo]::new('git')
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8
    $start.ArgumentList.Add('-C')
    $start.ArgumentList.Add($Root)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            throw 'Git context inspection timed out.'
        }
        $result = [pscustomobject]@{ Output = $stdout.GetAwaiter().GetResult(); Error = $stderr.GetAwaiter().GetResult(); ExitCode = $process.ExitCode }
        if ($result.ExitCode -ne 0 -and -not $AllowFailure) { throw "Git context inspection failed: $($result.Error)" }
        return $result
    }
    finally { $process.Dispose() }
}

function Get-StoryChangedPaths {
    param([Parameter(Mandatory)][string]$Root, [string]$BaseRef = '')
    if (-not $BaseRef) {
        foreach ($candidate in @('origin/main', 'main')) {
            $resolved = Invoke-ContextGit -Root $Root -Arguments @('rev-parse', '--verify', '--quiet', "$candidate^{commit}") -AllowFailure
            if ($resolved.ExitCode -eq 0) { $BaseRef = $candidate; break }
        }
    }
    if (-not $BaseRef) { throw 'Cannot resolve origin/main or main. Fetch main or select an explicit -Area.' }
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    # --no-renames reports a rename as delete + add: both systems must remain routed.
    $commands = @(
        @('diff', '--name-only', '--no-renames', '-z', "$BaseRef...HEAD", '--'),
        @('diff', '--cached', '--name-only', '--no-renames', '-z', 'HEAD', '--'),
        @('diff', '--name-only', '--no-renames', '-z', '--'),
        @('ls-files', '--others', '--exclude-standard', '-z')
    )
    foreach ($command in $commands) {
        $text = (Invoke-ContextGit -Root $Root -Arguments $command).Output
        foreach ($path in $text.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)) { [void]$paths.Add($path) }
    }
    return @($paths | Sort-Object -CaseSensitive)
}

function Get-VerificationContext {
    param([Parameter(Mandatory)][string]$Root)
    $head = (Invoke-ContextGit -Root $Root -Arguments @('rev-parse', 'HEAD')).Output.Trim()
    $branch = (Invoke-ContextGit -Root $Root -Arguments @('branch', '--show-current')).Output.Trim()
    $status = (Invoke-ContextGit -Root $Root -Arguments @('status', '--porcelain=v1', '-z', '--untracked-files=all')).Output
    $diff = (Invoke-ContextGit -Root $Root -Arguments @('diff', '--binary', '--no-ext-diff', 'HEAD', '--')).Output
    $untracked = (Invoke-ContextGit -Root $Root -Arguments @('ls-files', '--others', '--exclude-standard', '-z')).Output
    $parts = [Collections.Generic.List[string]]::new()
    $parts.Add($head); $parts.Add($status); $parts.Add($diff)
    foreach ($path in $untracked.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries) | Sort-Object -CaseSensitive) {
        $fullPath = Join-Path $Root $path
        $parts.Add($path)
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) { $parts.Add((Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash) }
        else { $parts.Add('MISSING_OR_NONFILE') }
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($parts -join "`0"))
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    return [pscustomobject]@{
        Head = $head; Branch = $branch; Dirty = [bool]$status; Fingerprint = $fingerprint
        PowerShell = $PSVersionTable.PSVersion.ToString(); OS = [Environment]::OSVersion.VersionString
    }
}

function Assert-VerificationContext {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Fingerprint)
    if ((Get-VerificationContext -Root $Root).Fingerprint -ne $Fingerprint) {
        throw 'Source context changed during verification. Re-run the plan and build; existing evidence must not be attributed to the changed tree.'
    }
}

function Get-PlanCheckNames {
    param([Parameter(Mandatory)]$Plan)
    foreach ($property in @('CoreTests', 'TransportTests', 'ServiceTests', 'ClientBuild', 'VersionChecks', 'MediaChecks', 'WorkflowTests')) {
        if ($Plan.$property) { $property }
    }
    foreach ($name in $Plan.RuntimeScripts) { "runtime:$name" }
    foreach ($name in $Plan.ExtendedScripts) { "extended:$name" }
    foreach ($name in $Plan.ManualScenarios) { "manual:$name" }
}

function Get-FastCheckExplanation {
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Paths)
    $combined = Get-FastCheckPlan -Paths $Paths
    $reasons = [ordered]@{}
    foreach ($path in $Paths) {
        $single = Get-FastCheckPlan -Paths @($path) -IncludeFeatureDocHints:$combined.HasProductionChanges
        foreach ($name in @(Get-PlanCheckNames -Plan $single)) {
            if (-not $reasons.Contains($name)) { $reasons[$name] = [Collections.Generic.List[string]]::new() }
            $reasons[$name].Add($path)
        }
    }
    return $reasons
}

function Get-RuntimeBuildSwitch {
    param([Parameter(Mandatory)][string]$ScriptPath)
    # The supported harnesses use Debug. The soak harness also needs Release tests;
    # let it retain its own setup rather than guessing that those binaries are current.
    if ((Split-Path -Leaf $ScriptPath) -eq 'check-network-soak.ps1') { return $null }
    $parameters = (Get-Command -Name $ScriptPath -ErrorAction Stop).Parameters
    if ($parameters -and $parameters.ContainsKey('NoBuild')) { return 'NoBuild' }
    if ($parameters -and $parameters.ContainsKey('SkipBuild')) { return 'SkipBuild' }
    return $null
}

function Invoke-AgentCheck {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Executable,
        [string[]]$Arguments = @(),
        [hashtable]$Parameters = @{},
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$LogDirectory,
        [ValidateRange(1, 86400)][int]$TimeoutSeconds = 1800,
        [switch]$DetailedOutput
    )
    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
    $stem = ([regex]::Replace($Name, '[^a-zA-Z0-9._-]', '-')) + '-' + [guid]::NewGuid().ToString('N')
    $stdoutPath = Join-Path $LogDirectory "$stem.stdout.log"
    $stderrPath = Join-Path $LogDirectory "$stem.stderr.log"
    # An isolated PowerShell process contains explicit exits and supports .cmd tools.
    # Positional arguments are literals; named script arguments use hashtable splatting.
    $tokens = @($Executable) + @($Arguments)
    $quoted = @($tokens | ForEach-Object { "'" + $_.Replace("'", "''") + "'" })
    $parameterBytes = [Text.Encoding]::UTF8.GetBytes(($Parameters | ConvertTo-Json -Depth 4 -Compress))
    $parameterData = [Convert]::ToBase64String($parameterBytes)
    $command = '$ErrorActionPreference = ''Stop''; $global:LASTEXITCODE = 0; $p = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(''' + $parameterData + ''')) | ConvertFrom-Json -AsHashtable; & ' + $quoted[0] + ' @p ' + (($quoted | Select-Object -Skip 1) -join ' ') + '; $ok = $?; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; if (-not $ok) { exit 1 }; exit 0'
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $shell = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
    $start = [Diagnostics.ProcessStartInfo]::new($shell)
    $start.WorkingDirectory = $Root
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-OutputFormat', 'Text', '-EncodedCommand', $encoded)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $result = [ordered]@{
        Name = $Name; Executable = $Executable; Arguments = @($Arguments); Parameters = $Parameters
        StartedUtc = [DateTime]::UtcNow.ToString('o'); Status = 'FAIL'; ExitCode = $null
        DurationSeconds = 0; StdoutLog = $stdoutPath; StderrLog = $stderrPath; Error = $null
    }
    $stdout = ''; $stderr = ''
    try {
        [void]$process.Start()
        $outTask = $process.StandardOutput.ReadToEndAsync()
        $errTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            $result.Error = "Timed out after $TimeoutSeconds seconds."
        }
        $stdout = $outTask.GetAwaiter().GetResult()
        $stderr = $errTask.GetAwaiter().GetResult()
        $result.ExitCode = $process.ExitCode
        if ($process.ExitCode -eq 0 -and -not $result.Error) { $result.Status = 'PASS' }
    }
    catch { $result.Error = $_.Exception.Message }
    finally {
        $watch.Stop(); $result.DurationSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
        [IO.File]::WriteAllText($stdoutPath, $stdout)
        [IO.File]::WriteAllText($stderrPath, $stderr)
        $process.Dispose()
    }
    Write-Host "$($result.Status): $Name ($($result.DurationSeconds)s); log: $stdoutPath"
    if ($DetailedOutput) { Write-Host $stdout; if ($stderr) { Write-Host $stderr } }
    elseif ($result.Status -ne 'PASS') {
        Write-Host (($stdout -split '\r?\n' | Select-Object -Last 30) -join "`n")
        Write-Host (($stderr -split '\r?\n' | Select-Object -Last 20) -join "`n")
        if ($result.Error) { Write-Host $result.Error }
    }
    return [pscustomobject]$result
}
