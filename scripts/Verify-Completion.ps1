#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$JournalPath,
    [Parameter(Mandatory=$true)][string]$TurnId,
    [Parameter(Mandatory=$true)][string]$PlayerPath,
    [int]$TimeoutSeconds=100
)
$ErrorActionPreference='Stop'
$stream=[IO.FileStream]::new((Resolve-Path -LiteralPath $JournalPath).Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
$reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8)
try {
    $meta=$reader.ReadLine() | ConvertFrom-Json
    $completed=$null
    while (($line=$reader.ReadLine()) -ne $null) {
        if ($line.Contains('"type":"task_complete"') -and $line.Contains($TurnId)) {
            $entry=$line | ConvertFrom-Json
            if ($entry.type -eq 'event_msg' -and $entry.payload.type -eq 'task_complete' -and $entry.payload.turn_id -eq $TurnId) { $completed=$entry.payload }
        }
    }
} finally { $reader.Dispose() }
if ($null -eq $completed -or -not $completed.last_agent_message) { throw 'No completed real turn with a final answer was found.' }
$sha=[Security.Cryptography.SHA256]::Create()
try { $task=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($meta.payload.id+"`n"+$TurnId)))).Replace('-','').ToLowerInvariant().Substring(0,12) } finally { $sha.Dispose() }
$log=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) '.codex-boot-animation\playback.log'
$offset=(Get-Item -LiteralPath $log).Length
$start=New-Object Diagnostics.ProcessStartInfo($PlayerPath,'--completion-journal-replay')
$start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.RedirectStandardInput=$true
$process=[Diagnostics.Process]::Start($start)
$replayPid=$process.Id
try {
    $writer=New-Object IO.StreamWriter -ArgumentList $process.StandardInput.BaseStream,(New-Object Text.UTF8Encoding($false))
    $writer.Write((@{session_id=$meta.payload.id; turn_id=$TurnId; last_assistant_message=$completed.last_agent_message; duration_ms=$completed.duration_ms} | ConvertTo-Json -Compress))
    $writer.Dispose()
    if (-not $process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'Real completion replay failed.' }
} finally { $process.Dispose() }
$deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    $logStream=[IO.FileStream]::new($log,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try { $logStream.Position=$offset; $logReader=[IO.StreamReader]::new($logStream,[Text.Encoding]::UTF8); $events=$logReader.ReadToEnd() } finally { if ($logReader) { $logReader.Dispose() } else { $logStream.Dispose() } }
    $rejection=[regex]::Match($events,'pid='+$replayPid+' completion-suppressed ([^\r\n]+)')
    if ($rejection.Success) { throw ('Real completion detection rejected the record: '+$rejection.Groups[1].Value) }
    $launch=[regex]::Match($events,'completion-player-launched task='+$task+' player-pid=(\d+)')
    if ($launch.Success) {
        $playerPid=$launch.Groups[1].Value
        $playerEvents=($events -split "`n" | Where-Object { $_ -match ('pid='+$playerPid+' ') }) -join "`n"
        if ($playerEvents -match 'completion-close reason=(\S+)') {
            $reason=$Matches[1]
            $moving=$playerEvents -match 'completion-first-moving-frame'
            $audio=$playerEvents -match 'completion-media-opened .* audio=True'
            [pscustomobject]@{Task=$task;PlayerPID=$playerPid;RealRecordDurationMs=$completed.duration_ms;MovingPicture=$moving;AudioTrack=$audio;CloseReason=$reason;Pass=($moving -and $audio -and $reason -eq 'ended')}
            if (-not ($moving -and $audio -and $reason -eq 'ended')) { throw 'Completion was delivered but did not play fully. Inspect the printed result.' }
            return
        }
    }
    Start-Sleep -Milliseconds 200
} while ([DateTime]::UtcNow -lt $deadline)
throw 'No complete playback observed for this task. It may already be claimed, blocked by another animation, or awaiting foreground Codex.'
