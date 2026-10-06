# Changelog

## Unreleased

## 0.7.0-alpha.5 — local release candidate (2026-10-06)

- Package the accepted completion-delivery baseline without rebuilding the
  executable or changing playback. Synchronize native/portable manifest versions
  and release guidance; retain earlier ZIPs. Local package verification status is
  recorded separately; no upload or cross-PC stability claim.

- Completion-delivery reliability patch: reserve anonymous requests
  separately, claim/consume only after durable playback-start or merge receipt;
  permit at most two retries in a two-second budget. Separate acknowledgement
  IPC merges simultaneous tasks without video overlap, preserves continuation
  identity across newer input and rejects stale installed executables. Explicit
  early Skip/Esc dismissal never retries into a new window. Existing
  candidate/claim formats and other scenes remain unchanged. Both PowerShell
  runtimes pass all 40 groups, with five extra isolated delivery repeats. Backed-up
  installed/cache deployment and helper-only restart passed; user accepted the
  notification and new-chat protection on 2026-10-06. A natural task completion
  reached accepted playback with audio and ended normally. No public package,
  second-PC guarantee or permanent exactly-once claim; see
  docs/COMPLETION-DELIVERY-2026-10-05.md.

- Freeze the previously tested reaction-flow and intro-recovery changes as
  separate source commits, then rebuild the package executable and run all 39
  groups under PowerShell 7 and Windows PowerShell 5.1. This is source/package
  archival only: no live deployment, connection/default-install change, or new
  visual acceptance. See docs/FREEZE-2026-10-05.md.

- 记录本机输入反馈连接规避验收：用户确认延迟可接受并批准保留，3个桌面
  新对话采用新提供方，配置及32保护文件复核通过。无播放器或默认安装变化；
  完整退出重开/跨电脑未验证，不宣称永久根治。个人配置/备份不提交。
  见 docs/CONNECTION-WORKAROUND-ACCEPTANCE-2026-10-05.md。

- Disable unnecessary paused-seek frame scrubbing for new-chat intro media,
  retaining startup and other scene policies, timing, media and sound. Extend
  isolated decoder checks to require changing non-black pixels through initial,
  recovered and healthy-cache reuse playback. Both PowerShell runtimes pass all
  39 groups; helper-only backed-up deployment and readiness checks pass. User
  accepted three real new-chat plays with sound, no picture freeze or stale-frame
  flash. Preserve 113 protected files, settings, startup/trust and Codex process
  identities. The intermittent rendering root cause and long-resident stability
  remain unproved; do not claim permanent resolution. This patch is not an anger
  hook delivery fix. See docs/NEW-CHAT-MEDIA-RECOVERY-2026-10-05.md.

- Recover failed resident intro media on the next trigger instead of retaining
  an unusable slot indefinitely. Retire the old identity before releasing it;
  preserve healthy preloads and bound retries to explicit requests. Hide the
  overlay before reset, handle media setup/start errors, and make timeouts
  recoverable. Add synthetic late-error and hidden real WPF recovery tests.
  Both PowerShell runtimes pass all 39 groups; backed-up helper-only update and
  protected-file checks pass. User accepted three real new-chat/skip cycles; the native
  HRESULT cause is not claimed fixed. See docs/NEW-CHAT-MEDIA-RECOVERY-2026-10-05.md.

- Dispatch matched anger reactions before task-state recording; preserve state
  in a synchronous finally block without changing storage, locks or classifiers.
  Both PowerShell runtimes pass all 37 groups, plus 10 isolated flow repeats.
  Backed-up deployment and one synthetic production playback pass; actual
  cold-start send acceptance is pending. Same-turn evidence shows upstream
  preparation before hook invocation, which this patch does not eliminate.
  See docs/ANGER-LATENCY-2026-10-04.md. Source archived separately as 447d472;
  the old upstream-delay limitation is not a claim of a new playback fix.

- Move watcher log/journal reconciliation off the click message loop into one
  non-backlogged background job; re-arm the click hook per new desktop identity.
  Preserve playback, discovery frequency, recognition, state ordering and media.
  Both PowerShell runtimes pass all 36 groups; backed-up local deployment and
  readiness/IPC pass. Real next-boot click acceptance remains pending. Startup
  network warnings and pre-hook reaction delay are not claimed fixed. See
  docs/COLD-START-REACTION-2026-10-04.md.

- Add anonymous stage timing for automatic anger reactions, retaining the pipe
  command and playback/filter decisions. Buffer hook writes until after dispatch
  and flush resident diagnostics asynchronously. Record fixed stages/results,
  bounded per-segment counts and process-origin correlation without prompt text,
  prompt hashes, session identifiers or media paths. Add isolated identity/privacy
  regressions; both PowerShell runtimes pass all 35 groups. User confirmed normal
  picture/audio for one actual send; its correlated stages end in normal closure.
  This is instrumentation, not a
  cold-start latency or missed-display fix. See docs/REACTION-TRACE-2026-10-04.md.

- Extend local anger recognition for directed ability/attitude criticism,
  repeated-failure complaints and explicit disappointment. Guard additional
  technical nouns and narrowly neutral fragments without suppressing hostile
  follow-up clauses; distinguish plain negation from rhetorical insults.
  Preserve the previous fixtures and add 98 recognition fixtures plus five
  task/reaction classification checks. Both PowerShell runtimes pass all 34
  groups; user accepted the first trigger, the second after isolated retest and
  the normal-request exclusion. Preserve the initial missed-display report;
  its cause is unresolved, not fixed by the successful retest. No playback,
  prompt dispatch, startup, hook trust or timing changes.
  See docs/ANGER-EXPANSION-2026-10-04.md.

- Expand local anger recognition for colloquial Chinese intensifiers/suffixes,
  direct belittling and short strong complaints; normalize compatibility forms
  such as full-width SB. Preserve translation/quotation/context exclusions and
  guard simple praise/negation, technical garbage nouns and USB references.
  Add 47 positive and 40 negative fixtures to the existing anger regression,
  retaining the original cases and anonymous case-index logging. Both PowerShell
  runtimes pass all 34 groups; user confirmed two actual message sends each
  played with picture and sound. This is not cold-start latency acceptance.
  This changes recognition only, not prompt dispatch, cold-start timing or
  playback. See docs/ANGER-DIAGNOSTICS-2026-10-03.md.

- Reserve a 48-DIP original-title-bar strip for owned anger, idle and foreground
  completion videos, instead of masking Codex's drag area. Keep fullscreen intros,
  independent completion notices, triggers, audio and preferences unchanged.
  User confirmed title-bar access and dragging but reported flashing during
  movement. Replace unconditional timer positioning with exact-owner,
  process/thread-scoped out-of-context location events, coalesced refreshes,
  unchanged-bound suppression and move-only positioning. Dispose subscriptions
  on close; keep changed-only timer fallback if registration fails. Add geometry,
  hidden synthetic-owner event/movement, filtering and cleanup regressions;
  both PowerShell runtimes pass all 34 groups. User confirmed smooth dragging,
  no flashing and working Skip in the actual anger preview. Do not extrapolate
  that acceptance to every scene or physical multi-monitor/DPI setup.
  Backed-up companion update does not restart Codex;
  see docs/DISPLAY-ACCEPTANCE-2026-10-03.md.

- Replace only the companion notification-area icon with the supplied artwork,
  preserving transparency in nine icon sizes. Retain the original square
  artwork; after the user found its full-body tray rendering unreadable, generate
  a head-only rabbit-ear/white-hair/red-eye design with the built-in image tool.
  User approved its design before integration, then preferred the original
  full-body artwork after trying it. Restore the original icon and converter
  default, retaining the compact design as an alternative; no subsequent C# changes.
  Embed the ICO as a managed resource so runtime never depends on Downloads or
  asset paths. Own/dispose the cloned icon and fall back to the system icon on
  resource errors. Keep executable/window/plugin-list icons, playback, settings,
  triggers and hook trust unchanged. Add resource and failure regressions; both
  PowerShell runtimes pass 32 groups. Companion and cached fallback EXE updated
  with backups; live resource loading, IPC, hook trust and untouched user data
  verified. The latest diagnostic could not read the compatible CLI/plugin state
  (UNKNOWN); do not claim every diagnostic passed. User accepted the restored
  full-body tray appearance; no separate manual menu-operation evidence was
  reported. See docs/TRAY-ICON-2026-10-03.md. Artwork redistribution
  licensing remains a separate pre-publication decision.

- Add independently persisted background completion-notice size/corner presets
  and a Chinese tray editor with unsaved-selection previews. Preserve the
  validated Large / BottomRight default, immediate automatic delivery, Codex
  foreground presentation, audio settings and non-activating/skippable windows.
  Reject conflicting/invalid saves and keep previous-version backups without
  migrating scene preferences. Both PowerShell runtimes pass 31 regression groups;
  live companion/cache updated with backups. User confirmed preview placement
  and sizes, and save/reopen persistence. An explicit one-shot task-player test
  loaded the shared saved layout and played to end. User confirmed correct layout,
  picture/audio and no switch back to Codex; retain the foreground-HWND-change
  log without claiming its cause was established. This is not a real model-complete
  event or proof of multi-monitor or second-PC behavior.

- Add Chinese per-scene automatic enablement, mute and 0–100% volume settings
  with immutable playback snapshots, a shared bounded/versioned JSON store,
  conflict rejection and atomic previous-version backups. Preserve startup 85%
  / other scenes 65% defaults, legacy idle-off compatibility, global automatic
  pause, explicit previews, existing rates, routing and window placement.
  Changes apply to next playback, without file reads on the click path. Add
  storage/editor/player-parameter regressions; both PowerShell runtimes pass
  30 groups. Update only the companion and cached fallback EXE with backups;
  Codex, media, hook definitions/trust and startup remain unchanged. Actual
  user acceptance confirms immediate muted New chat playback, disabled automatic
  New chat playback, working explicit previews while disabled, and restored
  automatic enablement. Remaining scenes, restart persistence and multi-DPI/PC
  cases remain separate; do not treat fixtures as all-trigger validation.

- Add an on-demand Chinese, read-only MP4/stream/audio/decode/first-frame checker and CLI/JSON entry point. Keep missing tools/timeouts explicitly unknown. Validate imports before copying, refuse existing video/cover names, and bound no-overwrite cover extraction. Prefer a shared UserProfile tools directory over redirected AppData after the user's Explorer-launched check exposed missing-tool results. Add real FFmpeg fixtures and dual-PowerShell regressions; keep optional GPL tools separate from the repository. Do not change the player, trigger hooks, startup, trust, media or playback settings. Independent external-process checks and the user's Explorer-launched retry passed: 12 videos, zero failed/unknown items and seven missing-cover warnings, with no automatic cover changes. This is checker acceptance, not revalidation of every animation trigger.

- Add Chinese read-only diagnostic labels, status counts and actionable explanations to check.cmd output, preserving detection and the existing JSON fields/statuses. Keep unknown future values visible and distinguish pending manual acceptance from failure. Add a dual-PowerShell presentation/data-preservation regression; do not update the player, hooks, startup or media.

- Show qualified automatic completion events without waiting for Codex foreground: retain the foreground-owned view, and use an immediate bottom-right audible, skippable, non-activating notification when Codex is background/minimized. Reuse media cleanup and preserve detection, deduplication, pause, manual previews and other scenes. Add routing/single-attempt/layout-DPI/native-style/skip regressions. User saw the background notification but found it too small; enlarge only its layout to 50% working-area width (height capped at 55%), with a regression that rejects the previous small layout. User confirmed the enlarged view, picture/audio and no switch back to Codex in a synthetic Stop-chain test; a real model-task completion and on-screen Skip remain separate acceptance items.

- Separate manual idle previews from passive automatic idle playback: prepare Codex after the tray menu closes, request a single acknowledged resident preview, and report busy/missing/unavailable failures. Manual previews accept Skip/Esc and survive input; automatic idle thresholds, quiet-return policy, input dismissal, and arbitration stay unchanged. Add isolated reply and unshown-WPF/native-style/skip regressions; user confirmed real tray playback, survival through mouse activity and effective Skip. Logs separately confirm two normal picture/audio playthroughs, not an on-screen skip event.

- Separate manual completion tray previews from automatic task playback: prepare the verified Codex foreground after the menu closes, attempt once, and report rejection instead of leaving a 90-second deferred preview. Keep automatic completion waiting/detection and idle previews unchanged. Add single-attempt success/failure regressions; user confirmed prompt real tray playback with picture/audio and normal-end logs.

- Prepare the verified Codex foreground window only for the manual anger tray preview, after closing/unwinding the menu. Report activation failure rather than silently discarding the request. Automatic triggers and other preview entries remain unchanged in this step; add deferred-dispatch/failure-path regressions. User confirmed the real tray preview, with moving-frame/audio/normal-end logs.

- Stop resident-owned anger/idle windows before a valid startup or New chat intro takes over, releasing old media before showing the intro. Preserve anger when the intro is missing or already failed, and keep the existing trigger/speed/volume paths. Add a real unshown-WPF close/idempotence regression; two live IPC preview transitions were logged and the user confirmed no visual/audio overlap. This is not new-click/cold-start event or independent-PC acceptance.

- Keep the resident alive with both intro folders empty, without a visible/activated shell or null-media failure. Preserve independent idle/reaction delivery and the existing fullscreen state when an intro is later added. Add an isolated empty-library WPF/IPC regression without moving user videos.

- Retain incomplete navigation-log lines until a newline arrives, including split UTF-8/CRLF, so file-write notifications cannot discard new-chat/frontend-ready events. Keep cached-click, deduplication and startup UIA gates unchanged.
- Scope resident IPC to the current user/session with an explicit protected ACL, 1-second absolute read deadline and 256-byte cap. Add isolated stalled/slow/oversized-client recovery and normal-message regressions; no hook definition, trust, media or Codex host changes.

- Raise only cold-start and its tray preview from 65% to 85% player volume at the user's request. Keep every other scene at 65%, playback speed and trigger behavior unchanged; add scene-isolation regression assertions. No media re-encoding or Windows system volume change.
- Startup safety: replace eager repeated full-tree UIA scans with current-process navigation readiness, foreground gating, isolated bounded probes and failure circuit breakers; keep cached mouse-click playback. Observe both physical and packaged desktop log roots. Add readiness/timeout/moving-layout regressions and a startup-disabled safety-update mode. User reported current tests passed; logs confirmed three separate Codex cold starts, one intro each and subsequent frontend readiness. Windows reboot/independent-PC acceptance remains pending.
- Keep companion runtime state/logs in `%USERPROFILE%\.codex-boot-animation` so packaged hooks and the independent helper share the same files. Explicit whitelist migration preserves old logs/media, backs up overwritten state, rejects ambiguity and supports rollback. Shared-file probes and non-UI tests passed; subsequent cold-start failure remains a separate open incident.
- Fix companion lifetime after Codex exits: detach a host-bound supervisor using local WMI, verify session/job ownership, recover the registered supervisor from SessionStart, and reject attached readiness. Add a real kill-on-close host regression with a normal-child control. User confirmed full exit/reopen and New chat playback. See the dated incident record; shared AppData isolation was discovered separately and needs its own fix.
- Recognize short Chinese insults and family-directed profanity without a preceding addressee, along with stronger complaints about repeated bad work. Keep quoted examples and positive exclamations excluded; preserve task-completion candidacy when an angry message also requests a repair.

## 0.7.0-alpha.4 — 2026-10-01

- Keep the clean-install integration home in a short Windows temporary path. Extracted-package verification caught Windows PowerShell 5.1 failing to enumerate a cache nested below a deep Chinese/spaced extraction directory. The real package cache and hook-discovery checks now use a separate short home without changing the user's Codex configuration.
- Bound native CLI inspection to 15 seconds and registration/install commands to 60 seconds after a real diagnostic plugin-list query stalled. Stop only the owned command on timeout, report unavailable state and preserve the rest of the diagnostic checks. Add native quoting and timeout regression cases.
- Send hook-inspection RPC through an explicit UTF-8 writer on .NET Framework. Windows PowerShell 5.1's legacy stdin encoding caused the app-server query to exit for Chinese installation paths; the same clean installation passed after the encoding fix.
- Retain alpha.3 deployment fixes. The failed alpha.3 archive is retained locally; alpha.4 is the package intended for distribution after extracted-package acceptance.

## 0.7.0-alpha.3 — 2026-10-01

- Fix a clean-install compatibility failure on CLI 0.159.2: the root portable manifest installed/enabled successfully but yielded zero runtime hooks. Use the native compatibility manifest with an explicit hook path, retain portable reference metadata outside the plugin, and verify all three runtime hooks are discovered before startup registration.
- Add read-only active-installation diagnostics for dependencies, plugin enablement, actual hook trust, helper/IPC health, active/cache paths, binary mismatches and whitelisted trigger reasons. Keep independent-PC/reboot and physical playback checks pending rather than treating diagnostic success as full acceptance.
- Reconcile session journals across both local and UTC date directories so UTC midnight in western time zones cannot hide a newly completed turn.
- Prefer the desktop-bundled CLI and fall back past incompatible CLI candidates. Check required commands and marketplace conflicts before changing installation settings.
- Verify plugin installation/enabled state. Installation and companion updates wait up to 20 seconds for supervisor, watcher, resident player and IPC readiness; roll back new startup registration/helper processes on failure while retaining a preexisting entry.
- Add `check.cmd`, an incomplete-ZIP extraction hint and a short bilingual setup guide.
- Add installation regression groups for CLI fallback, slow/missing helper readiness and startup rollback. Verify a real plugin registration/install into an isolated empty Codex home, including cached version, EXE and all eleven videos.
- Preserve video presentation, speed, volume and hook command definitions. This version also includes the separately committed anger-clip rotation fix (`4dd6a14`). Independent second-PC and genuine Windows reboot acceptance remain pending.

## 0.7.0-alpha.2 — 2026-09-30

- Preserve task-kind state across automatic continuation turns, while allowing new user input to replace it. Recognize concise requests such as “解决吧”.
- Recover confirmed long-task completion from the journal even if no prompt-hook state exists. Keep short chats, reaction-only requests, failed replies and duplicate events excluded.
- Do not mistake a pending independent second-PC/reboot acceptance caveat for failure of an otherwise completed repair; retain actual failure checks.
- Correlate anonymous completion delivery and player logs. Add production-path continuation/concurrency regression tests and a real-journal playback verification script.
- On the current PC, replay a previously missed real 23-minute task-completion record through detection, delivery and playback; confirm moving picture, audio track, normal ending, and user-observed picture/sound. Independent second-PC/reboot acceptance remains pending.

## 0.7.0-alpha.1 — 2026-09-30

- Debounce natural return input instead of canceling idle playback on a second mouse/key event. Preserve the opportunity for up to one minute while background/busy; consume only acknowledged resident acceptance, with idempotent retries.
- Persist desktop process-instance claims to handle Windows sign-in startup races and suppress replay after watcher recovery. Explicit updates adopt already-running instances.
- Bundle eleven explicitly CC BY 4.0 licensed clips and covers, covering all five scenes. Remove the old New chat duplicate from the active startup folder.
- Add portable plugin metadata, safe companion binary updates with rollback, immutable Git release ZIPs/checksums, and a Windows CI package artifact.
- Verify Windows PowerShell 5.1 script encoding and a clean package extracted to a Unicode/space-containing path. Mark independent second-PC validation as pending, not verified.

- Use the task-completion scene's 65% player volume for every scene, including cold start and idle. Add a non-destructive FFmpeg staging script to match the encoded loudness of future videos to a task-completion reference.
- Add a separately randomized `闲置互动` scene. After 15 minutes of Windows-session inactivity, a quiet return to foreground Codex can show one click-through video covering only the Codex window; input and competing animations dismiss it. The tray can preview or disable the scene without recording input contents.
- Play all anger-reaction clips at their original 1.0× speed; cold-start and New chat intros remain at 1.2×.
- Remove the post-playback anger cooldown; suppress only while an intro/reaction is actively visible or automatic reactions are paused.
- Keep an owner-bound anger clip alive through transient focus changes instead of closing it immediately after a prompt; ignore late media events once its window has closed. Task-completion clips retain their focus-loss behavior.
- Ignore structured questionnaire replies when matching anger prompts, so an assistant-authored test question containing an insult cannot start a clip before the user's real message.
- Keep every preloaded resident video hidden until selected, including after a video-library reload. Switching scenes now hides and mutes all other media layers, preventing a stale startup picture over an anger clip's audio.
- Close a queued full-screen playback if its MP4 fails to open or loading times out; do not show a known-failed clip until the library is reloaded.
- Exclude insult-only test messages from task-completion candidates, while preserving explicit repair requests that also contain an insult.
- Add a resident scene-switch regression test covering reload and subsequent selection.
- Play anger reactions in a skippable window owned by Codex instead of covering the whole monitor. Reuse the task-completion window's aspect-ratio-safe, blurred-backdrop presentation; keep cold-start and New chat full-screen.

## 0.6.4 — 2026-09-30

- Treat a turn lasting at least two minutes with a confirmed completion reply
  as a task even when its initial prompt looked like a question.
- Keep short question-and-answer exchanges excluded.

## 0.6.3 — 2026-09-30

- Hold a confirmed completion for up to 90 seconds if another app is in front,
  then play it when the Codex window regains focus instead of dropping it.
- Keep the completion overlay owned by Codex and avoid covering other apps.

## 0.6.2 — 2026-09-30

- Recover task-completion playback from Codex's local `task_complete` session
  record when the desktop does not invoke the `Stop` hook.
- Deduplicate journal and hook delivery with an atomic per-turn claim.
- Keep the journal watcher in the existing tray helper, without changing the
  startup or New chat playback paths.

## 0.6.1 — 2026-09-30

- Keep questions about completed tasks from being mistaken for new task requests.
- Recognize natural completion summaries such as “已经按规则改好” while
  excluding future plans and explicit failure language.
- Log anonymous entry and suppression reasons for the `Stop` hook, so a
  missing hook run can be distinguished from a rejected completion.

## 0.6.0 — 2026-09-30

- Give startup, New chat, anger reaction and task completion their own MP4
  folders. One clip plays consistently; multiple clips are random within the
  same scene, with no cross-scene fallback.
- Rescan scene folders after changes and release old media objects so replacing
  clips does not accumulate stale players.
- Move video routing to a dedicated source file and retire the old
  filename-based selection state.
- Route task-completion hooks through the installed resident before launching
  the proven one-shot window, so plugin caches do not normally freeze media
  choices.
- Fill task-completion letterbox space with a dimmed, blurred first-frame
  backdrop while keeping the foreground video uncropped.

## 0.5.0 — 2026-09-30

- Add a separate task-completion clip from a local MP4 library, with a tray
  preview and optional video selection.
- Use per-turn prompt/Stop hooks to suppress greetings, short chats and
  unsuccessful replies. Only a one-bit task flag is stored temporarily.
- Limit completion playback to the foreground Codex window instead of the
  whole display; close it when Codex loses focus.

## 0.4.1 — 2026-09-30

- Recognize directed colloquial profanity and frustrated complaints such as
  “你特么”, “你到底行不行”, and “越改越烂”.
- Keep standalone exclamations and quoted/example text from unexpectedly
  opening the full-screen reaction; add positive and negative regression cases.

## 0.4.0 — 2026-09-30

- Add an optional local emotion-response video, triggered by clear insults or
  strong disparagement directed at Codex when a message is submitted.
- Keep submitted prompt text out of logs. Ignore meta-discussion examples,
  avoid interrupting an active intro, and apply a 30-second reaction cooldown.
- Add a tray preview and video-role choice for the reaction, with descriptive
  names for the personal clips in the local video library.

## 0.3.0 — 2026-09-30

- Assign a separate fixed video to Codex cold starts and up to two videos to
  New chat. New chat chooses randomly between its two configured candidates.
- Preload all configured videos in the resident player so choosing a different
  clip does not add video-loading delay to the click path.
- Keep manual tray configuration, first-frame covers, sound, Skip/Escape and
  restart-persistent filename-only choices.

## 0.2.0 — 2026-09-29

- Add a dedicated MP4 library and tray-based manual video selection. The
  selection survives restarts, and the resident reloads the chosen video.
- Add a repository-scoped Codex marketplace, a prebuilt companion, and a
  preflighted installation script for another Windows computer.
- Keep newly added personal videos out of Git by default until their
  redistribution rights are reviewed.

## 0.1.0 — 2026-09-29

- Detect Codex desktop cold starts and new-chat mouse clicks with a persistent
  Windows companion; retain log-based navigation detection as a fallback.
- Preload the player to reduce click-to-video latency, play with sound at 1.2×,
  and provide Skip/Escape controls.
- Suppress duplicate startup/navigation triggers and avoid false playback when
  opening the Codex tray menu.
- Reset playback to the start and cover stale decoded frames with the exact
  first video frame before each replay.
- Keep the companion across Codex exits and Windows logins through a user-level
  sign-in entry. The SessionStart hook is recovery-only.

This changelog records product changes, not personal runtime logs.
