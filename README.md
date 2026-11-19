# Codex Boot Animation

A lightweight, **hook-free** Windows desktop companion for **Codex Desktop** that plays skippable local videos across 5 scenes — **cold startup**, **new chat**, **anger/roast reactions**, **restrained task completion**, and **human-centered idle companionship**.

The player uses native Windows WPF overlay windows; it **never patches Codex**, **never requires Codex CLI / plugin hook trust**, and **never stores chat text**.

中文简介：**免安装插件、免配置钩子、解压双击即用**的 Windows Codex 桌面版动画伴侣。支持冷启动、新聊天、高敏中文开怼/阴阳怪气生气回应、偏克制大任务完成提醒（支持后台角落召回）和桌宠式闲置互动；随时按 `Esc` 或点击“跳过”关闭，托盘一键换视频、调音量。

---

## Quick Start（30 秒快速上手）

> 详细中文图文步骤见 **[新手上手指南（START-HERE.md）](START-HERE.md)**。

1. Download and **extract** the latest Windows release ZIP to a permanent folder (do not run inside the ZIP preview or inside the Codex sandbox terminal).
2. Choose how you want to run it:
   - **Recommended (install + Windows sign-in autostart)**: Double-click **`install.cmd`** (no administrator rights needed; does **not** enable Codex autostart).
   - **Portable tryout (no registry changes)**: Double-click **`start.cmd`**.
   - **Stop & uninstall**: Double-click **`uninstall.cmd`** (personal videos and settings are preserved).
3. Right-click the **Codex 片头助手** icon in the Windows system tray to preview scenes, open scene video folders, adjust per-scene volume/mute/switches, or customize the corner notification layout.

---

## Five Scenes & Behavior（五大触发场景）

All 5 scenes work **100% locally without Codex plugin hooks** (compatible with modern Codex Desktop versions where `plugin_hooks` was removed):

| Scene Folder | Trigger Behavior |
| --- | --- |
| `videos/冷启动/` | Plays once when a new Codex Desktop process starts (1.2× speed, 85% default volume, smooth zero-stutter first-frame cover gate). |
| `videos/新聊天/` | Plays when opening a new blank chat in Codex (1.2× speed, 65% default volume, per-window route deduplication). |
| `videos/生气回应/` | Reads live session `UserMessage` entries in memory (never logged) with a high-sensitivity Chinese/English roast, sarcasm (阴阳怪气), disdain, and profanity lexicon (291 positive / 105 negative test cases). Plays inside the Codex window. |
| `videos/任务完成/` | **Restrained human-centered mode**: triggers on substantial work (≥ 3 tool calls or ≥ 45s) in foreground/background, or calls you back via a non-focus-stealing corner notification when Codex is in the background (≥ 15s or ≥ 1 tool call). Ignores quick foreground chat, failures, and mid-task questions waiting on your decision; 60s per-session cooldown. |
| `videos/闲置互动/` | **Companion idle mode**: proactively plays after **5 min** idle with Codex in foreground, or welcomes you back (after a **0.8s** settle) when you return to Codex after **10 min** in other apps or **5 min** away/sleep. 1.2s opening grace against mouse jitter; 15-min cooldown. |

### Replacing Videos（自定义视频）
- Put one or more `.mp4` files into any of the five folders under `plugins/codex-boot-animation/videos/` (1 video = fixed, multiple = random; empty folder disables only that scene). Changes take effect automatically within ~1.5s.
- Optional `*-first.png` first-frame covers prevent stale-frame flashes and provide blurred backdrops for aspect-ratio differences. All 15 bundled CC BY 4.0 videos include matching `-first.png` covers.

---

## Low-Memory & Reliability Design

- **Lazy media loading + idle recycle**: `--resident` stays around ~13 MB working set while idle and cleanly recycles itself after extended post-playback idle to reclaim native Windows Media Foundation memory.
- **Self-healing recovery**: `--supervise` and `--watch` monitor their child processes with bounded exponential backoff (2s launch grace up to 30s cap), 2-second UI pipe timeouts, and automatic pruning of expired transient state files.

---

## Repository Layout

```text
install.cmd                            One-click standalone install + Windows sign-in autostart
start.cmd                              Portable single-session launch (no registry changes)
uninstall.cmd                          Cleanly stop helper and remove autostart
check.cmd                              Read-only health diagnostics
plugins/codex-boot-animation/
  hooks/BootPlayer.exe                 Prebuilt .NET Framework 4.8 WPF/WinForms companion
  videos/                              Five scene folders + 15 CC BY 4.0 videos & first frames
src/                                   C# source & self-tests (zero external NuGet dependencies)
scripts/                               Build, test, standalone install, update & release packaging
docs/                                  Architecture, handoff, review & installation notes
```

---

## Build, Test & Package from Source

Requirements: Windows 10/11, .NET Framework 4.8+ (built into Windows), Windows PowerShell 5.1. Visual Studio is **not** required.

```powershell
# Compile BootPlayer.exe (zero warnings)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1

# Run full automated logic + install + real WPF media decoder test suite
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1 -IncludeMedia

# Build an immutable GitHub release ZIP + SHA256 under dist/
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-Release.ps1
```

To safely update an existing installation without touching videos or startup preferences:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Update-Companion.ps1 -PluginRoot "C:\path\to\installed\plugin"
```

---

## Logs and Privacy

Runtime diagnostics are written outside this repository to `%USERPROFILE%\.codex-boot-animation\playback.log` (bounded 2 MB rolling log). Logs record timestamps, PIDs, trigger reasons, and playback status — **never chat messages, prompts, or assistant answers**.

---

## License

- Software and documentation: [MIT](LICENSE).
- Bundled scene videos and first-frame PNGs in [media-catalog.json](plugins/codex-boot-animation/videos/media-catalog.json): [CC BY 4.0](plugins/codex-boot-animation/videos/LICENSE.md) — Attribution: **Codex Boot Animation contributors**.
- Tray icon artwork: [CC BY 4.0](assets/README.md#公开素材授权2026-10-06).
- Independent community project, not an official OpenAI product.
