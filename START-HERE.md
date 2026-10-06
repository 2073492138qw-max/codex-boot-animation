# 安装与使用 / Start here

这是 Windows Codex 桌面版的社区动画插件，当前为测试版。
支持 Windows 10/11、.NET Framework 4.8；Mac 和 Linux 暂不支持。

1. 先安装并打开一次 Codex 桌面版。建议更新到当前版本。
2. **完整解压 ZIP** 到自己的固定目录，例如用户目录里的 `Applications\CodexBootAnimation`。
   安装后保留这个目录；不要在 ZIP 预览窗口、临时目录中运行。
3. 双击 `install.cmd`，等待安装窗口明确显示完成。无需管理员权限。
4. 打开 Codex 插件设置，检查插件已启用，并按提示审核、信任钩子。
5. 完全退出 Codex，包括它的托盘图标，再打开：应播放一次片头。
   点击“新聊天”也应播放，可以按 Esc 或点击“跳过”。

安装失败时，双击 `check.cmd` 查看检查结果。它不会安装插件或修改自启动设置。
检查也适用于“已经安装却没反应”：它会显示插件启用、钩子信任、助手进程、
活动视频目录和缓存差异。`PASS` 为通过，`WARN` 为需检查，`FAIL` 为故障，
`UNKNOWN` 为无法确认，`PENDING` 为待真实验收。钩子显示 `untrusted` 或
`modified` 时需要你在 Codex 中审核；安装器不会代你信任。
普通输出已附中文状态和“下一步”，报告开头会汇总需处理的数量；“待实测”不是
故障，也不会自动读取此前的人工验收结论。开发者使用 `Diagnose.ps1 -Json` 时仍
得到原有字段和英文状态码。本机路径与日志不应直接上传公开社区。

视频问题可双击 `check-videos.cmd`：只读检查当前活动安装的五个场景目录。
也可以把一个 MP4 拖到这个入口上，只检查它。缺少首帧图不等于视频损坏；
缺少可选的 FFmpeg/ffprobe 时会显示“无法确认”，不会自动下载或安装工具。
中文使用说明见 [视频检查说明](docs/VIDEO-CHECK-2026-10-03.md)。

缺少 .NET 时先用 Windows Update 安装 .NET Framework 4.8；找不到兼容的 Codex
命令行时先更新、打开 Codex，再重试。Windows 若提示程序未签名，请审核来源；
本项目不会关闭安全防护或替你信任钩子。

已存在另一份插件安装时，安装器会提示先迁移旧安装，避免同时运行两个助手。
详细安装、更新和卸载说明见 [INSTALLATION.md](docs/INSTALLATION.md)。
默认不静音，各场景视频可从助手托盘菜单打开文件夹后替换。
要单独调整声音或开关，右键 **Codex 片头助手** 托盘图标，点击
**场景设置（音量 / 静音 / 开关）**。五类场景各自保存自动触发、静音和 0–100%
音量；点击保存后下次播放生效，关闭自动不禁止手动预览。冷启动默认 85%，
其余默认 65%，不会修改系统音量或视频。详见[场景设置说明](docs/SCENE-SETTINGS-2026-10-03.md)。
后台完成提醒的布局可在同一托盘的 **完成提醒设置（位置 / 大小）** 中选择。
先选大小、位置并点“预览当前选择”，满意后保存；默认仍是“大、右下角”。
这只影响后台/最小化提醒，Codex 前台仍保持原窗口内播放，不改声音设置。
详见[完成提醒布局说明](docs/COMPLETION-LAYOUT-2026-10-03.md)。
任务完成与生气回应依赖规则识别，可能漏触发；Codex 更新后可能需要适配。
第二台电脑及真实关机重启的完整验收仍待完成。

English: install/open the Windows Codex desktop app, fully extract this ZIP
to a permanent folder, and double-click `install.cmd`. Review and trust the
hooks in Codex, then fully quit and reopen Codex. Use `check.cmd` for a
read-only installation diagnosis. Keep the extracted folder after installation.
See [installation instructions](docs/INSTALLATION.md) for updates and removal.
