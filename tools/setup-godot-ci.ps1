param(
    [string]$ArchivePath = '',
    [string]$Destination = ''
)

$ErrorActionPreference = 'Stop'

$version = '4.7.2'
$archiveName = 'Godot_v4.7.2-stable_mono_win64.zip'
$expectedHash = 'A2A48473A7414C5F19FAB690518CAEBB738C09EF9601F6BD2388676A7F53B3C0'
$downloadUrl = "https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable/$archiveName"

if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $ArchivePath = Join-Path $PSScriptRoot "../.godot/godot-$version-stable-mono-win64.zip"
}
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $PSScriptRoot "../.godot/godot-ci/$version"
}

$ArchivePath = [IO.Path]::GetFullPath($ArchivePath)
$Destination = [IO.Path]::GetFullPath($Destination)

New-Item -ItemType Directory -Force (Split-Path $ArchivePath) | Out-Null

if (-not (Test-Path -LiteralPath $ArchivePath)) {
    Write-Host "Downloading pinned Godot $version .NET editor."
    Invoke-WebRequest $downloadUrl -OutFile $ArchivePath
}

$actualHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) {
    throw "Godot archive hash mismatch. Expected $expectedHash; actual $actualHash."
}

$exeName = 'Godot_v4.7.2-stable_mono_win64_console.exe'
$existing = Get-ChildItem -LiteralPath $Destination -Filter $exeName -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

if ($null -eq $existing) {
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Force $Destination | Out-Null
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $Destination -Force
    $existing = Get-ChildItem -LiteralPath $Destination -Filter $exeName -File -Recurse | Select-Object -First 1
}

if ($null -eq $existing) {
    throw "Pinned Godot executable '$exeName' was not found after extraction."
}

$versionOutput = & $existing.FullName --version 2>&1
$versionExitCode = $LASTEXITCODE
$versionText = ($versionOutput | Out-String).Trim()
if ($versionExitCode -ne 0 -or $versionText -notmatch '^4\.7\.2.*mono') {
    throw "Pinned Godot failed to launch correctly (exit code $versionExitCode; version '$versionText')."
}

Write-Host "Pinned Godot verified: $versionText"
Write-Output $existing.FullName
