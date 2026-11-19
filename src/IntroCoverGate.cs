using System;

// Decides when the resident player's static first-frame cover can safely drop.
// Rejects stale pre-seek positions (e.g. 0.416s / 0.6187s) from reused slots,
// while dropping quickly (~1-2 video frames past 0) instead of waiting >0.20s.
internal sealed class IntroCoverGate {
    internal const double NearZeroCeilingSeconds = 0.15;
    internal const double MinAdvanceSeconds = 0.05;
    internal const double MinDeltaSeconds = 0.03;
    internal const long FallbackElapsedMs = 350;

    bool active;
    bool seenNearZero;
    double minNearZero;
    double lastPosition;
    bool hasLastPosition;
    int forwardTicks;

    internal bool Active { get { return active; } }

    internal void Begin() {
        active = true;
        seenNearZero = false;
        minNearZero = double.MaxValue;
        lastPosition = 0;
        hasLastPosition = false;
        forwardTicks = 0;
    }

    internal void Reset() {
        active = false;
    }

    internal bool ShouldDismiss(double positionSeconds, long elapsedMs) {
        if (!active) return false;
        if (double.IsNaN(positionSeconds) || double.IsInfinity(positionSeconds) || positionSeconds < 0) return false;

        if (positionSeconds <= NearZeroCeilingSeconds) {
            seenNearZero = true;
            if (positionSeconds < minNearZero) minNearZero = positionSeconds;
        }

        if (hasLastPosition && positionSeconds > lastPosition + 0.005) {
            forwardTicks++;
        } else if (hasLastPosition && positionSeconds < lastPosition) {
            // Seek-to-zero landed after a stale position; restart forward motion count.
            forwardTicks = 0;
        }
        lastPosition = positionSeconds;
        hasLastPosition = true;

        // Primary path: confirmed near-zero playback that has genuinely advanced.
        if (seenNearZero && positionSeconds >= Math.Max(MinAdvanceSeconds, minNearZero + MinDeltaSeconds)) {
            active = false;
            return true;
        }

        // Safety fallback: if a file's initial timestamp never reports <= 0.15s,
        // dismiss after the seek-settle window once forward motion is observed.
        if (elapsedMs >= FallbackElapsedMs && forwardTicks >= 2) {
            active = false;
            return true;
        }

        return false;
    }

    internal static bool SelfTest() {
        // 1. Fresh cold start: 0.00 -> 0.00 -> 0.03 -> 0.055 dismisses quickly (~55ms)
        var fresh = new IntroCoverGate();
        fresh.Begin();
        if (fresh.ShouldDismiss(0.0, 16)) return false;
        if (fresh.ShouldDismiss(0.0, 32)) return false;
        if (fresh.ShouldDismiss(0.03, 48)) return false;
        if (!fresh.ShouldDismiss(0.055, 64)) return false;
        if (fresh.Active || fresh.ShouldDismiss(0.10, 80)) return false;

        // 2. Stale reused-slot seek (0.6187 / 0.416 bug): must NOT drop on stale position,
        // must wait until seek lands near zero and then advances cleanly.
        var stale = new IntroCoverGate();
        stale.Begin();
        if (stale.ShouldDismiss(0.6187, 32)) return false;
        if (stale.ShouldDismiss(0.4160, 64)) return false;
        if (stale.ShouldDismiss(0.4160, 140)) return false;
        if (stale.ShouldDismiss(0.0000, 172)) return false;
        if (stale.ShouldDismiss(0.0250, 188)) return false;
        if (!stale.ShouldDismiss(0.0550, 204)) return false;

        // 3. Reused slot starting slightly above zero (e.g. 0.08): requires +0.03 delta.
        var near = new IntroCoverGate();
        near.Begin();
        if (near.ShouldDismiss(0.08, 16)) return false;
        if (near.ShouldDismiss(0.095, 32)) return false;
        if (!near.ShouldDismiss(0.115, 48)) return false;

        // 4. Fallback for unusual media whose PTS never reads <= 0.15s.
        var offsetPts = new IntroCoverGate();
        offsetPts.Begin();
        if (offsetPts.ShouldDismiss(0.30, 50)) return false;
        if (offsetPts.ShouldDismiss(0.35, 150)) return false;
        if (offsetPts.ShouldDismiss(0.40, 250)) return false;
        if (!offsetPts.ShouldDismiss(0.45, 360)) return false;

        // 5. Frozen stale position past 350ms without forward motion must NOT trigger fallback.
        var frozen = new IntroCoverGate();
        frozen.Begin();
        if (frozen.ShouldDismiss(0.416, 100)) return false;
        if (frozen.ShouldDismiss(0.416, 400)) return false;
        frozen.Reset();
        if (frozen.Active || frozen.ShouldDismiss(0.06, 420)) return false;

        return true;
    }
}
