# Installation & Video Customization Guide

This package contains a prebuilt Windows desktop companion (`BootPlayer.exe`), **15 licensed CC BY 4.0 MP4 videos** with matching `-first.png` first-frame covers, and one-click hook-free launchers.
No administrator rights, Visual Studio, or Codex plugin-hook trust steps are required.

中文快速指南请先看 **[第一次使用：30 秒上手指南 (START-HERE.md)](../START-HERE.md)**。

---

## 1. System Requirements

- **OS**: Windows 10 or Windows 11 (x64)
- **Runtime**: .NET Framework 4.8+ and Windows PowerShell 5.1 (both built into Windows 10/11)
- **Target App**: Windows Codex Desktop app installed

---

## 2. Three Ways to Run

Always **extract the full release ZIP** to a permanent folder first (do not double-click scripts inside a ZIP preview window or from inside Codex's isolated sandbox shell).

| Launcher | What It Does |
| --- | --- |
| **`install.cmd`** *(Recommended)* | Deploys the companion, starts the background supervisor immediately, and registers `CodexBootAnimation` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` for current-user Windows sign-in autostart (does **not** enable Codex autostart). |
| **`start.cmd`** *(Portable Mode)* | Starts the companion for the current session only without writing any Windows startup registry entry. |
| **`uninstall.cmd`** | Cleanly stops all running `BootPlayer.exe` companion processes and removes the Windows sign-in autostart entry while preserving your personal videos and volume settings. |

### How Hook-Free Monitoring Works

Starting in `v0.8.0`, the companion operates **100% independently of Codex CLI `plugin_hooks`**:
- **Cold Startup (`冷启动`)**: Detects new Codex Desktop main window processes.
- **New Chat (`新聊天`)**: Reads local Codex desktop navigation logs with per-window route deduplication.
- **Anger / Roast Reaction (`生气回应`)**: Tails active session rollout JSONL logs (`~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl`) in memory for `UserMessage` events and evaluates them with `AngerLexicon` (never writing chat text to disk).
- **Task Completion (`任务完成`)**: Evaluates turn tool-call count, duration, foreground/background focus, and mid-task questions via `CompletionGate` with a 60s per-session cooldown.
- **Idle Companionship (`闲置互动`)**: Monitors foreground idle (5m), background neglect return (10m + 0.8s settle), and global away return (5m + 0.8s settle) via `IdleReturnPolicy` with a 1.2s opening grace window and 15m cooldown.

---

## 3. Adding or Replacing Videos

Right-click the **Codex 片头助手** system tray icon and choose **打开场景视频文件夹**.
Place `.mp4` files into any of the five scene folders:
- `冷启动` (Cold Startup)
- `新聊天` (New Chat)
- `生气回应` (Anger / Roast Reaction)
- `任务完成` (Task Completion)
- `闲置互动` (Idle Companionship)

### Playback Rules
- **1 MP4 file**: Plays that video every time.
- **Multiple MP4 files**: Picks randomly within that scene (`生气回应` uses shuffled non-repeating cycles).
- **Empty folder**: Disables automatic playback for that scene only.
- File changes are detected automatically within ~1.5 seconds (or click **重新扫描视频** in the tray menu).

### First-Frame Covers (`*-first.png`)
For any `<name>.mp4`, an optional `<name>-first.png` in the same folder acts as a zero-latency cover frame and aspect-ratio blur backdrop while Windows Media Foundation decodes the first frame.
You can import a video and automatically extract its `-first.png` cover (if FFmpeg is available under `%USERPROFILE%\.codex-boot-animation\tools\ffmpeg\bin` or `PATH`) via:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Add-Video.ps1 -Scene '任务完成' -VideoPath 'C:\path\to\clip.mp4'
```

To verify all videos and first-frame covers at any time, double-click **`check-videos.cmd`**.

---

## 4. Per-Scene Audio & Corner Notification Settings

Right-click the **Codex 片头助手** tray icon:
- **场景设置（音量 / 静音 / 开关）**: Independently configure automatic trigger enable/disable, mute, and volume (`0–100%`) for each of the 5 scenes. Saved in `%USERPROFILE%\.codex-boot-animation\scene-settings.json` with automatic rollback backups in `settings-backups/`.
- **完成提醒设置（位置 / 大小）**: Configure the non-focus-stealing background task-completion corner overlay size (`Large` / `Medium` / `Small`) and corner (`BottomRight` / `BottomLeft`). Saved in `%USERPROFILE%\.codex-boot-animation\completion-layout.json`.

---

## 5. Diagnostics & Updating

- Double-click **`check.cmd`** for a read-only health check of the active processes, startup registration, and runtime settings.
- To update an existing installation in place from a newer unpacked release without losing custom videos or preferences:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Update-Companion.ps1 -PluginRoot "C:\path\to\installed\plugins\codex-boot-animation"
```
