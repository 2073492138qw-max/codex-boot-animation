# Install on another Windows PC

The repository contains a prebuilt Windows companion, eleven licensed MP4s, a
repository-scoped Codex marketplace, and a current-user installation script.
No administrator rights or Visual Studio are needed for the prebuilt package.

中文用户先看[第一次安装：照着做即可](../START-HERE.md)。下面是详细说明；
没有审核提示时按本页的中文备用步骤处理，不需要创建任何诊断项目。

## Requirements

- Windows 10/11 with .NET Framework 4.8 or later and Windows PowerShell 5.1.
- The packaged Windows Codex desktop app installed. Other distribution types
  have not been verified. The installer finds its CLI in PATH or the desktop
  app's local bin directory.
- A stable folder for this repository. Do not install from a temporary unzip
  location: the Windows sign-in entry points to the executable here.

Double-click `install.cmd` from Explorer. Do not run the installer inside
Codex: its packaged shell may see an isolated Windows registry view. The CMD
launcher uses a PowerShell execution-policy override for **that one process**;
it does not change Windows' system or user execution policy. Inspect the
script before running it if you downloaded the project from a third party.

Fully extract the ZIP before running a launcher. Open Codex once first so its
bundled CLI is available. The installer tries the desktop CLI before PATH
candidates and checks plugin-install and marketplace commands. Incompatible
candidates are skipped; if none work, update/open Codex and retry. A CLI version
number alone is not treated as proof that these commands are supported.
Native CLI inspection has a 15-second timeout; registration/install commands
have a 60-second timeout. If a command times out, check Codex and network access
before retrying. A interrupted registration may already have installed files;
the installer checks existing state again rather than reporting success early.

For a non-mutating preflight, open Windows PowerShell from the Start menu,
change to the repository folder, and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install.ps1 -CheckOnly
```

The preflight checks files and prerequisites without changing settings.
`check.cmd` runs an active-installation diagnosis with a double-click. Use the
PowerShell `Install.ps1 -CheckOnly` command above for package-only preflight.
Diagnostics queries the read-only `hooks/list` API to distinguish enabled,
trusted, modified and untrusted hook definitions; it does not approve them.
If that API is unavailable, trust is explicitly reported as unknown.
The launcher registers the repository marketplace with
`codex plugin marketplace add`, installs
`codex-boot-animation@codex-boot-animation`, writes the current
user's Windows sign-in entry and starts the companion. Existing unrelated
`CodexBootAnimation` entries are not silently overwritten.

The repo layout follows [OpenAI's plugin packaging
documentation](https://developers.openai.com/plugins/build/plugins). A local
marketplace install and Windows sign-in registration are separate steps;
`install.cmd` handles both.

Review/trust the plugin's hooks in Codex if prompted. `SessionStart` restores
a missing watcher; `UserPromptSubmit` optionally checks a submitted message
for a direct insult toward Codex and signals the resident emotion-response
video. The independent Windows companion remains responsible for instant New
chat and cold-start playback. Restart Codex after installing, then verify the
triggers. A full Windows reboot verifies sign-in persistence.

The installer verifies the installed plugin is enabled, then allows up to
20 seconds for all three helper processes and a harmless IPC probe to succeed.
If startup fails, it rolls back a newly created sign-in entry and newly started
helper processes from this directory. It preserves any preexisting entry.
A plugin/marketplace that was already registered by the earlier step remains
available for a retry; hook trust is still reviewed in Codex.

The tested desktop/CLI pair can install a portable root `plugin.json` without
discovering plugin hooks. This package ships only the native `.codex-plugin`
manifest inside the plugin; the portable reference lives under `docs/`.
The installer checks that all three hooks are discovered before it registers
Windows startup. They remain untrusted until reviewed by the user.

<a id="hook-review-fallback"></a>

## 没有钩子审核提示时

先运行 `check.cmd`。只有本插件的钩子显示“尚未信任”或“定义已变更”时，才需要下面的审核。
若会话启动、消息提交、任务结束这三项均通过，显示“已启用：是 / 信任：已信任”，跳过此节；若没找到钩子或“无法确认”，把相关几行反馈给维护者。

桌面版未提供可见审核入口时，可尝试官方命令行入口：

1. 点 Windows“开始”，搜索 **Windows PowerShell**，普通方式打开，不用管理员。
2. 输入 `codex`，按回车。若提示“无法识别”，停止并反馈；安装器能找到桌面自带的程序，不代表这个命令已经在系统搜索路径中。不要为此随便下载另一份 CLI。
3. 若正常打开 Codex 命令行界面，在它的输入处输入 `/hooks`，按回车。**不是在桌面聊天框，也不是直接在 PowerShell 提示符输入 /hooks。** 如果命令不可用、三项找不全或没有审核入口，停止并反馈具体提示与相关检查行，不继续猜命令或重装。
4. 找到来源属于 `codex-boot-animation` 的 `SessionStart`、`UserPromptSubmit`、`Stop`。查看定义，确认调用的是本插件的 `hooks/BootPlayer.exe`，分别审核、信任并保持启用。**“启用”和“信任”是两件事，三项都要完成，不能只启用插件。** 界面文字可能因版本不同而变化；不认识来源就停止，不选择全部来源一起允许。
5. 审核完成后关闭这个 PowerShell 窗口，重新双击 `check.cmd`：这三项应通过，显示“已启用：是 / 信任：已信任”。仍不通过就反馈这几行，不手改信任记录。

普通插件安装不需要建立项目级计时 Hook 或诊断项目，不复制别人的信任配置，
不使用绕过信任的参数。信任针对当前定义；改变定义后需要重新审核。
这是 [OpenAI 官方的审核规则与 CLI 入口](https://learn.chatgpt.com/docs/hooks)，
安装、启用不等于已信任，见[插件说明](https://developers.openai.com/plugins/build/plugins)。
本轮仅核对文档和现有检查代码，没有在一台新电脑上验证上述点击流程。

## Add or switch videos

Right-click the **Codex 片头助手** tray icon and choose **打开场景视频文件夹**.
Put each MP4 in `冷启动`, `新聊天`, `生气回应`, `任务完成`, or `闲置互动` according to its
trigger. One MP4 plays every time; multiple MP4s are random within that
scene; anger clips cycle in shuffled order without immediate repeats. An empty folder disables that scene. File changes are rescanned
automatically after about 1.5 seconds, or choose **重新扫描视频** in the tray.
You can also use
`scripts\Add-Video.ps1 -Scene '任务完成' -VideoPath 'C:\path\clip.mp4'` to copy a video and,
if FFmpeg is installed, extract a matching first-frame cover.

The importer checks local MP4 contents, video/audio streams and decoding before
copying. A definite failure stops the import; missing tools/timeouts are marked
unknown and preserve the previous copy-with-warning behavior. Existing video
or first-frame filenames are never overwritten. Cover extraction is bounded
and uses FFmpeg's no-overwrite option; the source MP4 is not re-encoded.
The default destination remains this checkout's plugin videos, not a different
active installation. Pass `-VideoRoot 'C:\path\to\active\videos'` explicitly
when importing into that other installation, and check the printed destination.

Double-click `check-videos.cmd` for a read-only inspection of the installation
registered in the sign-in entry, or drag one MP4 onto it. `Check-Videos.ps1`
also accepts `-VideoRoot`, `-FFmpegPath`, `-FFprobePath`, `-TimeoutSeconds` and
`-Json`. Explicit tool paths take precedence. Default discovery checks
`%USERPROFILE%\.codex-boot-animation\tools\ffmpeg\bin`, then the legacy
LocalAppData tool folder, then PATH. Prefer the shared user-profile location:
packaged Codex can redirect LocalAppData into an app-only directory which
Explorer-launched checks cannot see. Tools are optional and are not bundled
or downloaded by the checker. FFmpeg success is not proof of Windows player
compatibility or audible output. Exit zero means a report was completed, not
that every item passed. See the [Chinese video-check guide](VIDEO-CHECK-2026-10-03.md).

Right-click the helper tray icon and choose **场景设置（音量 / 静音 / 开关）**
to save per-scene automatic enablement, mute and volume (0–100%). Save applies
to the next playback, not one already playing. Disabling an automatic scene
still allows manual previews using that scene's audio settings. Default volume
is 85% for startup and 65% for other scenes, unmuted; the global automatic pause
still takes priority. This does not alter system volume or video files.
Settings live in `%USERPROFILE%\.codex-boot-animation\scene-settings.json`,
shared by the active companion and hook-cache fallback. Close without Save
discards edits. Previous settings versions are preserved in `settings-backups`;
conflicting edits or unreadable files are not silently overwritten. See the
[Chinese scene-settings guide](SCENE-SETTINGS-2026-10-03.md) for acceptance status.

Choose **完成提醒设置（位置 / 大小）** in the helper tray to preview/save
Small/Medium/Large and bottom-left/right background completion notifications.
The validated default remains Large / BottomRight. Previews do not save or
change task state; Save applies to the next notification. Preferences are
independent in `%USERPROFILE%\.codex-boot-animation\completion-layout.json`,
with revision checks and previous-version backups. Foreground Codex playback,
audio preferences, immediate delivery and focus protection stay unchanged.
See the [layout guide](COMPLETION-LAYOUT-2026-10-03.md) for current acceptance limits.

MP4 files can have different encoded loudness even at the same player volume.
To match new clips to the single `任务完成` reference video,
run `scripts\Normalize-Audio.ps1 -VideoRoot 'C:\path\videos' -OutputRoot 'C:\path\staged' -FfmpegPath 'C:\path\ffmpeg.exe'`.
The script reports measured LUFS and stages video-stream-preserving copies;
it never edits the originals. Back up the originals before copying the
verified staged MP4s into their scene folders. If `任务完成` contains more than
one MP4, also pass `-ReferenceVideo` with the exact reference path.

`闲置互动` is optional and can be disabled from the tray without affecting the
other scenes. It plays once after a 15-minute idle period and a quiet return
to foreground Codex. Mouse movements/clicks restart the two-second quiet timer;
they do not discard the opportunity. Background or busy Codex can defer it for
up to 60 seconds after the return; after that the opportunity expires.
Input after playback starts immediately dismisses the video.

Adding a video to the **source** folder after plugin installation updates the
running companion (which uses this stable source path). It does not refresh the
Codex plugin's cached copy. The automatic completion sender uses the existing
sign-in registration to find the installed player, so normal playback uses
the same editable video folder. The sending and installed executables must
be byte-identical; stale mixed versions fail safely rather than run the new
command as an old intro. Without that registration, it can use its own packaged
player. A companion-only update does not refresh the plugin's cached hook
executable: intentionally update/reinstall the plugin cache through the reviewed
marketplace workflow too. Do not edit hook trust records to suppress an update review.

## Updating the code

Developers can run `scripts\Build.ps1` and `scripts\Test.ps1`. The build
replaces the prebuilt executable in the source package. Stop an older running
helper and restart it only as part of an intentional update; do not overwrite
the file of a running process. For a released plugin, increment its manifest
version and reinstall from the configured marketplace so Codex refreshes its
cached hook. The sign-in path should continue to point at this repository.

For unchanged hooks, the new release includes a guarded binary updater:

```powershell
.\scripts\Update-Companion.ps1 -PluginRoot 'C:\path\to\old\plugins\codex-boot-animation'
```

Run it from the **new** unpacked release in normal Windows PowerShell. It requires
the target to match the existing sign-in entry and hook definitions. It saves
the old EXE, stops/restarts only that helper, verifies all three processes, and
restores the old EXE on failure. It never overwrites your video library or edits
hook trust. This is a companion-only update, not a plugin metadata/cache reinstall.
Keep the backup and new source for reproducibility. To move the installation
directory, first remove the old sign-in entry deliberately, migrate your
videos, then install from the new permanent directory; do not run two helpers.

## Existing installations

If another `CodexBootAnimation` sign-in entry already exists, the installer
stops instead of taking over the running helper. First identify that old
installation and migrate it deliberately; do not run two supervisors with the
same named mutex. This protects a working setup from accidental replacement.

To remove **this repository's** sign-in entry, run
`scripts\Uninstall-Startup.ps1` from a normal Windows PowerShell window. It
refuses to delete an entry pointing somewhere else. Removing the Codex plugin
or its marketplace is a separate operation.

## Manual acceptance check

1. Fully exit Codex, including its tray icon. The companion remains running.
2. Open Codex: expect exactly one intro.
3. Click New chat twice, skipping the first: both should open immediately and
   start from the correct first frame.
4. Put different MP4s in `冷启动` and `新聊天`. Restart the helper: each
   trigger should use only its own folder. With two New chat clips, repeated
   clicks should eventually show each clip.
5. Right-click Codex's own tray icon: no intro should play.
6. Reboot Windows, then repeat the Codex-open and New chat checks.
7. If the new `UserPromptSubmit` hook has been trusted, send one clear direct
   insult to Codex and verify the separate reaction clip. Ordinary criticism,
   examples and discussion of the feature should not trigger it.
8. After trusting `Stop`, issue a concrete task. A clear completed response
   should play `任务完成` within the Codex window when Codex is foreground.
   When using another app or minimizing Codex, a qualified completion should
   immediately open an audible, skippable bottom-right notification without
   switching you back to Codex. Verify both with real tasks, not tray previews;
   click the notification's Skip button in a separate check. A greeting or
   failed task should not play. The filter is intentionally conservative and
   can miss some valid completions; event delivery and decoding take time.
9. Leave the Windows session without input for 15 minutes. Return with mouse
   movement and a click on Codex, then stop for two seconds. Expect one idle
   video only in Codex; any further input closes it. Continued work for over
   one minute should suppress that return, not interrupt work much later.

The package's tests include the idle state machine, process-instance claims,
media completeness and both hook stdin contracts. They do not replace a real
second-PC or Windows sign-in test. This release is labeled alpha accordingly.
