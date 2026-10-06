#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$VideoPath,
    [ValidateSet('冷启动', '新聊天', '生气回应', '任务完成', '闲置互动')]
    [string]$Scene = '新聊天',
    [string]$Name,
    [string]$FFmpegPath,
    [string]$FFprobePath,
    [string]$VideoRoot,
    [ValidateRange(1,120)][int]$TimeoutSeconds=15
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Media-Check.ps1')
if (-not $VideoRoot) { $VideoRoot=Join-Path $repo 'plugins\codex-boot-animation\videos' }
$folder = Join-Path ([IO.Path]::GetFullPath($VideoRoot)) $Scene
$source = (Resolve-Path -LiteralPath $VideoPath).Path
if (-not $Name) { $Name = [IO.Path]::GetFileName($source) }
if ([IO.Path]::GetFileName($Name) -ne $Name -or [IO.Path]::GetExtension($Name) -ine '.mp4') {
    throw '名称必须是单独的 .mp4 文件名，不能含目录路径。'
}
$destination = Join-Path $folder $Name
if (Test-Path -LiteralPath $destination) { throw "已有同名视频，不会覆盖：$destination" }
$firstFrame = Join-Path $folder ([IO.Path]::GetFileNameWithoutExtension($Name) + '-first.png')
if (Test-Path -LiteralPath $firstFrame) { throw "已有同名首帧图，不会覆盖；请换个视频名称或先自行管理旧图：$firstFrame" }
$inspection=Get-BootVideoInspection -VideoPath $source -FFmpegPath $FFmpegPath -FFprobePath $FFprobePath -TimeoutSeconds $TimeoutSeconds -SkipCover
Format-BootVideoInspection $inspection
if (@($inspection.Checks | Where-Object {$_.Status -eq 'FAIL' -or $_.Item -eq '检查期间变化'}).Count) { throw '视频检查未通过，尚未复制任何文件。' }
if (@($inspection.Checks | Where-Object Status -eq 'UNKNOWN').Count) { Write-Warning '存在无法确认的检查项；本次仍按原有行为导入，不能认为已验证可播放。' }
New-Item -ItemType Directory -Path $folder -Force | Out-Null
[IO.File]::Copy($source,$destination,$false)

$ffmpeg = Resolve-BootMediaTool 'ffmpeg' $FFmpegPath
if ($null -ne $ffmpeg) {
    $frameResult=Invoke-BootMediaTool $ffmpeg @('-nostdin','-hide_banner','-loglevel','error','-n','-protocol_whitelist','file,pipe','-i',$destination,'-map','0:v:0','-frames:v','1','-update','1',$firstFrame) $TimeoutSeconds
    if ($frameResult.Reason -or $frameResult.ExitCode -ne 0) { Write-Warning '视频已导入，但首帧图生成失败或超时；可能使用黑色过渡，原视频未改。请运行 check-videos.cmd 检查。' }
    else { Write-Output '已生成当前视频的首帧图；导入没有修改原视频。' }
} else {
    Write-Warning '未找到 FFmpeg，暂时使用黑色过渡；补充匹配的“视频名-first.png”后可重新检查。'
}
Write-Output "已导入：$destination"
Write-Output '助手会自动发现新 MP4；场景内一个视频固定播放，多个视频随机。项目素材目录不是运行安装目录，请核对上面的目标路径。'
