param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $Root 'assets/frontend/sources.json'
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$pointerSignature = 'version https://git-lfs.github.com/spec/v1'
foreach ($entry in $manifest.files) {
    $path = Join-Path $Root $entry.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing committed frontend media asset: $($entry.path)"
    }

    $stream = [System.IO.File]::OpenRead($path)
    try {
        $buffer = [byte[]]::new($pointerSignature.Length)
        $read = $stream.Read($buffer, 0, $buffer.Length)
    }
    finally {
        $stream.Dispose()
    }

    $header = [System.Text.Encoding]::ASCII.GetString($buffer, 0, $read)
    if ($header -eq $pointerSignature) {
        throw "Unresolved Git LFS pointer for frontend media asset: $($entry.path). Run 'git lfs pull'."
    }

    $item = Get-Item -LiteralPath $path
    if ($item.Length -ne $entry.bytes) {
        throw "Frontend media byte-length mismatch: $($entry.path); expected $($entry.bytes), actual $($item.Length)."
    }

    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Frontend media checksum mismatch: $($entry.path)"
    }
}

Write-Host "Frontend media verified: $($manifest.files.Count) materialized files."
