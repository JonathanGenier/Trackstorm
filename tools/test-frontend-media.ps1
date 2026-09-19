$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("trackstorm-lfs-pointer-" + [guid]::NewGuid().ToString('N'))
$fixtureAsset = Join-Path $fixtureRoot 'media.ogv'
$fixtureManifest = Join-Path $fixtureRoot 'sources.json'

try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    @(
        'version https://git-lfs.github.com/spec/v1'
        'oid sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
        'size 123'
    ) | Set-Content -LiteralPath $fixtureAsset
    @{
        files = @(
            @{
                path = 'media.ogv'
                bytes = 123
                sha256 = '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
            }
        )
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $fixtureManifest

    try {
        & "$PSScriptRoot/check-frontend-media.ps1" -Root $fixtureRoot -ManifestPath $fixtureManifest
        throw 'Frontend media verifier accepted an unresolved Git LFS pointer.'
    }
    catch {
        if ($_.Exception.Message -notmatch 'Unresolved Git LFS pointer') {
            throw
        }
    }

    Write-Host 'Frontend media verifier regression test passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
