#Requires -Version 5.1
[CmdletBinding()]
param([switch]$CheckOnly)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Support.ps1')
. (Join-Path $PSScriptRoot 'Media-Preflight.ps1')

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This companion supports Windows only.'
}
if ($env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY) {
    throw 'Run install.cmd from Windows File Explorer or Start-menu PowerShell, not inside the Codex terminal.'
}

$repo = Split-Path -Parent $PSScriptRoot
$plugin = Join-Path $repo 'plugins\codex-boot-animation'
$exe = Join-Path $plugin 'hooks\BootPlayer.exe'
$videos = Join-Path $plugin 'videos'

if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw 'BootPlayer.exe is missing. Extract the full release ZIP or run scripts\Build.ps1 first.'
}
$media = Assert-MediaPreflight -VideosRoot $videos
Write-Output "Media ready: $($media.Videos.Count) video(s), $($media.MissingCovers.Count) without an optional cover"

$frameworkRelease = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue).Release
if ($null -eq $frameworkRelease -or $frameworkRelease -lt 528040) {
    throw '.NET Framework 4.8 or later is required. Install it via Windows Update, then retry install.cmd.'
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$expectedRun = '"' + $exe + '" --supervise'
$existingRun = Get-ItemProperty -Path $runKey -Name 'CodexBootAnimation' -ErrorAction SilentlyContinue
if ($null -ne $existingRun -and $existingRun.CodexBootAnimation -ne $expectedRun) {
    if ($CheckOnly) {
        Write-Warning "Another CodexBootAnimation installation is already registered: $($existingRun.CodexBootAnimation)"
    } else {
        throw "Another CodexBootAnimation installation is already registered ($($existingRun.CodexBootAnimation)). Run its uninstall.cmd first before moving folders."
    }
}

Write-Output "Companion folder: $plugin"
Write-Output 'Mode: Standalone hook-free companion (no Codex plugin registration or hook trust required).'
if ($CheckOnly) {
    Write-Output 'Preflight passed; no system settings were changed.'
    return
}

& (Join-Path $PSScriptRoot 'Install-Startup.ps1') -PluginRoot $plugin
Write-Output 'Installed and running! Look for the tray icon in the Windows notification area.'
