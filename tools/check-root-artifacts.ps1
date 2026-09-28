param(
    [string]$WriteSnapshot,
    [string]$BaselineSnapshot
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

function Get-RootEntries {
    @(Get-ChildItem -LiteralPath $root -Force | Sort-Object Name | ForEach-Object {
        [pscustomobject]@{
            Name = $_.Name
            Kind = if ($_.PSIsContainer) { "directory" } else { "file" }
            CodePoints = [string]::Join(" ", @($_.Name.ToCharArray() | ForEach-Object { "U+{0:X4}" -f [int]$_ }))
        }
    })
}

function Get-UnexpectedEntries {
    param([object[]]$Entries)

    $trackedRoots = @(git -C $root ls-tree --name-only HEAD)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read tracked root entries from Git."
    }

    $allowed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($name in $trackedRoots) { [void]$allowed.Add($name) }
    [void]$allowed.Add(".git")
    [void]$allowed.Add(".godot")

    @($Entries | Where-Object { -not $allowed.Contains($_.Name) })
}

function Format-EntryDetails {
    param([object[]]$Entries)

    foreach ($entry in $Entries) {
        $path = Join-Path $root $entry.Name
        $status = @(git -C $root status --short --untracked-files=all -- $entry.Name)
        $children = if ($entry.Kind -eq "directory") {
            @(Get-ChildItem -LiteralPath $path -Force -ErrorAction SilentlyContinue |
                Select-Object -First 20 -ExpandProperty Name)
        }
        else { @() }

        [pscustomobject]@{
            Name = $entry.Name
            Kind = $entry.Kind
            CodePoints = $entry.CodePoints
            GitStatus = if ($status.Count -eq 0) { "not reported (possibly ignored)" } else { $status -join "; " }
            Children = $children -join "; "
        } | Format-List | Out-String
    }
}

$entries = @(Get-RootEntries)
$unexpected = @(Get-UnexpectedEntries -Entries $entries)
$added = @()
$removed = @()

if ($BaselineSnapshot) {
    $baselinePath = [System.IO.Path]::GetFullPath($BaselineSnapshot)
    if (-not (Test-Path -LiteralPath $baselinePath -PathType Leaf)) {
        throw "Root snapshot '$baselinePath' does not exist."
    }

    $baseline = @((Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json).Name)
    $current = @($entries.Name)
    $added = @($entries | Where-Object { $_.Name -cnotin $baseline })
    $removed = @($baseline | Where-Object { $_ -cnotin $current })
}

if ($WriteSnapshot) {
    $snapshotPath = [System.IO.Path]::GetFullPath($WriteSnapshot)
    [System.IO.File]::WriteAllText($snapshotPath, ($entries | ConvertTo-Json -Depth 3))
    Write-Host "Recorded $($entries.Count) repository-root entries in '$snapshotPath'."
}

if ($unexpected.Count -gt 0 -or $added.Count -gt 0 -or $removed.Count -gt 0) {
    if ($unexpected.Count -gt 0) {
        Write-Error ("Unexpected repository-root entries:`n" + (Format-EntryDetails -Entries $unexpected)) -ErrorAction Continue
    }
    if ($added.Count -gt 0) {
        Write-Error ("Entries created after the baseline snapshot:`n" + (Format-EntryDetails -Entries $added)) -ErrorAction Continue
    }
    if ($removed.Count -gt 0) {
        Write-Error ("Entries removed after the baseline snapshot: " + ($removed -join ", ")) -ErrorAction Continue
    }
    throw "Repository-root artifact check failed. Inspect the reported entries; this check never deletes them."
}

Write-Host "Repository-root artifact check passed: $($entries.Count) intentional entries; no unexpected additions."
