using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

// Automatic completion has one immediate display attempt, never a focus queue.
internal static class CompletionPlayback {
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr window,int index);
 internal static bool Select(CompletionPreview.CreateWindow foreground,CompletionPreview.CreateWindow notification,out CodexVideoWindow window,out string reason){
  if(foreground(out window,out reason))return true;
  if(reason!="not-codex-foreground")return false;
  return notification(out window,out reason);
 }
 internal static bool TryCreate(out CodexVideoWindow window,out string reason){
  return Select(delegate(out CodexVideoWindow owned,out string why){return CodexVideoWindow.TryCreate(VideoScene.Completion,out owned,out why);},
   delegate(out CodexVideoWindow notice,out string why){
    notice=null;string path=MediaLibrary.Completion();if(path==null){why="missing-video";return false;}
    notice=new CodexVideoWindow(IntPtr.Zero,path,VideoScene.Completion,false,true);why="ready";return true;
   },out window,out reason);
 }
 internal static int Run(CompletionPreview.CreateWindow create,Action<CodexVideoWindow> show){
  CodexVideoWindow window;string reason;
  if(!create(out window,out reason)){IntroLog.Write("completion-suppressed reason="+reason);return 2;}
  IntroLog.Write("completion-presentation mode="+(window.IsCompletionNotice?"notification":"codex-window"));
  show(window);return 0;
 }
 // Native-pixel bounds on the active display's working area, excluding its taskbar.
 internal static Rect NotificationBounds(Rect work,double scale){
  return NotificationBounds(work,scale,CompletionLayoutPreference.Default);
 }
 internal static Rect NotificationBounds(Rect work,double scale,CompletionLayoutPreference preference){
  if(Double.IsNaN(scale)||Double.IsInfinity(scale)||scale<=0)scale=1;
  double width=work.Width*preference.WidthFraction;
  double height=Math.Min(width*9/16,work.Height*preference.HeightFraction);width=height*16/9;
  double margin=Math.Min(20*scale,Math.Min(work.Width,work.Height)*0.03);
  return new Rect(preference.Corner==CompletionNoticeCorner.BottomLeft?work.Left+margin:work.Right-margin-width,work.Bottom-margin-height,width,height);
 }
 internal static bool SelfTest(){
  int foregroundCalls=0,noticeCalls=0;CodexVideoWindow selected;string reason;
  foreach(string status in new[]{"ready","not-codex-foreground","missing-video","target-error"}){
   int oldNotices=noticeCalls;
   bool accepted=Select(delegate(out CodexVideoWindow window,out string why){foregroundCalls++;window=null;why=status;return status=="ready";},
    delegate(out CodexVideoWindow window,out string why){noticeCalls++;window=null;why="ready";return true;},out selected,out reason);
   if(accepted!=(status=="ready"||status=="not-codex-foreground")||noticeCalls-oldNotices!=(status=="not-codex-foreground"?1:0))return false;
  }
  if(foregroundCalls!=4||noticeCalls!=1)return false;
  Rect standard=NotificationBounds(new Rect(0,0,1920,1040),1);
  if(Math.Abs(standard.Width-960)>0.001||Math.Abs(standard.Height-540)>0.001)return false;
  foreach(Rect work in new[]{new Rect(0,0,1920,1040),new Rect(-2560,0,2560,1400),new Rect(0,-1280,720,1280),new Rect(10,20,320,200),new Rect(0,0,5120,1400)}){
   foreach(double scale in new[]{1.0,1.25,2.0}){
    Rect bounds=NotificationBounds(work,scale);
    if(!work.Contains(bounds)||bounds.Width<=0||bounds.Width>work.Width*0.5+0.001||bounds.Height>work.Height*0.55+0.001||Math.Abs(bounds.Width/bounds.Height-16.0/9)>0.001)return false;
   }
  }
  string[] files=MediaLibrary.Prepared();if(files.Length==0)return false;
  var fixture=new CodexVideoWindow(IntPtr.Zero,files[0],VideoScene.Completion,false,true);int closed=0,attempts=0,shown=0;
  fixture.Closed+=delegate{closed++;};
  try{
   if(!fixture.IsCompletionNotice||!fixture.Topmost||fixture.ShowActivated||new WindowInteropHelper(fixture).Owner!=IntPtr.Zero)return false;
   if((GetWindowLong(new WindowInteropHelper(fixture).EnsureHandle(),-20)&0x08000000)==0)return false;
   Button skip=null;foreach(UIElement child in ((Grid)fixture.Content).Children)if(child is Button)skip=(Button)child;
   if(skip==null||skip.Focusable)return false;
   int result=Run(delegate(out CodexVideoWindow window,out string why){attempts++;window=fixture;why="ready";return true;},delegate(CodexVideoWindow window){shown++;skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));});
   if(result!=0||attempts!=1||shown!=1||closed!=1)return false;
   result=Run(delegate(out CodexVideoWindow window,out string why){attempts++;window=null;why="missing-video";return false;},delegate(CodexVideoWindow window){shown++;});
   return result==2&&attempts==2&&shown==1;
  }finally{if(closed==0)fixture.Close();}
 }
}
