using System;
using System.Diagnostics;
using System.Threading;

// The low-level click hook must keep pumping messages. Reconciliation is a
// safety net, not UI work: at most one worker runs, with no queued backlog.
internal sealed class WatcherReconcile {
    readonly object gate=new object();
    bool running,stopped;
    internal bool Stopped { get { lock(gate)return stopped; } }
    internal bool TryQueue(Action work,Action<Exception> failed) {
        lock(gate) {
            if(stopped||running)return false;
            running=true;
        }
        try {
            ThreadPool.QueueUserWorkItem(delegate {
                try { if(!Stopped)work(); }
                catch(Exception error) { try { if(failed!=null)failed(error); } catch { } }
                finally { lock(gate)running=false; }
            });
            return true;
        } catch { lock(gate)running=false;return false; }
    }
    internal void Stop() { lock(gate)stopped=true; }
    internal static bool SelfTest() {
        using(var entered=new ManualResetEvent(false))
        using(var release=new ManualResetEvent(false))
        using(var finished=new ManualResetEvent(false)) {
            var worker=new WatcherReconcile();var watch=Stopwatch.StartNew();
            if(!worker.TryQueue(delegate{entered.Set();release.WaitOne(1000);finished.Set();},null))return false;
            if(watch.ElapsedMilliseconds>500||!entered.WaitOne(1000)) { release.Set();return false; }
            // Slow I/O is not on the caller thread, and cannot build a backlog.
            if(worker.TryQueue(delegate{},null)) { release.Set();return false; }
            worker.Stop();release.Set();
            if(!finished.WaitOne(1000)||worker.TryQueue(delegate{},null))return false;
        }
        using(var faulted=new ManualResetEvent(false)) {
            var worker=new WatcherReconcile();
            if(!worker.TryQueue(delegate{throw new InvalidOperationException();},delegate{faulted.Set();}))return false;
            if(!faulted.WaitOne(1000))return false;
            // The failing job releases its gate, so the next sweep can run.
            using(var recovered=new ManualResetEvent(false)) {
                bool queued=false;
                for(int i=0;i<100&&!queued;i++) { queued=worker.TryQueue(delegate{recovered.Set();},null);if(!queued)Thread.Sleep(10); }
                if(!queued||!recovered.WaitOne(1000))return false;
            }
            worker.Stop();
        }
        return true;
    }
}
