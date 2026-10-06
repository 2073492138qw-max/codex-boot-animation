#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PluginRoot, [switch]$KeepStartupDisabled)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Support.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'plugins\codex-boot-animation'
$target = (Resolve-Path -LiteralPath $PluginRoot).Path
$sourceExe = Join-Path $source 'hooks\BootPlayer.exe'
$targetExe = Join-Path $target 'hooks\BootPlayer.exe'
$metadata = Get-Content -LiteralPath (Join-Path $target '.codex-plugin\plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($metadata.name -ne 'codex-boot-animation') { throw 'Target is not a Codex Boot Animation installation.' }
if ((Get-FileHash -LiteralPath (Join-Path $target 'hooks\hooks.json')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $source 'hooks\hooks.json')).Hash) {
    throw 'Hook definitions differ. Review/reinstall the plugin through Codex before updating; this script never bypasses hook trust.'
}
if ($targetExe -ieq $sourceExe) { throw 'Run this updater from the new release, not from the running installation.' }
$run = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CodexBootAnimation -ErrorAction SilentlyContinue).CodexBootAnimation
if ($KeepStartupDisabled) {
    if ($run) { throw 'Safety update requires the sign-in entry to have been explicitly disabled first. Nothing changed.' }
} elseif ($run -ne ('"' + $targetExe + '" --supervise')) { throw 'Target does not match the active Windows sign-in entry. Nothing changed.' }
& (Join-Path $PSScriptRoot 'Test.ps1')
$processes = @(Get-CimInstance Win32_Process -Filter "name='BootPlayer.exe'")
$other = @($processes | Where-Object { $_.ExecutablePath -ine $targetExe -and $_.CommandLine -match '--(supervise|watch|resident)' })
if ($other.Count -gt 0) { throw 'A different helper is running; stop/migrate that installation explicitly.' }
$backup = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) ('.codex-boot-animation\backup\update-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $targetExe -Destination (Join-Path $backup 'BootPlayer.exe')
function Start-Helper {
    $adopt = Start-Process -FilePath $targetExe -ArgumentList '--adopt-running' -WindowStyle Hidden -Wait -PassThru
    if ($adopt.ExitCode -ne 0) { throw 'Could not adopt the current Codex instance.' }
    Start-Process -FilePath $targetExe -ArgumentList '--supervise' -WindowStyle Hidden
}
function Stop-Helper {
    # Stop-Process requests termination; wait for exit/file-handle release before copying.
    foreach ($mode in @('--supervise','--watch','--resident')) {
        $active = @(Get-CimInstance Win32_Process -Filter "name='BootPlayer.exe'" | Where-Object { $_.ExecutablePath -ieq $targetExe -and $_.CommandLine -match ([regex]::Escape($mode) + '(\s|$)') })
        foreach ($process in $active) {
            $handle = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
            if ($null -ne $handle) { $handle.Kill(); if (-not $handle.WaitForExit(10000)) { throw "Helper did not stop: $mode" }; $handle.Dispose() }
        }
    }
    # Owned one-shot players can also hold the EXE open. Only this exact installation is in scope.
    foreach ($process in @(Get-CimInstance Win32_Process -Filter "name='BootPlayer.exe'" | Where-Object { $_.ExecutablePath -ieq $targetExe })) {
        $handle = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
        if ($null -ne $handle) { $handle.Kill(); if (-not $handle.WaitForExit(10000)) { throw 'Installed player did not stop.' }; $handle.Dispose() }
    }
}
try {
    # Stop the supervisor first so it cannot respawn a watcher during replacement.
    Stop-Helper
    Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force
    if ((Get-FileHash -LiteralPath $sourceExe).Hash -ne (Get-FileHash -LiteralPath $targetExe).Hash) { throw 'Installed binary hash mismatch.' }
    Start-Helper
    if (-not (Wait-CodexCompanion -Exe $targetExe)) { throw 'Updated helper readiness/IPC failed within 20 seconds.' }
} catch {
    $failure = $_
    Stop-Helper
    Copy-Item -LiteralPath (Join-Path $backup 'BootPlayer.exe') -Destination $targetExe -Force
    if (-not $KeepStartupDisabled) { Start-Helper }
    throw $failure
}
Write-Output "Updated companion: $targetExe"
Write-Output "Rollback binary: $backup"
Write-Output 'Videos, autostart path, hook definitions/trust, and Codex itself were not changed.'
if ($KeepStartupDisabled) { Write-Output 'Sign-in startup remains disabled pending real cold-start acceptance; failed updates leave the old helper stopped.' }
