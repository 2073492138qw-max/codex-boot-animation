#Requires -Version 5.1
[CmdletBinding()]
param()
# Integration check using a separate Codex home. It does not register startup
# or review/trust hooks, and never changes the user's real Codex config.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Support.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$cli = Resolve-CodexInstallerCli
$previousCodexHome = $env:CODEX_HOME
# Avoid adding a full cache tree beneath an already-deep extraction path.
# Windows PowerShell 5.1 cannot enumerate such paths beyond MAX_PATH.
$cleanHome = Join-Path ([IO.Path]::GetTempPath()) ('cba-clean-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $cleanHome -Force | Out-Null
try {
    $env:CODEX_HOME = $cleanHome
    $null = Invoke-CodexInstallerCommand $cli.Path @('plugin', 'marketplace', 'add', $repo) -TimeoutSeconds 60
    $null = Invoke-CodexInstallerCommand $cli.Path @('plugin', 'add', 'codex-boot-animation@codex-boot-animation') -TimeoutSeconds 60
    $result = Invoke-CodexInstallerCommand $cli.Path @('plugin', 'list', '--json')
    $plugins = ($result -join [Environment]::NewLine) | ConvertFrom-Json
    $installed = @($plugins.installed | Where-Object { $_.pluginId -eq 'codex-boot-animation@codex-boot-animation' })
    if ($installed.Count -ne 1 -or -not $installed[0].installed -or -not $installed[0].enabled) {
        throw 'The clean installation is not installed and enabled.'
    }
    $cache = Join-Path $cleanHome 'plugins\cache'
    $cachedManifests = @(Get-ChildItem -LiteralPath $cache -Filter plugin.json -File -Recurse |
        Where-Object { $_.Directory.Name -eq '.codex-plugin' })
    $matching = @($cachedManifests | Where-Object {
        (Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json).name -eq 'codex-boot-animation'
    })
    if ($matching.Count -ne 1) { throw 'The clean install did not cache exactly one plugin package.' }
    $plugin = Split-Path -Parent $matching[0].Directory.FullName
    $expected = Get-Content -LiteralPath (Join-Path $repo 'plugins\codex-boot-animation\.codex-plugin\plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $actual = Get-Content -LiteralPath $matching[0].FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($actual.version -ne $expected.version) { throw 'The cached plugin version does not match the package.' }
    $sourceExe = Join-Path $repo 'plugins\codex-boot-animation\hooks\BootPlayer.exe'
    $cachedExe = Join-Path $plugin 'hooks\BootPlayer.exe'
    if ((Get-FileHash -LiteralPath $sourceExe).Hash -ne (Get-FileHash -LiteralPath $cachedExe).Hash) {
        throw 'The cached executable does not match the package.'
    }
    $catalog = Get-Content -LiteralPath (Join-Path $plugin 'videos\media-catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($relative in $catalog.files) {
        if (-not (Test-Path -LiteralPath (Join-Path $plugin ('videos\' + $relative)))) { throw 'The cached package is missing media.' }
    }
    $hooks = @(Get-CodexBootHooks -CliPath $cli.Path -Cwd $repo)
    foreach ($event in @('sessionStart', 'userPromptSubmit', 'stop')) {
        $matchingHooks = @($hooks | Where-Object { $_.eventName -eq $event })
        if ($matchingHooks.Count -ne 1 -or -not $matchingHooks[0].enabled -or $matchingHooks[0].trustStatus -ne 'untrusted') {
            throw "Clean installed hook state is unexpected: $event. This check never approves hooks."
        }
    }
    Write-Output "PASS clean Codex home: marketplace registration, plugin install/enable, cache version, executable and $($catalog.files.Count) videos"
    Write-Output 'PASS clean hooks: all three discovered/enabled, untrusted until user review'
    Write-Output 'Hook trust and Windows startup still require real desktop acceptance.'
} finally { $env:CODEX_HOME = $previousCodexHome }
