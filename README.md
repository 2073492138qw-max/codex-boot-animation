# Codex Boot Animation

A Windows desktop companion that plays a skippable, sound-enabled intro when
Codex starts or a user clicks **New chat**. The player is a separate native
window; this project does not patch Codex or draw inside its UI.

中文简介：这是 Codex 桌面版的本地片头助手。打开 Codex、鼠标点击“新聊天”时播放；
可按 Esc 或点击“跳过”。代码可以继续修改，但它不是 Codex 官方内嵌动画接口。

## Status and behavior

**0.7.0-alpha.5 — Windows community preview, not a stable cross-platform plugin.**
[Download Windows Alpha.5](https://github.com/2073492138qw-max/codex-boot-animation/releases/tag/v0.7.0-alpha.5)
and follow the [beginner installation guide](START-HERE.md).
The first GitHub `windows-latest` build passed, but its video-decoder probe failed;
cloud CI has not passed. This does not replace the existing local acceptance evidence
or establish second-PC compatibility. The newly authorized optional videos also need
the source test script's mandatory-cover assertion reconciled with optional covers.
Earlier visual acceptance used packaged Codex desktop 26.924.6891.0;
earlier package checks also ran with desktop 26.928.2636.0 and CLI 0.159.2.
Alpha.5 local checks use desktop 26.930.3930.0 and CLI 0.160.0; see the
[package-check record](docs/ALPHA5-PACKAGE-CHECK-2026-10-06.md) for results.
A clean extracted package is tested on this PC; an independent
second-PC/reboot acceptance test is still required before claiming stable portability.

- Immediate path: a preloaded WPF player plus a Windows mouse/UI Automation
  watcher for the visible New chat button. Discovery is readiness-gated and
  bounded in an isolated child; clicks only use validated cached rectangles.
  Empty/error/timeout discovery stops rather than repeatedly scanning a starting app.
- Startup path: a user-level sign-in companion observes Codex desktop
  process instances. Persistent per-instance claims handle sign-in startup races
  and prevent replay when the watcher recovers. The Codex `SessionStart` hook only recovers a missing
  watcher; sending the first message does not trigger the intro.
- Fallback: a local Codex navigation-log watcher recognizes a new blank chat
  when the UI click cannot be identified. The fallback can be slower.
- Playback: cold-start and New chat intros use 1.2× speed; anger, task-completion, and idle clips use their original 1.0× speed. Cold-start (including its tray preview) uses 85% application volume; all other scenes retain 65%, with sound on by default and
  Skip/Escape. Replays prepare from zero behind a matching first-frame cover
  when available, or a black transition when it is absent; covers are optional.
- Video library: five independent scene folders under
  `plugins/codex-boot-animation/videos/` — `冷启动`, `新聊天`, `生气回应`,
  `任务完成`, and `闲置互动`. Each scene uses only its own MP4 files: one file plays every time;
  multiple files are selected randomly. The watcher reloads the resident
  player after files change. Legacy filename-selection settings are ignored.
  Empty startup and New chat folders disable only those intros: the resident
  remains hidden and responsive for idle/reaction delivery. Adding clips later
  uses the existing automatic rescan; no Codex restart is required.
- Optional emotion response: a local `UserPromptSubmit` hook checks only the
  submitted message for clear insults or strong disparagement directed at
  Codex, then plays a separate reaction clip within the Codex window. It does not inspect typing or
  save the prompt. Examples and meta-discussion are excluded; an active intro
  is not interrupted; a new insult can trigger again as soon as the previous
  reaction ends. Rules can miss
  sarcasm or misread context, so this is not a general sentiment model. Common
  colloquial forms such as “你特么” are recognized, but standalone exclamations
  such as “卧槽” do not trigger a full-screen reaction.
- Optional task completion: a `UserPromptSubmit` hook stores only a task-kind
  flag and hashed request identity. Automatic continuation turns inherit that
  request state; a new user message replaces it. `Stop` and the local session
  journal check the final answer and share request identity. A journal
  turn lasting at least two minutes can recover a missed prompt hook. A short greeting/chat or a
  failed/uncertain answer does not play a video. Codex does not expose a
  semantic "task succeeded" event, so this is deliberately conservative and
  may miss valid completions. On receipt, the separate skippable video covers
  foreground Codex and follows its bounds (closing on focus loss). If Codex is
  background/minimized, an audible notification at the saved size/corner plays immediately
  without waiting for a click, restoring Codex, or stealing keyboard focus.
  It has its own Skip button and closes at the end; it does not cover the whole
  monitor or embed into Codex. Event dispatch and media decoding are not zero-latency.
  Delivery claims the task only after durable playback-start confirmation, a
  merge into an active automatic reminder, or explicit early Skip/Esc dismissal.
  An initial attempt plus at most two retries has a two-second delivery budget;
  simultaneous tasks merge rather than overlap. Failed delivery is recorded,
  not silently called success or queued until Codex is opened. This does not
  guarantee delivery through crashes, power loss or every hardware failure.
- Optional idle interaction: after 15 minutes without keyboard or mouse input,
  return input arms a 2-second quiet period. Further return activity restarts
  this timer rather than canceling it. Within 60 seconds of the return, one
  random `闲置互动` clip may fill only the foreground, non-busy Codex
  window in a non-activating, input-transparent overlay. Any further input,
  Codex minimization, or higher-priority clip closes it. The tray can disable
  this feature. Its manual preview brings the verified Codex window forward,
  survives mouse/keyboard activity, and offers Skip/Esc; busy or unavailable
  previews report a tray message instead of waiting silently. Automatic idle
  detection reads only the Windows session's last-input time, not key
  content, mouse positions, or an activity history.
- Scope: Windows Codex desktop, mouse-click New chat detection. Keyboard-only
  New chat depends on the slower fallback. Codex UI updates may affect button
  recognition.

A valid cold-start or New chat intro interrupts the resident's anger/idle clip
and closes its media before showing the intro. A missing or already-failed intro
does not interrupt an anger clip. This is not a global priority rule for the
separate task-completion player.

## Repository layout

```text
.agents/plugins/marketplace.json       Repository-scoped Codex marketplace
plugins/codex-boot-animation/          Installable plugin package
  .codex-plugin/plugin.json             Tested Windows plugin metadata + hooks
  hooks/                               SessionStart, prompt and Stop hooks; EXE
  videos/                              Five scene folders and optional first frames
src/                                   C# WPF player, watcher, media routing
scripts/                               Installation, build and test commands
docs/                                  Architecture and diagnostics
.github/                               Windows CI and bug-report template
```

The reviewed, prebuilt companion and fifteen explicitly licensed video entries are included for easy
deployment. Build output, personal logs and old experimental binaries are not
tracked. Further personal videos are ignored by default until you decide
their public license. The catalog covers all five scenes and one inactive spare.
The frozen Alpha.5 installer ZIP has the original eleven videos; four later-authorized
scene paths are available in this repository, including one clip in two scenes.

## Install on another Windows PC

The tested Windows CLI 0.159.2 installs a root portable manifest but does not
discover this package's bundled hooks. This release uses the native
`.codex-plugin/plugin.json` and verifies hook discovery in a clean Codex home.
`docs/plugin.portable.example.json` is reference metadata, outside the active
plugin, until that packaging route passes real runtime verification.

Start with [the step-by-step setup guide / 第一次安装：照着做即可](START-HERE.md).
中文发布简介与必做的钩子允许步骤：[Windows Alpha.5 发布说明](docs/RELEASE-NOTES-0.7.0-alpha.5.zh-CN.md)。

Install Codex desktop. Clone or unzip this repository to a **permanent**
location, then double-click `install.cmd`. It invokes PowerShell with a
process-only execution-policy override; it does not change your system policy.
For a non-mutating preflight from a normal Windows PowerShell window:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install.ps1 -CheckOnly
```

This checks the package, registers this repository as a Codex marketplace,
installs the plugin and adds a current-user Windows sign-in entry for the
companion. It does not request administrator rights. Review/trust the hook in
Codex if prompted. Full instructions and existing-installation precautions:
[Installation](docs/INSTALLATION.md).

中文：在另一台电脑上先装 Codex，把仓库放在固定目录，双击 `install.cmd`。
不要从 Codex 内置终端运行安装脚本。

You can also double-click `check.cmd` to diagnose the active installation. The installer prefers the
desktop's bundled CLI and checks plugin commands before changing registration.
It confirms the plugin is installed/enabled and waits for the supervisor,
watcher, resident player and local pipe before reporting success. A failed
startup removes only a newly created startup entry and new helper processes
from this package; an existing installation is preserved.

`check.cmd` now diagnoses the active installation: dependencies, plugin
enablement, exact hook trust, sign-in registration, all three helper processes,
IPC, active media/cache folders, binary mismatches and recent sanitized trigger
reasons. It does not install anything or approve hooks. It marks physical
playback/reboot acceptance separately from configuration and log evidence.

## Build and test

Requirements: Windows 10/11, .NET Framework 4.8 or later with WPF assemblies, and Windows
PowerShell 5.1. Visual Studio is not required. From a PowerShell window:

```powershell
.\scripts\Build.ps1
.\scripts\Test.ps1
```

The completion regression tests exercise continuation, missing-hook recovery,
concurrent duplicate events, and failed/short/reaction-only exclusions. To
verify a real, previously unclaimed completed turn with Codex in front, run
`scripts/Verify-Completion.ps1 -JournalPath <local-jsonl> -TurnId <turn-id> -PlayerPath <installed-exe>`.
It replays the actual final record through detection and delivery, and checks
moving-picture/audio-track/normal-end logs; confirm audible sound visually on
the PC as well. Already-claimed turns are deliberately not replayed. Do not
publish the journal or private diagnostic inputs.

Installation regression tests cover CLI selection/fallback, slow startup,
missing processes, IPC readiness and startup rollback without changing the
real registry. Developers with Codex desktop can run
`scripts/Verify-CleanInstall.ps1` to register/install the package into a fresh,
isolated Codex home under ignored `build/` and check its cached EXE and videos.
This does not register Windows startup or approve hooks.

To add a clip, open the appropriate scene folder from the tray menu and copy
an MP4 into it, or run:

```powershell
.\scripts\Add-Video.ps1 -Scene '任务完成' -VideoPath 'C:\path\to\another-video.mp4'
```

Each scene is independent. To replace a video, move the old MP4 out of its
scene folder and put the new MP4 there; the program rescans automatically.
Keep only one MP4 for a fixed choice, or several for random playback. FFmpeg
is optional for additions: when available, the script creates a first-frame
PNG; without one, a black cover prevents stale frames. To replace the bundled
New chat default in a source checkout, FFmpeg is required:

```powershell
.\scripts\Set-IntroVideo.ps1 -VideoPath 'C:\path\to\your-video.mp4'
```

Check that you may redistribute a replacement video before committing it.

## Logs and privacy

Runtime diagnostics are written outside this repository at
`%USERPROFILE%\.codex-boot-animation\playback.log`. They contain timestamps,
process IDs, trigger reasons, media status and errors, not chat text. The
emotion hook reads the submitted prompt only in memory and never logs it. Some
errors can expose local file paths; redact before posting. See
[logging](docs/LOGGING.md). Never commit the log.

## License

Software and documentation: [MIT](LICENSE). The bundled scene videos and first-frame images:
[CC BY 4.0](plugins/codex-boot-animation/videos/LICENSE.md), as explicitly chosen by the media owner.
All media in [the release catalog](plugins/codex-boot-animation/videos/media-catalog.json),
including `任务完成后.mp4`, have explicit redistribution permission. New local files
outside that catalog remain excluded until licensed. The media
license does not grant rights to third-party trademarks. This is an
independent community project, not an official OpenAI product.

The AI-generated tray artwork and its derived icons are also licensed under
[CC BY 4.0](assets/README.md#公开素材授权2026-10-06), including the icon embedded in
the companion executable. Preserve the attribution and indicate modifications.

## Development

See [architecture](docs/ARCHITECTURE.md), [contributing](CONTRIBUTING.md),
[changelog](CHANGELOG.md) and [security policy](SECURITY.md). The current live
installation on its owner's PC is separate from this source checkout; building
this checkout does not silently replace that installation.

To update only an existing companion safely, run the new release's
`scripts/Update-Companion.ps1 -PluginRoot 'C:\path\to\installed\plugin'`.
It validates the target, backs up the EXE, restarts only the helper, and preserves
videos and hook trust. Changed hook definitions require a reviewed plugin reinstall.
See [release checklist](docs/RELEASE.md) for packaging and publication readiness.
