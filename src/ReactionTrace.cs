using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

// Diagnostic-only identity. The command bytes/pipe name and playback decisions
// remain unchanged. Keep a query handle across ReadCommand so sender exit/PID
// reuse cannot replace the originating process's creation time.
internal sealed class ReactionPeer : IDisposable {
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint id);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    IntPtr process;
    readonly uint pid;
    ReactionPeer(uint id, IntPtr handle) { pid=id; process=handle; }
    internal static ReactionPeer Capture(NamedPipeServerStream pipe) {
        try {
            uint id;
            if(GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out id))
                return new ReactionPeer(id,OpenProcess(0x1000,false,id));
        } catch { }
        return new ReactionPeer(0,IntPtr.Zero);
    }
    static string Identity(uint id, IntPtr handle) {
        long created,exited,kernel,user;
        return id!=0 && handle!=IntPtr.Zero && GetProcessTimes(handle,out created,out exited,out kernel,out user)
            ? "p"+id+"-"+created.ToString("x16") : null;
    }
    internal string Id { get { try { return Identity(pid,process); } catch { return null; } } }
    internal static string CurrentId() {
        try { using(var current=Process.GetCurrentProcess())return Identity((uint)current.Id,current.Handle); }
        catch { return null; }
    }
    public void Dispose() { IntPtr handle=Interlocked.Exchange(ref process,IntPtr.Zero); if(handle!=IntPtr.Zero)CloseHandle(handle); }
}

// Buffer hook diagnostics until AFTER dispatch. Window/pipe diagnostics flush on
// the existing thread pool, never synchronously write new logs on the UI path.
internal sealed class ReactionTrace {
    internal const int MaxRecords=32;
    static readonly Regex IdPattern=new Regex(@"^p[1-9][0-9]{0,9}-[0-9a-f]{16}$");
    static readonly HashSet<string> Phases=new HashSet<string>(new[]{
        "origin-unavailable","hook-entry","input-read","payload-parsed","state-begin","state-end",
        "match-begin","match","dispatch-begin","hook-exit","pipe-connect-begin","pipe-connected",
        "pipe-written","pipe-failed","pipe-received","dispatch-queued","dispatch-enter",
        "fallback-launched","fallback-entry","rejected","window-opened","content-rendered",
        "media-opened","first-moving-frame","closed","window-closed"});
    static readonly HashSet<string> Results=new HashSet<string>(new[]{
        "yes","no","audio","no-audio","input-too-large","invalid-payload","missing-prompt",
        "paused","scene-disabled","busy-or-paused","not-codex-foreground","missing-video",
        "fallback-launch-failed","skip-button","escape","ended","timeout","owner-gone",
        "codex-not-foreground","input-monitor-unavailable","user-active","owner-content-unavailable",
        "media-failed"});
    static readonly Regex ErrorPattern=new Regex(@"^(?:hook-error-|target-error-)?(?:IOException|TimeoutException|UnauthorizedAccessException|InvalidOperationException|ArgumentException|FormatException|ObjectDisposedException|Win32Exception|JsonException)$");
    static readonly int LoggerPid=Process.GetCurrentProcess().Id;
    static readonly object LogGate=new object();
    internal static readonly string LogPath=Path.Combine(IntroLog.Data,"reaction-trace.log");
    readonly object gate=new object(),writeGate=new object();
    readonly List<string> pending=new List<string>();
    readonly Stopwatch elapsed=Stopwatch.StartNew();
    readonly Action<string> sink;
    readonly string segment;
    int records,scheduled;
    internal readonly string Id;
    internal ReactionTrace(string id,string segment,Action<string> writer=null) {
        Id=ValidId(id)?id:"unavailable"; this.segment=segment=="hook"||segment=="resident"||segment=="fallback"?segment:"unspecified";
        sink=writer??WriteBatch;
        if(Id=="unavailable")Record("origin-unavailable");
    }
    internal static bool ValidId(string id) { return id!=null && id.Length<=38 && IdPattern.IsMatch(id); }
    static string Phase(string value) { return value!=null && Phases.Contains(value)?value:"unspecified"; }
    static string Result(string value) { return value!=null && (Results.Contains(value)||ErrorPattern.IsMatch(value))?value:"unspecified"; }
    internal void Record(string phase,string result=null) {
        try { lock(gate) {
            if(records>=MaxRecords)return;
            records++;
            pending.Add(DateTime.UtcNow.ToString("o")+" pid="+LoggerPid+
                " reaction-trace id="+Id+" segment="+segment+" phase="+Phase(phase)+
                " elapsed-ms="+elapsed.ElapsedMilliseconds+(result==null?"":" result="+Result(result)));
        } } catch { }
    }
    static void WriteBatch(string batch) {
        Directory.CreateDirectory(IntroLog.Data);
        // Separate from legacy playback writes. Exclusive append plus bounded
        // retry handles a hook batch arriving while the resident writes its batch.
        lock(LogGate) for(int attempt=0;attempt<3;attempt++) {
            try { File.AppendAllText(LogPath,batch);return; }
            catch(IOException) { if(attempt==2)return;Thread.Sleep(5); }
        }
    }
    internal void Flush() {
        try { lock(writeGate) {
            string batch;
            lock(gate) { if(pending.Count==0)return; batch=String.Join(Environment.NewLine,pending)+Environment.NewLine; pending.Clear(); }
            sink(batch);
        } } catch { } // A diagnostic failure must never block/fail playback.
    }
    internal void QueueFlush() {
        try {
            if(Interlocked.CompareExchange(ref scheduled,1,0)!=0)return;
            ThreadPool.QueueUserWorkItem(delegate {
                try { Flush(); } finally {
                    Interlocked.Exchange(ref scheduled,0);
                    bool more; lock(gate)more=pending.Count!=0;
                    if(more)QueueFlush();
                }
            });
        } catch { Interlocked.Exchange(ref scheduled,0); }
    }
    internal void Reject(string reason) { Record("rejected",reason); QueueFlush(); }
}
