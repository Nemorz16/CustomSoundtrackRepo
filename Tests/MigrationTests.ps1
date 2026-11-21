$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'InstallHelpers.ps1')
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('CustomSoundtrack-MigrationTest-' + [System.Guid]::NewGuid().ToString('N'))
try {
    $source = Join-Path $testRoot 'old'
    $target = Join-Path $testRoot 'new'
    New-Item -ItemType Directory -Path $source,$target -Force | Out-Null
    $sourceSong = Join-Path $source 'song.wav'
    $targetSong = Join-Path $target 'song.wav'
    [System.IO.File]::WriteAllText($sourceSong, 'original-radio-song')
    [System.IO.File]::WriteAllText($targetSong, 'existing-different-song')
    Copy-MusicPreservingExisting $sourceSong $target
    Copy-MusicPreservingExisting $sourceSong $target
    if ([System.IO.File]::ReadAllText($targetSong) -ne 'existing-different-song') { throw 'Migration overwrote an existing song.' }
    if (@(Get-ChildItem -LiteralPath $target -File).Count -ne 2) { throw 'Repeated migration duplicated or lost a song.' }
    $renamed = Join-Path $target 'song (migrated 1).wav'
    if ([System.IO.File]::ReadAllText($renamed) -ne 'original-radio-song') { throw 'Conflict copy changed song contents.' }
    if (-not (Test-Path -LiteralPath $sourceSong)) { throw 'Migration removed original music.' }
    $fresh = Join-Path $source 'fresh.ogg'
    [System.IO.File]::WriteAllText($fresh, 'fresh-song')
    Copy-MusicPreservingExisting $fresh $target
    Copy-MusicPreservingExisting $fresh $target
    if (@(Get-ChildItem -LiteralPath $target -File).Count -ne 3) { throw 'Identical migration is not idempotent.' }
    Write-Host 'PASS: music migration preserves originals, conflicting contents and repeat-install idempotence.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
