using System;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

// One-shot automatic completion window. The pipe is independent of resident
// intro IPC, so decoder readiness cannot stall new-chat/anger dispatch.
internal sealed class CompletionDeliveryServer:IDisposable {
 readonly string name;readonly CompletionDeliveryReceiver receiver;readonly Thread worker;
 readonly object gate=new object();NamedPipeServerStream current;bool stopping;
 internal CompletionDeliveryServer(string name,CompletionDeliveryReceiver receiver){this.name=name;this.receiver=receiver;worker=new Thread(Run){IsBackground=true,Name="Completion acknowledgement"};worker.Start();}
 void Run(){
  while(true){
   try{
    using(var pipe=ResidentProtocol.CreateServer(name)){
     lock(gate){if(stopping)return;current=pipe;}
     pipe.WaitForConnection();
     string message=ResidentProtocol.ReadCommand(pipe,200);
     string[] parts=(message??"").Split('|');long deadline;
     if(parts.Length!=2||!CompletionDeliveryStore.ValidKey(parts[0])||
      !Int64.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out deadline)||
      deadline<=DateTime.UtcNow.Ticks)continue;
     int wait=(int)Math.Min(CompletionDeliveryStore.WindowMs,Math.Max(0,(deadline-DateTime.UtcNow.Ticks)/TimeSpan.TicksPerMillisecond));
     string response=receiver.Receive(parts[0],Math.Max(0,wait-15));byte[] bytes=Encoding.ASCII.GetBytes(response+"\n");
     var write=pipe.WriteAsync(bytes,0,bytes.Length);if(!write.Wait(15))pipe.Dispose();
    }
   }catch(Exception e){lock(gate){if(stopping)return;}IntroLog.Write("completion-ack-pipe-error type="+e.GetType().Name);Thread.Sleep(10);}
   finally{lock(gate){current=null;}}
   lock(gate){if(stopping)return;}
  }
 }
 public void Dispose(){lock(gate){stopping=true;if(current!=null)current.Dispose();}worker.Join(250);}
}

internal static class CompletionDeliveryHost {
 internal static int Run(string key){
  if(!CompletionDeliveryStore.ValidKey(key))return 2;
  var request=CompletionDeliveryStore.ReadRequest(key);
  if(request==null||request.Status!="pending"||CompletionDeliveryStore.Remaining(request)==0||CompletionDeliveryStore.Receipt(key)!=null)return 2;
  if(System.IO.File.Exists(System.IO.Path.Combine(IntroLog.Data,"paused"))||!SceneSettings.Allows("task-complete"))return 2;
  bool created;
  using(var pending=new Mutex(true,"Local\\CodexCompletionPendingV1",out created)){
   if(!created)return 2; // Manual previews never count as automatic delivery.
   CodexVideoWindow window;string reason;
   if(!CompletionPlayback.TryCreate(out window,out reason)){IntroLog.Write("completion-delivery-host-rejected reason="+reason);return 2;}
   using(var receiver=new CompletionDeliveryReceiver())using(var server=new CompletionDeliveryServer(CompletionDelivery.PipeName,receiver)){
    window.CompletionStarted+=receiver.MarkReady;
    window.Closed+=delegate{
     try{if(window.CompletionCloseReason=="skip-button"||window.CompletionCloseReason=="escape")receiver.Dismiss(key);}
     catch(Exception e){IntroLog.Write("completion-dismiss-error type="+e.GetType().Name);}
     finally{receiver.MarkClosed();}
    };
    var guard=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};
    guard.Tick+=delegate{if(receiver.HasAccepted){guard.Stop();return;}if(CompletionDeliveryStore.Remaining(request)==0){guard.Stop();window.Close();IntroLog.Write("completion-delivery-host-expired task="+key.Substring(0,12));}};
    window.Closed+=delegate{guard.Stop();};
    if(CompletionDeliveryStore.Remaining(request)==0){window.Close();return 2;}
    IntroWatcher.SignalResident("__close_idle__");
    IntroLog.Write("completion-presentation mode="+(window.IsCompletionNotice?"notification":"codex-window"));
    var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
    if(CompletionDeliveryStore.Remaining(request)==0){window.Close();return 2;}
    guard.Start();app.Run(window);
    return receiver.HasAccepted?0:2;
   }
  }
 }
}
