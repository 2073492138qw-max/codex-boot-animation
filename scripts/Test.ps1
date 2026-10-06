#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$plugin = Join-Path $repo 'plugins\codex-boot-animation'
$exe = Join-Path $plugin 'hooks\BootPlayer.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Run scripts\Build.ps1 first.' }
$mediaRecoveryTest = Start-Process -FilePath $exe -ArgumentList '--intro-media-recovery-test' -WindowStyle Hidden -Wait -PassThru
if ($mediaRecoveryTest.ExitCode -ne 0) { throw 'Intro media failure recovery regression failed.' }
Write-Output 'PASS --intro-media-recovery-test'
$mediaDecoderProbe = Start-Process -FilePath $exe -ArgumentList '--intro-media-decoder-probe' -WindowStyle Hidden -PassThru
try {
    if (-not $mediaDecoderProbe.WaitForExit(60000)) { $mediaDecoderProbe.Kill(); throw 'Intro decoder recovery probe timed out.' }
    if ($mediaDecoderProbe.ExitCode -ne 0) { throw 'Intro decoder recovery probe failed.' }
} finally { $mediaDecoderProbe.Dispose() }
Write-Output 'PASS --intro-media-decoder-probe'
$traceTest = Start-Process -FilePath $exe -ArgumentList '--reaction-trace-test' -WindowStyle Hidden -Wait -PassThru
if ($traceTest.ExitCode -ne 0) { throw 'Reaction trace regression failed.' }
Write-Output 'PASS --reaction-trace-test'
$reactionFlowTest = Start-Process -FilePath $exe -ArgumentList '--reaction-flow-test' -WindowStyle Hidden -Wait -PassThru
if ($reactionFlowTest.ExitCode -ne 0) { throw 'Reaction dispatch-before-state regression failed.' }
Write-Output 'PASS --reaction-flow-test'
$reconcileTest = Start-Process -FilePath $exe -ArgumentList '--watcher-reconcile-test' -WindowStyle Hidden -Wait -PassThru
if ($reconcileTest.ExitCode -ne 0) { throw 'Watcher reconcile isolation regression failed.' }
Write-Output 'PASS --watcher-reconcile-test'
$completionDeliveryTest = Start-Process -FilePath $exe -ArgumentList '--completion-delivery-test' -WindowStyle Hidden -PassThru
try {
    if (-not $completionDeliveryTest.WaitForExit(15000)) { $completionDeliveryTest.Kill(); throw 'Completion delivery regression timed out.' }
    if ($completionDeliveryTest.ExitCode -ne 0) { throw 'Completion delivery regression failed.' }
} finally { $completionDeliveryTest.Dispose() }
Write-Output 'PASS --completion-delivery-test'
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
foreach ($file in (Get-Content -LiteralPath (Join-Path $plugin 'videos\media-catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json).files) {
    if ($file -match '(^[\\/]|\.\.|:)' -or $file -notmatch '\.mp4$') { throw 'Unsafe media catalog path.' }
    if (-not (Test-Path -LiteralPath (Join-Path $plugin "videos\$file"))) {
        throw "Required media is missing: $file"
    }
    $cover = [IO.Path]::ChangeExtension((Join-Path $plugin "videos\$file"), $null).TrimEnd('.') + '-first.png'
    if (-not (Test-Path -LiteralPath $cover)) { throw "First-frame cover is missing: $file" }
}
foreach ($scene in @('冷启动', '新聊天', '生气回应', '任务完成', '闲置互动')) {
    if (-not (Test-Path -LiteralPath (Join-Path $plugin "videos\$scene") -PathType Container)) {
        throw "Scene folder is missing: $scene"
    }
}
foreach ($case in @('--resident-protocol-test', '--navigation-log-test', '--button-discovery-test', '--runtime-data-test', '--lifetime-test', '--desktop-instance-test', '--new-dialog-parser-test', '--route-transition-test', '--media-library-test', '--anger-filter-test', '--anger-gate-test', '--window-focus-test', '--idle-policy-test', '--completion-filter-test', '--completion-flow-test', '--completion-journal-test', '--resident-scene-test', '--intro-priority-test', '--preview-focus-test', '--completion-preview-test', '--idle-preview-test', '--completion-playback-test')) {
    $process = Start-Process -FilePath $exe -ArgumentList $case -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "$case failed with exit code $($process.ExitCode)" }
    Write-Output "PASS $case"
}

$sceneTest = Start-Process -FilePath $exe -ArgumentList '--scene-settings-test' -WindowStyle Hidden -Wait -PassThru
if ($sceneTest.ExitCode -ne 0) { throw 'Scene settings regression failed.' }
Write-Output 'PASS --scene-settings-test'

$layoutTest = Start-Process -FilePath $exe -ArgumentList '--completion-layout-test' -WindowStyle Hidden -Wait -PassThru
if ($layoutTest.ExitCode -ne 0) { throw 'Completion layout regression failed.' }
Write-Output 'PASS --completion-layout-test'

$iconTest = Start-Process -FilePath $exe -ArgumentList '--tray-icon-test' -WindowStyle Hidden -Wait -PassThru
if ($iconTest.ExitCode -ne 0) { throw 'Embedded tray icon regression failed.' }
Write-Output 'PASS --tray-icon-test'

$viewportTest = Start-Process -FilePath $exe -ArgumentList '--owned-viewport-test' -WindowStyle Hidden -Wait -PassThru
if ($viewportTest.ExitCode -ne 0) { throw 'Owned video viewport regression failed.' }
Write-Output 'PASS --owned-viewport-test'

$followTest = Start-Process -FilePath $exe -ArgumentList '--owner-follow-test' -WindowStyle Hidden -Wait -PassThru
if ($followTest.ExitCode -ne 0) { throw 'Owner location-event follow regression failed.' }
Write-Output 'PASS --owner-follow-test'

# Exercise the real stdin contract of both hooks without playing a video or
# leaving a persistent turn file. The negative final reply must consume state.
$session = 'codex-boot-test-' + [guid]::NewGuid().ToString('N')
$turn = [guid]::NewGuid().ToString('N')
$bytes = [Text.Encoding]::UTF8.GetBytes($session + "`n" + $turn)
$sha = [Security.Cryptography.SHA256]::Create()
try { $key = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
finally { $sha.Dispose() }
$state = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) ".codex-boot-animation\turns\$key.candidate"
if (Test-Path -LiteralPath $state) { throw 'Unique test state unexpectedly exists.' }
function Invoke-Hook([string] $mode, [object] $payload) {
    $start = New-Object Diagnostics.ProcessStartInfo($exe, $mode)
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
    $claim = [IO.Path]::ChangeExtension($state,'claimed')
    if (Test-Path -LiteralPath $claim) { Remove-Item -LiteralPath $claim }
}
& (Join-Path $PSScriptRoot 'Test-EmptyResident.ps1')
& (Join-Path $PSScriptRoot 'Test-Install.ps1')
& (Join-Path $PSScriptRoot 'Test-DiagnosticText.ps1')
& (Join-Path $PSScriptRoot 'Test-MediaCheck.ps1')
Write-Output 'All non-UI tests passed.'
