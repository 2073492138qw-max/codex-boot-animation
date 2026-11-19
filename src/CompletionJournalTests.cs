using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

internal static class CompletionJournalTests {
    static void Require(bool value, string name) { if (!value) throw new InvalidOperationException("completion-budget-" + name); }
    static string Record(string turn, string message) {
        return "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"" + turn + "\",\"last_agent_message\":\"" + message + "\"}}";
    }
    sealed class CountedStream : MemoryStream {
        internal long BytesRead, FailAt = -1;
        internal Action OnRead;
        public override int Read(byte[] bytes, int offset, int count) {
            if (OnRead != null) OnRead();
            if (FailAt >= 0 && Position >= FailAt) throw new IOException("synthetic");
            int read = base.Read(bytes, offset, count); BytesRead += read; return read;
        }
    }
    static void Append(Stream stream, string text) {
        stream.Position = stream.Length;
        byte[] bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush();
    }
    static List<string> Drain(CompletionCursor cursor, Stream stream, out string skipped) {
        var lines = new List<string>(); bool more; skipped = null;
        for (int i = 0; i < 1000; i++) {
            string reason; string line = cursor.Read(stream, out more, out reason);
            if (line != null) lines.Add(line);
            if (reason != null) skipped = reason;
            if (!more) return lines;
        }
        throw new InvalidOperationException("completion-budget-no-progress");
    }
    internal static bool Run() {
        bool more; string skipped;
        using (var stream = new CountedStream()) {
            Append(stream, new string('x', 1024 * 1024));
            var cursor = new CompletionCursor(stream, false);
            for (int i = 1; i <= 4; i++) {
                stream.BytesRead = 0;
                Require(cursor.Read(stream, out more, out skipped) == null, "partial-output");
                Require(cursor.Position == i * CompletionCursor.ScanBudget, "partial-position");
                Require(stream.BytesRead <= CompletionCursor.ScanBudget + 192, "scan-budget");
            }
            stream.BytesRead = 0;
            for (int i = 0; i < 10; i++) cursor.Read(stream, out more, out skipped);
            Require(stream.BytesRead <= 10 * 192, "idle-reread");
            Append(stream, "\n" + Record("following", "完成") + "\n");
            var lines = Drain(cursor, stream, out skipped);
            Require(lines.Count == 1 && lines[0].Contains("following"), "following-line");
            Require(Drain(cursor, stream, out skipped).Count == 0, "duplicate");
        }
        // Large legitimate completion exceeds the navigation reader's 64 KiB
        // limit and crosses scan boundaries, including UTF-8 split boundaries.
        string record = Record("large", new string('中', 160000) + "😀");
        using (var stream = new CountedStream()) {
            Append(stream, record + "\r\n");
            var cursor = new CompletionCursor(stream, false);
            var lines = Drain(cursor, stream, out skipped);
            string turn, message; long duration;
            Require(lines.Count == 1 && lines[0] == record && CompletionJournal.TryParse(lines[0], out turn, out message, out duration), "large-utf8-event");
            Require(stream.BytesRead <= stream.Length * 2 + 1024, "complete-read-once");
        }
        // I/O failure after consuming a block cannot drop the completion.
        using (var stream = new CountedStream()) {
            Append(stream, Record("retry", new string('x', 9000)) + "\n");
            var cursor = new CompletionCursor(stream, false); stream.FailAt = 4096;
            try { cursor.Read(stream, out more, out skipped); Require(false, "expected-io"); } catch (IOException) { }
            Require(cursor.Position == 0, "io-position"); stream.FailAt = -1;
            Require(Drain(cursor, stream, out skipped).Count == 1, "io-retry");
        }
        using (var stream = new CountedStream()) {
            Append(stream, Record("decode-retry", new string('x', 9000)) + "\n");
            var cursor = new CompletionCursor(stream, false); int starts = 0;
            stream.OnRead = delegate { if (stream.Position == 0 && ++starts == 2) throw new IOException("decode-fault"); };
            try { cursor.Read(stream, out more, out skipped); Require(false, "expected-decode-io"); } catch (IOException) { }
            Require(cursor.Position == 0, "decode-io-position"); stream.OnRead = null;
            Require(Drain(cursor, stream, out skipped).Count == 1, "decode-io-retry");
        }
        using (var stream = new CountedStream()) {
            Append(stream, Record("old-turn", new string('x', 9000)) + "\n");
            var cursor = new CompletionCursor(stream, false); bool changed = false;
            stream.OnRead = delegate {
                if (!changed && stream.Position >= 4096) {
                    changed = true; long at = stream.Position;
                    stream.SetLength(0); Append(stream, Record("new-turn", new string('y', 9000)) + "\n"); stream.Position = at;
                }
            };
            string rewritten = cursor.Read(stream, out more, out skipped);
            Require(rewritten == null || rewritten == Record("new-turn", new string('y', 9000)), "mid-read-rewrite");
            stream.OnRead = null;
            var lines = Drain(cursor, stream, out skipped);
            Require(rewritten != null ? lines.Count == 0 : lines.Count == 1 && lines[0].Contains("new-turn"), "rewrite-retry");
        }
        using (var stream = new CountedStream()) {
            Append(stream, "old-partial"); var cursor = new CompletionCursor(stream, false);
            cursor.Read(stream, out more, out skipped);
            stream.SetLength(0); Append(stream, Record("replacement", "完成") + "\n");
            Require(Drain(cursor, stream, out skipped).Count == 1, "truncate-regrow");
        }
        // No partial body is cached: even an oversized line is scanned with
        // fixed-sized buffers, skipped at its boundary, then reading resumes.
        using (var stream = new CountedStream()) {
            Append(stream, "\"task_complete\"");
            stream.SetLength((long)CompletionCursor.MaxLineBytes + 1);
            Append(stream, "\n" + Record("after-limit", "完成") + "\n");
            var cursor = new CompletionCursor(stream, false);
            var lines = Drain(cursor, stream, out skipped);
            Require(skipped == "byte-limit" && lines.Count == 1 && lines[0].Contains("after-limit"), "byte-limit-recovery");
        }
        using (var stream = new CountedStream()) {
            string marker = "\"task_complete\"";
            Append(stream, marker + new string(' ', CompletionCursor.MaxChars - marker.Length) + "\r\n");
            var cursor = new CompletionCursor(stream, false);
            var lines = Drain(cursor, stream, out skipped);
            Require(lines.Count == 1 && lines[0].Length == CompletionCursor.MaxChars && skipped == null, "character-boundary");
            Append(stream, marker + new string(' ', CompletionCursor.MaxChars + 1 - marker.Length) + "\n" + Record("after-character-limit", "完成") + "\n");
            lines = Drain(cursor, stream, out skipped);
            Require(skipped == "character-limit" && lines.Count == 1, "character-limit-recovery");
        }
        TestFilesAndWorkers();
        return true;
    }
    static void TestFilesAndWorkers() {
        string root = Path.Combine(Path.GetTempPath(), "codex-completion-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var received = new List<string>();
        using (var entered = new ManualResetEvent(false))
        using (var release = new ManualResetEvent(false))
        using (var fastDelivered = new ManualResetEvent(false)) {
            CompletionJournal journal = null;
            string slow = Path.Combine(root, "rollout-" + Guid.NewGuid() + ".jsonl");
            string fast = Path.Combine(root, "rollout-" + Guid.NewGuid() + ".jsonl");
            try {
                journal = new CompletionJournal(root, delegate(string session, string turn, string message, long duration) {
                    lock (received) received.Add(turn);
                    if (turn == "fast") fastDelivered.Set();
                }, delegate(string path) { if (path == slow) { entered.Set(); if (!release.WaitOne(5000)) throw new IOException("probe-timeout"); } }, false);
                File.WriteAllText(slow, Record("slow", new string('中', 160000)) + "\n", new UTF8Encoding(false));
                File.WriteAllText(fast, Record("fast", "完成") + "\n", new UTF8Encoding(false));
                journal.Read(slow);
                Require(entered.WaitOne(2000), "slow-enter");
                for (int i = 0; i < 100; i++) { journal.Read(slow); journal.Read(fast); }
                Require(fastDelivered.WaitOne(2000), "other-file-blocked");
                release.Set(); Require(journal.WaitForIdle(5000), "drain-timeout");
                lock (received) Require(received.Count == 2, "burst-duplicate-or-no-continuation");
                // Startup history ends in a partial line: discard its suffix.
                string seed = Path.Combine(root, "seed.jsonl");
                File.WriteAllText(seed, "old-partial", new UTF8Encoding(false));
                CompletionCursor cursor;
                using (var file = File.OpenRead(seed)) cursor = new CompletionCursor(file, true);
                File.AppendAllText(seed, Record("historical-suffix", "完成") + "\n" + Record("fresh", "完成") + "\n", new UTF8Encoding(false));
                string skipped;
                using (var file = File.OpenRead(seed)) {
                    var lines = Drain(cursor, file, out skipped);
                    Require(lines.Count == 1 && lines[0].Contains("fresh"), "seed-partial");
                }
                File.Move(seed, seed + ".old");
                File.WriteAllText(seed, Record("recreated", "完成") + "\n", new UTF8Encoding(false));
                using (var file = File.OpenRead(seed)) Require(Drain(cursor, file, out skipped).Count == 1, "file-identity");
                string same = Path.Combine(root, "same-anchor.jsonl");
                File.WriteAllText(same, Record("old-turn", new string('x', 100)) + "\n", new UTF8Encoding(false));
                using (var file = File.OpenRead(same)) { cursor = new CompletionCursor(file, false); Drain(cursor, file, out skipped); }
                File.Move(same, same + ".old");
                File.WriteAllText(same, Record("new-turn", new string('x', 100)) + "\n", new UTF8Encoding(false));
                using (var file = File.OpenRead(same)) {
                    var lines = Drain(cursor, file, out skipped);
                    Require(lines.Count == 1 && lines[0].Contains("new-turn"), "same-anchor-replacement");
                }
                for (int i = 0; i < CompletionJournal.MaxTrackedFiles + 10; i++) journal.Read(Path.Combine(root, "rollout-" + Guid.NewGuid() + ".jsonl"));
                Require(journal.WaitForIdle(5000), "capacity-idle");
                var entries = (IDictionary)typeof(CompletionJournal).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(journal);
                Require(entries.Count == CompletionJournal.MaxTrackedFiles, "state-capacity");
                journal.Dispose();
                File.AppendAllText(fast, Record("disposed", "完成") + "\n", new UTF8Encoding(false));
                journal.Read(fast); Require(journal.WaitForIdle(1000), "disposed-work");
                lock (received) Require(received.Count == 2, "disposed-delivery");

                // Journal UserMessage fallback (for Codex versions where plugin_hooks is removed).
                string userLine = "{\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"item_completed\",\"turn_id\":\"turn-u1\",\"item\":{\"type\":\"UserMessage\",\"content\":[{\"type\":\"text\",\"text\":\"你这个傻逼，帮我修复这个问题\"}]}}}";
                string uTurn, uPrompt; DateTime? uTime;
                Require(CompletionJournal.TryParseUserPrompt(userLine, out uTurn, out uPrompt, out uTime) && uTurn == "turn-u1" && uPrompt.Contains("修复这个问题") && uTime.HasValue, "parse-user-message");
                // Non-UserMessage items or unrelated strings containing "UserMessage" must not match.
                Require(!CompletionJournal.TryParseUserPrompt("{\"type\":\"event_msg\",\"payload\":{\"type\":\"item_completed\",\"turn_id\":\"turn-a1\",\"item\":{\"type\":\"AgentMessage\",\"content\":[{\"type\":\"text\",\"text\":\"UserMessage\"}]}}}", out uTurn, out uPrompt, out uTime), "reject-agent-message");
                // Cursor must pick up UserMessage lines and deliver them through CompletionJournal.
                var promptReceived = new List<string>();
                string promptFile = Path.Combine(root, "rollout-" + Guid.NewGuid() + ".jsonl");
                File.WriteAllText(promptFile, userLine + "\n", new UTF8Encoding(false));
                using (var promptJournal = new CompletionJournal(root, delegate(string s, string t, string m, long d, int tools) { }, delegate(string s, string t, string p, DateTime? ts) { lock (promptReceived) promptReceived.Add(t + ":" + p); }, null, false)) {
                    promptJournal.Read(promptFile);
                    Require(promptJournal.WaitForIdle(2000), "prompt-drain");
                }
                lock (promptReceived) Require(promptReceived.Count == 1 && promptReceived[0].StartsWith("turn-u1:", StringComparison.Ordinal), "prompt-delivered");
                // Cross-source prompt claim deduplicates hook vs journal for the same turn.
                string claimSession = Guid.NewGuid().ToString(), claimTurn = "turn-dedupe";
                Require(CompletionStateStore.TryClaimPrompt(claimSession, claimTurn), "first-prompt-claim");
                Require(!CompletionStateStore.TryClaimPrompt(claimSession, claimTurn), "duplicate-prompt-claim");
                // Tool item_completed records are counted per turn and passed with task_complete.
                int deliveredTools = -1;
                string toolFile = Path.Combine(root, "rollout-" + Guid.NewGuid() + ".jsonl");
                string toolItem1 = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"item_completed\",\"turn_id\":\"turn-t1\",\"item\":{\"type\":\"CommandExecution\"}}}";
                string toolItem2 = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"item_completed\",\"turn_id\":\"turn-t1\",\"item\":{\"type\":\"FileChange\"}}}";
                string toolItem3 = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"item_completed\",\"turn_id\":\"turn-t1\",\"item\":{\"type\":\"CommandExecution\"}}}";
                string reasoningItem = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"item_completed\",\"turn_id\":\"turn-t1\",\"item\":{\"type\":\"Reasoning\"}}}";
                File.WriteAllText(toolFile, toolItem1 + "\n" + reasoningItem + "\n" + toolItem2 + "\n" + toolItem3 + "\n" + Record("turn-t1", "整理好了。") + "\n", new UTF8Encoding(false));
                using (var toolJournal = new CompletionJournal(root, delegate(string s, string t, string m, long d, int tools) { deliveredTools = tools; }, null, null, false)) {
                    toolJournal.Read(toolFile);
                    Require(toolJournal.WaitForIdle(2000), "tool-count-drain");
                }
                Require(deliveredTools == 3, "tool-count-mismatch");
            } finally {
                release.Set(); if (journal != null) journal.Dispose();
                foreach (string file in Directory.GetFiles(root)) File.Delete(file);
                Directory.Delete(root);
            }
        }
    }
}
