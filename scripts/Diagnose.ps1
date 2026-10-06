#Requires -Version 5.1
[CmdletBinding()]
param([switch]$Json)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Support.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$checks = New-Object 'Collections.Generic.List[object]'
function Add-BootCheck($Item, $Status, $Detail, $NextStep) {
    if ($Status -eq 'PASS') { $NextStep = '' }
    $checks.Add([pscustomobject]@{ Item = $Item; Status = $Status; Detail = $Detail; NextStep = $NextStep })
}
$framework = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue).Release
if ($framework -ge 528040) { Add-BootCheck '.NET' 'PASS' '.NET Framework 4.8 or later' '' }
else { Add-BootCheck '.NET' 'FAIL' 'Required runtime is missing' 'Install .NET Framework 4.8 with Windows Update, then retry.' }
$wpf = @('Framework64', 'Framework' | ForEach-Object {
    Join-Path $env:WINDIR ('Microsoft.NET\' + $_ + '\v4.0.30319\WPF\PresentationFramework.dll')
} | Where-Object { Test-Path -LiteralPath $_ })
if ($wpf.Count) { Add-BootCheck 'WPF' 'PASS' 'Native WPF runtime is present' '' }
else { Add-BootCheck 'WPF' 'FAIL' 'WPF runtime is missing' 'Repair/install .NET Framework 4.8 before retrying.' }
try {
    $desktop = @(Get-AppxPackage -Name 'OpenAI.Codex' -ErrorAction Stop)
    if ($desktop.Count) { Add-BootCheck 'Codex desktop' 'PASS' ($desktop[0].Version.ToString()) '' }
    else { Add-BootCheck 'Codex desktop' 'WARN' 'The supported packaged desktop app was not found' 'Install/open the packaged Windows Codex desktop app.' }
} catch { Add-BootCheck 'Codex desktop' 'UNKNOWN' 'Package identity could not be queried' 'Confirm that the packaged Windows Codex app is installed.' }
$cli = $null
try {
    $cli = Resolve-CodexInstallerCli
    Add-BootCheck 'Codex CLI' 'PASS' $cli.Version ''
    $raw = Invoke-CodexInstallerCommand $cli.Path @('plugin', 'list', '--json')
    $plugins = ($raw -join [Environment]::NewLine) | ConvertFrom-Json
    $installed = @($plugins.installed | Where-Object { $_.name -eq 'codex-boot-animation' })
    if (-not $installed.Count) { Add-BootCheck 'Plugin' 'FAIL' 'No installed plugin was found' 'Fully extract the package and run install.cmd from Explorer.' }
    else {
        foreach ($plugin in $installed) {
            $status = if ($plugin.enabled) { 'PASS' } else { 'FAIL' }
            Add-BootCheck 'Plugin' $status ($plugin.pluginId + ' / ' + $plugin.version) 'If disabled, enable this plugin in Codex Plugins.'
        }
        if ($installed.Count -gt 1) { Add-BootCheck 'Multiple installs' 'WARN' 'More than one copy is installed' 'Identify the active copy before updating; avoid running two helpers.' }
    }
} catch { Add-BootCheck 'Codex CLI / plugin' 'UNKNOWN' 'Compatible CLI or plugin state could not be read' 'Open/update Codex desktop, then run check.cmd again.' }
if ($cli) {
    try {
        $hooks = @(Get-CodexBootHooks -CliPath $cli.Path -Cwd $repo)
        foreach ($event in @('sessionStart', 'userPromptSubmit', 'stop')) {
            $matching = @($hooks | Where-Object { $_.eventName -eq $event })
            if (-not $matching.Count) { Add-BootCheck ('Hook: ' + $event) 'WARN' 'No active hook was discovered for this directory' 'Check plugin enablement and project/enterprise hook policy in Codex.' }
            else {
                foreach ($hook in $matching) {
                    $status = if ($hook.enabled -and $hook.trustStatus -in @('trusted', 'managed')) { 'PASS' } else { 'WARN' }
                    Add-BootCheck ('Hook: ' + $event) $status ($hook.pluginId + ' / enabled=' + $hook.enabled + ' / trust=' + $hook.trustStatus) 'Review the current hook definition in Codex (/hooks in the CLI).'
                }
            }
        }
    } catch { Add-BootCheck 'Hook trust' 'UNKNOWN' 'This CLI could not query hook state' 'Review hooks in Codex; installing/enabling a plugin does not automatically trust them.' }
}
$run = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CodexBootAnimation -ErrorAction SilentlyContinue).CodexBootAnimation
$activeExe = $null
if ($run -match '^"([^"]+BootPlayer\.exe)"\s+--supervise\s*$') {
    $activeExe = $Matches[1]
    if (Test-Path -LiteralPath $activeExe) { Add-BootCheck 'Sign-in startup' 'PASS' $activeExe 'Keep this installation folder in place. Also check Windows Startup Apps is enabled.' }
    else { Add-BootCheck 'Sign-in startup' 'FAIL' 'Startup points to a missing executable' 'Restore the original folder or deliberately migrate the installation.' }
} elseif ($run) { Add-BootCheck 'Sign-in startup' 'WARN' 'An unexpected startup command is registered' 'Review the old installation before changing it.' }
else { Add-BootCheck 'Sign-in startup' 'FAIL' 'No current-user startup entry is registered' 'Run install.cmd from Explorer, not from the Codex terminal.' }
$activeRoot = $null
if ($activeExe -and (Test-Path -LiteralPath $activeExe)) {
    $activeRoot = Split-Path -Parent (Split-Path -Parent $activeExe)
    Add-BootCheck 'Active videos folder' 'INFO' (Join-Path $activeRoot 'videos') 'Use this folder or the helper tray menu to change the active videos.'
    Add-BootCheck 'Windows Startup Apps' 'PENDING' 'The Run entry is registered; Windows enablement/sign-in execution still needs acceptance' 'Check Windows Settings > Apps > Startup. A real reboot is a separate manual test.'
    if ($hooks) {
        $cacheRoots = @($hooks | ForEach-Object { Split-Path -Parent (Split-Path -Parent $_.sourcePath) } | Select-Object -Unique)
        foreach ($cacheRoot in $cacheRoots) {
            $cacheExe = Join-Path $cacheRoot 'hooks\BootPlayer.exe'
            Add-BootCheck 'Hook cache root' 'INFO' $cacheRoot 'Hook caches and the active media folder are separate. Edit active videos through the tray menu.'
            if ((Test-Path -LiteralPath $cacheExe) -and (Get-FileHash -LiteralPath $cacheExe).Hash -ne (Get-FileHash -LiteralPath $activeExe).Hash) {
                Add-BootCheck 'Cached vs active EXE' 'WARN' 'Cached hook fallback and active helper have different binaries' 'Online hooks route to the active helper. A reviewed plugin reinstall is needed to refresh cached fallback code; do not edit trust records.'
            }
        }
    }
    try {
        $processes = @(Get-CodexCompanionProcesses -Exe $activeExe)
        foreach ($mode in @('--supervise', '--watch', '--resident')) {
            $count = @($processes | Where-Object { $_.CommandLine -match ('(?:^|\s)' + $mode + '(?:\s|$)') }).Count
            Add-BootCheck ('Helper ' + $mode) $(if ($count -eq 1) { 'PASS' } else { 'FAIL' }) ('Running processes: ' + $count) 'Retry installation from the active folder; check security software notices and the local log if startup fails.'
        }
        if (Wait-CodexCompanion -Exe $activeExe -TimeoutSeconds 0) { Add-BootCheck 'Helper IPC' 'PASS' 'Resident pipe responded without requesting playback' '' }
        else { Add-BootCheck 'Helper IPC' 'FAIL' 'Helper readiness/communication failed' 'Check the local log; close an obsolete helper through its tray before retrying.' }
        $other = @(Get-CimInstance Win32_Process -Filter "name='BootPlayer.exe'" | Where-Object {
            $_.ExecutablePath -and $_.ExecutablePath -ine $activeExe -and $_.CommandLine -match '(?:^|\s)--(?:supervise|watch|resident)(?:\s|$)'
        })
        if ($other.Count) { Add-BootCheck 'Other helper copies' 'WARN' 'Helpers from another directory are running' 'Identify and close the obsolete copy through its tray; do not delete its videos.' }
    } catch { Add-BootCheck 'Helper processes' 'UNKNOWN' 'Windows process state could not be queried' 'Check the helper tray and Windows Task Manager.' }
    $packageExe = Join-Path $repo 'plugins\codex-boot-animation\hooks\BootPlayer.exe'
    if ((Get-FileHash -LiteralPath $activeExe).Hash -ne (Get-FileHash -LiteralPath $packageExe).Hash) {
        Add-BootCheck 'Package vs active EXE' 'WARN' 'This package and the active helper have different binaries' 'Use Update-Companion.ps1 with the active plugin root for an intentional update.'
    } else { Add-BootCheck 'Package vs active EXE' 'PASS' 'Executable hashes match' '' }
    foreach ($scene in @('冷启动', '新聊天', '生气回应', '任务完成', '闲置互动')) {
        $folder = Join-Path $activeRoot ('videos\' + $scene)
        $media = @(Get-ChildItem -LiteralPath $folder -Filter '*.mp4' -File -ErrorAction SilentlyContinue)
        $covers = @($media | Where-Object { Test-Path -LiteralPath ([IO.Path]::ChangeExtension($_.FullName, $null).TrimEnd('.') + '-first.png') }).Count
        Add-BootCheck ('Scene: ' + $scene) $(if ($media.Count) { 'INFO' } else { 'WARN' }) ("MP4=$($media.Count); covers=$covers") 'An empty folder disables this scene. Missing covers use a black transition; decoding/audio requires actual playback.'
    }
}
$dataRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.codex-boot-animation'
foreach ($flag in @('paused', 'idle-disabled')) {
    if (Test-Path -LiteralPath (Join-Path $dataRoot $flag)) { Add-BootCheck ('Tray setting: ' + $flag) 'WARN' 'This option is disabled/paused' 'Change it through the helper tray menu if you want automatic playback.' }
}
$log = Join-Path $dataRoot 'playback.log'
if (Test-Path -LiteralPath $log) {
    $lines = @(Get-Content -LiteralPath $log -Tail 1000)
    # Whitelist event names/reasons. Never print arbitrary errors, paths, prompts,
    # final answers or the full log as part of this diagnostic summary.
    foreach ($pattern in @('new-dialog-click-detected', 'new-dialog-watch-unavailable', 'new-dialog-click-targets-failed', 'completion-journal-unavailable', 'completion-deferred', 'completion-suppressed', 'idle-policy-state=', 'idle-return-accepted', 'media-failed')) {
        $last = $lines | Where-Object { $_ -match [regex]::Escape($pattern) } | Select-Object -Last 1
        if ($last -and $last -match '^([^ ]+).*?\bpid=\d+.*?' + [regex]::Escape($pattern) + '([a-zA-Z0-9=:_-]*)') {
            $detail = $Matches[1] + ' ' + $pattern + $Matches[2]
            if ($pattern -eq 'completion-suppressed' -and $last -match 'completion-suppressed ([a-z-]+)') { $detail += ' reason=' + $Matches[1] }
            if ($pattern -eq 'completion-deferred' -and $last -match 'reason=([a-z-]+)') { $detail += ' reason=' + $Matches[1] }
            Add-BootCheck 'Recent local event' 'INFO' $detail 'Recent events may include tests. Check an actual trigger; a suppressed event is not automatically a fault.'
        }
    }
    $launch = $lines | Where-Object { $_ -match 'completion-player-launched task=[a-f0-9]{12} player-pid=\d+' } | Select-Object -Last 1
    if ($launch -and $launch -match 'completion-player-launched task=[a-f0-9]{12} player-pid=(\d+)') {
        $loggedPlayer = $Matches[1]
        $playerLines = @($lines | Where-Object { $_ -match ('\bpid=' + $loggedPlayer + ' ') })
        $moving = @($playerLines | Where-Object { $_ -match 'completion-first-moving-frame' }).Count -gt 0
        $audio = @($playerLines | Where-Object { $_ -match 'completion-media-opened .* audio=True' }).Count -gt 0
        $close = $playerLines | Where-Object { $_ -match 'completion-close reason=[a-z-]+' } | Select-Object -Last 1
        $reason = 'not observed in recent log'
        if ($close -and $close -match 'completion-close reason=([a-z-]+)') { $reason = $Matches[1] }
        Add-BootCheck 'Logged completion playback' 'INFO' ("MovingPicture=$moving; AudioTrack=$audio; Close=$reason") 'This is evidence for the logged record, not a new trigger test or proof of audible speakers.'
    } else { Add-BootCheck 'Logged completion playback' 'UNKNOWN' 'No delivered completion player was found in the recent log' 'Finish a real task in foreground Codex, then run check.cmd. A tray preview does not test task-end detection.' }
    Add-BootCheck 'Runtime log' 'INFO' $log 'Keep this file private. Public issues should contain only necessary redacted facts.'
} else { Add-BootCheck 'Runtime log' 'INFO' 'No runtime log exists yet' 'Install/start the helper, perform an actual trigger, then check again.' }
Add-BootCheck 'Acceptance' 'PENDING' 'Independent PC/reboot, audible sound and all real trigger scenarios are not established by this check' 'Use docs/INSTALLATION.md and docs/PORTABILITY-ISSUES.md for real acceptance.'
if ($Json) { $checks.ToArray() | ConvertTo-Json -Depth 4 }
else {
    . (Join-Path $PSScriptRoot 'Diagnostic-Text.ps1')
    Format-BootDiagnostics -Checks $checks.ToArray()
}
