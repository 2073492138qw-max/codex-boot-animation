#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Diagnostic-Text.ps1')
function Assert-DiagnosticText([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
$rows = @(
    [pscustomobject]@{Item='Helper --resident'; Status='PASS'; Detail='Running processes: 1'; NextStep=''},
    [pscustomobject]@{Item='Hook: stop'; Status='WARN'; Detail='fixture / enabled=False / trust=untrusted'; NextStep='Review the current hook definition in Codex (/hooks in the CLI).'},
    [pscustomobject]@{Item='.NET'; Status='FAIL'; Detail='Required runtime is missing'; NextStep='Install .NET Framework 4.8 with Windows Update, then retry.'},
    [pscustomobject]@{Item='Helper processes'; Status='UNKNOWN'; Detail='Windows process state could not be queried'; NextStep='Check the helper tray and Windows Task Manager.'},
    [pscustomobject]@{Item='Acceptance'; Status='PENDING'; Detail='Independent PC/reboot, audible sound and all real trigger scenarios are not established by this check'; NextStep='Use docs/INSTALLATION.md and docs/PORTABILITY-ISSUES.md for real acceptance.'},
    [pscustomobject]@{Item='Active videos folder'; Status='INFO'; Detail='C:\测试 空格\videos'; NextStep='Use this folder or the helper tray menu to change the active videos.'},
    [pscustomobject]@{Item='Scene: 生气回应'; Status='WARN'; Detail='MP4=0; covers=0'; NextStep='An empty folder disables this scene. Missing covers use a black transition; decoding/audio requires actual playback.'},
    [pscustomobject]@{Item='Logged completion playback'; Status='INFO'; Detail='MovingPicture=False; AudioTrack=True; Close=ended'; NextStep='This is evidence for the logged record, not a new trigger test or proof of audible speakers.'},
    [pscustomobject]@{Item='Recent local event'; Status='INFO'; Detail='2026-10-03T02:03:04 new-dialog-click-detected'; NextStep='Recent events may include tests. Check an actual trigger; a suppressed event is not automatically a fault.'},
    [pscustomobject]@{Item='Future item'; Status='FUTURE'; Detail='future detail'; NextStep='future action'}
)
$before = $rows | ConvertTo-Json -Depth 4
$text = (Format-BootDiagnostics -Checks $rows) -join "`n"
$after = $rows | ConvertTo-Json -Depth 4
Assert-DiagnosticText ($before -ceq $after) 'Formatting changed the original JSON data.'
foreach ($status in @('通过 PASS','需检查 WARN','故障 FAIL','无法确认 UNKNOWN','待实测 PENDING','说明 INFO','未识别 FUTURE')) {
    Assert-DiagnosticText ($text.Contains('['+$status+']')) ('Missing status: '+$status)
}
foreach ($expected in @('常驻播放器','正在运行：1 个进程','任务结束钩子','已启用：否','尚未信任','Windows 更新','下一步：','当前视频目录','C:\测试 空格\videos','视频 0 个；首帧图 0 个','空目录会停用该场景','动态画面：未见记录','音轨：有记录','正常播完','不等于扬声器实际有声','检测到新聊天点击','future detail','future action','未识别 1 项')) {
    Assert-DiagnosticText ($text.Contains($expected)) ('Missing explanation: '+$expected)
}
Assert-DiagnosticText (-not $text.Contains('全部通过')) 'Summary overstated acceptance.'
$knownRows = @($rows | Where-Object { $_.Status -ne 'FUTURE' })
$knownText = (Format-BootDiagnostics -Checks $knownRows) -join "`n"
Assert-DiagnosticText ($knownText.Contains('通过 1 项；需检查 2 项；故障 1 项；无法确认 1 项；待实测 1 项')) 'Summary counts changed.'
$empty = (Format-BootDiagnostics -Checks @()) -join "`n"
Assert-DiagnosticText ($empty.Contains('未收到检查结果') -and -not $empty.Contains('全部通过')) 'Empty report claimed success.'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Diagnose.ps1'))
Assert-DiagnosticText ($source.Contains('if ($Json) { $checks.ToArray() | ConvertTo-Json -Depth 4 }')) 'JSON output contract changed.'
# Cover every current literal fact/action, not just the healthy-machine fixture.
$parseTokens=$null; $parseErrors=$null
$ast = [Management.Automation.Language.Parser]::ParseInput($source,[ref]$parseTokens,[ref]$parseErrors)
Assert-DiagnosticText ($parseErrors.Count -eq 0) 'Diagnostic script has syntax errors.'
$commands = $ast.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Add-BootCheck'},$true)
foreach ($command in $commands) {
    $parts = $command.CommandElements
    foreach ($field in @(3,4)) {
        if ($parts.Count -le $field -or $parts[$field] -isnot [Management.Automation.Language.StringConstantExpressionAst]) { continue }
        $value = $parts[$field].Value
        if (-not $value) { continue }
        $row = [pscustomobject]@{Item='fixture'; Status='INFO'; Detail=''; NextStep=''}
        if ($field -eq 3) {$row.Detail=$value} else {$row.NextStep=$value}
        $translated = (Format-BootDiagnostics -Checks @($row)) -join "`n"
        Assert-DiagnosticText (-not $translated.Contains($value)) ('Untranslated diagnostic fact/action: '+$value)
    }
}
$launcher = [IO.File]::ReadAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'check.cmd'))
Assert-DiagnosticText ($launcher -notmatch '[^\x00-\x7F]|(?im)^\s*chcp\b') 'Launcher introduced code-page-dependent text.'
Write-Output 'PASS diagnostic-chinese-text-and-json-preservation'
