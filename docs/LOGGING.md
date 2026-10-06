# Diagnostics and logs

The companion writes its runtime log to
`%USERPROFILE%\.codex-boot-animation\playback.log`. It is not part of the Git
repository and must not be uploaded wholesale. `paused` in the same folder is
local tray state and also must not be committed.

Useful events include `supervisor-started`, `watcher-started`,
`resident-player-ready`, `desktop-instance-detected`,
`new-dialog-click-detected`, `trigger-delivered=resident`,
`resident-playback-start`, `playback-progress`, and `close reason=...`.
`new-dialog-log-duplicate-suppressed` means a slower log event was correctly
ignored after a faster click/start trigger.
Task-completion checks log only a boolean candidate and a playback/suppression
reason, never the user's request or assistant reply. Temporary files under
`%USERPROFILE%\.codex-boot-animation\turns\` contain only task kind (`0`, `1`,
or reaction-only `x`) and hashed identities. Session-scoped candidate files
preserve the originating request across continuation turns; new input replaces
the scope. Candidate files are consumed when completion is evaluated. Empty
`.claimed` files prevent duplicate playback. Old state is pruned after one day
when new prompt input arrives.

`completion-confirmed` identifies the state source (`turn`, `session-inherited`,
or `duration-fallback`). `completion-player-launched` correlates an anonymous
12-character task hash with a player PID. That PID's `completion-first-moving-frame`,
`completion-media-opened ... audio=True`, and `completion-close reason=ended`
confirm delivery and normal playback, not physical speaker volume. If Codex
is behind another app, `completion-deferred` explains the foreground wait;
the player expires after 90 seconds rather than covering unrelated work.
`Verify-Completion.ps1` reads a real local task-completion record without
printing its message and tests this chain. Duplicate claims remain respected.

Idle transitions appear as `idle-policy-state=Armed`, `Settling`, `Watching`;
`idle-return-accepted` confirms actual resident acceptance, not merely sending
a pipe message. Settling expires after one minute; repeated input restarts
the quiet timer. No input content is logged.
`desktop-instances/*.claim` holds empty files named by a hash of PID/start ticks
for startup de-duplication. These are runtime state, never release assets.

If the intro fails, check whether the supervisor, watcher and resident
processes are running, then inspect the most recent events. A missing video,
media error or UI Automation failure should be reported with only the relevant
redacted lines. Do not include private Codex logs, full user paths, chat IDs,
access tokens or entire diagnostic files in a public issue.

旧版本 LocalAppData 日志可能被 Windows 包重定向到 Codex LocalCache。
旧目录保留作历史证据；它不是当前助手的日志目录，也不能拼接后当作同一运行记录。
2026-10-01 启动事故正在排查，见 `INCIDENT-2026-10-01-STARTUP.md`。
