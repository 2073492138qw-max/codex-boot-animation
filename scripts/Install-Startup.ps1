#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Support.ps1')
if ($env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY) {
    throw 'Open Windows PowerShell from the Start menu and run this script there. The Codex terminal can write to an isolated registry view.'
}
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'plugins\codex-boot-animation\hooks\BootPlayer.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Run scripts\Build.ps1 first.' }
if (-not (Get-ChildItem -LiteralPath (Join-Path $repo 'plugins\codex-boot-animation\videos\新聊天') -Filter '*.mp4' -File -ErrorAction SilentlyContinue)) {
    throw 'The intro video is missing.'
}

$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$command = '"' + $exe + '" --supervise'
$existing = Get-ItemProperty -Path $key -Name 'CodexBootAnimation' -ErrorAction SilentlyContinue
if ($null -ne $existing -and $existing.CodexBootAnimation -ne $command) {
    throw 'Another CodexBootAnimation sign-in entry is already installed. Remove or migrate that installation explicitly before replacing it.'
}
$before = @(Get-CodexCompanionProcesses -Exe $exe | ForEach-Object { $_.ProcessId })
try {
    New-Item -Path $key -Force | Out-Null
    New-ItemProperty -Path $key -Name 'CodexBootAnimation' -Value $command -PropertyType String -Force | Out-Null
    $saved = (Get-ItemProperty -Path $key -Name 'CodexBootAnimation').CodexBootAnimation
    if ($saved -ne $command) { throw 'Windows sign-in entry verification failed.' }
    # Installing/restarting the helper is not a cold launch of an already-open Codex.
    $adopt = Start-Process -FilePath $exe -ArgumentList '--adopt-running' -WindowStyle Hidden -Wait -PassThru
    if ($adopt.ExitCode -ne 0) { throw 'Could not adopt existing Codex processes.' }
    Start-Process -FilePath $exe -ArgumentList '--supervise' -WindowStyle Hidden
    if (-not (Wait-CodexCompanion -Exe $exe)) {
        throw 'The companion did not become ready within 20 seconds. Check %USERPROFILE%\.codex-boot-animation\playback.log and retry install.cmd.'
    }
} catch {
    # Roll back only a new entry and new helper processes from this directory.
    $installError = $_
    try {
        if ($null -eq $existing) {
            $current = Get-ItemProperty -Path $key -Name 'CodexBootAnimation' -ErrorAction SilentlyContinue
            if ($null -ne $current -and $current.CodexBootAnimation -eq $command) {
                Remove-ItemProperty -Path $key -Name 'CodexBootAnimation' -ErrorAction Stop
            }
        }
        Get-CodexCompanionProcesses -Exe $exe | Where-Object {
            $_.ProcessId -notin $before -and $_.CommandLine -match '(?:^|\s)--(?:supervise|watch|resident)(?:\s|$)'
        } | ForEach-Object { Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }
    } catch { Write-Warning 'Could not fully roll back startup. Check the tray and Windows Startup Apps before retrying.' }
    throw $installError
}
Write-Output "Installed the current-user Windows sign-in entry: $command"
Write-Output 'This does not modify Codex itself. See docs/INSTALLATION.md for the plugin hook.'
