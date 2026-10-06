#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'plugins\codex-boot-animation'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('codex-empty-resident-' + [guid]::NewGuid().ToString('N'))
$hooks = Join-Path $fixture 'hooks'
$idle = Join-Path $fixture 'videos\闲置互动'
$process = $null
try {
    New-Item -ItemType Directory -Path $hooks,$idle | Out-Null
    Copy-Item -LiteralPath (Join-Path $source 'hooks\BootPlayer.exe') -Destination (Join-Path $hooks 'BootPlayer.exe')
    $clip = Get-ChildItem -LiteralPath (Join-Path $source 'videos') -Filter '*.mp4' -Recurse -File | Select-Object -First 1
    if (-not $clip) { throw 'A packaged sample video is required for the isolated idle fixture.' }
    Copy-Item -LiteralPath $clip.FullName -Destination (Join-Path $idle 'fixture.mp4')
    $process = Start-Process -FilePath (Join-Path $hooks 'BootPlayer.exe') -ArgumentList '--resident-empty-test' -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(10000)) { throw 'Empty-resident test exceeded 10 seconds.' }
    if ($process.ExitCode -ne 0) { throw "Empty-resident test failed: $($process.ExitCode)" }
    Write-Output 'PASS isolated-empty-intros-resident-and-idle-library'
} finally {
    if ($process) {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(3000) | Out-Null }
        $process.Dispose()
    }
    # Remove only exact fixture files; never recurse into a computed path.
    foreach ($file in @((Join-Path $hooks 'BootPlayer.exe'),(Join-Path $idle 'fixture.mp4'))) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    foreach ($directory in @($hooks,$idle,(Join-Path $fixture 'videos'),$fixture)) {
        if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory }
    }
}
