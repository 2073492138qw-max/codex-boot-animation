using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

internal static class PromptReactionFlowTests {
    internal static bool Run() {
        var order = new List<string>();
        PromptReactionFlow.Run(true, delegate { order.Add("dispatch"); }, delegate { order.Add("state"); });
        if (String.Join(",", order) != "dispatch,state") return false;
        order.Clear();
        PromptReactionFlow.Run(false, delegate { order.Add("dispatch"); }, delegate { order.Add("state"); });
        if (String.Join(",", order) != "state") return false;
        // A paused/disabled dispatch returns without losing task bookkeeping.
        int recorded = 0;
        PromptReactionFlow.Run(true, delegate { }, delegate { recorded++; });
        if (recorded != 1) return false;
        bool threw = false;
        try { PromptReactionFlow.Run(true, delegate { throw new InvalidOperationException(); }, delegate { recorded++; }); }
        catch (InvalidOperationException) { threw = true; }
        if (!threw || recorded != 2) return false;

        // Exercise the actual per-session mutex and production OnPrompt path.
        // State is isolated, and there is no real pipe or playback in this test.
        string session = Guid.NewGuid().ToString("N"), turn = "reaction";
        string folder = Path.Combine(Path.GetTempPath(), "codex-reaction-flow-" + Guid.NewGuid().ToString("N"));
        CompletionStateStore.TestFolder = folder;
        using (var stateEntered = new ManualResetEvent(false))
        using (var finished = new ManualResetEvent(false)) {
            bool dispatched = false, failure = false;
            Thread worker = null;
            try {
                using (new CompletionStateStore.ScopeLock(session)) {
                    worker = new Thread(delegate() {
                        try {
                            PromptReactionFlow.Run(true, delegate { dispatched = true; }, delegate {
                                stateEntered.Set();
                                TaskCompletion.OnPrompt(new Dictionary<string, object> {
                                    { "session_id", session }, { "turn_id", turn }, { "prompt", "你是傻逼（测试）" }
                                });
                            });
                        } catch { failure = true; }
                        finally { finished.Set(); }
                    });
                    worker.IsBackground = true;
                    worker.Start();
                    if (!stateEntered.WaitOne(1000) || !dispatched || finished.WaitOne(50)) return false;
                    // Release the mutex only after observing dispatch. Old
                    // state-first ordering cannot satisfy the above assertion.
                }
                if (!finished.WaitOne(1500) || failure) return false;
                var key = CompletionStateStore.Key(session, turn);
                var state = CompletionStateStore.Resolve(session, key);
                if (state == null || state.Kind != "x") return false;
                return true;
            } finally {
                if (worker != null) worker.Join(4000);
                CompletionStateStore.TestFolder = null;
                if (Directory.Exists(folder)) {
                    foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
                    Directory.Delete(folder);
                }
            }
        }
    }
}
