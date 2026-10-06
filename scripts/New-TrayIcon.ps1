#Requires -Version 5.1
[CmdletBinding()]
param([string]$SourcePath, [string]$OutputPath)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $SourcePath) { $SourcePath = Join-Path $repo 'assets\tray-icon-source.png' }
if (-not $OutputPath) { $OutputPath = Join-Path $repo 'assets\tray-icon.ico' }
$source = (Resolve-Path -LiteralPath $SourcePath).Path
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw 'Output already exists; choose a new path. No file was overwritten.' }
if (-not (Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($output)) -PathType Container)) {
    throw 'The output directory must already exist.'
}

# Deterministic format conversion only: preserve the complete square artwork
# and transparency. No AI redraw, crop, external tool, or runtime dependency.
Add-Type -AssemblyName System.Drawing
$image = [Drawing.Image]::FromFile($source)
try {
    if ($image.Width -ne $image.Height -or $image.Width -gt 8192) {
        throw 'Expected square artwork no larger than 8192 pixels.'
    }
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $frames = @()
    foreach ($size in $sizes) {
        $bitmap = New-Object Drawing.Bitmap($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $attributes = New-Object Drawing.Imaging.ImageAttributes
                try {
                    $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
                    $rectangle = New-Object Drawing.Rectangle(0, 0, $size, $size)
                    $graphics.DrawImage($image, $rectangle, 0, 0, $image.Width, $image.Height, [Drawing.GraphicsUnit]::Pixel, $attributes)
                } finally { $attributes.Dispose() }
            } finally { $graphics.Dispose() }
            $frame = New-Object IO.MemoryStream
            try {
                $bitmap.Save($frame, [Drawing.Imaging.ImageFormat]::Png)
                $frames += ,$frame.ToArray()
            } finally { $frame.Dispose() }
        } finally { $bitmap.Dispose() }
    }

    $container = New-Object IO.MemoryStream
    try {
        $writer = New-Object IO.BinaryWriter($container)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$sizes.Count)
            $offset = 6 + 16 * $sizes.Count
            for ($index = 0; $index -lt $sizes.Count; $index++) {
                $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
                $writer.Write([byte]$dimension)
                $writer.Write([byte]$dimension)
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([uint16]1)
                $writer.Write([uint16]32)
                $writer.Write([uint32]$frames[$index].Length)
                $writer.Write([uint32]$offset)
                $offset += $frames[$index].Length
            }
            foreach ($frameBytes in $frames) { $writer.Write([byte[]]$frameBytes) }
            $writer.Flush()
            $bytes = $container.ToArray()
        } finally { $writer.Dispose() }
    } finally { $container.Dispose() }

    # CreateNew protects existing artwork even if another process creates the
    # destination after the initial check. Finish conversion before touching it.
    $file = New-Object IO.FileStream($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $file.Write($bytes, 0, $bytes.Length); $file.Flush($true) }
    finally { $file.Dispose() }
    Write-Output ('Created tray icon: ' + $output)
    Write-Output ('Sizes: ' + ($sizes -join ', '))
} finally { $image.Dispose() }
