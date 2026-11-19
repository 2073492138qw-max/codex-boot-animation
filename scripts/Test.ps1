#Requires -Version 5.1
[CmdletBinding()]
param([switch]$IncludeMedia)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-Process.ps1')
. (Join-Path $PSScriptRoot 'Media-Preflight.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$plugin = Join-Path $repo 'plugins\codex-boot-animation'
$exe = Join-Path $plugin 'hooks\BootPlayer.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Run scripts\Build.ps1 first.' }

# Every C# self-test binds this isolated runtime root before it touches a state
# or log type. The real %USERPROFILE% state is read-only for the whole run.
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('codex-boot-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$realRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-boot-animation'
function Get-RealRunEntry {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try { if ($key) { return $key.GetValue('CodexBootAnimation') } else { return $null } }
    finally { if ($key) { $key.Dispose() } }
}
# Guard against the regression this run is meant to prevent: a passing suite that
# still changed the pause flag, the startup entry or the user's settings.
function Get-ProtectedState {
    $state = [ordered]@{ RunEntry = Get-RealRunEntry }
    foreach ($name in @('disabled', 'paused', 'scene-settings.json', 'completion-layout.json')) {
        $file = Join-Path $realRoot $name
        $state[$name] = if (Test-Path -LiteralPath $file -PathType Leaf) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash } else { '<absent>' }
    }
    return $state
}
function Assert-ProtectedState($Before) {
    $after = Get-ProtectedState
    foreach ($name in $Before.Keys) {
        if ($Before[$name] -ne $after[$name]) { throw "Tests changed real user state: $name" }
    }
}
$protectedBefore = Get-ProtectedState
$stateFailure = $null

function Invoke-BootTest {
    param([string]$Case, [int]$TimeoutSeconds = 60)
    $process = Start-TestProcess -FilePath $exe -ArgumentList @('--test-root', $testRoot, $Case) -WindowStyle Hidden -Wait -PassThru -TimeoutSeconds $TimeoutSeconds
    if ($process.ExitCode -ne 0) { throw "$Case failed with exit code $($process.ExitCode)" }
    Write-Output "PASS $Case"
}
try {
    # A test entry without an isolated root must fail before its first write, and
    # the real profile must never be accepted as that root.
    $unbound = Start-TestProcess -FilePath $exe -ArgumentList '--completion-journal-test' -WindowStyle Hidden -Wait -PassThru
    if ($unbound.ExitCode -ne 46) { throw 'A test entry without --test-root was not refused.' }
    $rejectedRoot = Start-TestProcess -FilePath $exe -ArgumentList @('--test-root', $realRoot, '--media-library-test') -WindowStyle Hidden -Wait -PassThru
    if ($rejectedRoot.ExitCode -ne 46) { throw 'The real runtime root was accepted as a test root.' }
    Write-Output 'PASS runtime-root-isolation-guards'
    & (Join-Path $PSScriptRoot 'Test-RuntimeIsolation.ps1') -Exe $exe

    Invoke-BootTest '--navigation-tail-test'
    Invoke-BootTest '--reaction-trace-test'
    Invoke-BootTest '--reaction-flow-test'
    Invoke-BootTest '--watcher-reconcile-test'
    Invoke-BootTest '--completion-delivery-test' -TimeoutSeconds 15
    foreach ($script in (Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File)) {
        $content = [IO.File]::ReadAllText($script.FullName)
        $bytes = [IO.File]::ReadAllBytes($script.FullName)
        if ($content -match '[^\x00-\x7F]' -and ($bytes.Length -lt 3 -or $bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191)) {
            throw "Non-ASCII PowerShell script needs UTF-8 BOM for Windows PowerShell 5.1: $($script.Name)"
        }
    }

    $manifest = Get-Content -LiteralPath (Join-Path $plugin '.codex-plugin\plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $portable = Get-Content -LiteralPath (Join-Path $repo 'docs\plugin.portable.example.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($portable.name -ne $manifest.name -or $portable.version -ne $manifest.version) { throw 'Portable and compatibility manifests disagree.' }
    if ((Test-Path -LiteralPath (Join-Path $plugin 'plugin.json')) -or -not $manifest.interface.displayName) {
        throw 'OpenAI hooks/interface must use the compatibility manifest for the tested desktop runtime.'
    }
    if ($manifest.hooks -ne './hooks/hooks.json') {
        throw 'The compatibility manifest must explicitly point to the shipped hook definitions.'
    }
    $hooks = Get-Content -LiteralPath (Join-Path $plugin 'hooks\hooks.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $marketplace = Get-Content -LiteralPath (Join-Path $repo '.agents\plugins\marketplace.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.name -ne 'codex-boot-animation') { throw 'Unexpected plugin name.' }
    if ($marketplace.name -ne 'codex-boot-animation' -or $marketplace.plugins[0].source.path -ne './plugins/codex-boot-animation') {
        throw 'Repository marketplace does not point to the plugin package.'
    }
    if (-not $hooks.hooks.SessionStart) { throw 'SessionStart hook is missing.' }
    if (-not $hooks.hooks.UserPromptSubmit) { throw 'UserPromptSubmit hook is missing.' }
    if (-not $hooks.hooks.Stop) { throw 'Stop hook is missing.' }
    $media = Assert-MediaPreflight -VideosRoot (Join-Path $plugin 'videos')
    Write-Output "PASS media-preflight videos=$($media.Videos.Count) covers-missing=$($media.MissingCovers.Count)"
    foreach ($scene in @('冷启动', '新聊天', '生气回应', '任务完成', '闲置互动')) {
        if (-not (Test-Path -LiteralPath (Join-Path $plugin "videos\$scene") -PathType Container)) {
            throw "Scene folder is missing: $scene"
        }
    }
    foreach ($case in @('--repair-regression-test', '--resident-protocol-test', '--navigation-log-test', '--button-discovery-test', '--runtime-data-test', '--lifetime-test', '--desktop-instance-test', '--new-dialog-parser-test', '--route-transition-test', '--media-library-test', '--anger-filter-test', '--anger-gate-test', '--window-focus-test', '--idle-policy-test', '--completion-filter-test', '--completion-flow-test', '--completion-journal-test', '--intro-priority-test', '--preview-focus-test', '--completion-preview-test', '--idle-preview-test', '--completion-playback-test', '--scene-settings-test', '--completion-layout-test', '--tray-icon-test', '--owned-viewport-test', '--owner-follow-test')) {
        Invoke-BootTest $case
    }

    # Exercise the real stdin contract of both hooks without playing a video or
    # leaving a persistent turn file. State and logs stay under $testRoot.
    $session = 'codex-boot-test-' + [guid]::NewGuid().ToString('N')
    $turn = [guid]::NewGuid().ToString('N')
    $bytes = [Text.Encoding]::UTF8.GetBytes($session + "`n" + $turn)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $key = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    $state = Join-Path $testRoot "turns\$key.candidate"
    if (Test-Path -LiteralPath $state) { throw 'Unique test state unexpectedly exists.' }
    function Invoke-Hook([string] $mode, [object] $payload) {
        $argumentLine = '--test-root "' + $testRoot + '" ' + $mode
        $start = New-Object Diagnostics.ProcessStartInfo($exe, $argumentLine)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $process = [Diagnostics.Process]::Start($start)
        try {
            # .NET Framework lacks ProcessStartInfo.StandardInputEncoding.
            # Write directly to the pipe using UTF-8 on both PowerShell runtimes.
            $writer = New-Object IO.StreamWriter -ArgumentList $process.StandardInput.BaseStream, (New-Object Text.UTF8Encoding($false))
            $writer.Write(($payload | ConvertTo-Json -Compress -Depth 4))
            $writer.Dispose()
            if (-not $process.WaitForExit(10000)) {
                $process.Kill()
                throw "$mode timed out."
            }
            if ($process.ExitCode -ne 0) { throw "$mode failed with exit code $($process.ExitCode)" }
        } finally { $process.Dispose() }
    }
    try {
        Invoke-Hook '--prompt-hook' @{ session_id = $session; turn_id = $turn; prompt = '帮我修复这个插件' }
        if (-not (Test-Path -LiteralPath $state) -or (Get-Content -LiteralPath $state -Raw).Trim() -ne '1') {
            throw 'UserPromptSubmit did not record the task candidate.'
        }
        Invoke-Hook '--stop-hook' @{ session_id = $session; turn_id = $turn; last_assistant_message = '我已经看过了，之后会修复。' }
        if (Test-Path -LiteralPath $state) { throw 'Stop did not consume the turn state.' }
        Write-Output 'PASS prompt-to-stop-hook-roundtrip'
    } finally {
        if (Test-Path -LiteralPath $state) { Remove-Item -LiteralPath $state }
        $claim = [IO.Path]::ChangeExtension($state, 'claimed')
        if (Test-Path -LiteralPath $claim) { Remove-Item -LiteralPath $claim }
    }
    & (Join-Path $PSScriptRoot 'Test-Install.ps1')
    & (Join-Path $PSScriptRoot 'Test-StartupPolicy.ps1')
    & (Join-Path $PSScriptRoot 'Test-DiagnosticText.ps1')
    & (Join-Path $PSScriptRoot 'Test-MediaCheck.ps1')
    & (Join-Path $PSScriptRoot 'Test-MediaPreflight.ps1')
    if ($IncludeMedia) { & (Join-Path $PSScriptRoot 'Test-Media.ps1') }
    Write-Output 'All non-UI tests passed.'
} finally {
    if ($testRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $testRoot) -like 'codex-boot-tests-*' -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
    try { Assert-ProtectedState $protectedBefore } catch { $stateFailure = $_; Write-Output "FAIL real-profile-state-changed: $_" }
}
if ($stateFailure) { throw $stateFailure }
