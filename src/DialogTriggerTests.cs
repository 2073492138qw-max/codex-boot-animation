using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

internal static class DialogTriggerTests {
    static void Require(bool value, string step) { if (!value) throw new InvalidOperationException("dialog-trigger-" + step); }
    static readonly DateTime Epoch = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
    static string Line(int tick, int window, string token, string route) {
        return Epoch.AddMilliseconds(tick).ToString("O") + " IAB_LIFECYCLE received browser sidebar owner sync conversationId=" + token + " ownerRoutePath=" + route + " windowId=" + window;
    }

    sealed class Fixture : IDisposable {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "codex-dialog-trigger-" + Guid.NewGuid().ToString("N"));
        internal readonly DialogRouteState Routes = new DialogRouteState();
        readonly IntroWatcher watcher;
        readonly WatcherReconcile worker = new WatcherReconcile();
        readonly NavigationTailPoll tail = new NavigationTailPoll();
        static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
        internal int Dispatches;
        internal string PathOne, PathTwo;
        internal Fixture() {
            Directory.CreateDirectory(Root);
            PathOne = Path.Combine(Root, "one.log"); PathTwo = Path.Combine(Root, "two.log");
            File.WriteAllText(PathOne, ""); File.WriteAllText(PathTwo, "");
            tail.Track(PathOne, "10:1"); tail.Track(PathTwo, "10:1");
            watcher = (IntroWatcher)FormatterServices.GetUninitializedObject(typeof(IntroWatcher));
            Set("dialogRoutes", Routes); Set("dialogRouteLock", new object());
            Set("dialogIdentity", new Func<string,string,string>(delegate(string path,string line) { return path == PathOne || path == PathTwo ? "10:1" : null; }));
            Set("dialogDispatch", new Action<string>(delegate(string reason) { Require(reason == "new-dialog-log-fallback", "dispatch-reason"); Interlocked.Increment(ref Dispatches); }));
            Set("navigationTail", tail); Set("navigationWorker", worker); Set("navigationClock", Stopwatch.StartNew());
            Set("navigationCursors", new ConcurrentDictionary<string,NavigationCursor>(StringComparer.OrdinalIgnoreCase));
            Set("navigationHistoryPending", new ConcurrentDictionary<string,bool>());
            Set("navigationStartedUtc", DateTime.UtcNow.AddMinutes(-1)); Set("logOffsets", new Dictionary<string,long>());
            Set("readyProcesses", new ConcurrentDictionary<string,bool>());
        }
        internal void Set(string name, object value) { typeof(IntroWatcher).GetField(name, Flags).SetValue(watcher, value); }
        internal void Append(string path, string line) { File.AppendAllText(path, line + "\n", new UTF8Encoding(false)); }
        internal void Read(string path) { typeof(IntroWatcher).GetMethod("ReadNewLogLines", Flags).Invoke(watcher, new object[] { path, "test-event" }); }
        internal void Click() {
            Set("nextClickRead", 0L); // Separate gestures; production still limits hint I/O to 250 ms.
            watcher.QueueNavigationReadHint();
            WaitIdle();
        }
        void WaitIdle() {
            object gate = typeof(WatcherReconcile).GetField("gate", Flags).GetValue(worker);
            FieldInfo running = typeof(WatcherReconcile).GetField("running", Flags);
            var timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 3000) {
                lock (gate) { if (!(bool)running.GetValue(worker)) return; }
                Thread.Sleep(5);
            }
            throw new InvalidOperationException("dialog-trigger-worker-timeout");
        }
        public void Dispose() {
            worker.Stop(); WaitIdle(); tail.Stop();
            File.Delete(PathOne); File.Delete(PathTwo); Directory.Delete(Root);
        }
    }

    internal static bool Run() {
        using (var f = new Fixture()) {
            // Click first: no speculative animation, then one confirmed event.
            f.Click(); Require(f.Dispatches == 0, "click-without-log");
            string a = Line(1, 1, "client-new-thread:a", "/");
            f.Append(f.PathOne, a); f.Read(f.PathOne); f.Click();
            Require(f.Dispatches == 1, "click-then-log");
            // Log first / delayed click callback must not dispatch again.
            string b = Line(2, 1, "client-new-thread:b", "/");
            f.Append(f.PathOne, b); f.Read(f.PathOne); f.Click();
            Require(f.Dispatches == 2, "log-then-click-and-rapid-new-token");
            // The same source event in another file does not replay.
            f.Append(f.PathTwo, b); f.Read(f.PathTwo); f.Click();
            Require(f.Dispatches == 2, "duplicate-across-files");
            // Different windows are independent even with the same token/time.
            f.Append(f.PathTwo, Line(2, 2, "client-new-thread:b", "/")); f.Read(f.PathTwo);
            Require(f.Dispatches == 3, "other-window");
            // Older files cannot undo a more recent window state.
            f.Append(f.PathTwo, Line(0, 1, "existing-thread", "/local/old")); f.Append(f.PathTwo, a); f.Read(f.PathTwo);
            Require(f.Dispatches == 3, "stale-event-order");
            // Returning from an ordinary (non client-new-thread) conversation resets this window only.
            f.Append(f.PathOne, Line(3, 1, "existing-thread", "/local/old"));
            f.Append(f.PathOne, Line(4, 1, "client-new-thread:b", "/")); f.Click();
            Require(f.Dispatches == 4, "leave-and-return");
            f.Set("paused", true); f.Append(f.PathOne, Line(5, 1, "client-new-thread:c", "/")); f.Read(f.PathOne);
            f.Set("paused", false); f.Append(f.PathTwo, Line(5, 1, "client-new-thread:c", "/")); f.Read(f.PathTwo);
            Require(f.Dispatches == 4, "paused-event-does-not-replay");
            f.Append(f.PathOne, Line(6, 1, "client-new-thread:d", "/"));
            // Event and accelerated tail readers race on the same production cursor.
            Exception failure = null;
            var thread = new Thread(delegate() { try { f.Read(f.PathOne); } catch (Exception e) { failure=e; } });
            thread.Start(); f.Click(); Require(thread.Join(3000) && failure == null && f.Dispatches == 5, "concurrent-read-once");
        }
        using (var f = new Fixture()) {
            f.Routes.MarkDesktopStarted("10:1");
            f.Append(f.PathOne, Line(1, 1, "client-new-thread:home", "/")); f.Click();
            Require(f.Dispatches == 0, "startup-home");
            f.Append(f.PathOne, Line(2, 1, "client-new-thread:user-action", "/")); f.Click();
            Require(f.Dispatches == 1, "no-global-startup-cooldown");
        }
        bool blank; string token;
        Require(!DialogRouteState.TryRoute(Line(1, 1, "client-new-thread:", "/"), out blank, out token), "empty-token");
        Require(!DialogRouteState.TryRoute(Line(1, 1, "existing", "/"), out blank, out token), "non-new-home");
        Require(!DialogRouteState.TryRoute(Line(1, 1, "client-new-thread:a", "/") + " conversationId=client-new-thread:b", out blank, out token), "ambiguous-token");
        Require(DialogRouteState.TryRoute(Line(1, 1, "client-new-thread:a", "/"), out blank, out token) && token.Length == 44 && !token.Contains("client-new-thread"), "bounded-fingerprint");
        return true;
    }
}
