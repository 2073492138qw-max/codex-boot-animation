#Requires -Version 5.1
# One media preflight shared by Test, Install and Package so the three entry
# points cannot disagree. Video is required. A missing first-frame cover is a
# supported runtime condition (black transition), so it is a warning, not an
# error. Loading this file does not change any settings.
function Get-MediaPreflight {
    param([Parameter(Mandatory=$true)][string]$VideosRoot)
    if (-not (Test-Path -LiteralPath $VideosRoot -PathType Container)) { throw "Media root is missing: $VideosRoot" }
    $root = (Resolve-Path -LiteralPath $VideosRoot).Path
    $catalogPath = Join-Path $root 'media-catalog.json'
    if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) { throw "Media catalog is missing: $catalogPath" }
    $catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $present = @()
    $missingVideos = @()
    $missingCovers = @()
    foreach ($relative in @($catalog.files)) {
        $text = [string]$relative
        if ([string]::IsNullOrWhiteSpace($text) -or $text -match '(^[\\/]|\.\.|:)' -or $text -notmatch '\.mp4$') {
            throw "Unsafe media catalog path: $text"
        }
        $media = Join-Path $root $text
        if (-not (Test-Path -LiteralPath $media -PathType Leaf)) { $missingVideos += $text; continue }
        $present += $text
        # Same rule as MediaLibrary.FirstFrame: keep a trailing dot in the stem.
        $cover = Join-Path (Split-Path -Parent $media) ([IO.Path]::GetFileNameWithoutExtension($media) + '-first.png')
        if (-not (Test-Path -LiteralPath $cover -PathType Leaf)) { $missingCovers += $text }
    }
    return [pscustomobject]@{
        VideosRoot = $root
        Videos = $present
        MissingVideos = $missingVideos
        MissingCovers = $missingCovers
    }
}
function Assert-MediaPreflight {
    param([Parameter(Mandatory=$true)][string]$VideosRoot)
    $result = Get-MediaPreflight -VideosRoot $VideosRoot
    if ($result.MissingVideos.Count -gt 0) {
        throw ("Required release media is missing (video): " + ($result.MissingVideos -join ', '))
    }
    foreach ($relative in $result.MissingCovers) {
        Write-Warning "Optional first-frame cover is absent (black transition): $relative"
    }
    return $result
}
