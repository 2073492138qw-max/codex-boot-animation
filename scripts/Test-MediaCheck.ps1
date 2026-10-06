#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Media-Check.ps1')
function Assert-Media([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Media-Status($Report,$Item) { @($Report.Checks | Where-Object Item -eq $Item | Select-Object -Last 1)[0].Status }
$root=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('cba-media-check-'+[guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $root | Out-Null
$broken=Join-Path $root '损坏 样本.mp4'
[IO.File]::WriteAllText($broken,'not a media file')
$missingTool=Join-Path $root 'missing-tool.exe'
$unknown=Get-BootVideoInspection $broken -FFmpegPath $missingTool -FFprobePath $missingTool
Assert-Media ((Media-Status $unknown '媒体与音轨') -eq 'UNKNOWN' -and (Media-Status $unknown '解码') -eq 'UNKNOWN') 'Missing tools were reported as success or corruption.'
$zero=Join-Path $root '空视频.mp4'; [IO.File]::WriteAllBytes($zero,[byte[]]@())
Assert-Media ((Media-Status (Get-BootVideoInspection $zero) '文件') -eq 'FAIL') 'Accepted empty media.'
Assert-Media ((Media-Status (Get-BootVideoInspection (Join-Path $root 'missing.mp4')) '文件') -eq 'FAIL') 'Accepted absent media.'
$shell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$timer=[Diagnostics.Stopwatch]::StartNew()
$timeout=Invoke-BootMediaTool $shell @('-NoProfile','-Command','Start-Sleep -Seconds 5') 1
Assert-Media ($timeout.Reason -eq 'timeout' -and $timer.Elapsed.TotalSeconds -lt 3) 'Native tool timeout did not stop its owned process promptly.'
$failure=Invoke-BootMediaTool $shell @('-NoProfile','-Command','exit 7')
Assert-Media ($failure.ExitCode -eq 7) 'Lost the tool exit code.'
$ffmpeg=Resolve-BootMediaTool 'ffmpeg'; $ffprobe=Resolve-BootMediaTool 'ffprobe'
$sharedBin=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-boot-animation\tools\ffmpeg\bin'
foreach ($name in @('ffmpeg','ffprobe')) {
    $sharedTool=Join-Path $sharedBin ($name+'.exe')
    if (Test-Path -LiteralPath $sharedTool -PathType Leaf) {
        Assert-Media ((Resolve-BootMediaTool $name) -eq $sharedTool) 'Shared user-profile tool must take precedence over redirected AppData and PATH.'
        Assert-Media ($null -eq (Resolve-BootMediaTool $name -Path $missingTool)) 'Explicit missing tool must not silently fall back to a default.'
    }
}
if (-not $ffmpeg -or -not $ffprobe) {
    Write-Output 'PASS media-check-missing-input-tools-and-timeout'
    Write-Output 'SKIP media-check-real-decoding-and-import: optional FFmpeg/ffprobe are unavailable.'
    Write-Output ('Fixture retained: '+$root)
    return
}
function Run-MediaFixture([string[]]$Arguments) {
    $result=Invoke-BootMediaTool $ffmpeg $Arguments
    Assert-Media (-not $result.Reason -and $result.ExitCode -eq 0) 'Could not create the isolated media fixture.'
}
$good=Join-Path $root '中文 空格 & 测试.mp4'
Run-MediaFixture @('-nostdin','-hide_banner','-v','error','-n','-f','lavfi','-i','color=c=red:s=96x64:r=10','-f','lavfi','-i','sine=frequency=440:sample_rate=44100','-t','0.4','-c:v','libx264','-pix_fmt','yuv420p','-c:a','aac','-shortest',$good)
$cover=Join-Path $root '中文 空格 & 测试-first.png'
Run-MediaFixture @('-nostdin','-hide_banner','-v','error','-n','-i',$good,'-map','0:v:0','-frames:v','1','-update','1',$cover)
$beforeVideo=(Get-FileHash -LiteralPath $good).Hash; $beforeCover=(Get-FileHash -LiteralPath $cover).Hash
$goodReport=Get-BootVideoInspection $good
foreach ($item in @('文件','视频流','音轨','解码','首帧图')) { Assert-Media ((Media-Status $goodReport $item) -eq 'PASS') ('Valid fixture failed: '+$item) }
Assert-Media ((Get-FileHash -LiteralPath $good).Hash -eq $beforeVideo -and (Get-FileHash -LiteralPath $cover).Hash -eq $beforeCover) 'Read-only inspection modified media.'
$silent=Join-Path $root '无声.mp4'
Run-MediaFixture @('-nostdin','-hide_banner','-v','error','-n','-i',$good,'-an','-c:v','copy',$silent)
$silentReport=Get-BootVideoInspection $silent
Assert-Media ((Media-Status $silentReport '音轨') -eq 'WARN' -and (Media-Status $silentReport '解码') -eq 'PASS' -and (Media-Status $silentReport '首帧图') -eq 'WARN') 'Silent video or absent cover misclassified.'
$audioOnly=Join-Path $root '只有声音.mp4'
Run-MediaFixture @('-nostdin','-hide_banner','-v','error','-n','-f','lavfi','-i','sine=frequency=440:sample_rate=44100','-t','0.4','-c:a','aac',$audioOnly)
Assert-Media ((Media-Status (Get-BootVideoInspection $audioOnly) '视频流') -eq 'FAIL') 'Audio-only file accepted as animation.'
Assert-Media ((Media-Status (Get-BootVideoInspection $broken) '媒体读取') -eq 'FAIL') 'Corrupt file accepted.'
$oldCover=Join-Path $root '旧首帧.png'
Run-MediaFixture @('-nostdin','-hide_banner','-v','error','-n','-f','lavfi','-i','color=c=blue:s=96x64','-frames:v','1','-update','1',$oldCover)
Copy-Item -LiteralPath $oldCover -Destination $cover -Force
Assert-Media ((Media-Status (Get-BootVideoInspection $good) '首帧图') -eq 'WARN') 'Stale first-frame cover accepted.'
# Same pixel count, different dimensions must not be mistaken for a match.
$wrongSize=Join-Path $root '错误尺寸.png'
Run-MediaFixture @('-nostdin','-hide_banner','-v','error','-n','-i',$good,'-vf','transpose=1','-frames:v','1','-update','1',$wrongSize)
Copy-Item -LiteralPath $wrongSize -Destination $cover -Force
Assert-Media ((Media-Status (Get-BootVideoInspection $good) '首帧图') -eq 'WARN') 'Wrong dimensions accepted as matching pixels.'
$sceneRoot=Join-Path $root '隔离 场景'
New-Item -ItemType Directory -Path (Join-Path $sceneRoot '新聊天') | Out-Null
Copy-Item -LiteralPath $silent -Destination (Join-Path $sceneRoot '新聊天\无声.mp4')
$json=& (Join-Path $PSScriptRoot 'Check-Videos.ps1') -VideoRoot $sceneRoot -Json
$parsed=@($json | ConvertFrom-Json)
Assert-Media ($parsed.Count -eq 1 -and (Media-Status $parsed[0] '音轨') -eq 'WARN') 'CLI JSON did not preserve the inspection.'
$human=(& (Join-Path $PSScriptRoot 'Check-Videos.ps1') -VideoPath $silent) -join "`n"
Assert-Media ($human.Contains('没有音轨') -and $human.Contains('缺少对应的首帧图') -and $human.Contains('只读')) 'Human report is not actionable Chinese.'
$importRoot=Join-Path $root '导入 目标'
$added=& (Join-Path $PSScriptRoot 'Add-Video.ps1') -VideoPath $good -Scene '新聊天' -VideoRoot $importRoot -Name '导入的视频.mp4'
$importVideo=Join-Path $importRoot '新聊天\导入的视频.mp4'
$importCover=Join-Path $importRoot '新聊天\导入的视频-first.png'
Assert-Media ((Get-FileHash -LiteralPath $importVideo).Hash -eq $beforeVideo -and (Test-Path -LiteralPath $importCover)) 'Import changed source pixels/audio or failed to generate a cover.'
Assert-Media ((Media-Status (Get-BootVideoInspection $importVideo) '首帧图') -eq 'PASS') 'Import cover does not match the new video.'
$savedImport=(Get-FileHash -LiteralPath $importVideo).Hash
$rejected=$false
try { & (Join-Path $PSScriptRoot 'Add-Video.ps1') -VideoPath $silent -VideoRoot $importRoot -Name '导入的视频.mp4' | Out-Null } catch {$rejected=$true}
Assert-Media ($rejected -and (Get-FileHash -LiteralPath $importVideo).Hash -eq $savedImport) 'Import overwrote an existing video.'
$orphan=Join-Path $importRoot '新聊天\孤立-first.png'; Copy-Item -LiteralPath $oldCover -Destination $orphan
$orphanHash=(Get-FileHash -LiteralPath $orphan).Hash; $rejected=$false
try { & (Join-Path $PSScriptRoot 'Add-Video.ps1') -VideoPath $good -VideoRoot $importRoot -Name '孤立.mp4' | Out-Null } catch {$rejected=$true}
Assert-Media ($rejected -and -not (Test-Path -LiteralPath (Join-Path $importRoot '新聊天\孤立.mp4')) -and (Get-FileHash -LiteralPath $orphan).Hash -eq $orphanHash) 'Import overwrote an orphan cover or copied before collision check.'
$rejected=$false
try { & (Join-Path $PSScriptRoot 'Add-Video.ps1') -VideoPath $broken -VideoRoot $importRoot -Name '坏视频.mp4' | Out-Null } catch {$rejected=$true}
Assert-Media ($rejected -and -not (Test-Path -LiteralPath (Join-Path $importRoot '新聊天\坏视频.mp4'))) 'Import copied corrupt media despite a failed precheck.'
Assert-Media ((Get-FileHash -LiteralPath $good).Hash -eq $beforeVideo) 'Import modified the source video.'
Write-Output 'PASS media-check-readonly-decoding-audio-cover-and-safe-import'
Write-Output ('Fixture retained: '+$root)
