#Requires -Version 5.1
[CmdletBinding()]
param([string]$TestRoot)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-Process.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'plugins\codex-boot-animation\hooks\BootPlayer.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Run scripts\Build.ps1 first.' }
$ownRoot = $false
if (-not $TestRoot) {
    $TestRoot = Join-Path ([IO.Path]::GetTempPath()) ('codex-boot-media-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $TestRoot | Out-Null
    $ownRoot = $true
}
try {
    # Real media is an explicit entry point: these cases decode packaged video
    # and are not part of the default logic suite.
    foreach ($case in @('--intro-media-recovery-test', '--intro-media-decoder-probe', '--resident-scene-test')) {
        $process = Start-TestProcess -FilePath $exe -ArgumentList @('--test-root', $TestRoot, $case) -WindowStyle Hidden -PassThru
        try {
            if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "$case timed out." }
            if ($process.ExitCode -ne 0) { throw "$case failed with exit code $($process.ExitCode)" }
        } finally { $process.Dispose() }
        Write-Output "PASS $case"
    }
    & (Join-Path $PSScriptRoot 'Test-EmptyResident.ps1') -TestRoot $TestRoot
    Write-Output 'All real-media tests passed.'
} finally {
    if ($ownRoot -and (Test-Path -LiteralPath $TestRoot) -and (Split-Path -Leaf $TestRoot) -like 'codex-boot-media-*') {
        Remove-Item -LiteralPath $TestRoot -Recurse -Force
    }
}
