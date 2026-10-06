#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$VideoPath,
    [string]$FFmpegPath = 'ffmpeg'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$mediaDir = Join-Path $repo 'plugins\codex-boot-animation\videos\新聊天'
$destination = Join-Path $mediaDir '新聊天-线框全息兔耳.mp4'
$firstFrame = Join-Path $mediaDir '新聊天-线框全息兔耳-first.png'
$source = (Resolve-Path -LiteralPath $VideoPath).Path
$ffmpeg = Get-Command $FFmpegPath -ErrorAction SilentlyContinue
if ($null -eq $ffmpeg) {
    throw 'FFmpeg was not found. Install it or pass -FFmpegPath with its executable path.'
}
$staging = Join-Path $repo 'build\video-staging'
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$stagedVideo = Join-Path $staging '新聊天-线框全息兔耳.mp4'
$stagedFrame = Join-Path $staging '新聊天-线框全息兔耳-first.png'
Copy-Item -LiteralPath $source -Destination $stagedVideo -Force
& $ffmpeg.Source -hide_banner -loglevel error -y -i $stagedVideo -frames:v 1 -update 1 $stagedFrame
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $stagedFrame)) {
    throw 'FFmpeg could not extract the first frame. Check FFmpegPath and the video.'
}
Copy-Item -LiteralPath $stagedVideo -Destination $destination -Force
Copy-Item -LiteralPath $stagedFrame -Destination $firstFrame -Force
Write-Output "Video: $destination"
Write-Output "First-frame cover: $firstFrame"
