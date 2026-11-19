using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

internal static class RepairRegressionTests {
    static void Require(bool value, string step) { if (!value) throw new InvalidOperationException(step); }

    internal static int LogChild(string folder, string identity) {
        string full = Path.GetFullPath(folder);
        if (!full.StartsWith(Path.Combine(Path.GetTempPath(), "codex-repair-"), StringComparison.OrdinalIgnoreCase)) return 2;
        for (int i = 0; i < 30; i++)
            if (!RotatingLog.Append(Path.Combine(full, "shared.log"), identity + "-" + i + "\n", 65536)) return 3;
        return 0;
    }

    internal static bool Run() {
        string folder = Path.Combine(Path.GetTempPath(), "codex-repair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            // Real independent processes must not lose records to sharing/rotation races.
            var children = new Process[4];
            try {
                for (int i = 0; i < children.Length; i++)
                    children[i] = Process.Start(new ProcessStartInfo {
                        FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BootPlayer.exe"),
                        Arguments = "--repair-log-child \"" + folder + "\" " + i,
                        UseShellExecute = false, CreateNoWindow = true
                    });
                foreach (var child in children) {
                    if (!child.WaitForExit(5000)) { child.Kill(); throw new InvalidOperationException("log-child-timeout"); }
                    Require(child.ExitCode == 0, "log-child-failed");
                }
            } finally { foreach (var child in children) if (child != null) child.Dispose(); }
            Require(File.ReadAllLines(Path.Combine(folder, "shared.log")).Length == 120, "concurrent-log-loss");
            string log = Path.Combine(folder, "rotate.log");
            for (int i = 0; i < 100; i++) Require(RotatingLog.Append(log, i + new string('x', 80) + "\n", 1024), "log-write-failed");
            Require(new FileInfo(log).Length <= 1024 && new FileInfo(log + ".1").Length <= 1024, "rotation-size");

            // One silent client must not block a second connection to the same name.
            string name = ResidentProtocol.Name + "-repair-" + Guid.NewGuid().ToString("N");
            using (var blocked = ResidentProtocol.CreateServer(name))
            using (var ready = ResidentProtocol.CreateServer(name)) {
                var accept = blocked.BeginWaitForConnection(null, null);
                using (var silent = new NamedPipeClientStream(".", name, PipeDirection.Out)) {
                    silent.Connect(500); blocked.EndWaitForConnection(accept);
                    Task<string> slow = Task.Factory.StartNew(delegate { return ResidentProtocol.ReadCommand(blocked, 700); });
                    var next = ready.BeginWaitForConnection(null, null);
                    using (var client = new NamedPipeClientStream(".", name, PipeDirection.Out)) {
                        var elapsed = Stopwatch.StartNew();
                        client.Connect(300); ready.EndWaitForConnection(next);
                        byte[] data = Encoding.UTF8.GetBytes("__reload__\n"); client.Write(data, 0, data.Length); client.Flush();
                        Require(ResidentProtocol.ReadCommand(ready, 300) == "__reload__", "second-client-lost");
                        Require(elapsed.ElapsedMilliseconds < 600, "silent-client-blocked-second");
                    }
                    Require(slow.Wait(1500) && slow.Result == null, "silent-client-not-expired");
                }
            }
            // Real register/unregister exercises ABI, hidden HWND and native cleanup.
            using (var input = new RawMouseInput(delegate(int x, int y) { })) { }
            Require(NewChatHitTest.Matches("New chat (Ctrl+N)", "", new[] { "New chat" }, new string[0]), "shortcut-label");
            Require(NewChatHitTest.Matches("", "new-thread-button", new string[0], new[] { "new-thread-button" }), "icon-only-selector");
            Require(!NewChatHitTest.Matches("Delete chat", "", new[] { "New chat" }, new string[0]), "false-button");
            Require(NavigationCursorTests.Run(), "bounded-navigation-tail-and-generation");
            Require(CompletionJournalTests.Run(), "bounded-completion-journal");
            Require(NavigationStartupTests.Run(), "bounded-navigation-startup");
            Require(StartupConnectionTests.Run(), "startup-resident-connection");
            Require(DialogRouteStateTests.Run(), "window-scoped-dialog-route");
            Require(DialogTriggerTests.Run(), "confirmed-dialog-event-arbitration");
            Require(StartupFrameTrace.SelfTest(), "bounded-startup-frame-trace");
            Require(IntroCoverGate.SelfTest(), "intro-cover-gate");
            Require(RestartBackoff.SelfTest(), "restart-backoff");
            Require(RuntimeCleanup.SelfTest(), "runtime-cleanup");
            Require(CompletionGate.SelfTest(), "completion-gate");
            // Release the actual WPF cache and confirm a fresh media slot is created.
            var window = new IntroWindow(null, "--resident", "desktop-start");
            try {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var slots = (IDictionary)typeof(IntroWindow).GetField("slots", flags).GetValue(window);
                string[] paths = MediaLibrary.Prepared(); Require(paths.Length > 0, "media-fixture");
                var prepare = typeof(IntroWindow).GetMethod("EnsurePrepared", flags);
                object previous = prepare.Invoke(window, new object[] { paths[0] });
                // Pre-created resident slots stay lazy (Source == null) until explicitly opened.
                var mediaField = previous.GetType().GetField("Media", BindingFlags.Instance | BindingFlags.Public);
                Require(((System.Windows.Controls.MediaElement)mediaField.GetValue(previous)).Source == null, "lazy-slot-opened-before-loaded");
                window.ReleaseIdleMedia();
                Require(slots.Count == 0, "idle-cache-not-released");
                object current = prepare.Invoke(window, new object[] { paths[0] });
                Require(!Object.ReferenceEquals(previous, current) && slots.Count == 1, "idle-cache-not-recreated");
                // Queuing an unready slot must not flash a frozen window before MediaOpened,
                // and a failure on that pending slot must clear pending state cleanly.
                window.ReleaseIdleMedia();
                window.PlayResident("preview-startup");
                string pendingPath = (string)typeof(IntroWindow).GetField("pendingPath", flags).GetValue(window);
                Require(!window.IsVisible && !window.ShowInTaskbar && !String.IsNullOrEmpty(pendingPath), "pending-slot-flashed-window");
                var fail = typeof(IntroWindow).GetMethod("HandleMediaFailure", flags);
                fail.Invoke(window, new object[] { slots[pendingPath], "synthetic-pending-failure" });
                Require(typeof(IntroWindow).GetField("pendingPath", flags).GetValue(window) == null, "pending-failure-not-cleared");
            } finally { window.Close(); }
            IntroLog.Write("repair-regression-test pass=True");
            return true;
        } catch (Exception error) {
            IntroLog.Write("repair-regression-test pass=False stage=" + error.Message);
            return false;
        } finally {
            foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }
}
