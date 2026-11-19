using System;
using System.IO;
using System.Text;
using System.Threading;

internal static class NavigationTailPollTests {
 static void Require(bool value,string stage){if(!value)throw new InvalidOperationException(stage);}
 internal static bool Run(){
  try{
   var tail=new NavigationTailPoll();Require(!tail.Take(0),"idle-polling");
   tail.Track("fixture.log","desktop-a");tail.Track("FIXTURE.log","desktop-a");
   Require(tail.Snapshot().Length==1&&tail.Contains("fixture.log"),"alias-case-duplicated");
   Require(tail.Take(0)&&!tail.Take(249)&&tail.Take(250)&&!tail.Take(249),"poll-boundary");
   for(int i=0;i<40;i++)tail.Track("fixture-"+i+".log","desktop-a");
   Require(tail.Snapshot().Length==16&&!tail.Contains("fixture.log")&&tail.Contains("fixture-39.log"),"unbounded-paths");
   tail.Retain(delegate(string path,string identity){return path=="fixture-39.log";});
   Require(tail.Snapshot().Length==1,"dead-logs-retained");
   tail.Track("fixture-39.log","desktop-b");tail.Retain(delegate(string path,string identity){return identity=="desktop-b";});
   Require(tail.Snapshot().Length==1,"reused-identity-lost");tail.Stop();tail.Track("other.log","desktop-c");
   Require(tail.Stopped&&!tail.Take(10000)&&tail.Snapshot().Length==0,"shutdown-polls");

   // No file notification arrives. An event immediately after a 5s sweep must
   // still be consumed by the fast tail, including a split UTF-8/CRLF write.
   byte[] bytes=Encoding.UTF8.GetBytes("2026-10-08T12:29:20Z info IAB_LIFECYCLE received browser sidebar owner sync conversationId=client-new-thread:test ownerRoutePath=/ windowId=1\r\n");
   using(var stream=new MemoryStream()){
    var scheduler=new NavigationTailPoll();scheduler.Track("synthetic.log","desktop");
    long offset=0;bool blankRoute=false;int dispatches=0,first=-1;int split=bytes.Length-1;
    for(int time=0;time<=5500;time+=100){
     if(time==100){stream.Position=stream.Length;stream.Write(bytes,0,split);}
     if(time==400){stream.Position=stream.Length;stream.Write(bytes,split,bytes.Length-split);}
     if(!scheduler.Take(time))continue;
     string appended=NavigationLogReader.Read(stream,ref offset);bool blank;
     if(appended.Length>0&&IntroWatcher.TryGetDialogRoute(appended,out blank)&&IntroWatcher.AdvanceDialogRoute(ref blankRoute,blank)){dispatches++;first=time;}
    }
    Require(dispatches==1&&first>=400&&first<=700&&offset==bytes.Length,"missed-notification-waits-five-seconds");
    Require(NavigationLogReader.Read(stream,ref offset)=="","event-and-tail-double-read");
    Require(!IntroWatcher.AdvanceDialogRoute(ref blankRoute,true),"duplicate-route-replayed");
    Require(!IntroWatcher.AdvanceDialogRoute(ref blankRoute,false)&&IntroWatcher.AdvanceDialogRoute(ref blankRoute,true),"second-real-navigation-lost");
   }
   // A blocked directory/completion reconciliation must not hold the new
   // navigation queue or make the low-level mouse/message loop do file I/O.
   using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false))using(var observed=new ManualResetEvent(false)){
    var slow=new WatcherReconcile();var fast=new WatcherReconcile();
    try{
     Require(slow.TryQueue(delegate{entered.Set();release.WaitOne(3000);},null)&&entered.WaitOne(1000),"slow-fixture-not-entered");
     Require(fast.TryQueue(delegate{observed.Set();},null)&&observed.WaitOne(1000),"fast-tail-behind-reconciliation");
     fast.Stop();Require(!fast.TryQueue(delegate{},null),"stopped-worker-queues");
    }finally{slow.Stop();fast.Stop();release.Set();}
   }
   IntroLog.Write("navigation-tail-test pass=True missed-notification=True split-line=True bounded-paths=True");return true;
  }catch(Exception e){IntroLog.Write("navigation-tail-test pass=False type="+e.GetType().Name+" stage="+e.Message);return false;}
 }
}
