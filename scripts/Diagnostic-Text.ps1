#Requires -Version 5.1
# Presentation only: do not mutate rows, query state, or change JSON contracts.
function Format-BootDiagnostics {
    [CmdletBinding()]
    param([AllowEmptyCollection()][object[]]$Checks)
    $statusNames = @{PASS='通过'; WARN='需检查'; FAIL='故障'; UNKNOWN='无法确认'; PENDING='待实测'; INFO='说明'}
    $itemNames = @{
        '.NET'='基础运行环境（.NET）'; 'WPF'='视频窗口运行环境（WPF）';
        'Codex desktop'='Codex 桌面版本'; 'Codex CLI'='Codex 命令行版本';
        'Plugin'='插件安装与启用'; 'Multiple installs'='重复安装'; 'Codex CLI / plugin'='命令行与插件状态';
        'Hook: sessionStart'='会话启动钩子'; 'Hook: userPromptSubmit'='消息提交钩子'; 'Hook: stop'='任务结束钩子';
        'Hook trust'='钩子信任'; 'Sign-in startup'='登录自启动登记'; 'Active videos folder'='当前视频目录';
        'Windows Startup Apps'='Windows 自启动实测'; 'Hook cache root'='钩子缓存目录';
        'Cached vs active EXE'='缓存与运行程序一致性'; 'Helper --supervise'='助手守护进程';
        'Helper --watch'='触发监控进程'; 'Helper --resident'='常驻播放器'; 'Helper IPC'='助手通信';
        'Other helper copies'='其他目录的助手'; 'Helper processes'='助手进程状态';
        'Package vs active EXE'='项目包与运行程序一致性'; 'Tray setting: paused'='托盘设置：暂停全部自动动画';
        'Tray setting: idle-disabled'='托盘设置：停用闲置互动'; 'Recent local event'='最近触发记录';
        'Logged completion playback'='最近任务完成播放记录'; 'Runtime log'='本机运行日志'; 'Acceptance'='实际使用验收'
    }
    $facts = @{
        '.NET Framework 4.8 or later'='.NET Framework 4.8 或更新版本已安装';
        'Required runtime is missing'='缺少所需运行环境'; 'Native WPF runtime is present'='视频窗口运行环境已安装';
        'WPF runtime is missing'='缺少视频窗口运行环境';
        'The supported packaged desktop app was not found'='没有找到支持的 Windows 商店版 Codex';
        'Package identity could not be queried'='无法查询桌面应用包信息';
        'No installed plugin was found'='没有找到已安装的片头插件'; 'More than one copy is installed'='发现多份插件安装';
        'Compatible CLI or plugin state could not be read'='无法读取兼容的命令行或插件状态';
        'No active hook was discovered for this directory'='没有发现对此目录生效的钩子';
        'This CLI could not query hook state'='当前命令行无法查询钩子状态';
        'Startup points to a missing executable'='自启动指向的程序文件不存在';
        'An unexpected startup command is registered'='自启动登记了非预期命令';
        'No current-user startup entry is registered'='当前用户没有登记助手自启动';
        'The Run entry is registered; Windows enablement/sign-in execution still needs acceptance'='已登记自启动；Windows 是否允许及登录后能否启动，仍需实际验收';
        'Cached hook fallback and active helper have different binaries'='缓存中的备用程序与正在使用的助手版本不一致';
        'Resident pipe responded without requesting playback'='常驻播放器通信正常；本检查没有请求播放';
        'Helper readiness/communication failed'='助手未就绪或通信失败';
        'Helpers from another directory are running'='另一个目录的助手也在运行';
        'Windows process state could not be queried'='无法查询 Windows 进程状态';
        'This package and the active helper have different binaries'='本项目包与正在使用的助手程序不一致';
        'Executable hashes match'='程序文件哈希一致'; 'This option is disabled/paused'='已暂停全部动画，或已停用此项互动';
        'No delivered completion player was found in the recent log'='最近日志未找到已送达的任务完成播放器记录';
        'No runtime log exists yet'='尚未生成运行日志';
        'Independent PC/reboot, audible sound and all real trigger scenarios are not established by this check'='本诊断不能证明其他电脑、真实重启、扬声器声音及所有场景均已通过'
    }
    $steps = @{
        'Install .NET Framework 4.8 with Windows Update, then retry.'='通过 Windows 更新安装 .NET Framework 4.8，然后重试。';
        'Repair/install .NET Framework 4.8 before retrying.'='修复或安装 .NET Framework 4.8 后重试。';
        'Install/open the packaged Windows Codex desktop app.'='安装并打开 Windows 商店版 Codex 桌面应用。';
        'Confirm that the packaged Windows Codex app is installed.'='确认已安装 Windows 商店版 Codex。';
        'Fully extract the package and run install.cmd from Explorer.'='先完整解压项目包，再从资源管理器双击 install.cmd。';
        'If disabled, enable this plugin in Codex Plugins.'='在 Codex 的插件设置中启用本插件。';
        'Identify the active copy before updating; avoid running two helpers.'='先确认正在使用的安装目录，避免两份助手同时运行。';
        'Open/update Codex desktop, then run check.cmd again.'='打开或更新 Codex 桌面应用，再运行 check.cmd。';
        'Check plugin enablement and project/enterprise hook policy in Codex.'='检查插件是否启用，以及 Codex 的项目或企业钩子策略。';
        'Review the current hook definition in Codex (/hooks in the CLI).'='在 Codex 中核对当前钩子定义与信任状态；命令行可用 /hooks。';
        'Review hooks in Codex; installing/enabling a plugin does not automatically trust them.'='在 Codex 中检查钩子；安装或启用插件不等于已经信任钩子。';
        'Restore the original folder or deliberately migrate the installation.'='恢复原安装目录，或按迁移说明处理；不要直接删视频。';
        'Review the old installation before changing it.'='先核对旧安装和自启动命令，不要直接覆盖。';
        'Run install.cmd from Explorer, not from the Codex terminal.'='从资源管理器双击 install.cmd，不要在 Codex 内的终端安装。';
        'Keep this installation folder in place. Also check Windows Startup Apps is enabled.'='保留当前安装目录，并确认 Windows 自启动设置允许此助手运行。';
        'Use this folder or the helper tray menu to change the active videos.'='把视频放在此目录对应的场景文件夹，也可从助手托盘菜单打开目录。';
        'Check Windows Settings > Apps > Startup. A real reboot is a separate manual test.'='查看 Windows 设置 → 应用 → 启动。真实重启需单独手测；这不是判定已验收失败。';
        'Hook caches and the active media folder are separate. Edit active videos through the tray menu.'='钩子缓存不是实际视频目录；请从助手托盘打开视频目录，不要改缓存。';
        'Online hooks route to the active helper. A reviewed plugin reinstall is needed to refresh cached fallback code; do not edit trust records.'='运行中的钩子转交当前助手；刷新备用缓存需评估后重装插件，不要手改信任记录。';
        'Retry installation from the active folder; check security software notices and the local log if startup fails.'='先检查本机日志和安全软件提示；需要重装时使用当前安装目录。';
        'Check the local log; close an obsolete helper through its tray before retrying.'='检查本机日志；若有旧助手，从旧助手托盘退出后重试。';
        'Identify and close the obsolete copy through its tray; do not delete its videos.'='确认旧助手位置，从它的托盘退出；不要删除它的视频。';
        'Check the helper tray and Windows Task Manager.'='查看助手托盘和 Windows 任务管理器，确认进程是否运行。';
        'Use Update-Companion.ps1 with the active plugin root for an intentional update.'='确需更新时，按更新说明将 Update-Companion.ps1 指向当前安装目录。';
        'An empty folder disables this scene. Missing covers use a black transition; decoding/audio requires actual playback.'='空目录会停用该场景；缺少首帧图会使用黑色过渡，不代表无法播放。画面和声音需实际播放验证。';
        'Change it through the helper tray menu if you want automatic playback.'='如需恢复自动播放，请在助手托盘菜单取消对应暂停或停用设置。';
        'Recent events may include tests. Check an actual trigger; a suppressed event is not automatically a fault.'='记录可能含测试；请核对真实触发。被去重或抑制不一定是故障。';
        'This is evidence for the logged record, not a new trigger test or proof of audible speakers.'='仅说明这次日志记录，不是新的触发测试；有音轨不等于扬声器实际有声。';
        'Finish a real task in foreground Codex, then run check.cmd. A tray preview does not test task-end detection.'='在前台 Codex 完成一个真实任务后再诊断；托盘预览不能证明任务结束检测正常。';
        'Keep this file private. Public issues should contain only necessary redacted facts.'='此日志仅供本机排查；公开问题只提供必要的脱敏摘要，不上传原日志。';
        'Install/start the helper, perform an actual trigger, then check again.'='正常启动助手并实际触发一次，再运行诊断。';
        'Use docs/INSTALLATION.md and docs/PORTABILITY-ISSUES.md for real acceptance.'='按 docs/INSTALLATION.md 和 docs/PORTABILITY-ISSUES.md 做真实验收；本报告不会自动更新历史验收。'
    }
    $trustNames = @{trusted='已信任'; managed='由策略管理'; untrusted='尚未信任'; modified='定义已变更'}
    $closeNames = @{ended='正常播完'; 'skip-button'='点击跳过'; 'codex-not-foreground'='Codex 不在前台'; timeout='超时'; 'media-failed'='媒体播放失败'; 'owner-gone'='所属窗口已关闭'; 'not observed in recent log'='最近日志未见关闭记录'}
    $eventNames = @{
        'new-dialog-click-detected'='检测到新聊天点击'; 'new-dialog-watch-unavailable'='新聊天监控暂不可用';
        'new-dialog-click-targets-failed'='新聊天按钮位置探测失败'; 'completion-journal-unavailable'='任务完成日志暂不可读';
        'completion-deferred'='完成通知被推迟'; 'completion-suppressed'='完成通知被去重或抑制';
        'idle-policy-state'='闲置策略状态'; 'idle-return-accepted'='接受闲置返回触发'; 'media-failed'='媒体播放失败'
    }
    Write-Output 'Codex 片头助手运行诊断'
    Write-Output '本检查不会修改安装、信任、自启动或视频，也不会播放动画。'
    if (-not $Checks.Count) { Write-Output '未收到检查结果，不能判断是否正常。' }
    else {
        $counts = @{}
        foreach ($status in $statusNames.Keys) { $counts[$status] = @($Checks | Where-Object { $_.Status -eq $status }).Count }
        $unknown = @($Checks | Where-Object { -not $statusNames.ContainsKey([string]$_.Status) }).Count
        Write-Output ('汇总：通过 {0} 项；需检查 {1} 项；故障 {2} 项；无法确认 {3} 项；待实测 {4} 项；说明 {5} 项；未识别 {6} 项。' -f $counts.PASS,$counts.WARN,$counts.FAIL,$counts.UNKNOWN,$counts.PENDING,$counts.INFO,$unknown)
    }
    Write-Output '先处理“故障”，再查看“需检查”和“无法确认”；“待实测”不等于失败。'
    Write-Output ''
    foreach ($check in $Checks) {
        $status = [string]$check.Status
        $label = if ($statusNames.ContainsKey($status)) { $statusNames[$status] } else { '未识别' }
        $item = [string]$check.Item
        if ($itemNames.ContainsKey($item)) { $item = $itemNames[$item] }
        elseif ($item -like 'Scene: *') { $item = '场景视频：' + $item.Substring(7) }
        $detail = [string]$check.Detail
        if ($facts.ContainsKey($detail)) { $detail = $facts[$detail] }
        elseif ($check.Item -like 'Helper --*' -and $detail -match '^Running processes: (\d+)$') { $detail = '正在运行：' + $Matches[1] + ' 个进程' }
        elseif ($check.Item -like 'Hook: *' -and $detail -match '^(.*?) / enabled=(True|False) / trust=(\S+)$') {
            $pluginId=$Matches[1]; $enabled=$Matches[2]; $trust=$Matches[3]
            $trustLabel = if ($trustNames.ContainsKey($trust)) { $trustNames[$trust] } else { $trust }
            $detail = $pluginId + ' / 已启用：' + $(if ($enabled -eq 'True') {'是'} else {'否'}) + ' / 信任：' + $trustLabel
        }
        elseif ($check.Item -like 'Scene: *' -and $detail -match '^MP4=(\d+); covers=(\d+)$') { $detail = '视频 ' + $Matches[1] + ' 个；首帧图 ' + $Matches[2] + ' 个' }
        elseif ($check.Item -eq 'Logged completion playback' -and $detail -match '^MovingPicture=(True|False); AudioTrack=(True|False); Close=(.*)$') {
            $moving=$Matches[1]; $audio=$Matches[2]; $reason=$Matches[3]
            $reasonLabel = if ($closeNames.ContainsKey($reason)) {$closeNames[$reason]} else {$reason}
            $detail = '动态画面：' + $(if ($moving -eq 'True') {'有记录'} else {'未见记录'}) + '；音轨：' + $(if ($audio -eq 'True') {'有记录'} else {'未见记录'}) + '；结束：' + $reasonLabel
        }
        elseif ($check.Item -eq 'Recent local event' -and $detail -match '^\S+ ([a-z-]+)') {
            $eventCode=$Matches[1]
            if ($eventNames.ContainsKey($eventCode)) { $detail = $eventNames[$eventCode] + '（' + $detail + '）' }
        }
        Write-Output ('[{0} {1}] {2}：{3}' -f $label,$status,$item,$detail)
        if ($check.NextStep) {
            $step = [string]$check.NextStep
            if ($steps.ContainsKey($step)) { $step = $steps[$step] }
            Write-Output ('  下一步：' + $step)
        }
    }
    Write-Output ''
    Write-Output '诊断结束。报告包含本机路径，请勿直接公开；配置和日志检查不能代替实际播放验收。'
}
