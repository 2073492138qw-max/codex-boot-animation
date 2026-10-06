#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $VideoRoot,
    [Parameter(Mandatory = $true)][string] $OutputRoot,
    [Parameter(Mandatory = $true)][string] $FfmpegPath,
    [string] $ReferenceVideo
)

$ErrorActionPreference = 'Stop'
$videoRootPath = [IO.Path]::GetFullPath($VideoRoot).TrimEnd('\')
$outputRootPath = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\')
if (-not (Test-Path -LiteralPath $videoRootPath -PathType Container)) { throw "Video root not found: $videoRootPath" }
if (-not (Test-Path -LiteralPath $FfmpegPath -PathType Leaf)) { throw "FFmpeg not found: $FfmpegPath" }
if ($outputRootPath.Equals($videoRootPath, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRootPath.StartsWith($videoRootPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputRoot must be outside VideoRoot; this script never edits the originals.'
}

function Measure-Audio([string] $path) {
    $output = & $FfmpegPath -hide_banner -nostats -i $path -vn -af 'loudnorm=I=-12:TP=-1:LRA=7:print_format=json' -f null NUL 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Could not measure audio: $path`n$output" }
    $match = [regex]::Match($output, '(?s)\{\s*"input_i"\s*:.*?\}')
    if (-not $match.Success) { throw "No loudness result for: $path" }
    return $match.Value | ConvertFrom-Json
}

$completionFolder = Join-Path $videoRootPath '任务完成'
if ($ReferenceVideo) {
    $reference = [IO.Path]::GetFullPath($ReferenceVideo)
} else {
    $choices = @(Get-ChildItem -LiteralPath $completionFolder -Filter '*.mp4' -File -ErrorAction Stop)
    if ($choices.Count -ne 1) { throw 'Put exactly one MP4 in 任务完成, or pass -ReferenceVideo.' }
    $reference = $choices[0].FullName
}
if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) { throw "Reference video not found: $reference" }
$target = [double]::Parse((Measure-Audio $reference).input_i, [Globalization.CultureInfo]::InvariantCulture)
$filter = 'loudnorm=I={0}:TP=-1.5:LRA=7' -f $target.ToString([Globalization.CultureInfo]::InvariantCulture)
$scenes = @('冷启动', '新聊天', '生气回应', '任务完成', '闲置互动', '未启用素材')
$videos = foreach ($scene in $scenes) {
    $folder = Join-Path $videoRootPath $scene
    if (Test-Path -LiteralPath $folder -PathType Container) {
        Get-ChildItem -LiteralPath $folder -Filter '*.mp4' -File | ForEach-Object { [pscustomobject]@{ Scene = $scene; File = $_ } }
    }
}
foreach ($video in $videos) {
    $original = $video.File.FullName
    $before = [double]::Parse((Measure-Audio $original).input_i, [Globalization.CultureInfo]::InvariantCulture)
    if ($original.Equals($reference, [StringComparison]::OrdinalIgnoreCase) -or [Math]::Abs($before - $target) -le 1.0) {
        [pscustomobject]@{ Scene = $video.Scene; File = $video.File.Name; BeforeLUFS = $before; AfterLUFS = $before; Action = 'unchanged' }
        continue
    }
    $destinationFolder = Join-Path $outputRootPath $video.Scene
    New-Item -ItemType Directory -Path $destinationFolder -Force | Out-Null
    $destination = Join-Path $destinationFolder $video.File.Name
    foreach ($audioFilter in @($filter, ('acompressor=threshold=-24dB:ratio=4:attack=5:release=80,' + $filter))) {
        & $FfmpegPath -hide_banner -loglevel error -y -i $original -map 0:v:0 -map 0:a:0 -map_metadata 0 -c:v copy -c:a aac -b:a 192k -af $audioFilter -movflags +faststart $destination
        if ($LASTEXITCODE -ne 0) { throw "Could not normalize audio: $original" }
        $measurement = Measure-Audio $destination
        $after = [double]::Parse($measurement.input_i, [Globalization.CultureInfo]::InvariantCulture)
        $peak = [double]::Parse($measurement.input_tp, [Globalization.CultureInfo]::InvariantCulture)
        if ([Math]::Abs($after - $target) -le 1.5 -and $peak -le 0) { break }
    }
    if ([Math]::Abs($after - $target) -gt 2.5 -or $peak -gt 0) {
        throw "Loudness verification failed for $original (LUFS=$after, peak=$peak). Originals remain untouched."
    }
    [pscustomobject]@{ Scene = $video.Scene; File = $video.File.Name; BeforeLUFS = $before; AfterLUFS = $after; Action = 'staged' }
}
