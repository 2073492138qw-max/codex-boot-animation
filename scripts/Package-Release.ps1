#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $dirty = @(& git status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Commit reviewed files before packaging; a release is an immutable Git snapshot.' }
    & (Join-Path $PSScriptRoot 'Test.ps1')
    $version = (Get-Content 'plugins\codex-boot-animation\.codex-plugin\plugin.json' -Raw -Encoding UTF8 | ConvertFrom-Json).version
    $dist = Join-Path $repo 'dist'
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    $zip = Join-Path $dist "codex-boot-animation-$version-windows.zip"
    if (Test-Path -LiteralPath $zip) { throw 'Release ZIP already exists. Preserve it; use a new version or move it aside deliberately.' }
    & git archive --format=zip "--output=$zip" HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Git archive failed.' }
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    # Output artifacts, not source edits. UTF-8 without BOM, filenames are ASCII.
    [IO.File]::WriteAllText($zip + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($zip) + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
    Write-Output "Release: $zip"
    Write-Output "SHA256: $hash"
} finally { Pop-Location }
