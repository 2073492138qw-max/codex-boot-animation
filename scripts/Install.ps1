#Requires -Version 5.1
[CmdletBinding()]
param([switch]$CheckOnly)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Support.ps1')
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This companion supports Windows only.'
}
$repo = Split-Path -Parent $PSScriptRoot
$plugin = Join-Path $repo 'plugins\codex-boot-animation'
$exe = Join-Path $plugin 'hooks\BootPlayer.exe'
$marketplace = Join-Path $repo '.agents\plugins\marketplace.json'
$videos = Join-Path $plugin 'videos'
if (-not (Test-Path -LiteralPath $marketplace)) { throw 'The repository marketplace is missing.' }
if (-not (Test-Path -LiteralPath $exe)) { throw 'BootPlayer.exe is missing. Run scripts\Build.ps1.' }
if (-not (Get-ChildItem -LiteralPath (Join-Path $videos '新聊天') -Filter '*.mp4' -File -ErrorAction SilentlyContinue)) {
    throw 'Add at least one MP4 to videos\新聊天.'
}
$manifest = Get-Content -LiteralPath (Join-Path $plugin '.codex-plugin\plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (Test-Path -LiteralPath (Join-Path $plugin 'plugin.json')) { throw 'This Windows package must use its tested .codex-plugin manifest; a root portable manifest can hide hooks on the supported runtime.' }
if ($manifest.hooks -ne './hooks/hooks.json') { throw 'The plugin manifest does not point to its hooks.' }
$hooks = Get-Content -LiteralPath (Join-Path $plugin 'hooks\hooks.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $hooks.hooks.SessionStart -or -not $hooks.hooks.UserPromptSubmit -or -not $hooks.hooks.Stop) { throw 'Required hook definitions are missing.' }
$mediaCatalog = Get-Content -LiteralPath (Join-Path $videos 'media-catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($relative in $mediaCatalog.files) {
    if ($relative -match '(^[\\/]|\.\.|:)' -or $relative -notmatch '\.mp4$') { throw 'Unsafe media catalog path.' }
    $media = Join-Path $videos $relative
    $cover = [IO.Path]::ChangeExtension($media,$null).TrimEnd('.') + '-first.png'
    if (-not (Test-Path -LiteralPath $media) -or -not (Test-Path -LiteralPath $cover)) { throw "Release media is missing: $relative" }
}
$frameworkRelease = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue).Release
if ($null -eq $frameworkRelease -or $frameworkRelease -lt 528040) { throw '.NET Framework 4.8 or later is required. Install it with Windows Update, then retry install.cmd.' }
$catalog = Get-Content -LiteralPath $marketplace -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.name -ne 'codex-boot-animation' -or $catalog.name -ne 'codex-boot-animation') {
    throw 'Plugin or marketplace name does not match this installer.'
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$expectedRun = '"' + $exe + '" --supervise'
$existingRun = Get-ItemProperty -Path $runKey -Name 'CodexBootAnimation' -ErrorAction SilentlyContinue
$startupConflict = $null -ne $existingRun -and $existingRun.CodexBootAnimation -ne $expectedRun
if ($startupConflict) {
    if ($CheckOnly) {
        Write-Warning 'A different CodexBootAnimation installation is active. A full install will refuse to replace it automatically.'
    } else {
        throw 'A different CodexBootAnimation installation is active. This installer will not silently replace it.'
    }
}
$cli = Resolve-CodexInstallerCli
$codexExe = $cli.Path
$sources = $cli.Sources
$registered = @($sources.marketplaces | Where-Object { $_.name -eq $catalog.name })
$marketplaceConflict = $false
if ($registered.Count -gt 0) {
    $registeredRoot = [IO.Path]::GetFullPath($registered[0].root)
    if (-not [string]::Equals($registeredRoot,[IO.Path]::GetFullPath($repo),[StringComparison]::OrdinalIgnoreCase)) {
        $marketplaceConflict = $true
        if ($CheckOnly) { Write-Warning "Marketplace name is already used by a different directory: $registeredRoot. A full install will refuse to replace it." }
        else { throw "Marketplace name is already used by a different directory: $registeredRoot" }
    }
}
Write-Output "Repository: $repo"
Write-Output "Plugin: $plugin"
Write-Output "Codex CLI: $codexExe"
Write-Output "CLI version: $($cli.Version)"
Write-Output "Videos: $(@(Get-ChildItem -LiteralPath $videos -Filter '*.mp4' -File -Recurse).Count)"
if ($CheckOnly) {
    if ($startupConflict -or $marketplaceConflict) { Write-Output 'Package checks passed, but a full install is blocked by an existing installation or marketplace. No settings changed.' }
    else { Write-Output 'Preflight passed; no settings changed.' }
    return
}
if ($env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY) {
    throw 'Run this installer from Windows PowerShell opened via the Start menu, not from the Codex terminal. The Codex registry view may be isolated.'
}
& (Join-Path $PSScriptRoot 'Test.ps1')

if ($registered.Count -eq 0) {
    Invoke-CodexInstallerCommand $codexExe @('plugin', 'marketplace', 'add', $repo) -TimeoutSeconds 60
}

Invoke-CodexInstallerCommand $codexExe @('plugin', 'add', 'codex-boot-animation@codex-boot-animation') -TimeoutSeconds 60
$installedJson = Invoke-CodexInstallerCommand $codexExe @('plugin', 'list', '--json')
$installedPlugins = ($installedJson -join [Environment]::NewLine) | ConvertFrom-Json
$installed = @($installedPlugins.installed | Where-Object { $_.pluginId -eq 'codex-boot-animation@codex-boot-animation' })
if ($installed.Count -ne 1 -or -not $installed[0].installed -or -not $installed[0].enabled) {
    throw 'The plugin is not installed and enabled. Check Codex Plugins, then retry install.cmd. Windows startup was not changed.'
}
$loadedHooks = @(Get-CodexBootHooks -CliPath $codexExe -Cwd $repo)
foreach ($event in @('sessionStart', 'userPromptSubmit', 'stop')) {
    $matchingHook = @($loadedHooks | Where-Object { $_.pluginId -eq 'codex-boot-animation@codex-boot-animation' -and $_.eventName -eq $event })
    if ($matchingHook.Count -ne 1 -or -not $matchingHook[0].enabled) {
        throw "Plugin files were installed, but Codex did not discover exactly one enabled $event hook. Update Codex/check hook policy; startup was not changed."
    }
}
& (Join-Path $PSScriptRoot 'Install-Startup.ps1')
Write-Output 'Installed. Review/trust the plugin hook in Codex when prompted, then restart Codex and verify playback.'
