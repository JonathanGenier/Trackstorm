param (
    [Parameter(Mandatory)] [string]$SonnissDirectory,
    [string]$PythonPath = 'python',
    [string]$FfmpegPath = 'ffmpeg'
)

$ErrorActionPreference = 'Stop'
# Acquire the official archives linked in assets/audio/sources.json and extract them
# outside Git. This importer verifies every selected recording before processing it.
& $PythonPath (Join-Path $PSScriptRoot 'tools/import-audio.py') --sources $SonnissDirectory --ffmpeg $FfmpegPath
if ($LASTEXITCODE -ne 0) { throw 'Audio import failed.' }
