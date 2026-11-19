using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal static class StartupConnectionTests {
    static void Require(bool value, string stage) { if (!value) throw new InvalidOperationException("startup-connect-" + stage); }
    internal static bool Run() {
        string name = "codex-startup-connect-" + Guid.NewGuid().ToString("N");
        Task<string> receiver = Task.Factory.StartNew(delegate {
            Thread.Sleep(300); // Longer than the old 80 ms connection deadline.
            using (var pipe = ResidentProtocol.CreateServer(name)) {
                var accept = pipe.BeginWaitForConnection(null, null);
                if (!accept.AsyncWaitHandle.WaitOne(3500)) return null;
                pipe.EndWaitForConnection(accept);
                using (var reader = new StreamReader(pipe)) {
                    Task<string> text = reader.ReadToEndAsync();
                    return text.Wait(1500) ? text.Result : null;
                }
            }
        });
        Require(IntroWatcher.SignalResident("desktop-start", null, name), "late-server-not-delivered");
        Require(receiver.Wait(4500) && receiver.Result == "desktop-start" + Environment.NewLine, "not-exactly-one-command");
        var elapsed = Stopwatch.StartNew();
        Require(!IntroWatcher.SignalResident("new-dialog-click", null, name + "-missing"), "missing-ordinary-server");
        Require(elapsed.ElapsedMilliseconds < 1000, "ordinary-deadline-regressed");
        elapsed.Restart();
        Require(!IntroWatcher.SignalResident("desktop-start", null, name + "-missing"), "missing-startup-server");
        Require(elapsed.ElapsedMilliseconds >= 1500 && elapsed.ElapsedMilliseconds < 4000, "startup-deadline-unbounded");
        return true;
    }
}
