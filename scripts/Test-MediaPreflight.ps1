#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Media-Preflight.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('cba-media-preflight-' + [guid]::NewGuid().ToString('N'))
function New-PreflightCase {
    param([string]$Name, [string[]]$Catalog, [string[]]$Present, [string[]]$Covers)
    $root = Join-Path $fixture $Name
    $null = New-Item -ItemType Directory -Path $root -Force
    foreach ($relative in $Present) {
        $media = Join-Path $root $relative
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $media) -Force
        [IO.File]::WriteAllBytes($media, [byte[]](1, 2, 3, 4))
    }
    foreach ($relative in $Covers) {
        $media = Join-Path $root $relative
        $cover = [IO.Path]::ChangeExtension($media, $null).TrimEnd('.') + '-first.png'
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $cover) -Force
        [IO.File]::WriteAllBytes($cover, [byte[]](1))
    }
    $json = '{"files":["' + (@($Catalog) -join '","') + '"]}'
    [IO.File]::WriteAllText((Join-Path $root 'media-catalog.json'), $json, (New-Object Text.UTF8Encoding($false)))
    return $root
}
try {
    # The four combinations must agree across Test, Install and Package, which
    # all call Assert-MediaPreflight. A missing cover is a supported runtime
    # condition; a missing video is fatal and names the actual missing item.
    $both = New-PreflightCase -Name 'video and cover 中文' -Catalog @('新聊天/有封面.mp4') -Present @('新聊天/有封面.mp4') -Covers @('新聊天/有封面.mp4')
    $result = Assert-MediaPreflight -VideosRoot $both
    if ($result.MissingVideos.Count -ne 0 -or $result.MissingCovers.Count -ne 0 -or $result.Videos.Count -ne 1) { throw 'video-and-cover case disagreed.' }
    Write-Output 'PASS media-preflight-video-and-cover'

    # The catalog validator rejects any '..' occurrence, so a stem ending in a
    # dot cannot reach the cover rule at all. Keep that documented as a case.
    $dotted = New-PreflightCase -Name 'double dot name' -Catalog @('冷启动/版本1..mp4') -Present @('冷启动/版本1..mp4') -Covers @()
    $rejected = $false
    try { $null = Assert-MediaPreflight -VideosRoot $dotted 3>$null } catch { $rejected = $_.Exception.Message -like '*Unsafe media catalog path*' }
    if (-not $rejected) { throw 'A catalog name containing .. was accepted.' }
    Write-Output 'PASS media-preflight-rejects-double-dot-names'

    $noCover = New-PreflightCase -Name 'video no cover 空格' -Catalog @('任务完成/无封面 视频.mp4') -Present @('任务完成/无封面 视频.mp4') -Covers @()
    $result = Assert-MediaPreflight -VideosRoot $noCover 3>$null
    if ($result.MissingVideos.Count -ne 0 -or $result.MissingCovers.Count -ne 1) { throw 'video-without-cover case disagreed.' }
    Write-Output 'PASS media-preflight-video-without-cover-warns'

    $missing = New-PreflightCase -Name 'missing video no cover' -Catalog @('冷启动/缺失.mp4') -Present @() -Covers @()
    $message = ''
    try { $null = Assert-MediaPreflight -VideosRoot $missing 3>$null } catch { $message = $_.Exception.Message }
    if ($message -notmatch '\(video\)' -or $message -notmatch '缺失\.mp4') { throw "missing-video error did not name the video: $message" }
    Write-Output 'PASS media-preflight-missing-video-is-fatal'

    $coverOnly = New-PreflightCase -Name 'missing video with cover' -Catalog @('冷启动/只有封面.mp4') -Present @() -Covers @('冷启动/只有封面.mp4')
    $message = ''
    try { $null = Assert-MediaPreflight -VideosRoot $coverOnly 3>$null } catch { $message = $_.Exception.Message }
    if ($message -notmatch '\(video\)' -or $message -match 'cover') { throw "cover-only case misreported the missing item: $message" }
    Write-Output 'PASS media-preflight-cover-only-still-fails-on-video'

    $unsafe = New-PreflightCase -Name 'unsafe path' -Catalog @('../escape.mp4') -Present @() -Covers @()
    $rejected = $false
    try { $null = Assert-MediaPreflight -VideosRoot $unsafe } catch { $rejected = $_.Exception.Message -like '*Unsafe media catalog path*' }
    if (-not $rejected) { throw 'Unsafe catalog path was accepted.' }
    Write-Output 'PASS media-preflight-rejects-unsafe-path'
} finally {
    if ((Test-Path -LiteralPath $fixture) -and (Split-Path -Leaf $fixture) -like 'cba-media-preflight-*') { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
