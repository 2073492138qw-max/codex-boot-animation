using System;

// Bounded exponential backoff for companion process recovery (--supervise -> --watch
// and --watch -> --resident). Allows immediate recovery after a healthy run,
// keeps a 2s launch grace window so a starting child is not double-spawned,
// and caps rapid crash loops at 30 seconds.
internal sealed class RestartBackoff {
    static readonly long[] DelaysMs = { 2000, 4000, 8000, 15000, 30000 };
    internal const long HealthyRunMs = 30000;

    long lastStartMs = -1;
    long nextAllowedMs = 0;
    int failureLevel = 0;

    internal int FailureLevel { get { return failureLevel; } }

    internal void MarkAlive(long nowMs) {
        if (lastStartMs >= 0 && nowMs - lastStartMs >= HealthyRunMs) {
            failureLevel = 0;
        }
    }

    internal bool CanAttempt(long nowMs) {
        return nowMs >= nextAllowedMs;
    }

    internal long RecordAttempt(long nowMs, bool cleanRecycle) {
        lastStartMs = nowMs;
        if (cleanRecycle) {
            failureLevel = 0;
            nextAllowedMs = nowMs + DelaysMs[0];
            return DelaysMs[0];
        }
        long delay = DelaysMs[failureLevel];
        nextAllowedMs = nowMs + delay;
        if (failureLevel < DelaysMs.Length - 1) failureLevel++;
        return delay;
    }

    internal static bool SelfTest() {
        var b = new RestartBackoff();
        if (!b.CanAttempt(0)) return false;
        // First launch is allowed immediately, then enforces a 2s launch grace window.
        if (b.RecordAttempt(1000, false) != 2000) return false;
        if (b.CanAttempt(1063) || !b.CanAttempt(3000)) return false;
        // Rapid crashes back off: 4s -> 8s -> 15s -> 30s (capped).
        if (b.RecordAttempt(3000, false) != 4000) return false;
        if (b.CanAttempt(6999) || !b.CanAttempt(7000)) return false;
        if (b.RecordAttempt(7000, false) != 8000) return false;
        if (b.RecordAttempt(15000, false) != 15000) return false;
        if (b.RecordAttempt(30000, false) != 30000) return false;
        // Waiting out 30s backoff without observing the process alive stays capped.
        if (b.RecordAttempt(60000, false) != 30000) return false;
        // Observing the process alive for >= 30s resets backoff so next recovery is immediate.
        b.MarkAlive(75000);
        if (b.FailureLevel == 0) return false;
        b.MarkAlive(90000);
        if (b.FailureLevel != 0 || !b.CanAttempt(91000)) return false;
        if (b.RecordAttempt(91000, false) != 2000) return false;
        // Clean recycle resets level while keeping the 2s launch grace window.
        if (b.RecordAttempt(95000, true) != 2000 || b.FailureLevel != 0 || b.CanAttempt(96000) || !b.CanAttempt(97000)) return false;
        return true;
    }
}
