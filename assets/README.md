# 片头助手托盘图标

- `tray-icon-source.png`：用户提供的全身原图副本，原样保留。
- `tray-icon-fullbody.ico`：第一版全身图标；用户比较简化版后选择恢复此版。
- `tray-icon-compact.png`：试用过的简化头像设计，白发、兔耳、红眼、透明背景，保留作备选。
- `tray-icon.ico`：当前恢复的全身角色 Windows 多尺寸图标，与 `tray-icon-fullbody.ico` 相同，包含
  16 / 20 / 24 / 32 / 40 / 48 / 64 / 128 / 256 像素及透明通道。
- `scripts/New-TrayIcon.ps1`：仅格式转换，无外部依赖，不覆盖已有文件。
  默认输入为原图 `tray-icon-source.png`。如需重新生成，明确指定新的输出路径，
  检查后再确认替换；转换过程不负责设计或重画。

构建时将 ICO 嵌入助手 EXE 的托管资源；运行时只用作 NotifyIcon 图标。
不修改 EXE 外观、设置窗口图标、Codex 本体或插件列表展示图标，也不依赖
下载目录或外部 ICO。读取异常时回退默认系统图标，不阻塞助手启动。

## 公开素材授权（2026-10-06）

维护者确认原图由其使用 AI 生成，并同意四个图标文件及其衍生版本公开上传、
以 [Creative Commons Attribution 4.0 International（CC BY 4.0）](https://creativecommons.org/licenses/by/4.0/)
授权使用、修改和再分发。此确认也覆盖 BootPlayer.exe 内嵌的图标以及已有 Alpha.5 安装包。

署名：**Codex Boot Animation contributors — Codex 片头助手兔耳角色图标**。
再分发时保留署名、许可链接，并注明修改。图标的 CC BY 4.0 与代码/文档的 MIT
分开适用；不授权第三方商标。此前仅本机使用的授权限制已由这次明确确认取代。
