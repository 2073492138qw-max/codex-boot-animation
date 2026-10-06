using System;

// Dispatch a reaction before unrelated durable task bookkeeping. Keep both on
// the hook thread: no new worker, lost background write, or changed state format.
internal static class PromptReactionFlow {
    internal static void Run(bool matched, Action dispatch, Action recordState) {
        if (!matched) { recordState(); return; }
        try { dispatch(); }
        finally { recordState(); }
    }
}
