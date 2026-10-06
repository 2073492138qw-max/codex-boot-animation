using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

// Startup is not UI readiness. Never call a provider while Codex is on its
// splash screen; a failed provider is optional, not a reason to flood the app.
internal sealed class ButtonScanGate {
 internal bool Busy,Blocked;
 int failures;
 DateTime next=DateTime.MinValue;
 internal bool Begin(DateTime now,bool ready,bool needed){
  if(!ready||!needed||Busy||Blocked||now<next)return false;
  Busy=true;return true;
 }
 internal void Complete(DateTime now,bool found,bool timeout){
  Busy=false;
  if(timeout){Blocked=true;return;}
  failures=found?0:failures+1;
  if(failures>=2)Blocked=true;
  next=now.AddSeconds(found?1:3);
 }
 internal void Cancel(DateTime now){Busy=false;next=now.AddSeconds(1);}
}

internal static class NewChatDiscovery {
 internal static bool CanDiscover(bool ready,bool foreground,bool minimized){return ready&&foreground&&!minimized;}
 internal sealed class Result {
  internal IntPtr Window;internal int Pid;internal Rect Bounds;
  internal List<Rect> Buttons=new List<Rect>();internal bool TimedOut,Failed,Stale;internal ButtonScanGate Gate;
 }
 [StructLayout(LayoutKind.Sequential)] struct NativeRect {internal int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
 static readonly Regex LogPid=new Regex(@"^codex-desktop-[a-f0-9-]+-(\d+)-t\d+-i\d+-[0-9]+-[0-9]+\.log$",RegexOptions.IgnoreCase);
 static string Output(string token){
  if(!Regex.IsMatch(token,@"^[a-f0-9]{32}$"))throw new ArgumentException("Invalid discovery token.");
  return Path.Combine(IntroLog.Data,"button-probes",token+".rects");
 }
 internal static bool ReadyLine(string file,string line,int pid,DateTime started){
  Match match=LogPid.Match(Path.GetFileName(file));int logPid;
  if(!match.Success||!Int32.TryParse(match.Groups[1].Value,out logPid)||logPid!=pid)return false;
  if(line.IndexOf("IAB_LIFECYCLE received browser sidebar owner sync",StringComparison.Ordinal)<0||line.IndexOf(" ownerRoutePath=",StringComparison.Ordinal)<0)return false;
  int end=line.IndexOf(' ');DateTime timestamp;
  return end>0&&DateTime.TryParse(line.Substring(0,end),CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal,out timestamp)&&timestamp>=started;
 }
 internal static bool TryIdentity(string file,out string identity,out DateTime started,out int pid){
  identity=null;started=DateTime.MinValue;pid=0;Match match=LogPid.Match(Path.GetFileName(file));
  if(!match.Success||!Int32.TryParse(match.Groups[1].Value,out pid))return false;
  try{using(var process=Process.GetProcessById(pid)){
   if(!DesktopInstancePolicy.IsCodex(process))return false;
   started=process.StartTime.ToUniversalTime();identity=DesktopInstancePolicy.Identity(process);return true;
  }}catch{return false;}
 }
 // A killable child owns all UIA calls. A stuck provider never holds the
 // watcher's mouse hook/message loop, and only this exact child is terminated.
 internal static Result Discover(IntPtr hwnd,int pid,Rect bounds){
  string token=Guid.NewGuid().ToString("N"),file=Output(token);
  var result=new Result{Window=hwnd,Pid=pid,Bounds=bounds};Process child=null;
  try{
   Directory.CreateDirectory(Path.GetDirectoryName(file));
   child=Process.Start(new ProcessStartInfo{
    FileName=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),
    Arguments="--button-probe "+pid+" "+hwnd.ToInt64()+" "+token,
    UseShellExecute=false,CreateNoWindow=true
   });
   if(!WaitForChild(child,1200)){result.TimedOut=true;return result;}
   if(child.ExitCode==124){result.TimedOut=true;return result;}
   if(child.ExitCode==0&&File.Exists(file)&&new FileInfo(file).Length<=2048){
    foreach(string line in File.ReadAllLines(file)){
     string[] parts=line.Split('|');double x,y,w,h;
     if(parts.Length!=4||!Number(parts[0],out x)||!Number(parts[1],out y)||!Number(parts[2],out w)||!Number(parts[3],out h)||w<=0||h<=0||result.Buttons.Count>=4)throw new InvalidDataException("Invalid discovery result.");
     Rect rect=new Rect(x,y,w,h);if(!bounds.Contains(rect)){result.Buttons.Clear();result.Stale=true;return result;}result.Buttons.Add(rect);
    }
   }
  }catch(Exception e){result.Buttons.Clear();result.Failed=true;IntroLog.Write("button-discovery-failed type="+e.GetType().Name);}
  finally{if(child!=null)child.Dispose();try{if(File.Exists(file))File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}
  return result;
 }
 static bool Number(string text,out double value){return Double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!Double.IsNaN(value)&&!Double.IsInfinity(value);}
 internal static bool WaitForChild(Process child,int milliseconds){
  if(child.WaitForExit(milliseconds))return true;
  try{child.Kill();child.WaitForExit(2000);}catch(InvalidOperationException){}
  return false;
 }
 internal static int Probe(string[] args){
  try{
   int pid;long handle;if(args.Length!=4||!Int32.TryParse(args[1],out pid)||!Int64.TryParse(args[2],out handle))return 2;
   string file=Output(args[3]);IntPtr hwnd=new IntPtr(handle);uint owner;NativeRect native;
   GetWindowThreadProcessId(hwnd,out owner);if(owner!=pid||!GetWindowRect(hwnd,out native))return 2;
   using(var process=Process.GetProcessById(pid))if(!DesktopInstancePolicy.IsCodex(process))return 2;
   Rect bounds=new Rect(native.Left,native.Top,native.Right-native.Left,native.Bottom-native.Top);
   List<Rect> buttons;
   using(var watchdog=new Timer(delegate{Environment.Exit(124);},null,1000,Timeout.Infinite))buttons=Collect(hwnd,bounds);
   var lines=new List<string>();foreach(Rect rect in buttons)lines.Add(String.Join("|",new[]{rect.X.ToString(CultureInfo.InvariantCulture),rect.Y.ToString(CultureInfo.InvariantCulture),rect.Width.ToString(CultureInfo.InvariantCulture),rect.Height.ToString(CultureInfo.InvariantCulture)}));
   File.WriteAllLines(file,lines);return 0;
  }catch(Exception e){IntroLog.Write("button-probe-failed type="+e.GetType().Name);return 2;}
 }
 // Walk only the upper-left navigation region, with bounded nodes/depth/time.
 // Do not enumerate the full conversation tree, including its long transcript.
 static List<Rect> Collect(IntPtr hwnd,Rect bounds){
  var matches=new List<Rect>();var elapsed=Stopwatch.StartNew();
  var queue=new Queue<KeyValuePair<AutomationElement,int>>();
  {
   var cache=new CacheRequest();
   cache.Add(AutomationElement.NameProperty);cache.Add(AutomationElement.ControlTypeProperty);cache.Add(AutomationElement.BoundingRectangleProperty);
   cache.TreeScope=TreeScope.Element;
   using(cache.Activate()){
    queue.Enqueue(new KeyValuePair<AutomationElement,int>(AutomationElement.FromHandle(hwnd),0));
    var walker=TreeWalker.ControlViewWalker;int visited=0;
    while(queue.Count>0&&visited++<160&&elapsed.ElapsedMilliseconds<400){
     var entry=queue.Dequeue();AutomationElement element=entry.Key;
     Rect rect=element.Cached.BoundingRectangle;string name=element.Cached.Name;
     if(element.Cached.ControlType==ControlType.Button&&(name=="新聊天"||name=="新对话"||String.Equals(name,"New chat",StringComparison.OrdinalIgnoreCase))&&!rect.IsEmpty&&rect.Width>0&&rect.Height>0&&bounds.Contains(rect))matches.Add(rect);
     if(matches.Count>=2)break;
     if(entry.Value>=12||(!rect.IsEmpty&&(rect.Left>bounds.Left+bounds.Width*0.55||rect.Top>bounds.Top+bounds.Height*0.55)))continue;
     for(AutomationElement child=walker.GetFirstChild(element,cache);child!=null&&queue.Count<160&&elapsed.ElapsedMilliseconds<400;child=walker.GetNextSibling(child,cache))queue.Enqueue(new KeyValuePair<AutomationElement,int>(child,entry.Value+1));
    }
    IntroLog.Write("button-probe-complete visited="+visited+" elapsed-ms="+elapsed.ElapsedMilliseconds+" matches="+matches.Count);
   }
  }
  return matches;
 }
 internal static bool SelfTest(){
  DateTime now=DateTime.UtcNow;var gate=new ButtonScanGate();
  if(CanDiscover(false,true,false)||CanDiscover(true,false,false)||CanDiscover(true,true,true)||!CanDiscover(true,true,false))return false;
  for(int i=0;i<10000;i++)if(gate.Begin(now.AddMilliseconds(i),false,true))return false;
  if(!gate.Begin(now,true,true)||gate.Begin(now,true,true))return false;
  gate.Complete(now,false,false);if(gate.Begin(now.AddSeconds(2),true,true)||!gate.Begin(now.AddSeconds(3),true,true))return false;
  gate.Complete(now.AddSeconds(3),false,false);for(int i=0;i<10000;i++)if(gate.Begin(now.AddDays(i),true,true))return false;
  var timeout=new ButtonScanGate();if(!timeout.Begin(now,true,true))return false;timeout.Complete(now,false,true);if(!timeout.Blocked||timeout.Begin(now.AddDays(1),true,true))return false;
  var success=new ButtonScanGate();if(!success.Begin(now,true,true))return false;success.Complete(now,true,false);
  if(success.Begin(now.AddDays(1),true,false)||!success.Begin(now.AddSeconds(1),true,true))return false;
  var moved=new ButtonScanGate();for(int i=0;i<5;i++){if(!moved.Begin(now.AddSeconds(i*2),true,true))return false;moved.Cancel(now.AddSeconds(i*2));}if(moved.Blocked)return false;
  string file="codex-desktop-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee-123-t0-i1-030000-0.log";
  string line="2026-10-01T03:00:00Z info IAB_LIFECYCLE received browser sidebar owner sync conversationId=client-new-thread:test ownerRoutePath=/ windowId=1";
  DateTime start=new DateTime(2026,10,1,2,59,0,DateTimeKind.Utc);
  if(!ReadyLine(file,line,123,start)||ReadyLine(file,line,124,start)||ReadyLine(file,line,123,start.AddMinutes(2))||ReadyLine(file,"2026-10-01T03:00:00Z info window ready-to-show",123,start))return false;
  // A real owned child timeout, without interrogating Codex or closing it.
  using(var child=Process.Start(new ProcessStartInfo{FileName=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BootPlayer.exe"),Arguments="--button-probe-hang-test",UseShellExecute=false,CreateNoWindow=true})){
   var elapsed=Stopwatch.StartNew();if(WaitForChild(child,100)||!child.HasExited||elapsed.ElapsedMilliseconds>3000)return false;
  }
  return true;
 }
}
