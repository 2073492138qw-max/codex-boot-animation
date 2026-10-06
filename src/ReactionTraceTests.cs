using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class ReactionTraceTests {
    internal static bool Run() {
        try {
            string id=ReactionPeer.CurrentId();
            if(!ReactionTrace.ValidId(id)||ReactionTrace.ValidId("p1-abc\nprivate-text"))return false;
            var rows=new List<string>();
            var trace=new ReactionTrace(id,"hook",delegate(string batch){rows.Add(batch);});
            trace.Record("hook-entry"); trace.Record("match","yes");
            if(rows.Count!=0)return false; // No new disk/sink work before dispatch.
            trace.Record("private\ntext","secret=value"); trace.Record("match","private-message");trace.Flush();
            string logged=String.Join("",rows);
            if(!logged.Contains("phase=hook-entry")||!logged.Contains("phase=match")||
                logged.Contains("private")||logged.Contains("secret")||!logged.Contains("id="+id))return false;
            rows.Clear();
            for(int i=0;i<100;i++)trace.Record("match");
            trace.Flush();
            if(String.Join("",rows).Split(new[]{Environment.NewLine},StringSplitOptions.RemoveEmptyEntries).Length!=ReactionTrace.MaxRecords-4)return false;
            var broken=new ReactionTrace(id,"hook",delegate{throw new InvalidOperationException();});
            broken.Record("match","yes");broken.Flush();broken.Record("pipe-written");broken.Flush();
            string name=ResidentProtocol.Name+"-trace-test-"+Guid.NewGuid().ToString("N");
            using(var server=ResidentProtocol.CreateServer(name)) {
                IAsyncResult accept=server.BeginWaitForConnection(null,null);
                var result=Task.Factory.StartNew(delegate {
                    server.EndWaitForConnection(accept);
                    using(var peer=ReactionPeer.Capture(server)) {
                        string command=ResidentProtocol.ReadCommand(server,1000);
                        return command=="reaction-angry" && peer.Id==id;
                    }
                });
                using(var client=new NamedPipeClientStream(".",name,PipeDirection.Out)) {
                    client.Connect(1000);
                    byte[] bytes=Encoding.UTF8.GetBytes("reaction-angry\r\n");
                    client.Write(bytes,0,bytes.Length);client.Flush();
                    if(!result.Wait(1500)||!result.Result)return false;
                }
            }
            // Hold the kernel process object through sender exit. No guessing by
            // PID or querying a later process that happens to reuse that PID.
            name=ResidentProtocol.Name+"-trace-child-"+Guid.NewGuid().ToString("N");
            using(var server=ResidentProtocol.CreateServer(name)) {
                IAsyncResult accept=server.BeginWaitForConnection(null,null);
                string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe");
                using(var child=Process.Start(new ProcessStartInfo{FileName=exe,Arguments="--reaction-trace-peer-test "+name,UseShellExecute=false,CreateNoWindow=true})) {
                    if(!accept.AsyncWaitHandle.WaitOne(1500)) { child.Kill();return false; }
                    server.EndWaitForConnection(accept);
                    using(var peer=ReactionPeer.Capture(server)) {
                        string identity=peer.Id;
                        if(!ReactionTrace.ValidId(identity)||!identity.StartsWith("p"+child.Id+"-",StringComparison.Ordinal))return false;
                        if(ResidentProtocol.ReadCommand(server,1000)!="reaction-angry")return false;
                        if(!child.WaitForExit(1500)||child.ExitCode!=0||peer.Id!=identity)return false;
                        peer.Dispose();if(peer.Id!=null)return false;
                    }
                }
            }
            // Independent traces cannot steal each other's stages; async writes
            // also capture event time before worker scheduling.
            var done=new ManualResetEvent(false);
            var asyncTrace=new ReactionTrace(id,"resident",delegate(string batch){if(batch.Contains("phase=window-opened"))done.Set();});
            asyncTrace.Record("window-opened");asyncTrace.QueueFlush();
            if(!done.WaitOne(1500))return false;
            done.Dispose();
            rows.Clear();
            var other=new ReactionTrace("p1-0000000000000001","resident",delegate(string batch){rows.Add(batch);});
            other.Reject("busy-or-paused");other.Flush();
            string separate=String.Join("",rows);
            if(separate.Contains("id="+id)||!separate.Contains("result=busy-or-paused"))return false;
            IntroLog.Write("reaction-trace-test pass=True");return true;
        } catch(Exception e) { IntroLog.Write("reaction-trace-test-error type="+e.GetType().Name);return false; }
    }
    internal static int PeerChild(string name) {
        using(var client=new NamedPipeClientStream(".",name,PipeDirection.Out)) {
            client.Connect(1000);Thread.Sleep(100);
            byte[] bytes=Encoding.UTF8.GetBytes("reaction-angry\n");
            client.Write(bytes,0,bytes.Length);client.Flush();
        }
        return 0;
    }
}
