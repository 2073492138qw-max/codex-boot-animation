using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// All records, endpoints and peer processes are isolated. No Codex windows,
// registered helper, user settings, real task IDs or actual playback are used.
internal static class CompletionDeliveryTests {
 static Dictionary<string,object> Payload(string session,string turn,bool prompt) {
  return new Dictionary<string,object>{{"session_id",session},{"turn_id",turn},
   {prompt?"prompt":"last_assistant_message",prompt?"帮我修复插件":"已修复。"}};
 }
 static void Require(bool pass,string name) {
  if(!pass)throw new InvalidOperationException(name);
  IntroLog.Write("completion-delivery-test-case pass=True case="+name);
 }
 static string Key(){return CompletionStateStore.Hash(Guid.NewGuid().ToString("N"));}
 internal static bool Run() {
  string folder=Path.Combine(Path.GetTempPath(),"codex-completion-delivery-"+Guid.NewGuid().ToString("N"));
  CompletionStateStore.TestFolder=folder;
  try {
   Directory.CreateDirectory(folder);
   string key=Key();int calls=0;
   Require(CompletionDeliveryStore.Begin(key),"reserve");
   Require(CompletionDelivery.Send(key,delegate(string id){calls++;return calls==2;}),"transient-failure-retry");
   Require(calls==2&&CompletionDeliveryStore.ReadRequest(key).Attempts==2&&CompletionDeliveryStore.Receipt(key)=="accepted","confirmed-not-launched");
   CompletionDelivery.Send(key,delegate(string id){calls++;return true;});
   Require(calls==2&&!CompletionDeliveryStore.Begin(key),"receipt-restart-dedupe");

   key=Key();calls=0;CompletionDeliveryStore.Begin(key);
   Require(!CompletionDelivery.Send(key,delegate(string id){calls++;return false;}),"delivery-failure");
   Require(calls==3&&CompletionDeliveryStore.ReadRequest(key).Status=="failed"&&!CompletionDeliveryStore.Begin(key),"two-retries-no-infinite-replay");
   key=Key();CompletionDeliveryStore.Begin(key);int unconfirmedLaunches=0;
   Require(!CompletionDelivery.SendTo(key,delegate(string id,int deadline){return "accepted";},delegate(string id){unconfirmedLaunches++;return null;})&&unconfirmedLaunches==0,"unconfirmed-pipe-reply-not-success");
   key=Key();calls=0;CompletionDeliveryStore.Begin(key);
   Require(CompletionDelivery.Send(key,delegate(string id){calls++;if(calls==1)throw new IOException();return true;})&&calls==2,"transient-exception-retry");

   key=Key();CompletionDeliveryStore.Begin(key);
   File.WriteAllText(Path.Combine(CompletionDeliveryStore.Folder,key+".request"),"1|"+DateTime.UtcNow.AddSeconds(-3).Ticks+"|1|pending");
   Require(!CompletionDeliveryStore.Begin(key)&&CompletionDeliveryStore.ReadRequest(key).Status=="failed"&&!CompletionDeliveryStore.Accept(key,"accepted"),"expired-after-crash-no-late-play");

   using(var receiver=new CompletionDeliveryReceiver()) {
    key=Key();CompletionDeliveryStore.Begin(key);
    Require(receiver.Receive(key,1)=="pending"&&CompletionDeliveryStore.Receipt(key)==null,"loading-not-delivered");
    receiver.MarkReady();Require(receiver.Receive(key,50)=="accepted","ready-delivered");
    string second=Key();CompletionDeliveryStore.Begin(second);
    Require(receiver.Receive(second,50)=="merged"&&CompletionDeliveryStore.Receipt(second)=="merged","second-task-merged");
    Require(receiver.Receive(key,50)=="accepted","same-task-not-merged-again");
    receiver.MarkClosed();string third=Key();CompletionDeliveryStore.Begin(third);
    Require(receiver.Receive(third,50)=="closed"&&CompletionDeliveryStore.Receipt(third)==null,"closed-window-not-merged");
   }
   using(var receiver=new CompletionDeliveryReceiver()) {
    key=Key();CompletionDeliveryStore.Begin(key);receiver.Dismiss(key);receiver.MarkClosed();
    Require(receiver.Receive(key,20)=="dismissed","skip-before-first-frame-acknowledged");
    calls=0;Require(CompletionDelivery.Send(key,delegate(string id){calls++;return true;})&&calls==0,"explicit-skip-never-reopens");
    string other=Key();CompletionDeliveryStore.Begin(other);
    Require(receiver.Receive(other,20)=="closed"&&CompletionDeliveryStore.Receipt(other)==null,"skip-not-applied-to-another-task");
   }
   // A receipt survives lost IPC acknowledgement; never play a duplicate.
   LostAcknowledgement();
   // Silent receiver cannot keep a hook waiting forever.
   SilentReceiver();
   // Actual private named pipe + separately launched peer, two production flows.
   IndependentPeer(folder);
   SenderDeadline();
   VersionGuard(folder);

   string session=Guid.NewGuid().ToString(),origin="continued-origin",continued="continued-final";
   TaskCompletion.OnPrompt(Payload(session,origin,true));calls=0;
   TaskCompletion.TestDelivery=delegate(string id){calls++;return false;};
   TaskCompletion.ProcessCompletion(Payload(session,continued,false),"journal",30000);
   Require(calls==3&&!CompletionStateStore.Claimed(CompletionStateStore.Key(session,origin))&&File.Exists(CompletionStateStore.TurnPath(CompletionStateStore.Key(session,origin))),"failed-continuation-state-retained");
   TaskCompletion.ProcessCompletion(Payload(session,origin,false),"journal",130000);
   Require(calls==3,"late-origin-does-not-retry-terminal-failure");
   TaskCompletion.OnPrompt(Payload(session,"newer-task",true));
   TaskCompletion.ProcessCompletion(Payload(session,continued,false),"journal",130000);
   Require(calls==3&&File.Exists(CompletionStateStore.TurnPath(CompletionStateStore.Key(session,"newer-task"))),"failed-old-final-never-consumes-new-input");
   ReceiptRecovery();
   string oldKey=CompletionStateStore.Key(Guid.NewGuid().ToString(),"legacy");
   CompletionStateStore.Claim(oldKey);
   Require(File.ReadAllText(Path.Combine(folder,oldKey+".claimed"))=="","legacy-claim-format-preserved");

   foreach(string file in Directory.GetFiles(CompletionDeliveryStore.Folder)) {
    string text=File.ReadAllText(file);
    Require(!text.Contains(session)&&!text.Contains(origin)&&!text.Contains("修复")&&text.Length<=256,"anonymous-bounded-record");
   }
   string malformed=Key();File.WriteAllText(Path.Combine(CompletionDeliveryStore.Folder,malformed+".request"),"unexpected");
   bool refused=false;try{CompletionDeliveryStore.Begin(malformed);}catch(InvalidDataException){refused=true;}
   Require(refused,"corrupt-record-fails-closed");
   Require(!CompletionDeliveryStore.ValidKey("../escape"),"invalid-key-rejected");
   return true;
  } catch(Exception e) {
   IntroLog.Write("completion-delivery-test-failed type="+e.GetType().Name+" case="+e.Message);return false;
  } finally {
   TaskCompletion.TestDelivery=null;CompletionStateStore.TestFolder=null;
   if(Directory.Exists(folder))Directory.Delete(folder,true);
  }
 }
 static void ReceiptRecovery() {
  string session=Guid.NewGuid().ToString(),origin=CompletionStateStore.Key(session,"crash-origin"),final=CompletionStateStore.Key(session,"crash-final");
  TaskCompletion.OnPrompt(Payload(session,"crash-origin",true));
  CompletionDeliveryStore.Begin(origin);CompletionDeliveryStore.Link(final,origin);CompletionDeliveryStore.Accept(origin,"accepted");
  // Synthetic crash point: persisted receipt, no legacy claim/consumption yet.
  TaskCompletion.OnPrompt(Payload(session,"after-crash-new-input",true));
  int calls=0;TaskCompletion.TestDelivery=delegate(string key){calls++;return true;};
  TaskCompletion.ProcessCompletion(Payload(session,"crash-final",false),"journal",130000);
  Require(calls==0&&CompletionDeliveryStore.ReadRequest(origin).Status=="delivered"&&CompletionStateStore.Claimed(origin)&&CompletionStateStore.Claimed(final)&&
   File.Exists(CompletionStateStore.TurnPath(CompletionStateStore.Key(session,"after-crash-new-input"))),"receipt-recovery-protects-new-input");
  session=Guid.NewGuid().ToString();final=CompletionStateStore.Key(session,"stateless-final");
  CompletionDeliveryStore.Begin(final);CompletionDeliveryStore.Link(final,final);CompletionDeliveryStore.Accept(final,"accepted");
  TaskCompletion.OnPrompt(Payload(session,"new-after-fallback",true));calls=0;
  TaskCompletion.ProcessCompletion(Payload(session,"stateless-final",false),"journal",130000);
  Require(calls==0&&CompletionStateStore.Claimed(final)&&File.Exists(CompletionStateStore.TurnPath(CompletionStateStore.Key(session,"new-after-fallback"))),"stateless-receipt-recovery-protects-new-input");
  session=Guid.NewGuid().ToString();origin=CompletionStateStore.Key(session,"orphan-origin");final=CompletionStateStore.Key(session,"orphan-final");
  CompletionDeliveryStore.Link(final,origin);TaskCompletion.OnPrompt(Payload(session,"after-orphan-new-input",true));
  TaskCompletion.ProcessCompletion(Payload(session,"orphan-final",false),"journal",130000);
  Require(calls==0&&CompletionDeliveryStore.ReadRequest(origin).Status=="failed"&&!CompletionStateStore.Claimed(final)&&File.Exists(CompletionStateStore.TurnPath(CompletionStateStore.Key(session,"after-orphan-new-input"))),"partial-reservation-fails-closed-without-new-input-consumption");
 }
 static void SenderDeadline() {
  string key=Key();CompletionDeliveryStore.Begin(key);int launches=0;
  var elapsed=Stopwatch.StartNew();
  Require(!CompletionDelivery.SendTo(key,delegate(string id,int deadline){Thread.Sleep(deadline);return null;},delegate(string id){launches++;return null;}),"unavailable-receiver-fails");
  Require(elapsed.ElapsedMilliseconds<2500&&CompletionDeliveryStore.ReadRequest(key).Attempts<=3&&launches<=3,"total-two-second-budget");
 }
 static void VersionGuard(string folder) {
  string installed=Path.Combine(folder,"中文 空格路径","BootPlayer.exe");Directory.CreateDirectory(Path.GetDirectoryName(installed));
  string local=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe");File.Copy(local,installed);
  Require(CompletionDelivery.SelectPlayer(local,"\""+installed+"\" --supervise")==installed,"installed-editable-media-path");
  File.AppendAllText(installed,"stale");
  Require(CompletionDelivery.SelectPlayer(local,"\""+installed+"\" --supervise")==null,"stale-exe-never-legacy-intro");
  Require(CompletionDelivery.SelectPlayer(local,"unsupported registration")==null&&CompletionDelivery.SelectPlayer(local,null)==local,"registration-fails-closed-or-uninstalled-local");
 }
 static void LostAcknowledgement() {
  string name=CompletionDelivery.PipeName+"-lost-"+Guid.NewGuid().ToString("N"),key=Key();
  CompletionDeliveryStore.Begin(key);
  using(var receiver=new CompletionDeliveryReceiver())using(var server=ResidentProtocol.CreateServer(name)) {
   receiver.MarkReady();var connection=server.BeginWaitForConnection(null,null);
   var task=Task.Factory.StartNew(delegate {
    server.EndWaitForConnection(connection);ResidentProtocol.ReadCommand(server,200);
    receiver.Receive(key,100);server.Disconnect();
   });
   Require(CompletionDelivery.Exchange(name,key,500)==null&&task.Wait(1000),"lost-ack-transport");
   int calls=0;Require(CompletionDelivery.Send(key,delegate(string id){calls++;return true;})&&calls==0,"lost-ack-durable-receipt");
  }
 }
 static void SilentReceiver() {
  string name=CompletionDelivery.PipeName+"-silent-"+Guid.NewGuid().ToString("N"),key=Key();
  using(var server=ResidentProtocol.CreateServer(name)) {
   var connection=server.BeginWaitForConnection(null,null);
   var task=Task.Factory.StartNew(delegate {server.EndWaitForConnection(connection);Thread.Sleep(300);});
   var timer=Stopwatch.StartNew();Require(CompletionDelivery.Exchange(name,key,100)==null&&timer.ElapsedMilliseconds<600,"silent-ack-deadline");
   Require(task.Wait(1000),"silent-peer-joined");
  }
 }
 static void IndependentPeer(string folder) {
  string name=CompletionDelivery.PipeName+"-peer-"+Guid.NewGuid().ToString("N");
  string marker=Path.Combine(folder,"peer-ready");
  var start=new ProcessStartInfo {FileName=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),
   Arguments="--completion-delivery-peer-test "+CompanionLifetime.Quote(folder)+" "+name,UseShellExecute=false,CreateNoWindow=true};
  using(var peer=Process.Start(start)) {
   try {
    for(int i=0;i<100&&!File.Exists(marker)&&!peer.HasExited;i++)Thread.Sleep(20);
    Require(File.Exists(marker),"isolated-peer-ready");
    TaskCompletion.TestDelivery=delegate(string key) {string result=CompletionDelivery.Exchange(name,key,300);return result=="accepted"||result=="merged";};
    string a=Guid.NewGuid().ToString(),b=Guid.NewGuid().ToString();
    TaskCompletion.OnPrompt(Payload(a,"task-a",true));TaskCompletion.OnPrompt(Payload(b,"task-b",true));
    Parallel.Invoke(delegate {TaskCompletion.ProcessCompletion(Payload(a,"task-a",false),"stop-hook",0);},
     delegate {TaskCompletion.ProcessCompletion(Payload(b,"task-b",false),"journal",30000);});
    string keyA=CompletionStateStore.Key(a,"task-a"),keyB=CompletionStateStore.Key(b,"task-b");
    var results=new HashSet<string> {CompletionDeliveryStore.Receipt(keyA),CompletionDeliveryStore.Receipt(keyB)};
    Require(results.SetEquals(new[]{"accepted","merged"})&&CompletionStateStore.Claimed(keyA)&&CompletionStateStore.Claimed(keyB),"two-tasks-one-automatic-window");
    int unexpected=0;TaskCompletion.TestDelivery=delegate(string key){Interlocked.Increment(ref unexpected);return true;};
    Parallel.For(0,16,delegate(int i){TaskCompletion.ProcessCompletion(Payload(i%2==0?a:b,i%2==0?"task-a":"task-b",false),"journal",30000);});
    Require(unexpected==0,"concurrent-hook-journal-dedupe");
    string coreA=Key(),coreB=Key();CompletionDeliveryStore.Begin(coreA);CompletionDeliveryStore.Begin(coreB);
    bool first=false,second=false;int launches=0;
    Func<string,int,string> exchange=delegate(string key,int deadline){return CompletionDelivery.Exchange(name,key,deadline);};
    Func<string,Process> launch=delegate(string key){Interlocked.Increment(ref launches);return null;};
    Parallel.Invoke(delegate{first=CompletionDelivery.SendTo(coreA,exchange,launch);},delegate{second=CompletionDelivery.SendTo(coreB,exchange,launch);});
    Require(first&&second&&launches==0&&CompletionDeliveryStore.Receipt(coreA)=="merged"&&CompletionDeliveryStore.Receipt(coreB)=="merged","production-sender-private-ipc");
   } finally {
    TaskCompletion.TestDelivery=null;
    File.WriteAllText(Path.Combine(folder,"peer-stop"),"");
    if(!peer.WaitForExit(3000)){peer.Kill();peer.WaitForExit(1000);}
   }
   Require(peer.ExitCode==0,"isolated-peer-exit");
  }
 }
 internal static int Peer(string[] args) {
  if(args.Length!=3)return 2;
  string folder=Path.GetFullPath(args[1]);
  string root=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(!folder.StartsWith(root+"codex-completion-delivery-",StringComparison.OrdinalIgnoreCase)||!Directory.Exists(folder)||!args[2].StartsWith(CompletionDelivery.PipeName+"-peer-",StringComparison.Ordinal))return 2;
  CompletionStateStore.TestFolder=folder;
  try {
   using(var receiver=new CompletionDeliveryReceiver())using(var server=new CompletionDeliveryServer(args[2],receiver)) {
    receiver.MarkReady();File.WriteAllText(Path.Combine(folder,"peer-ready"),"");
    var timer=Stopwatch.StartNew();while(!File.Exists(Path.Combine(folder,"peer-stop"))&&timer.ElapsedMilliseconds<5000)Thread.Sleep(20);
    receiver.MarkClosed();return 0;
   }
  } finally {CompletionStateStore.TestFolder=null;}
 }
}
