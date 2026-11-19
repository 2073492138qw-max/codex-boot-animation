using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class NavigationStartupTests {
    static void Require(bool value, string stage) { if (!value) throw new InvalidOperationException("navigation-startup-" + stage); }
    sealed class CountedStream : MemoryStream {
        internal long BytesRead;
        internal bool ShortRead;
        public override int Read(byte[] bytes, int offset, int count) {
            if (ShortRead) return 0;
            int read = base.Read(bytes, offset, count); BytesRead += read; return read;
        }
        public override int ReadByte() { int value = base.ReadByte(); if (value >= 0) BytesRead++; return value; }
    }
    static void Append(Stream stream, string text) {
        stream.Position = stream.Length; byte[] bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush();
    }
    static string Route(string id) { return "IAB_LIFECYCLE received browser sidebar owner sync conversationId=client-new-thread:" + id + " ownerRoutePath=/ windowId=1\n"; }
    internal static bool Run() {
        using (var stream = new CountedStream()) {
            stream.SetLength(4 * 1024 * 1024);
            var cursor = NavigationLogStartup.Seed(stream);
            Require(cursor.Position == stream.Length && stream.BytesRead <= NavigationLogReader.InitialScanBudget + 64, "seed-budget");
            Append(stream, Route("old-suffix") + Route("fresh"));
            Require(cursor.Read(stream) == Route("fresh") && cursor.Read(stream) == "", "oversize-suffix-not-replayed");
        }
        foreach (int size in new[] { 0, 4095, 4096, 65535, 65536, 65537 }) using (var stream = new CountedStream()) {
            Append(stream, "history\n" + new string('x', size));
            var cursor = NavigationLogStartup.Seed(stream);
            Require(stream.BytesRead <= NavigationLogReader.InitialScanBudget + 64, "boundary-budget");
            Append(stream, "\n" + Route("next"));
            string read = cursor.Read(stream);
            string expected = size <= NavigationCursor.LineLimit ? new string('x', size) + "\n" + Route("next") : Route("next");
            Require(read == expected, "boundary-tail");
        }
        string unicode = "就绪😀\r\n";
        byte[] splitLine = Encoding.UTF8.GetBytes(unicode);
        for (int split = 1; split < splitLine.Length; split++) using (var stream = new CountedStream()) {
            Append(stream, "old\n"); stream.Position = stream.Length; stream.Write(splitLine, 0, split);
            var cursor = NavigationLogStartup.Seed(stream);
            stream.Position = stream.Length; stream.Write(splitLine, split, splitLine.Length - split);
            Require(cursor.Read(stream) == unicode && cursor.Read(stream) == "", "unicode-pending");
        }
        using (var stream = new CountedStream()) {
            stream.SetLength(4 * 1024 * 1024);
            int predicates = 0;
            Require(!NavigationLogStartup.HasReadyLine(stream, delegate(string line) { predicates++; return true; }), "huge-line-ready");
            Require(predicates == 0 && stream.BytesRead <= NavigationLogStartup.HeadBudget + NavigationLogStartup.TailBudget + 1, "ready-budget");
            Append(stream, "\nREADY-尾部😀\n"); stream.BytesRead = 0;
            Require(NavigationLogStartup.HasReadyLine(stream, delegate(string line) { return line == "READY-尾部😀"; }), "tail-ready");
            Require(stream.BytesRead <= NavigationLogStartup.HeadBudget + NavigationLogStartup.TailBudget + 1, "tail-budget");
        }
        using (var stream = new CountedStream()) {
            Append(stream, "\uFEFFREADY-头部\r\n" + new string('x', 1024 * 1024));
            Require(NavigationLogStartup.HasReadyLine(stream, delegate(string line) { return line == "READY-头部"; }), "head-ready");
        }
        using (var stream = new CountedStream()) {
            // A marker at the beginning of a clipped tail is part of an oversized
            // old line, not a complete readiness event.
            Append(stream, new string('x', 1024 * 1024) + "READY\n" + new string('z', NavigationLogStartup.TailBudget - 6));
            Require(!NavigationLogStartup.HasReadyLine(stream, delegate(string line) { return line.Contains("READY"); }), "clipped-tail");
        }
        using (var stream = new CountedStream()) {
            Append(stream, new string('x', NavigationLogStartup.HeadBudget - 5) + "READY\n");
            Require(!NavigationLogStartup.HasReadyLine(stream, delegate(string line) { return line.Contains("READY"); }), "clipped-head");
        }
        using (var stream = new CountedStream()) {
            Append(stream, "READY\n"); stream.ShortRead = true;
            try { NavigationLogStartup.Seed(stream); Require(false, "seed-short-read"); } catch (EndOfStreamException) { }
            try { NavigationLogStartup.HasReadyLine(stream, delegate(string line) { return true; }); Require(false, "ready-short-read"); } catch (EndOfStreamException) { }
        }
        TestSubscription();
        TestProductionRecovery();
        return true;
    }
    static void TestProductionRecovery() {
        string root = Path.Combine(Path.GetTempPath(), "codex-navigation-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "history.log");
        File.WriteAllText(path, Route("old-complete"), new UTF8Encoding(false));
        // Exercise the real methods without constructing a tray, input hooks,
        // timers or a player. Pause also prevents any launch on the fresh event.
        Type type = typeof(IntroWatcher);
        object watcher = FormatterServices.GetUninitializedObject(type);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var cursors = new ConcurrentDictionary<string, NavigationCursor>();
        var pending = new ConcurrentDictionary<string, bool>();
        var fields = new Dictionary<string, object> {
            { "navigationCursors", cursors }, { "navigationHistoryPending", pending },
            { "logOffsets", new Dictionary<string, long>() }, { "navigationStartedUtc", DateTime.UtcNow.AddMinutes(-1) },
            { "navigationTail", new NavigationTailPoll() }, { "dialogRouteLock", new object() },
            { "readyProcesses", new ConcurrentDictionary<string, bool>() }, { "paused", true },
            { "dialogRoutes", new DialogRouteState() },
            { "dialogIdentity", new Func<string,string,string>(delegate { return "fixture:1"; }) }
        };
        foreach (var pair in fields) type.GetField(pair.Key, flags).SetValue(watcher, pair.Value);
        MethodInfo seed = type.GetMethod("SeedNavigationLog", flags), read = type.GetMethod("ReadNewLogLines", flags);
        try {
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                try { seed.Invoke(watcher, new object[] { path }); Require(false, "expected-locked-seed"); }
                catch (TargetInvocationException error) { Require(error.InnerException is IOException, "locked-seed-error"); }
            }
            Require(pending.ContainsKey(path) && !cursors.ContainsKey(path), "failed-seed-tracked");
            read.Invoke(watcher, new object[] { path, "test" });
            Require(cursors.ContainsKey(path) && cursors[path].Position == new FileInfo(path).Length && pending.Count == 0, "production-recovery");
            var routes = (DialogRouteState)type.GetField("dialogRoutes", flags).GetValue(watcher);
            Require(routes.Count == 0, "production-history-replay");
            File.AppendAllText(path, Route("fresh-complete"), new UTF8Encoding(false));
            read.Invoke(watcher, new object[] { path, "test" });
            Require(routes.Count == 1 && !routes.Advance("fixture:1", 1, true), "production-fresh-event");
        } finally { File.Delete(path); Directory.Delete(root); }
    }
    static void TestSubscription() {
        string root = Path.Combine(Path.GetTempPath(), "codex-navigation-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string bad = Path.Combine(root, "bad.log"), good = Path.Combine(root, "good.log"), created = Path.Combine(root, "created.log");
        File.WriteAllText(bad, Route("bad-history"), new UTF8Encoding(false));
        File.WriteAllText(good, Route("history"), new UTF8Encoding(false));
        var cursors = new ConcurrentDictionary<string, NavigationCursor>();
        int errors = 0, seeds = 0, records = 0;
        using (var delivered = new ManualResetEvent(false)) {
            FileSystemWatcher watcher = null;
            try {
                watcher = NavigationLogStartup.Start(root, delegate(string path) {
                    if (path == bad) throw new UnauthorizedAccessException("synthetic");
                    using (var file = File.OpenRead(path)) cursors[path] = NavigationLogStartup.Seed(file);
                    Interlocked.Increment(ref seeds);
                    // Append while startup events are coalesced. Catch-up must
                    // deliver it even if the notification is ignored.
                    File.AppendAllText(path, Route("during-seed"), new UTF8Encoding(false));
                }, delegate(string path) {
                    if (path == bad) throw new IOException("synthetic");
                    NavigationCursor cursor = cursors.GetOrAdd(path, delegate { return new NavigationCursor(0); });
                    lock (cursor) using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                        string text = cursor.Read(file);
                        Require(!text.Contains("client-new-thread:history"), "history-replay");
                        if (text.Contains("during-seed")) Interlocked.Increment(ref records);
                        if (text.Contains("after-setup")) delivered.Set();
                    }
                }, delegate(string stage, Exception error) { Interlocked.Increment(ref errors); });
                Require(watcher.EnableRaisingEvents && seeds == 1 && records == 1 && errors >= 2, "file-failure-isolation");
                File.WriteAllText(created, Route("after-setup"), new UTF8Encoding(false));
                Require(delivered.WaitOne(3000), "listener-survives");
                Require(records == 1, "catch-up-duplicate");
            } finally {
                if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); }
                foreach (string file in Directory.GetFiles(root)) File.Delete(file);
                Directory.Delete(root);
            }
        }
    }
}
