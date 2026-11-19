using System;
using System.Globalization;
using System.Threading;

// At most 20 metadata samples per startup, using the existing UI timer.
// No pixels, paths or user content; disk I/O is never performed on the UI thread.
internal sealed class StartupFrameTrace {
    readonly Action<string> write;
    int remaining;
    long previous;
    internal StartupFrameTrace(Action<string> write) { this.write = write; }
    internal void Begin(bool startup) { remaining = startup ? 20 : 0; previous = 0; }
    internal void Sample(string stage, long elapsedMs, double position, bool covered) {
        if (remaining == 0) return;
        if (elapsedMs > 2000) { remaining = 0; return; }
        remaining--;
        string line = "startup-frame stage=" + stage + " elapsed-ms=" + elapsedMs +
            " gap-ms=" + (elapsedMs-previous) + " position=" + position.ToString("F4", CultureInfo.InvariantCulture) + " covered=" + covered;
        previous = elapsedMs;
        write(line);
    }
    internal static StartupFrameTrace Create() {
        return new StartupFrameTrace(delegate(string line) { ThreadPool.QueueUserWorkItem(delegate { IntroLog.Write(line); }); });
    }
    internal static bool SelfTest() {
        int count = 0;
        var trace = new StartupFrameTrace(delegate { count++; });
        trace.Begin(false); trace.Sample("tick", 1, 0, true); if(count!=0)return false;
        trace.Begin(true); for(int i=0;i<30;i++)trace.Sample("tick", i*50, 0, true); if(count!=20)return false;
        trace.Begin(true);trace.Sample("tick",2001,0,true);trace.Sample("tick",1,0,true);if(count!=20)return false;
        trace.Begin(true);trace.Sample("tick",50,0.6,true);return count==21;
    }
}
