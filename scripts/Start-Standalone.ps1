#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Support.ps1')

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This companion supports Windows only.'
}

$repo = Split-Path -Parent $PSScriptRoot
$plugin = Join-Path $repo 'plugins\codex-boot-animation'
$exe = Join-Path $plugin 'hooks\BootPlayer.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw 'BootPlayer.exe is missing. Extract the full release ZIP or run scripts\Build.ps1 first.'
}

# Remove pause/disable flags only if the user explicitly double-clicked start.cmd.
$dataRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-boot-animation'
$disabledFlag = Join-Path $dataRoot 'disabled'
if (Test-Path -LiteralPath $disabledFlag -PathType Leaf) {
    Remove-Item -LiteralPath $disabledFlag -Force
}

$adopt = Invoke-BoundedCompanion -Exe $exe -Arguments @('--adopt-running') -TimeoutMs 10000
if ($adopt.TimedOut -or $adopt.ExitCode -ne 0) {
    throw 'Could not initialize running Codex instance state.'
}

Start-Process -FilePath $exe -ArgumentList '--watch' -WindowStyle Hidden
Write-Output 'Codex Boot Animation started (current session only; Windows autostart was not modified).'
Write-Output 'Right-click the tray icon to preview scenes, change volume, or open video folders.'
