param ([string]$ArchivePath)

$ErrorActionPreference = 'Stop'
$expectedHash = 'F14B93AAC2B4339492F7E1C4E0314F60F3E0CB02E176993BE8E247589D938FFF'
$cache = Join-Path $PSScriptRoot '.godot'
New-Item -ItemType Directory -Force $cache | Out-Null
if (-not $ArchivePath) {
    $ArchivePath = Join-Path $cache 'eos-sdk.zip'
    if (-not (Test-Path -LiteralPath $ArchivePath)) {
        Write-Host 'Downloading official EOS C# SDK 1.19.1.2-CL53289219 (Epic SDK agreement applies).'
        Invoke-WebRequest 'https://onlineservices.epicgames.com/api/cosmos/sdk/download?archive_id=872&archive_type=c_sharp' -OutFile $ArchivePath
    }
}
if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'EOS archive does not match the pinned official release. Do not substitute another version.'
}
$destination = [IO.Path]::GetFullPath((Join-Path $cache 'eos-sdk'))
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath))
try {
    foreach ($entry in $archive.Entries) {
        # Extract only the C# binding, Windows x64 runtime, and required notices. No EAC/tools/installers.
        if ($entry.Name -and ($entry.FullName.StartsWith('SDK/Source/') -or
            $entry.FullName -eq 'SDK/Bin/EOSSDK-Win64-Shipping.dll' -or
            $entry.FullName -eq 'ThirdPartyNotices/ThirdPartySoftwareNotice.txt')) {
            $target = [IO.Path]::GetFullPath((Join-Path $destination $entry.FullName))
            if (-not $target.StartsWith($destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Invalid SDK archive path.'
            }
            New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    }
}
finally { $archive.Dispose() }
Write-Host 'Pinned official EOS SDK ready. Source and downloaded artifacts remain in ignored .godot/eos-sdk.'
