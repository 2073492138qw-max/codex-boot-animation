# Architecture

## Components

1. `BootPlayer.exe --supervise` is the Windows sign-in process. It keeps one
   watcher alive independently of the Codex app lifecycle.
2. `--watch` owns a tray icon, checks Codex process identity, tracks the New
   chat button through readiness-gated, bounded `--button-probe` children,
   listens for mouse release using cached button rectangles, and reads local
   Codex navigation logs as a fallback. It does not log chat text.
   UIA never runs on its mouse/message loop or before current-process navigation
   readiness. Background/minimized windows are not queried; a timeout stops
   discovery for that window, and empty results get at most two attempts.
   The probe has both its own watchdog and a parent kill deadline. Physical and
   packaged log roots are observed; stale process logs cannot establish readiness.
   `NavigationLogReader` advances byte offsets only past complete LF-terminated
   lines. An incomplete UTF-8/CRLF tail survives file notifications and initial
   watcher seeding; completed historical lines are not replayed.
   Five-second log/journal reconciliation runs as one coalesced background job,
   not on the mouse hook's message loop. `WatcherReconcile` rejects queued
   duplicates and stops accepting work on exit. Each newly observed desktop
   process identity re-arms the click hook, because Windows can silently remove
   a timed-out low-level hook. Re-arming does not itself trigger playback or
   increase UIA discovery frequency. Actual restart/click acceptance remains
   separate from the component regression (COLD-START-REACTION-2026-10-04.md).
3. `--resident` preloads startup and New chat clips
   from their separate scene folders in hidden WPF `MediaElement` instances and
   receives trigger reasons through a local named pipe. A cold `--play`
   process is a fallback when the resident is unavailable.
   `ResidentProtocol` scopes the pipe name to the user SID and Windows session,
   applies a protected current-user ACL, and bounds each connected read to
   1 second and 256 bytes. Timeout closes only that connection, allowing the
   next client. Hook and companion binaries must use the same protocol version.
4. `hooks/hooks.json` registers a `SessionStart` recovery command. This hook
   is deliberately not the click trigger; it can run only after a session
   begins, which is later than opening an empty chat page.
   `UserPromptSubmit` dispatches a matched anger reaction before unrelated
   task-state bookkeeping, then records that state synchronously in `finally`.
   `PromptReactionFlow` creates no new worker and leaves state formats, locks,
   classification and playback policy unchanged. It removes a dispatch-time
   state-lock wait, not Codex's preparation before invoking the hook.
5. `CompletionStateStore` records a task-kind flag keyed by a hash of
   the session and turn IDs, plus a session-scoped flag and hashed origin key.
   Automatic continuation turns inherit this state; new user input replaces
   the scope. A per-session mutex protects state transitions. The `Stop` hook normally consumes it. If Codex
   desktop writes `task_complete` to its local session journal without running
   the `Stop` command, the resident watcher consumes the same flag as a
   fallback. The journal path also accepts a turn lasting at least two minutes
   when the prompt classifier missed a task or the prompt hook did not run. Both paths
   check the final answer for affirmative completion (excluding failure
   language and future plans). The reliability patch reserves an
   anonymous delivery separately, leaving the old candidate intact until an
   automatic completion host persists accepted/merged receipt (or dismissed for
   an explicit early Skip/Esc, which never reopens the same reminder). Delivery permits
   an initial attempt plus two retries within a two-second budget, without a
   foreground queue. Concurrent distinct tasks merge only into a live automatic
   completion window. A separate per-user/session acknowledgement pipe avoids
   blocking resident intro IPC on decoding. The Run-registered player must match
   the sender bytes; stale versions fail closed instead of misreading new modes.
   Anonymous final/origin aliases protect receipt recovery after newer input;
   partial/orphaned reservations fail closed, not perpetual retry. Existing
   candidate/claim formats remain untouched. See COMPLETION-DELIVERY-2026-10-05.md;
   locally deployed and accepted on 2026-10-06; cross-PC validation remains pending.
   Consumed candidate state is deleted; empty claims suppress duplicate
   journal/hook/origin-turn delivery. State older than one day is pruned on prompt input.
   This is a conservative heuristic, not a semantic success signal from Codex.
   Each `Stop` invocation logs only whether the required fields exist and why
   it was suppressed; it never logs the prompt or final answer text. The
   session-journal format is an internal Codex detail and must be regression
   tested on future app versions.
   A pending independent reboot/second-PC acceptance caveat is distinguished
   from failure of the main task; actual failure language is still rejected.
   `CompletionFlowTests` exercises these production paths without opening a
   video. `Verify-Completion.ps1` separately replays a real journal record and
   correlates its delivery with moving-picture, audio-track and normal-end logs.
6. The one-shot completion player and optional anger reaction share an owned,
   borderless window below a reserved 48-DIP strip at the top of the foreground
   Codex window, using the existing WPF DPI context. The strip leaves room for
   the original title bar; actual custom-title-bar/DPI behavior requires manual
   acceptance. Owned videos follow process/thread-scoped, out-of-context
   EVENT_OBJECT_LOCATIONCHANGE notifications for the exact owner HWND only.
   Pending refreshes are coalesced on the existing WPF dispatcher; unchanged
   bounds issue no positioning request and pure moves use SWP_NOSIZE.
   The subscription is removed on the same UI thread when the video closes.
   The existing 50-ms timer retains lifecycle/input checks, using changed-only
   positioning as fallback only if native subscription registration failed.
   No UIA queries, injected DLL, new polling thread or foreground activation
   are introduced by this follow path;
   task completion closes if Codex loses focus, while anger continues behind
   other apps until it ends or is skipped. It is separate from the monitor-wide resident intro player.
   Before a valid startup/New chat intro shows (including its loading cover),
   the resident closes its anger/idle windows. Closing releases their media
   synchronously; references are cleared before Closed callbacks. The same
   idempotent cleanup guards delayed MediaOpened playback. Missing/known-failed
   intros leave anger alone. This does not arbitrate the separate completion player.
   The video keeps its full aspect ratio; a dimmed, blurred first-frame cover
   fills any remaining space instead of stretching or cropping the video.
   The legacy `--task-play` performs one immediate attempt via `CompletionPlayback`;
   the automatic `--completion-host` uses the same creation/presentation:
   foreground Codex uses the owned view; not-codex-foreground uses an independent
   topmost, non-activating notification using the saved size/corner preference,
   including when Codex is
   minimized. It has no Codex owner/focus-loss gate and therefore cannot wait
   for or disappear with a minimized owner. It reuses the same media lifecycle,
   ratio/blur, speed and volume, with a clickable non-focusable Skip button.
   Layout uses the active display's working area/native pixels and WPF DPI scale.
   Small/Medium/Large widths are 30%/40%/50% of that working area, clamped by
   35%/45%/55% height caps, with a 16:9 viewport. Saved BottomLeft/BottomRight
   placement stays inside the working area, outside the taskbar. The default
   remains Large/BottomRight; actual saved preferences may differ. The original
   Large preset replaced the initial 640-DIP cap after user feedback.
   No global keyboard hook or foreground activation is used. Event dispatch and
   decoding still take time; no zero-latency or secure-desktop promise is made.
   The manual completion tray menu closes first, prepares a verified Codex
   foreground once, and uses `--task-preview` for one creation attempt only.
   It shares the completion pending mutex but never queues a delayed preview;
   rejection is reported to the tray. Neither path waits for Codex foreground.
7. `IdleReturnPolicy` reads only the Windows session's last-input tick. At 15
   minutes it arms one return. Further return input restarts a two-second quiet
   timer. Foreground/busy checks defer acceptance for at most 60 seconds from
   the first return. The watcher requests playback on a worker thread; an
   acknowledged, idempotent request consumes the episode only after acceptance.
   This request does not block the UI/mouse fast path. The resident applies scene priority
   and opens a non-activating, input-transparent `闲置互动` video covering only
   the foreground Codex window below the same reserved title-bar strip.
   A new input, Codex minimization, or another scene
   dismisses it. No key content, pointer coordinate, or input history is read
   or persisted. The tray idle toggle and scene editor share the same idle
   preference; the legacy off flag is retained only as a pre-JSON default.
   The manual idle menu uses `PreviewFocus` and a single acknowledged
   `__idle_preview_request__` over the same private/bounded resident pipe.
   The reply reports acceptance or rejection without waiting for foreground.
   Only `preview-idle` uses an interactive window (Skip/Esc, no input dismissal);
   automatic idle remains passive. Both share resident ownership and the same
   busy/intro interruption rules. Client work is off the watcher UI thread.

```text
Windows sign-in -> supervisor -> watcher -> named pipe -> resident player
Codex process ------^        ^
New chat UI click -----------|
Codex navigation log --------|  (fallback)
SessionStart hook -> ensure watcher only
```

## Replay and de-duplication

The resident keeps a still PNG extracted from the video's first frame above
the media surface. Before each replay it pauses, seeks to zero, shows the
cover and then starts playback. The cover disappears after the newly decoded
video has advanced. This prevents a previous end frame flashing at the start.

The watcher records recent click and desktop-start triggers. A later log-based
notification within the suppression window is treated as a duplicate; a
subsequent explicit New chat click is still allowed to play.

`DesktopInstancePolicy` claims each desktop PID/start-tick pair in the current
user's shared `.codex-boot-animation` runtime directory. This removes the old one-second start-time
cutoff that missed Codex when Windows launched it before the helper. Claims
survive watcher recovery and distinguish PID reuse. An intentional installation
or binary upgrade adopts already-running Codex instances without playing an
intro. Tray menus do not create a new process identity and cannot trigger it.

## Video selection

`MediaLibrary.cs` maps each trigger to exactly one directory: `冷启动`,
`新聊天`, `生气回应`, `任务完成`, or `闲置互动`. It enumerates only top-level `*.mp4` files in
that directory. One file is fixed; two or more are chosen randomly; an empty
folder disables only that scene. Even if BOTH startup and New chat folders are
empty, the resident remains hidden and keeps IPC available for independent
Codex-window scenes. An empty initial shell is normal/non-activating; an actual
intro restores the existing activated/maximized playback state. There is no cross-scene fallback and no
filename-specific routing. The old `*-video.txt` settings are ignored.

The watcher observes video file changes with a 1.5-second debounce and asks
the resident to reload. Reload releases old `MediaElement` instances and
prepares current startup and New chat clips. A reload during playback is
deferred until playback ends. Anger, idle, and completion media are loaded only
when their Codex-window players open. The tray also offers a manual rescan.

The `UserPromptSubmit` hook receives a JSON payload on stdin. A bounded local
filter reads only its `prompt` field and sends a reaction reason through the
same pipe. It writes no prompt text to disk or hook output. Meta-discussion
examples are excluded. The resident suppresses reactions while another intro
or reaction is visible, but applies no cooldown after playback ends. The anger clip plays only over
the foreground Codex window, not over the whole monitor. No keyboard hook is used.

Automatic reactions also write best-effort anonymous stages to the separate
runtime `reaction-trace.log`. `ReactionTrace.cs` accepts only fixed phase/result
values and a validated process-origin identifier, never prompt text, prompt hashes,
session identifiers or media paths. `ReactionPeer` queries the pipe client's PID
and holds a limited-query process handle for its creation time; unavailable
identity is marked rather than inferred. The pipe command bytes are unchanged.
The existing fallback player accepts an optional validated diagnostic identifier.
Hook records flush after dispatch; resident/window records use the existing thread
pool rather than synchronous new UI-path file writes. Each segment is bounded to
32 records; file contention has bounded retries and failure never changes playback.
Cross-process analysis uses event UTC times, not append order or independent segment
stopwatches. The existing moving-frame marker (media position over 0.18s, checked
at 50ms intervals) is not a precise first-presented-pixel timestamp. Timing begins
at hook entry, not the user's send click. See REACTION-TRACE-2026-10-04.md.

For a video named `example.mp4`, an optional `example-first.png` in the same
scene folder is used as the
exact first-frame cover. Without it the cover is black, still hiding stale
decoded frames. Additional personal videos are ignored by Git by default.

## Scene preferences

`SceneSettings.cs` owns immutable five-scene snapshots and the bounded,
versioned `%USERPROFILE%\.codex-boot-animation\scene-settings.json` store.
`SceneSettingsForm.cs` is a non-modal Chinese editor: save enablement, mute and
volume for next playback; closing without saving changes nothing. It does not
change media, system volume, playback rate or window placement. Automatic-off
is enforced at dispatch and actual player entry, while explicit previews remain
available. Global automatic pause remains independent and has priority.

The click/playback path reads only the current in-memory snapshot. Initialization,
coalesced background file notifications and an explicit resident reload after
saving update the snapshot. Saves compare the read revision under a bounded
per-file mutex, then atomically replace with a previous-version backup. Invalid
first loads disable automatic scenes; subsequent failures keep the last valid
snapshot rather than overwriting the file. Legacy `idle-disabled` is consulted
only before a valid JSON exists and is never removed. See the settings guide
for implementation tests, manual acceptance limits and rollback.

Background completion placement reads an independent, bounded/versioned
`completion-layout.json` once during a one-shot task player's initialization.
No extra layout I/O is added to the New chat/watcher path. The notification
captures its immutable size/corner preference; saving affects the next player.
`CompletionLayoutForm` offers Small/Medium/Large and bottom-left/right presets,
atomic saves with revision checks/backups, and unsaved-selection previews.
The original Large/bottom-right bounds and automatic foreground-vs-notification
selection remain unchanged. `--notice-preview` validates preset tokens, uses
the existing completion-pending mutex, and never claims an actual task or queues
for focus. Invalid persisted layout falls back to the original notification
layout without overwriting the user's file or disabling task reminders.

## Boundaries

- UI Automation button names and Codex log formats are not stable APIs. A
  Codex update may require detection changes.
- The fast path handles mouse clicks. Other ways of opening a chat may use the
  slower navigation-log fallback.
- A tray context menu is not a New chat trigger. Opening it must not play.
- Windows user-level startup is distinct from a Codex plugin install. When
  running PowerShell within the packaged Codex environment, the registry view
  can be isolated; startup registration must be performed and verified from a
  normal Windows PowerShell process outside Codex.
