using System;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Hidden, unshown windows only. Synthetic failure timing tests the production
// slot lifecycle; this does not claim to reproduce a Windows decoder HRESULT.
internal static class IntroMediaRecoveryTests {
 const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
 static object Call(IntroWindow window,string name,params object[] args){
  MethodInfo method=typeof(IntroWindow).GetMethod(name,Private);
  if(method==null)throw new InvalidOperationException("Missing recovery method: "+name);
  return method.Invoke(window,args);
 }
 static object Field(object target,string name){return target.GetType().GetField(name,Private|BindingFlags.Public).GetValue(target);}
 static void Set(object target,string name,object value){target.GetType().GetField(name,Private|BindingFlags.Public).SetValue(target,value);}
 static void Require(bool value,string stage){if(!value)throw new InvalidOperationException(stage);}
 internal static bool Run(){
  IntroWindow window=null;
  try{
   string[] paths=MediaLibrary.Prepared();Require(paths.Length>0,"fixture-missing");
   window=new IntroWindow(null,"--resident","desktop-start");
   IDictionary slots=(IDictionary)Field(window,"slots");
   Grid surface=(Grid)Field(window,"surface");
   int count=slots.Count,children=surface.Children.Count;
   foreach(string candidate in MediaLibrary.List(VideoScene.NewChat)){
    MediaElement element=(MediaElement)Field(Call(window,"EnsurePrepared",candidate),"Media");
    Require(!element.ScrubbingEnabled,"new-chat-paused-scrubbing-still-enabled");
    Require(element.SpeedRatio==1.2,"new-chat-speed-changed");
   }
   foreach(string candidate in MediaLibrary.List(VideoScene.Startup)){
    MediaElement element=(MediaElement)Field(Call(window,"EnsurePrepared",candidate),"Media");
    Require(element.ScrubbingEnabled,"startup-scrubbing-changed");
   }
   string path=paths[0];
   object old=Call(window,"EnsurePrepared",path);
   Set(old,"Ready",true);
   Call(window,"SelectActiveSlot",old);
   Call(window,"Finish","skip-button");
   // The observed late MediaFailed leaves exactly these flags on the old slot.
   Set(old,"Ready",false);Set(old,"Failed",true);
   object recovered=Call(window,"EnsurePrepared",path);
   Require(!Object.ReferenceEquals(old,recovered)&&!(bool)Field(recovered,"Failed"),"failed-slot-still-latched");
   Require(slots.Count==count&&surface.Children.Count==children,"replacement-leaks-slot-or-element");
   Require(Object.ReferenceEquals(recovered,Call(window,"EnsurePrepared",path)),"healthy-slot-replaced");
   Require(((MediaElement)Field(recovered,"Media")).Volume==0,"recovery-preload-not-muted");
   Call(window,"HandleMediaFailure",old,"synthetic-old-callback");
   Require(!(bool)Field(recovered,"Failed"),"stale-failure-poisons-new-slot");
   Call(window,"HandleMediaFailure",recovered,"synthetic-current-failure");
   Require((bool)Field(recovered,"Failed")&&Object.ReferenceEquals(slots[path],recovered),"failure-retries-without-trigger");
   object second=Call(window,"EnsurePrepared",path);
   Require(!Object.ReferenceEquals(second,recovered),"second-trigger-cannot-recover");
   Call(window,"HandleMediaFailure",second,"synthetic-repeat-failure");
   Require(Object.ReferenceEquals(slots[path],second)&&!window.IsVisible,"repeat-failure-loops-or-keeps-overlay");
   object timeout=Call(window,"EnsurePrepared",path);
   Call(window,"SelectActiveSlot",timeout);
   Call(window,"Finish","load-timeout");
   Require((bool)Field(timeout,"Failed")&&!window.IsVisible&&!(bool)Field(window,"closing"),"timeout-not-recoverable");
   Require(!Object.ReferenceEquals(timeout,Call(window,"EnsurePrepared",path)),"timeout-next-trigger-refused");
   Require(slots.Count==count&&surface.Children.Count==children,"repeated-recovery-leaks");
   Require(!window.IsVisible&&!window.ShowInTaskbar,"test-shows-player");
   IntroLog.Write("intro-media-recovery-test pass=True");return true;
  }catch(Exception e){IntroLog.Write("intro-media-recovery-test pass=False type="+e.GetType().Name+" stage="+e.Message);return false;}
  finally{if(window!=null)window.Close();}
 }
 static string FrameFingerprint(MediaElement element){
  var visual=new DrawingVisual();
  using(DrawingContext drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(element),null,new Rect(0,0,64,36));
  var image=new RenderTargetBitmap(64,36,96,96,PixelFormats.Pbgra32);image.Render(visual);
  byte[] pixels=new byte[64*36*4];image.CopyPixels(pixels,64*4,0);
  bool valid=false;for(int i=0;i<pixels.Length;i+=4)if(pixels[i]>8||pixels[i+1]>8||pixels[i+2]>8){valid=true;break;}
  if(!valid)return null;
  using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(pixels));
 }
 internal static bool RunDecoderProbe(){
  Application app=null;IntroWindow window=null;
  DispatcherTimer poll=null;bool pass=false;
  try{
   IntroLog.Write("decoder-probe-enter interactive="+Environment.UserInteractive+" session="+System.Diagnostics.Process.GetCurrentProcess().SessionId);
   string[] paths=MediaLibrary.List(VideoScene.NewChat);Require(paths.Length>0,"decoder-fixture-missing");
   app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
   IntroLog.Write("decoder-probe-application-created clips="+paths.Length);
   window=new IntroWindow(null,"--resident","desktop-start");
   // Initialize actual WPF media off-screen without focus, sound or live IPC.
   window.ShowActivated=false;window.Topmost=false;window.WindowState=WindowState.Normal;
   window.Width=320;window.Height=180;window.Left=-32000;window.Top=-32000;window.Opacity=0;
   window.Show();
   IntroLog.Write("decoder-probe-window-initialized");
   int index=0;object slot=Call(window,"EnsurePrepared",paths[index]);int stage=0,frameChanges=0;string lastFrame=null;
   DateTime deadline=DateTime.UtcNow.AddSeconds(12);
   poll=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
   poll.Tick+=delegate{
    try{
     if(DateTime.UtcNow>=deadline)throw new InvalidOperationException("decoder-probe-timeout-stage-"+stage);
     MediaElement element=(MediaElement)Field(slot,"Media");
     if((stage==1||stage==3||stage==5)&&element.Position.TotalSeconds>0.2){
      string frame=FrameFingerprint(element);
      if(frame!=null){if(lastFrame!=null&&frame!=lastFrame)frameChanges++;lastFrame=frame;}
     }
     if(stage==0&&(bool)Field(slot,"Ready")){
      Call(window,"SelectActiveSlot",slot);element.IsMuted=true;element.Volume=0;element.Play();stage=1;
     }else if(stage==1&&element.Position.TotalSeconds>0.5&&frameChanges>=2){
      Call(window,"Finish","skip-button");
      Call(window,"HandleMediaFailure",slot,"synthetic-late-failure-after-real-skip");
      object previous=slot;slot=Call(window,"EnsurePrepared",paths[index]);
      Require(!Object.ReferenceEquals(previous,slot),"decoder-probe-slot-not-replaced");frameChanges=0;lastFrame=null;stage=2;
     }else if(stage==2&&(bool)Field(slot,"Ready")){
      Call(window,"SelectActiveSlot",slot);element.IsMuted=true;element.Volume=0;element.Play();stage=3;
     }else if(stage==3&&element.Position.TotalSeconds>0.5&&frameChanges>=2){
      Call(window,"SelectActiveSlot",slot);Call(window,"Finish","skip-button");
      Require(!window.IsVisible&&!window.ShowInTaskbar,"decoder-probe-cover-remains");
      object previous=slot;slot=Call(window,"EnsurePrepared",paths[index]);
      Require(Object.ReferenceEquals(previous,slot),"decoder-probe-healthy-cache-replaced");
      Require(element.Volume==0,"decoder-probe-skip-keeps-audio");frameChanges=0;lastFrame=null;stage=4;
     }else if(stage==4&&(bool)Field(slot,"Ready")){
      Call(window,"SelectActiveSlot",slot);element.IsMuted=true;element.Volume=0;
      element.Pause();element.Position=TimeSpan.Zero;element.Play();stage=5;
     }else if(stage==5&&element.Position.TotalSeconds>0.5&&frameChanges>=2){
      Call(window,"Finish","skip-button");
      Require(!window.IsVisible&&!window.ShowInTaskbar,"decoder-probe-reuse-cover-remains");
      index++;
      if(index<paths.Length){slot=Call(window,"EnsurePrepared",paths[index]);frameChanges=0;lastFrame=null;stage=0;}
      else{pass=true;poll.Stop();app.Shutdown();}
     }
    }catch(Exception e){IntroLog.Write("intro-media-decoder-probe-error type="+e.GetType().Name+" stage="+e.Message);poll.Stop();app.Shutdown();}
   };
   poll.Start();IntroLog.Write("decoder-probe-dispatcher-start");app.Run();
   IntroLog.Write("intro-media-decoder-probe pass="+pass+" clips="+index+" pixel-motion-checked=True");return pass;
  }catch(Exception e){IntroLog.Write("intro-media-decoder-probe-error type="+e.GetType().Name);return false;}
  finally{if(poll!=null)poll.Stop();if(window!=null)window.Close();if(app!=null&&!app.Dispatcher.HasShutdownStarted)app.Shutdown();}
 }
}
