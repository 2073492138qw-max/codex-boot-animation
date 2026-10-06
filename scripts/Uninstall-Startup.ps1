#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ($env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY) {
    throw 'Open Windows PowerShell from the Start menu and run this script there. The Codex terminal can see an isolated registry view.'
}
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$value = Get-ItemProperty -Path $key -Name 'CodexBootAnimation' -ErrorAction SilentlyContinue
if ($null -ne $value) {
    $repo = Split-Path -Parent $PSScriptRoot
    $exe = Join-Path $repo 'plugins\codex-boot-animation\hooks\BootPlayer.exe'
    $expected = '"' + $exe + '" --supervise'
    if ($value.CodexBootAnimation -ne $expected) {
        throw 'The sign-in entry points to a different installation. Nothing was removed.'
    }
    Remove-ItemProperty -Path $key -Name 'CodexBootAnimation'
    Write-Output 'Removed the CodexBootAnimation Windows sign-in entry.'
} else {
    Write-Output 'No CodexBootAnimation Windows sign-in entry was present.'
}
Write-Output 'This does not stop an already-running helper or remove the Codex plugin.'
