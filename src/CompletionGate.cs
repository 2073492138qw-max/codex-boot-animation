using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Human-centered restrained completion policy:
// - Never celebrate failures, reaction-only turns, or mid-task questions waiting on the user.
// - In foreground: only celebrate substantial work (tools >= 3 or duration >= 45s).
// - In background: recall the user via corner notice for real work (tools >= 1 or >= 15s).
// - Cooldown (60s per session) prevents rapid-fire popups during quick iterative tweaks.
internal static class CompletionGate {
    internal const int BigTaskToolCalls = 3;
    internal const long BigTaskDurationMs = 45000;
    internal const long BackgroundDurationMs = 15000;
    internal const long LongFallbackDurationMs = 120000;
    internal const long CooldownMs = 60000;
    internal const long CooldownBypassDurationMs = 90000;

    static readonly object CooldownLock = new object();
    static readonly Dictionary<string, DateTime> LastDeliveredUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

    // Explicit pre-execution blockers where Codex paused to ask the user to decide/confirm.
    static readonly Regex BlockingConfirmation = new Regex(
        @"(?:请先确认|确认后(?:我)?再|你(?:更)?倾向(?:于)?(?:方案|哪)|选哪(?:一)?个|方案\s*[A1]\s*(?:还是|或)\s*方案\s*[B2]|要不要我(?:先|继续|现在)|是否(?:需要我)?继续|可以开始(?:执行|修改)吗|请告诉我你的选择|在(?:动手|修改|执行)之前)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Trailing question without a confirmed completion indicates a reply waiting for user input.
    static readonly Regex TrailingQuestion = new Regex(@"[？?][""'”’）)\s]*$", RegexOptions.Compiled);

    internal sealed class Decision {
        internal bool Allow;
        internal string Reason;
    }

    internal static bool IsWaitingForUser(string message, bool hasConfirmedSuccess) {
        if (String.IsNullOrWhiteSpace(message)) return false;
        string trimmed = message.Trim();
        if (BlockingConfirmation.IsMatch(trimmed)) return true;
        if (!hasConfirmedSuccess && TrailingQuestion.IsMatch(trimmed)) return true;
        return false;
    }

    internal static bool InCooldown(string session, DateTime utcNow) {
        if (String.IsNullOrEmpty(session)) return false;
        lock (CooldownLock) {
            DateTime last;
            return LastDeliveredUtc.TryGetValue(session, out last) && (utcNow - last).TotalMilliseconds < CooldownMs;
        }
    }

    internal static void MarkDelivered(string session, DateTime utcNow) {
        if (String.IsNullOrEmpty(session)) return;
        lock (CooldownLock) {
            if (LastDeliveredUtc.Count >= 256) {
                var expired = new List<string>();
                foreach (var pair in LastDeliveredUtc) {
                    if ((utcNow - pair.Value).TotalMilliseconds >= CooldownMs) expired.Add(pair.Key);
                }
                foreach (string key in expired) LastDeliveredUtc.Remove(key);
                if (LastDeliveredUtc.Count >= 256) LastDeliveredUtc.Clear();
            }
            LastDeliveredUtc[session] = utcNow;
        }
    }

    internal static void ClearCooldownForTests() {
        lock (CooldownLock) LastDeliveredUtc.Clear();
    }

    internal static Decision Evaluate(
        string source,
        bool hasState,
        bool promptCandidate,
        bool reactionOnly,
        long durationMs,
        int toolCalls,
        bool codexForeground,
        bool hasConfirmedSuccess,
        bool hasFailure,
        string message,
        bool inCooldown) {

        if (reactionOnly) return new Decision { Allow = false, Reason = "reaction-only" };
        if (hasFailure) return new Decision { Allow = false, Reason = "no-confirmed-success" };
        if (IsWaitingForUser(message, hasConfirmedSuccess)) return new Decision { Allow = false, Reason = "waiting-for-user" };

        // Legacy stop-hook lacks duration and tool count: preserve explicit task+success contract.
        if (source == "stop-hook") {
            if (!hasState) return new Decision { Allow = false, Reason = "no-task-state" };
            if (!promptCandidate) return new Decision { Allow = false, Reason = "non-task" };
            if (!hasConfirmedSuccess) return new Decision { Allow = false, Reason = "no-confirmed-success" };
            if (inCooldown) return new Decision { Allow = false, Reason = "cooldown" };
            return new Decision { Allow = true, Reason = "stop-hook" };
        }

        // Journal source has real duration and tool-call counts.
        bool heavyTools = toolCalls >= BigTaskToolCalls;
        bool heavyDuration = durationMs >= BigTaskDurationMs;
        bool longFallback = durationMs >= LongFallbackDurationMs;

        if (!hasState && !heavyTools && !longFallback) {
            return new Decision { Allow = false, Reason = "no-task-state" };
        }

        if (inCooldown && durationMs < CooldownBypassDurationMs) {
            return new Decision { Allow = false, Reason = "cooldown" };
        }

        // Tier 1: Big task (tools >= 3, or task duration >= 45s, or >= 120s long turn).
        // Heavy multi-tool turns do not require rigid "已完成" wording once failure/questions are ruled out.
        if (heavyTools) {
            return new Decision { Allow = true, Reason = "big-task-tools" };
        }
        if (longFallback && hasConfirmedSuccess) {
            return new Decision { Allow = true, Reason = "big-task-long" };
        }
        if (promptCandidate && heavyDuration && (hasConfirmedSuccess || toolCalls >= 1)) {
            return new Decision { Allow = true, Reason = "big-task-duration" };
        }

        // Tier 2: Background recall (user switched away from Codex to another window).
        if (!codexForeground) {
            if (toolCalls >= 2) {
                return new Decision { Allow = true, Reason = "background-recall-tools" };
            }
            if (promptCandidate && (toolCalls >= 1 || durationMs >= BackgroundDurationMs) && hasConfirmedSuccess) {
                return new Decision { Allow = true, Reason = "background-recall" };
            }
            if (!promptCandidate) return new Decision { Allow = false, Reason = "non-task" };
            if (!hasConfirmedSuccess) return new Decision { Allow = false, Reason = "no-confirmed-success" };
            return new Decision { Allow = false, Reason = "background-too-brief" };
        }

        // Tier 3: Foreground small task -> restrained mode stays quiet to avoid interrupting active chat.
        if (!promptCandidate) return new Decision { Allow = false, Reason = "non-task" };
        if (!hasConfirmedSuccess) return new Decision { Allow = false, Reason = "no-confirmed-success" };
        return new Decision { Allow = false, Reason = "foreground-small-task" };
    }

    internal static bool SelfTest() {
        // 1. Foreground quick small task (5s, 1 tool) must stay quiet in restrained mode.
        var fgSmall = Evaluate("journal", true, true, false, 5000, 1, true, true, false, "已修复。", false);
        if (fgSmall.Allow || fgSmall.Reason != "foreground-small-task") return false;

        // 2. Foreground big task by tool count (>= 3 tools) triggers even without rigid "已完成" keyword.
        var fgBigTools = Evaluate("journal", true, true, false, 20000, 4, true, false, false, "目录里共发现 5 个文件，配置与预期一致。", false);
        if (!fgBigTools.Allow || fgBigTools.Reason != "big-task-tools") return false;

        // 3. Foreground big task by duration (>= 45s) with tool or confirmation triggers.
        var fgBigDur = Evaluate("journal", true, true, false, 50000, 1, true, false, false, "修改如下：更新了启动参数和超时处理。", false);
        if (!fgBigDur.Allow || fgBigDur.Reason != "big-task-duration") return false;

        // 4. Background recall: user switched away, task took 20s and confirmed -> corner notice triggers.
        var bgTask = Evaluate("journal", true, true, false, 20000, 0, false, true, false, "已完成整理。", false);
        if (!bgTask.Allow || bgTask.Reason != "background-recall") return false;

        // 5. Background tiny 3s 0-tool chat -> suppressed (no spam).
        var bgBrief = Evaluate("journal", true, true, false, 3000, 0, false, true, false, "已完成。", false);
        if (bgBrief.Allow) return false;

        // 6. Mid-task question / decision blocker -> suppressed even with 5 tools!
        var waiting = Evaluate("journal", true, true, false, 60000, 5, false, false, false, "我查完了代码，你更倾向于方案A还是方案B？", false);
        if (waiting.Allow || waiting.Reason != "waiting-for-user") return false;

        // 7. Polite closing offer AFTER explicit completion on a big task -> STILL allowed!
        var politeEnd = Evaluate("journal", true, true, false, 60000, 5, true, true, false, "已修复并全部测试通过。接下来需要顺便清理旧文件吗？", false);
        if (!politeEnd.Allow) return false;

        // 8. Cooldown blocks quick follow-up, but allows >= 90s heavy task.
        var cooled = Evaluate("journal", true, true, false, 30000, 4, true, true, false, "已更新。", true);
        if (cooled.Allow || cooled.Reason != "cooldown") return false;
        var bypass = Evaluate("journal", true, true, false, 95000, 5, true, true, false, "已重构完成。", true);
        if (!bypass.Allow) return false;

        // 9. Failure always blocks even with 10 tools and 120s.
        var failed = Evaluate("journal", true, true, false, 120000, 10, false, false, true, "测试失败，暂时无法完成。", false);
        if (failed.Allow) return false;

        // 10. Cooldown state tracking.
        ClearCooldownForTests();
        DateTime t0 = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
        if (InCooldown("s1", t0)) return false;
        MarkDelivered("s1", t0);
        if (!InCooldown("s1", t0.AddSeconds(30))) return false;
        if (InCooldown("s1", t0.AddSeconds(61))) return false;
        ClearCooldownForTests();

        return true;
    }
}
