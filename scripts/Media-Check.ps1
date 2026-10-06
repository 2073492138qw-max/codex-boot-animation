#Requires -Version 5.1
# Optional, on-demand checks only. Never loaded by the resident or trigger hooks.
function Resolve-BootMediaTool {
    param([string]$Name, [string]$Path)
    if ($Path) { $command = Get-Command $Path -CommandType Application -ErrorAction SilentlyContinue }
    else {
        # Packaged Codex redirects AppData. UserProfile is shared with Explorer.
        $shared = Join-Path ([Environment]::GetFolderPath('UserProfile')) ('.codex-boot-animation\tools\ffmpeg\bin\' + $Name + '.exe')
        if (Test-Path -LiteralPath $shared -PathType Leaf) { return $shared }
        $local = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('CodexBootAnimation\tools\ffmpeg\bin\' + $Name + '.exe')
        if (Test-Path -LiteralPath $local -PathType Leaf) { return $local }
        $command = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue
    }
    if ($command -and [IO.Path]::GetExtension($command.Source) -ieq '.exe') { return $command.Source }
    return $null
}

function Invoke-BootMediaTool {
    param([string]$Exe, [string[]]$Arguments, [ValidateRange(1,120)][int]$TimeoutSeconds=15)
    # ProcessStartInfo bypasses CMD; quote Windows argv without shell expansion.
    $quoted = @($Arguments | ForEach-Object {
        $escaped = [regex]::Replace($_, '(\\*)"', '$1$1\"')
        $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
        '"' + $escaped + '"'
    })
    $start = New-Object Diagnostics.ProcessStartInfo($Exe, ($quoted -join ' '))
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.StandardOutputEncoding=[Text.Encoding]::UTF8
    $process=$null
    try {
        $process = [Diagnostics.Process]::Start($start)
        $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds*1000)) {
            $process.Kill(); $null=$process.WaitForExit(1000)
            return [pscustomobject]@{ExitCode=$null; Output=''; Reason='timeout'}
        }
        if (-not $stdout.Wait(1000) -or -not $stderr.Wait(1000) -or $stdout.Result.Length -gt 1048576) {
            return [pscustomobject]@{ExitCode=$null; Output=''; Reason='unexpected-output'}
        }
        # Raw errors can include private paths. Callers report fixed reasons only.
        return [pscustomobject]@{ExitCode=$process.ExitCode; Output=$stdout.Result; Reason=''}
    } catch { return [pscustomobject]@{ExitCode=$null; Output=''; Reason='tool-unavailable'} }
    finally { if ($process) { $process.Dispose() } }
}

function Get-BootFrameFingerprint {
    param([string]$Path, [string]$FFmpeg, [int]$TimeoutSeconds=15)
    $result = Invoke-BootMediaTool $FFmpeg @('-nostdin','-hide_banner','-v','error','-protocol_whitelist','file,pipe','-i',$Path,'-map','0:v:0','-frames:v','1','-an','-c:v','rawvideo','-pix_fmt','rgb24','-f','framehash','-hash','sha256','-') $TimeoutSeconds
    if ($result.Reason -or $result.ExitCode -ne 0) { return $null }
    $size = [regex]::Match($result.Output,'(?m)^#dimensions\s+0:\s*(\d+)x(\d+)\s*$')
    $hash = [regex]::Match($result.Output,'(?m)^\s*0,\s*-?\d+,\s*-?\d+,\s*\d+,\s*\d+,\s*([a-fA-F0-9]{64})\s*$')
    if (-not $size.Success -or -not $hash.Success) { return $null }
    return ($size.Groups[1].Value+'x'+$size.Groups[2].Value+':'+$hash.Groups[1].Value.ToLowerInvariant())
}

function Get-BootVideoInspection {
    [CmdletBinding()]
    param([Parameter(Mandatory=$true)][string]$VideoPath, [string]$FFmpegPath, [string]$FFprobePath,
          [ValidateRange(1,120)][int]$TimeoutSeconds=15, [switch]$SkipCover)
    $checks = New-Object 'Collections.Generic.List[object]'
    function Add-MediaCheck($Item,$Status,$Detail,$NextStep='') {
        $checks.Add([pscustomobject]@{Item=$Item;Status=$Status;Detail=$Detail;NextStep=$NextStep})
    }
    $path = [IO.Path]::GetFullPath($VideoPath)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or [IO.Path]::GetExtension($path) -ine '.mp4') {
        Add-MediaCheck '文件' 'FAIL' '没有找到本地 MP4 文件。' '选择实际存在的 .mp4 文件，不要仅修改其他格式的扩展名。'
        return [pscustomobject]@{VideoPath=$path; Checks=$checks.ToArray()}
    }
    $file = Get-Item -LiteralPath $path
    if ($file.Length -eq 0) {
        Add-MediaCheck '文件' 'FAIL' '文件大小为零，不能播放。' '重新导出或复制完整的视频。'
        return [pscustomobject]@{VideoPath=$path; Checks=$checks.ToArray()}
    }
    Add-MediaCheck '文件' 'PASS' '本地 MP4 文件存在且非空。'
    $ffmpeg = Resolve-BootMediaTool 'ffmpeg' $FFmpegPath
    if (-not $FFprobePath -and $ffmpeg) {
        $sibling = Join-Path (Split-Path -Parent $ffmpeg) 'ffprobe.exe'
        if (Test-Path -LiteralPath $sibling -PathType Leaf) { $FFprobePath=$sibling }
    }
    $ffprobe = Resolve-BootMediaTool 'ffprobe' $FFprobePath
    $hasVideo=$null
    if (-not $ffprobe) {
        Add-MediaCheck '媒体与音轨' 'UNKNOWN' '未找到 ffprobe，无法确认视频流和音轨。' '提供 -FFprobePath，或按说明准备可选工具；本检查不会自动安装。'
    } else {
        $probe = Invoke-BootMediaTool $ffprobe @('-v','error','-protocol_whitelist','file,pipe','-show_entries','format=format_name,duration:stream=codec_type,codec_name,width,height,channels','-of','json',$path) $TimeoutSeconds
        if ($probe.Reason) {
            Add-MediaCheck '媒体与音轨' 'UNKNOWN' ('检查工具不可用或超时（'+$probe.Reason+'）。') '检查工具路径和文件占用情况后重试；这不等于视频已经损坏。'
        } elseif ($probe.ExitCode -ne 0) {
            Add-MediaCheck '媒体读取' 'FAIL' 'ffprobe 无法读取此媒体。' '文件可能不完整、格式不支持或无法访问；重新导出后检查。'
        } else {
            try {
                $metadata=$probe.Output | ConvertFrom-Json -ErrorAction Stop
                if ($null -eq $metadata -or $metadata.PSObject.Properties.Name -notcontains 'streams') { throw 'Missing streams.' }
                $videos=@($metadata.streams | Where-Object {$_.codec_type -eq 'video'})
                $audio=@($metadata.streams | Where-Object {$_.codec_type -eq 'audio'})
                $hasVideo=$videos.Count -gt 0
                if (-not $hasVideo) { Add-MediaCheck '视频流' 'FAIL' '文件没有视频流，不能作为动画播放。' '导出包含画面的视频。' }
                else {
                    Add-MediaCheck '视频流' 'PASS' ('视频编码：'+$videos[0].codec_name+'；尺寸：'+$videos[0].width+'×'+$videos[0].height+'。')
                    if ($videos[0].codec_name -ne 'h264') { Add-MediaCheck 'Windows 兼容性' 'WARN' '视频不是常见的 H.264 编码。' 'FFmpeg 解码成功也不保证 Windows 播放器支持；请实际预览，必要时另存 H.264/AAC MP4。' }
                }
                if ($audio.Count) { Add-MediaCheck '音轨' 'PASS' ('存在音轨，编码：'+$audio[0].codec_name+'。有音轨不代表实际有声。') }
                else { Add-MediaCheck '音轨' 'WARN' '没有音轨，此视频本身没有声音。' '若需要声音，请重新导出带音轨的版本；调高播放器音量不能补出音轨。' }
                if ($metadata.format.format_name -notmatch '(^|,)mp4(,|$)') { Add-MediaCheck '容器格式' 'WARN' '检测到的容器不是 MP4。' '不要只改扩展名；需要时另存为真正的 MP4 文件。' }
            } catch { Add-MediaCheck '媒体与音轨' 'UNKNOWN' '工具输出不是可识别的媒体信息。' '核对 ffprobe 的版本和路径后重试。' }
        }
    }
    if ($hasVideo -eq $false) {
        Add-MediaCheck '解码' 'INFO' '没有视频流，已跳过解码和首帧检查。'
    } elseif (-not $ffmpeg) {
        Add-MediaCheck '解码' 'UNKNOWN' '未找到 FFmpeg，无法确认媒体能否解码。' '提供 -FFmpegPath，或按说明准备可选工具。'
    } else {
        $decode = Invoke-BootMediaTool $ffmpeg @('-nostdin','-hide_banner','-v','error','-xerror','-err_detect','explode','-protocol_whitelist','file,pipe','-i',$path,'-map','0:v:0','-map','0:a:0?','-f','null','-') $TimeoutSeconds
        if ($decode.Reason) { Add-MediaCheck '解码' 'UNKNOWN' ('解码未确认（'+$decode.Reason+'）。') '长视频可能超过检查时限；可增加 -TimeoutSeconds，或实际预览，不要直接当作损坏。' }
        elseif ($decode.ExitCode -ne 0) { Add-MediaCheck '解码' 'FAIL' 'FFmpeg 不能完整解码此视频或第一条音轨。' '检查文件是否完整、编码是否支持；不要覆盖原文件，可另存后再检查。' }
        else { Add-MediaCheck '解码' 'PASS' 'FFmpeg 已完整解码视频和可用的第一条音轨；仍需实际预览确认 Windows 播放效果。' }
    }
    if ($SkipCover) { Add-MediaCheck '首帧图' 'INFO' '本次导入前检查不校验源目录首帧图，成功复制后再生成新图。' }
    elseif ($hasVideo -ne $false) {
        $cover = Join-Path $file.DirectoryName ($file.BaseName+'-first.png')
        if (-not (Test-Path -LiteralPath $cover -PathType Leaf)) { Add-MediaCheck '首帧图' 'WARN' '缺少对应的首帧图；播放会使用黑色过渡。' '通过导入工具生成首帧图，或提供同名的“视频名-first.png”；本检查不会修改文件。' }
        elseif (-not $ffmpeg) { Add-MediaCheck '首帧图' 'UNKNOWN' '首帧图存在，但缺少 FFmpeg，无法确认内容是否匹配。' '准备可选工具后重新检查；仅同名不能证明匹配。' }
        else {
            $videoFrame=Get-BootFrameFingerprint $path $ffmpeg $TimeoutSeconds
            $coverFrame=Get-BootFrameFingerprint $cover $ffmpeg $TimeoutSeconds
            if (-not $videoFrame -or -not $coverFrame) { Add-MediaCheck '首帧图' 'UNKNOWN' '无法读取视频首帧或首帧图，未确认一致性。' '检查图片和视频格式；不要把未知结果当作已匹配。' }
            elseif ($videoFrame -ceq $coverFrame) { Add-MediaCheck '首帧图' 'PASS' '首帧图的尺寸和解码像素与视频第一帧一致。' }
            else { Add-MediaCheck '首帧图' 'WARN' '首帧图与当前视频第一帧不一致，可能是旧图或经过修改。' '先保留旧图，重新生成或换名导入；本检查不自动覆盖。' }
        }
    }
    $currentFile=Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
    if (-not $currentFile -or $file.Length -ne $currentFile.Length -or $file.LastWriteTimeUtc -ne $currentFile.LastWriteTimeUtc) {
        Add-MediaCheck '检查期间变化' 'UNKNOWN' '视频在检查期间发生变化，以上结果不能代表当前文件。' '等复制或替换完成后重新检查。'
    }
    return [pscustomobject]@{VideoPath=$path; Checks=$checks.ToArray()}
}

function Format-BootVideoInspection {
    param([object]$Inspection)
    $names=@{PASS='通过';WARN='需检查';FAIL='故障';UNKNOWN='无法确认';INFO='说明'}
    Write-Output ('视频：'+$Inspection.VideoPath)
    foreach ($check in $Inspection.Checks) {
        Write-Output ('[{0} {1}] {2}：{3}' -f $names[$check.Status],$check.Status,$check.Item,$check.Detail)
        if ($check.NextStep) { Write-Output ('  下一步：'+$check.NextStep) }
    }
    Write-Output ''
}
