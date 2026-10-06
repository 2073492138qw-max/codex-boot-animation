#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Position=0)][string]$VideoPath, [string]$VideoRoot,
      [string]$FFmpegPath, [string]$FFprobePath, [ValidateRange(1,120)][int]$TimeoutSeconds=15, [switch]$Json)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Media-Check.ps1')
if ($VideoPath -and $VideoRoot) { throw '只能选择单个视频或视频目录，不能同时指定。' }
if ($VideoPath) { $paths=@($VideoPath) }
else {
    if (-not $VideoRoot) {
        $run=(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CodexBootAnimation -ErrorAction SilentlyContinue).CodexBootAnimation
        if ($run -match '^"([^"]+BootPlayer\.exe)"\s+--supervise\s*$') {
            $VideoRoot=Join-Path (Split-Path -Parent (Split-Path -Parent $Matches[1])) 'videos'
        } else { throw '未找到活动助手目录。请使用 -VideoRoot 指定托盘菜单打开的视频目录。' }
    }
    if (-not (Test-Path -LiteralPath $VideoRoot -PathType Container)) { throw '视频目录不存在；请检查路径，不会自动改查其他安装副本。' }
    $paths=@(foreach ($scene in @('冷启动','新聊天','生气回应','任务完成','闲置互动')) {
        Get-ChildItem -LiteralPath (Join-Path $VideoRoot $scene) -Filter '*.mp4' -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object {$_.FullName}
    })
}
if (-not $Json) {
    Write-Output '片头视频检查（只读，不播放，不修改视频或首帧图）'
    if ($VideoRoot) { Write-Output ('检查目录：'+[IO.Path]::GetFullPath($VideoRoot)) }
    Write-Output '工具缺失或超时会显示“无法确认”；FFmpeg 检查不能替代 Codex 实际预览。'
}
$results=@(foreach ($path in $paths) { Get-BootVideoInspection -VideoPath $path -FFmpegPath $FFmpegPath -FFprobePath $FFprobePath -TimeoutSeconds $TimeoutSeconds })
if ($Json) { ConvertTo-Json -InputObject $results -Depth 5 }
else {
    foreach ($result in $results) { Format-BootVideoInspection $result }
    if (-not $results.Count) { Write-Output '没有找到 MP4；空场景文件夹表示停用，不代表安装失败。' }
    $checks=@($results | ForEach-Object {$_.Checks})
    Write-Output ('检查结束：视频 {0} 个；故障 {1} 项；需检查 {2} 项；无法确认 {3} 项。' -f $results.Count,@($checks|Where-Object Status -eq 'FAIL').Count,@($checks|Where-Object Status -eq 'WARN').Count,@($checks|Where-Object Status -eq 'UNKNOWN').Count)
    Write-Output '报告包含本机路径，请勿直接公开。此入口不会因发现异常删除或改写文件。'
}
