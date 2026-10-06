using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

// Manual tray previews only. Automatic triggers must never call this helper.
internal static class PreviewFocus {
 [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr window,int command);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);

 static bool IsCodexWindow(IntPtr window){
  if(window==IntPtr.Zero||!IsWindow(window)||!IsWindowVisible(window))return false;
  try{uint pid;GetWindowThreadProcessId(window,out pid);using(Process process=Process.GetProcessById((int)pid))return DesktopInstancePolicy.IsCodex(process)&&process.MainWindowHandle==window;}
  catch{return false;}
 }
 static IntPtr FindWindow(){
  IntPtr front=GetAncestor(GetForegroundWindow(),2);if(IsCodexWindow(front))return front;
  IntPtr target=IntPtr.Zero;
  foreach(Process process in Process.GetProcessesByName("ChatGPT"))using(process)try{
   if(!DesktopInstancePolicy.IsCodex(process))continue;
   IntPtr candidate=process.MainWindowHandle;if(!IsCodexWindow(candidate))continue;
   // Do not guess which instance to activate when multiple desktop apps exist.
   if(target!=IntPtr.Zero&&target!=candidate)return IntPtr.Zero;
   target=candidate;
  }catch{}
  return target;
 }
 static bool Activate(IntPtr window){
  if(!IsCodexWindow(window))return false;
  if(IsIconic(window))ShowWindowAsync(window,9);
  SetForegroundWindow(window);
  return IsCodexWindow(window)&&!IsIconic(window)&&GetAncestor(GetForegroundWindow(),2)==window;
 }
 internal static string Prepare(){return Prepare(FindWindow,Activate);}
 internal static string Prepare(Func<IntPtr> find,Func<IntPtr,bool> activate){
  IntPtr target=find();if(target==IntPtr.Zero)return "no-unambiguous-codex-window";
  return activate(target)?null:"foreground-denied";
 }
 internal static void Queue(Forms.ContextMenuStrip menu,Func<string> prepare,Action play,Action<string> failed){
  // SetForegroundWindow requires no active menu. Post once, after Close has
  // unwound the click handler; no worker/poll loop or automatic focus stealing.
  menu.Close();
  menu.BeginInvoke(new Action(delegate{
   try{string reason=prepare();if(reason!=null){failed(reason);return;}play();}
   catch(Exception e){failed("preview-error-"+e.GetType().Name);}
  }));
 }
 internal static bool SelfTest(){
  IntPtr target=new IntPtr(123);int activated=0;
  if(Prepare(delegate{return IntPtr.Zero;},delegate(IntPtr window){activated++;return true;})!="no-unambiguous-codex-window"||activated!=0)return false;
  if(Prepare(delegate{return target;},delegate(IntPtr window){activated++;return false;})!="foreground-denied"||activated!=1)return false;
  if(Prepare(delegate{return target;},delegate(IntPtr window){activated++;return window==target;})!=null||activated!=2)return false;
  using(var menu=new Forms.ContextMenuStrip()){
   IntPtr handle=menu.Handle;int prepared=0,played=0,failures=0;
   Queue(menu,delegate{prepared++;return null;},delegate{played++;},delegate(string reason){failures++;});
   if(prepared!=0||played!=0)return false;
   Forms.Application.DoEvents();
   if(prepared!=1||played!=1||failures!=0||menu.Visible)return false;
   Queue(menu,delegate{return "foreground-denied";},delegate{played++;},delegate(string reason){if(reason=="foreground-denied")failures++;});
   Forms.Application.DoEvents();
   if(played!=1||failures!=1)return false;
   Queue(menu,delegate{return null;},delegate{throw new InvalidOperationException();},delegate(string reason){if(reason=="preview-error-InvalidOperationException")failures++;});
   Forms.Application.DoEvents();return failures==2;
  }
 }
}
